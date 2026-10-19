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
    /// Reads MCHOSE G98 V2 battery data through the 2.4G TopSpeed receiver.
    /// The command and report layout are taken from MCHOSE's official web
    /// driver configuration for VID 41E4 / PID 2004.
    /// </summary>
    internal static class MchoseTopSpeedKeyboardReader
    {
        private const ushort VendorId = 0x41E4;
        private const ushort ProductId = 0x2004;
        private const ushort CommandUsagePage = 0xFF02;
        private const ushort CommandUsage = 0x0002;
        private const byte ReportId = 0x13;
        private const byte BatteryCommand = 0x4A;
        private const int ReportLength = 20;
        private const int QueryTimeoutMilliseconds = 1200;
        private const int QueryAttempts = 2;

        public static Task<GenericReceiverScanResult> ScanAsync(CancellationToken cancellationToken)
        {
            return Task.Run(delegate { return Scan(cancellationToken); }, cancellationToken);
        }

        private static GenericReceiverScanResult Scan(CancellationToken cancellationToken)
        {
            GenericReceiverScanResult result = new GenericReceiverScanResult();
            StringBuilder diagnostics = new StringBuilder();
            diagnostics.AppendLine("2.4G/迈从 TopSpeed 扫描：");
            IList<HidInterfaceInfo> candidates = EnumerateInterfaces(diagnostics);

            foreach (HidInterfaceInfo candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] request = BuildBatteryRequest();
                for (int attempt = 1; attempt <= QueryAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        byte[] response = Exchange(candidate.Path, request, candidate.InputLength, QueryTimeoutMilliseconds);
                        string validationError;
                        int battery;
                        bool valid = TryParseBattery(response, out battery, out validationError);
                        diagnostics.AppendLine(
                            "  G98 V2 查询 | Attempt=" + attempt +
                            " | Request=" + ToHex(request) +
                            " | Response=" + ToHex(response) +
                            " | Valid=" + valid +
                            " | Battery=" + (valid ? battery + "%" : "null") +
                            " | Detail=" + validationError +
                            " | Path=" + candidate.Path);

                        if (!valid) continue;
                        result.Devices.Add(new BluetoothDeviceBattery
                        {
                            StableId = "receiver:41e4:2004:topspeed:g98v2",
                            DeviceId = candidate.Path,
                            Name = "迈从 G98 V2",
                            DetectedName = "迈从 G98 V2",
                            Address = "USB 41E4:2004",
                            Transport = DeviceTransport.Receiver24G,
                            IsLowEnergy = false,
                            IsConnected = true,
                            IsPaired = true,
                            BatteryPercent = battery,
                            BatterySource = "迈从 TopSpeed HID 0x4A",
                            Error = string.Empty,
                            Kind = DeviceKind.Keyboard,
                            DetectedKind = DeviceKind.Keyboard,
                            LastRefreshedAt = DateTime.Now
                        });
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        diagnostics.AppendLine(
                            "  G98 V2 查询失败 | Attempt=" + attempt +
                            " | Error=" + ex.Message +
                            " | Path=" + candidate.Path);
                    }
                }

                if (result.Devices.Count > 0) break;
            }

            if (candidates.Count == 0)
            {
                diagnostics.AppendLine("  未找到在线的迈从 G98 V2 TopSpeed 接收器（41E4:2004）。");
            }
            else if (result.Devices.Count == 0)
            {
                diagnostics.AppendLine("  接收器存在，但键盘没有返回有效电量；按未连接处理，不使用历史电量冒充本次结果。");
            }
            result.Diagnostics = diagnostics.ToString().TrimEnd();
            return result;
        }

        private static byte[] BuildBatteryRequest()
        {
            byte[] request = new byte[ReportLength];
            request[0] = ReportId;
            request[1] = BatteryCommand;
            request[2] = 0x01;
            request[ReportLength - 1] = Checksum(request, ReportLength - 1);
            return request;
        }

        private static bool TryParseBattery(byte[] response, out int battery, out string error)
        {
            battery = 0;
            if (response == null)
            {
                error = "timeout";
                return false;
            }
            if (response.Length != ReportLength)
            {
                error = "unexpected-length-" + response.Length;
                return false;
            }
            if (response[0] != ReportId || response[1] != BatteryCommand)
            {
                error = "unexpected-report-or-command";
                return false;
            }
            if (Checksum(response, ReportLength - 1) != response[ReportLength - 1])
            {
                error = "checksum-mismatch";
                return false;
            }
            if (response[5] > 100)
            {
                error = "battery-out-of-range-" + response[5];
                return false;
            }
            battery = response[5];
            error = "ok; status=0x" + response[6].ToString("X2");
            return true;
        }

        private static byte Checksum(byte[] bytes, int count)
        {
            int sum = 0;
            for (int index = 0; index < count; index++) sum += bytes[index];
            return unchecked((byte)sum);
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
                        if (subKeyName.IndexOf("vid_41e4", StringComparison.OrdinalIgnoreCase) < 0 ||
                            subKeyName.IndexOf("pid_2004", StringComparison.OrdinalIgnoreCase) < 0)
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
                                             info.InputLength == ReportLength && info.OutputLength == ReportLength;
                            diagnostics.AppendLine(
                                "  G98 V2 HID 接口 | Manufacturer=" + info.Manufacturer +
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
                            diagnostics.AppendLine("  G98 V2 HID 历史/离线接口跳过 | Error=" + ex.Message + " | Path=" + path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine("  枚举迈从 TopSpeed HID 接口失败：" + ex.Message);
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

        private static byte[] Exchange(string path, byte[] request, int inputLength, int timeoutMilliseconds)
        {
            using (SafeFileHandle handle = CreateFile(path, GenericRead | GenericWrite,
                FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                IntPtr readEvent = CreateEvent(IntPtr.Zero, true, false, null);
                IntPtr writeEvent = CreateEvent(IntPtr.Zero, true, false, null);
                if (readEvent == IntPtr.Zero || writeEvent == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                byte[] response = new byte[inputLength];
                GCHandle responsePin = GCHandle.Alloc(response, GCHandleType.Pinned);
                GCHandle requestPin = GCHandle.Alloc(request, GCHandleType.Pinned);
                try
                {
                    NativeOverlapped readOverlapped = new NativeOverlapped { EventHandle = readEvent };
                    NativeOverlapped writeOverlapped = new NativeOverlapped { EventHandle = writeEvent };
                    int bytesRead;
                    bool readStarted = ReadFile(handle, responsePin.AddrOfPinnedObject(), response.Length,
                        out bytesRead, ref readOverlapped);
                    if (!readStarted && Marshal.GetLastWin32Error() != ErrorIoPending)
                        throw new Win32Exception(Marshal.GetLastWin32Error());

                    int bytesWritten;
                    bool writeStarted = WriteFile(handle, requestPin.AddrOfPinnedObject(), request.Length,
                        out bytesWritten, ref writeOverlapped);
                    if (!writeStarted && Marshal.GetLastWin32Error() != ErrorIoPending)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (!writeStarted)
                    {
                        if (WaitForSingleObject(writeEvent, 1000) != WaitObject0)
                            throw new TimeoutException("HID 写入超时");
                        if (!GetOverlappedResult(handle, ref writeOverlapped, out bytesWritten, false))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    uint wait = readStarted ? WaitObject0 : WaitForSingleObject(readEvent, (uint)timeoutMilliseconds);
                    if (wait == WaitTimeout)
                    {
                        CancelIoEx(handle, ref readOverlapped);
                        WaitForSingleObject(readEvent, 200);
                        return null;
                    }
                    if (wait != WaitObject0) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (!readStarted && !GetOverlappedResult(handle, ref readOverlapped, out bytesRead, false))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (bytesRead <= 0) return null;
                    if (bytesRead == response.Length) return response;
                    byte[] exact = new byte[bytesRead];
                    Array.Copy(response, exact, bytesRead);
                    return exact;
                }
                finally
                {
                    requestPin.Free();
                    responsePin.Free();
                    CloseHandle(readEvent);
                    CloseHandle(writeEvent);
                }
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
        private const uint FileFlagOverlapped = 0x40000000;
        private const int ErrorIoPending = 997;
        private const uint WaitObject0 = 0;
        private const uint WaitTimeout = 258;

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
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeFileHandle handle, IntPtr buffer, int count,
            out int read, ref NativeOverlapped overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(SafeFileHandle handle, IntPtr buffer, int count,
            out int written, ref NativeOverlapped overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr security, bool manualReset, bool initialState, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(SafeFileHandle handle,
            ref NativeOverlapped overlapped, out int transferred, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(SafeFileHandle handle, ref NativeOverlapped overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetManufacturerString(SafeFileHandle handle, IntPtr buffer, int length);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetProductString(SafeFileHandle handle, IntPtr buffer, int length);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsedData);
        [DllImport("hid.dll")]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);
        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsedData, ref HidCaps capabilities);
    }
}
