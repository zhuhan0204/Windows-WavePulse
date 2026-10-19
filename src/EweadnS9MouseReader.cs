using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace BluetoothBatteryMonitor
{
    /// <summary>
    /// Reads the EWEADN S9 Pro through its 2.4G MousePix feature channel.
    /// The request layout and field offsets are taken from EWEADN's official
    /// E-HUB web driver. WavePulse talks to the receiver directly and does not
    /// depend on E-HUB being installed or running.
    /// </summary>
    internal static class EweadnS9MouseReader
    {
        private const ushort VendorId = 0x372E;
        private const ushort ProductId = 0x1026;
        private const ushort CommandUsagePage = 0xFF00;
        private const ushort CommandUsage = 0x0001;
        private const byte ReportId = 0x03;
        private const int FeatureReportLength = 64;
        private const byte CommandType = 0x50;
        private const byte ReadMainCommand = 0x4F;
        private const byte UuidSubCommand = 0x80;
        private const byte BasicsSubCommand = 0x81;
        private const byte OnlineMainCommand = 0x47;
        private const ulong S9ProUuid = 9895604649998UL;
        private const int QueryAttempts = 3;
        private const int PollAttempts = 4;

        public static Task<GenericReceiverScanResult> ScanAsync(CancellationToken cancellationToken)
        {
            return Task.Run(delegate { return Scan(cancellationToken); }, cancellationToken);
        }

        private static GenericReceiverScanResult Scan(CancellationToken cancellationToken)
        {
            GenericReceiverScanResult result = new GenericReceiverScanResult();
            StringBuilder diagnostics = new StringBuilder();
            diagnostics.AppendLine("2.4G/前行者 S9 Pro MousePix 扫描：");
            IList<HidInterfaceInfo> candidates = EnumerateInterfaces(diagnostics);

            foreach (HidInterfaceInfo candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using (SafeFileHandle handle = CreateFile(candidate.Path, GenericRead | GenericWrite,
                        FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
                    {
                        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());

                        byte[] uuidResponse = Query(
                            handle,
                            ReadMainCommand,
                            UuidSubCommand,
                            delegate(byte[] response) { return IsProtocolResponse(response, 0x00, 8); },
                            diagnostics,
                            "UUID",
                            cancellationToken);
                        ulong uuid;
                        if (!TryParseUuid(uuidResponse, out uuid) || uuid != S9ProUuid)
                        {
                            diagnostics.AppendLine(
                                "  S9 型号校验未通过 | ExpectedUuid=" + S9ProUuid +
                                " | ActualUuid=" + (uuid == 0 ? "invalid" : uuid.ToString()) +
                                " | Path=" + candidate.Path);
                            continue;
                        }

                        byte[] onlineResponse = Query(
                            handle,
                            OnlineMainCommand,
                            0x00,
                            delegate(byte[] response) { return IsProtocolResponse(response, OnlineMainCommand, 1); },
                            diagnostics,
                            "在线状态",
                            cancellationToken);
                        bool online = onlineResponse != null && onlineResponse[6] == 0x01;
                        diagnostics.AppendLine(
                            "  S9 Pro 在线校验 | Online=" + online +
                            " | Raw=" + ToHex(onlineResponse));
                        if (!online)
                        {
                            diagnostics.AppendLine("  接收器存在，但鼠标没有返回在线状态；按未连接处理，不使用历史电量。");
                            continue;
                        }

                        byte[] basicsResponse = Query(
                            handle,
                            ReadMainCommand,
                            BasicsSubCommand,
                            delegate(byte[] response) { return IsProtocolResponse(response, 0x01, 7); },
                            diagnostics,
                            "基础状态/电量",
                            cancellationToken);
                        int battery;
                        bool charging;
                        string validation;
                        if (!TryParseBasics(basicsResponse, out battery, out charging, out validation))
                        {
                            diagnostics.AppendLine(
                                "  S9 Pro 电量校验失败 | Detail=" + validation +
                                " | Raw=" + ToHex(basicsResponse));
                            continue;
                        }

                        diagnostics.AppendLine(
                            "  S9 Pro 电量确认 | RawBattery=" + battery + "%" +
                            " | Charging=" + charging +
                            " | DisplayBattery=" + (charging ? "hidden" : battery + "%") +
                            " | UUID=" + uuid +
                            " | Raw=" + ToHex(basicsResponse));
                        result.Devices.Add(new BluetoothDeviceBattery
                        {
                            StableId = "receiver:372e:1026:eweadn:s9pro",
                            DeviceId = candidate.Path,
                            Name = "前行者 S9 Pro",
                            DetectedName = "前行者 S9 Pro",
                            Address = "USB 372E:1026",
                            Transport = DeviceTransport.Receiver24G,
                            IsLowEnergy = false,
                            IsConnected = true,
                            IsPaired = true,
                            // S9 Pro reports a voltage-derived value while charging. Historical
                            // captures jumped 28 -> 92/94 -> 63 around cable insertion/removal,
                            // so the value is deliberately hidden until charging ends.
                            BatteryPercent = charging ? (int?)null : battery,
                            IsCharging = charging,
                            BatterySource = charging ? "前行者 MousePix HID（充电中）" : "前行者 MousePix HID",
                            Error = string.Empty,
                            Kind = DeviceKind.Mouse,
                            DetectedKind = DeviceKind.Mouse,
                            LastRefreshedAt = DateTime.Now
                        });
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    diagnostics.AppendLine(
                        "  S9 Pro 查询失败 | Type=" + ex.GetType().Name +
                        " | Error=" + ex.Message +
                        " | Path=" + candidate.Path);
                }
            }

            if (candidates.Count == 0)
            {
                diagnostics.AppendLine("  未找到在线的前行者 S9 Pro MousePix 接口（372E:1026 / FF00:0001）。");
            }
            else if (result.Devices.Count == 0)
            {
                diagnostics.AppendLine("  未得到同时通过型号、在线状态与协议校验的新鲜电量，本轮不创建设备结果。");
            }
            result.Diagnostics = diagnostics.ToString().TrimEnd();
            return result;
        }

        private static byte[] Query(
            SafeFileHandle handle,
            byte mainCommand,
            byte subCommand,
            Func<byte[], bool> responseValidator,
            StringBuilder diagnostics,
            string label,
            CancellationToken cancellationToken)
        {
            byte[] request = BuildRequest(mainCommand, subCommand);
            for (int attempt = 1; attempt <= QueryAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!HidD_SetFeature(handle, request, request.Length))
                {
                    int error = Marshal.GetLastWin32Error();
                    diagnostics.AppendLine(
                        "  " + label + "写入失败 | Attempt=" + attempt +
                        " | Win32=" + error +
                        " | Request=" + ToHex(request));
                    continue;
                }

                for (int poll = 1; poll <= PollAttempts; poll++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Thread.Sleep(poll == 1 ? 25 : 70);
                    byte[] response = new byte[FeatureReportLength];
                    response[0] = ReportId;
                    bool success = HidD_GetFeature(handle, response, response.Length);
                    bool valid = success && responseValidator(response);
                    diagnostics.AppendLine(
                        "  " + label + "查询 | Attempt=" + attempt +
                        " | Poll=" + poll +
                        " | Success=" + success +
                        " | Valid=" + valid +
                        " | Request=" + ToHex(request) +
                        " | Response=" + ToHex(response) +
                        (success ? string.Empty : " | Win32=" + Marshal.GetLastWin32Error()));
                    if (valid) return response;
                }
            }
            return null;
        }

        private static byte[] BuildRequest(byte mainCommand, byte subCommand)
        {
            // Windows feature-report buffers include the report id at byte 0.
            // The MousePix protocol payload that follows is 63 bytes long.
            byte[] request = new byte[FeatureReportLength];
            request[0] = ReportId;
            request[2] = CommandType;
            request[3] = 0x00;
            request[4] = 0x00;
            request[5] = mainCommand;
            request[6] = subCommand;
            int checksum = 0;
            for (int index = 2; index < request.Length; index++) checksum += request[index];
            request[1] = unchecked((byte)checksum);
            return request;
        }

        private static bool IsProtocolResponse(byte[] response, byte responseOffset, int minimumPayloadLength)
        {
            return response != null &&
                   response.Length == FeatureReportLength &&
                   response[0] == ReportId &&
                   response[1] == CommandType &&
                   response[2] == 0x00 &&
                   response[3] >= minimumPayloadLength &&
                   response[4] == 0x00 &&
                   response[5] == responseOffset;
        }

        private static bool TryParseUuid(byte[] response, out ulong uuid)
        {
            uuid = 0;
            if (!IsProtocolResponse(response, 0x00, 8)) return false;
            // Official SDK combines payload uint32@2 and uint16@6 as a
            // big-endian six-byte device UUID.
            for (int index = 8; index <= 13; index++) uuid = (uuid << 8) | response[index];
            return uuid != 0;
        }

        private static bool TryParseBasics(byte[] response, out int battery, out bool charging, out string error)
        {
            battery = 0;
            charging = false;
            if (!IsProtocolResponse(response, 0x01, 7))
            {
                error = "invalid-header";
                return false;
            }

            byte batteryStatus = response[6];
            battery = batteryStatus & 0x7F;
            charging = (batteryStatus & 0x80) != 0;
            if (battery < 0 || battery > 100)
            {
                error = "battery-out-of-range-" + battery;
                return false;
            }
            error = "ok";
            return true;
        }

        private static IList<HidInterfaceInfo> EnumerateInterfaces(StringBuilder diagnostics)
        {
            const string hidClass = @"SYSTEM\CurrentControlSet\Control\DeviceClasses\{4d1e55b2-f16f-11cf-88cb-001111000030}";
            List<HidInterfaceInfo> result = new List<HidInterfaceInfo>();
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(hidClass, false))
                {
                    if (key == null) return result;
                    foreach (string subKeyName in key.GetSubKeyNames())
                    {
                        if (subKeyName.IndexOf("vid_372e", StringComparison.OrdinalIgnoreCase) < 0 ||
                            subKeyName.IndexOf("pid_1026", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                        string path = subKeyName.StartsWith("##?#", StringComparison.OrdinalIgnoreCase)
                            ? @"\\?\" + subKeyName.Substring(4)
                            : subKeyName;
                        try
                        {
                            HidInterfaceInfo info = Probe(path);
                            bool candidate = info.VendorId == VendorId && info.ProductId == ProductId &&
                                             info.UsagePage == CommandUsagePage && info.Usage == CommandUsage &&
                                             info.FeatureLength == FeatureReportLength;
                            diagnostics.AppendLine(
                                "  S9 HID 接口 | Manufacturer=" + info.Manufacturer +
                                " | Product=" + info.Product +
                                " | VID=" + info.VendorId.ToString("X4") +
                                " | PID=" + info.ProductId.ToString("X4") +
                                " | UsagePage=0x" + info.UsagePage.ToString("X4") +
                                " | Usage=0x" + info.Usage.ToString("X4") +
                                " | InputLength=" + info.InputLength +
                                " | OutputLength=" + info.OutputLength +
                                " | FeatureLength=" + info.FeatureLength +
                                " | CommandCandidate=" + candidate +
                                " | Path=" + info.Path);
                            if (candidate) result.Add(info);
                        }
                        catch (Exception ex)
                        {
                            diagnostics.AppendLine("  S9 HID 历史/离线接口跳过 | Error=" + ex.Message + " | Path=" + path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine("  枚举前行者 S9 HID 接口失败：" + ex.Message);
            }
            return result;
        }

        private static HidInterfaceInfo Probe(string path)
        {
            using (SafeFileHandle handle = CreateFile(path, 0, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                HidAttributes attributes = new HidAttributes { Size = Marshal.SizeOf(typeof(HidAttributes)) };
                if (!HidD_GetAttributes(handle, ref attributes)) throw new Win32Exception(Marshal.GetLastWin32Error());
                HidCaps caps = new HidCaps { Reserved = new ushort[17] };
                IntPtr preparsed;
                if (!HidD_GetPreparsedData(handle, out preparsed)) throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    int status = HidP_GetCaps(preparsed, ref caps);
                    if (status < 0) throw new InvalidOperationException("HidP_GetCaps 失败：0x" + status.ToString("X8"));
                }
                finally
                {
                    HidD_FreePreparsedData(preparsed);
                }
                return new HidInterfaceInfo
                {
                    Path = path,
                    VendorId = attributes.VendorId,
                    ProductId = attributes.ProductId,
                    Manufacturer = ReadHidString(handle, HidD_GetManufacturerString),
                    Product = ReadHidString(handle, HidD_GetProductString),
                    UsagePage = caps.UsagePage,
                    Usage = caps.Usage,
                    InputLength = caps.InputReportByteLength,
                    OutputLength = caps.OutputReportByteLength,
                    FeatureLength = caps.FeatureReportByteLength
                };
            }
        }

        private delegate bool HidStringReader(SafeFileHandle handle, IntPtr buffer, int length);

        private static string ReadHidString(SafeFileHandle handle, HidStringReader reader)
        {
            IntPtr buffer = Marshal.AllocHGlobal(512);
            try
            {
                for (int index = 0; index < 512; index++) Marshal.WriteByte(buffer, index, 0);
                return reader(handle, buffer, 512) ? (Marshal.PtrToStringUni(buffer) ?? string.Empty) : string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string ToHex(byte[] bytes)
        {
            return bytes == null
                ? "<timeout>"
                : string.Join(" ", bytes.Select(delegate(byte value) { return value.ToString("X2"); }));
        }

        private sealed class HidInterfaceInfo
        {
            public string Path { get; set; }
            public ushort VendorId { get; set; }
            public ushort ProductId { get; set; }
            public string Manufacturer { get; set; }
            public string Product { get; set; }
            public ushort UsagePage { get; set; }
            public ushort Usage { get; set; }
            public ushort InputLength { get; set; }
            public ushort OutputLength { get; set; }
            public ushort FeatureLength { get; set; }
        }

        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 1;
        private const uint FileShareWrite = 2;
        private const uint OpenExisting = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct HidAttributes
        {
            public int Size;
            public ushort VendorId;
            public ushort ProductId;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidCaps
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
            IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetManufacturerString(SafeFileHandle handle, IntPtr buffer, int length);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetProductString(SafeFileHandle handle, IntPtr buffer, int length);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] reportBuffer, int reportBufferLength);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] reportBuffer, int reportBufferLength);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsedData);
        [DllImport("hid.dll")]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);
        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsedData, ref HidCaps capabilities);
    }
}
