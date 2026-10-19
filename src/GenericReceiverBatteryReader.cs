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
    internal sealed class GenericReceiverScanResult
    {
        public IList<BluetoothDeviceBattery> Devices { get; set; }
        public string Diagnostics { get; set; }

        public GenericReceiverScanResult()
        {
            Devices = new List<BluetoothDeviceBattery>();
            Diagnostics = string.Empty;
        }
    }

    /// <summary>
    /// Reads the compact 8-byte HID feature report used by the Compx FC00
    /// receiver attached to the development computer. Some OEM/rebranded mice
    /// expose the same safe, read-only report: report id 0x06 and percentage at
    /// byte 6 (zero-based index 5).
    /// </summary>
    internal static class GenericReceiverBatteryReader
    {
        private const ushort BatteryUsagePage = 0xFF02;
        private const ushort BatteryUsage = 0x0002;
        private const byte BatteryReportId = 0x06;
        private const int BatteryReportLength = 8;

        public static Task<GenericReceiverScanResult> ScanAsync(CancellationToken cancellationToken)
        {
            return Task.Run(delegate { return Scan(cancellationToken); }, cancellationToken);
        }

        private static GenericReceiverScanResult Scan(CancellationToken cancellationToken)
        {
            GenericReceiverScanResult result = new GenericReceiverScanResult();
            StringBuilder diagnostics = new StringBuilder();
            diagnostics.AppendLine("2.4G/HID Feature 扫描：");

            foreach (HidInterfaceInfo hidInterface in EnumerateCandidateInterfaces(diagnostics, result.Devices))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    byte[] report = GetFeature(hidInterface.Path, BatteryReportId, BatteryReportLength);
                    diagnostics.AppendLine(
                        "  候选接收器 | Manufacturer=" + hidInterface.Manufacturer +
                        " | Product=" + hidInterface.Product +
                        " | VID=" + hidInterface.VendorId.ToString("X4") +
                        " | PID=" + hidInterface.ProductId.ToString("X4") +
                        " | Report=" + ToHex(report) +
                        " | BatteryCandidate=" + (IsValidBatteryReport(report) ? report[5] + "%" : "invalid") +
                        " | Path=" + hidInterface.Path);

                    if (!IsValidBatteryReport(report)) continue;
                    int battery = report[5];
                    string brand = NormalizeBrand(hidInterface.Manufacturer);
                    bool unknownBrand = string.IsNullOrWhiteSpace(brand);
                    if (unknownBrand) brand = "其他未知品牌";
                    string detectedName = BuildDetectedName(brand, hidInterface.VendorId, hidInterface.ProductId);
                    string stableId = "receiver:" + hidInterface.VendorId.ToString("x4") + ":" +
                                      hidInterface.ProductId.ToString("x4") + ":feature:06";

                    result.Devices.Add(new BluetoothDeviceBattery
                    {
                        StableId = stableId,
                        DeviceId = hidInterface.Path,
                        Name = detectedName,
                        DetectedName = detectedName,
                        Address = "USB " + hidInterface.VendorId.ToString("X4") + ":" + hidInterface.ProductId.ToString("X4"),
                        Transport = DeviceTransport.Receiver24G,
                        IsLowEnergy = false,
                        IsConnected = true,
                        IsPaired = true,
                        BatteryPercent = battery,
                        BatterySource = unknownBrand
                            ? "未知品牌 HID Feature 0x06"
                            : brand + " HID Feature 0x06",
                        Error = string.Empty,
                        Kind = DeviceKind.Mouse,
                        DetectedKind = DeviceKind.Mouse,
                        LastRefreshedAt = DateTime.Now
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    diagnostics.AppendLine("  HID Feature 读取失败：" + ex.Message + " | " + hidInterface.Path);
                }
            }

            result.Devices = result.Devices
                .GroupBy(delegate(BluetoothDeviceBattery device) { return device.StableId; }, StringComparer.OrdinalIgnoreCase)
                .Select(delegate(IGrouping<string, BluetoothDeviceBattery> group) { return group.First(); })
                .ToList();
            if (result.Devices.Count == 0) diagnostics.AppendLine("  未找到可识别或可读取电量的通用 2.4G 设备。");
            result.Diagnostics = diagnostics.ToString().TrimEnd();
            return result;
        }

        private static string NormalizeBrand(string manufacturer)
        {
            string value = (manufacturer ?? string.Empty).Trim();
            if (value.Equals("Compx", StringComparison.OrdinalIgnoreCase)) return "Compx";
            if (value.Length == 0 ||
                value.IndexOf("standard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("标准", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return string.Empty;
            }
            return value;
        }

        private static string BuildDetectedName(string brand, ushort vendorId, ushort productId)
        {
            if (vendorId == 0x3554 && productId == 0xFC00)
            {
                // The receiver reports its chipset/OEM name only, not the retail
                // mouse model. Keep the name honest until the model is supplied.
                return "Compx 2.4G 鼠标";
            }
            return brand + " 2.4G 鼠标";
        }

        private static bool IsValidBatteryReport(byte[] report)
        {
            if (report == null || report.Length != BatteryReportLength) return false;
            if (report[0] != BatteryReportId || report[5] > 100) return false;
            // Reject an all-zero payload; it is a common unsupported-report reply.
            for (int index = 1; index < report.Length; index++)
            {
                if (report[index] != 0) return true;
            }
            return false;
        }

        private static IEnumerable<HidInterfaceInfo> EnumerateCandidateInterfaces(
            StringBuilder diagnostics,
            IList<BluetoothDeviceBattery> detectedDevices)
        {
            const string hidClass = @"SYSTEM\CurrentControlSet\Control\DeviceClasses\{4d1e55b2-f16f-11cf-88cb-001111000030}";
            List<HidInterfaceInfo> interfaces = new List<HidInterfaceInfo>();
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(hidClass, false))
                {
                    if (key == null) return interfaces;
                    foreach (string subKeyName in key.GetSubKeyNames())
                    {
                        if (subKeyName.IndexOf("vid_", StringComparison.OrdinalIgnoreCase) < 0 ||
                            subKeyName.IndexOf("pid_", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                        string path = subKeyName.StartsWith("##?#", StringComparison.OrdinalIgnoreCase)
                            ? @"\\?\" + subKeyName.Substring(4)
                            : subKeyName;
                        try
                        {
                            HidInterfaceInfo info = Probe(path);
                            bool receiverLike = LooksLikeReceiver(info.Product);
                            bool knownBatteryProtocol = info.UsagePage == BatteryUsagePage &&
                                                        info.Usage == BatteryUsage &&
                                                        info.FeatureLength == BatteryReportLength;
                            diagnostics.AppendLine(
                                "  HID 接口 | Manufacturer=" + info.Manufacturer +
                                " | Product=" + info.Product +
                                " | VID=" + info.VendorId.ToString("X4") +
                                " | PID=" + info.ProductId.ToString("X4") +
                                " | ReceiverLike=" + receiverLike +
                                " | UsagePage=0x" + info.UsagePage.ToString("X4") +
                                " | Usage=0x" + info.Usage.ToString("X4") +
                                " | InputLength=" + info.InputLength +
                                " | OutputLength=" + info.OutputLength +
                                " | FeatureLength=" + info.FeatureLength +
                                " | KnownBatteryProtocol=" + knownBatteryProtocol +
                                " | Path=" + info.Path);

                            if (IsFeaulleShine(info) && info.UsagePage == 0x000C && info.Usage == 0x0001)
                            {
                                const string detectedName = "绯乐 曜";
                                detectedDevices.Add(new BluetoothDeviceBattery
                                {
                                    StableId = "receiver:be57:1213:feaulle:shine",
                                    DeviceId = info.Path,
                                    Name = detectedName,
                                    DetectedName = detectedName,
                                    Address = "USB BE57:1213",
                                    Transport = DeviceTransport.Receiver24G,
                                    IsLowEnergy = false,
                                    IsConnected = true,
                                    IsPaired = true,
                                    BatteryPercent = null,
                                    BatterySource = "绯乐 曜 2.4G HID（电量协议待适配）",
                                    Error = "已识别绯乐曜接收器，但接收器未主动上报电量。",
                                    Kind = DeviceKind.Headphones,
                                    DetectedKind = DeviceKind.Headphones,
                                    LastRefreshedAt = DateTime.Now
                                });
                                diagnostics.AppendLine(
                                    "  已识别 2.4G 耳机 | Brand=绯乐 | Model=曜 | " +
                                    "Identity=BE57:1213 | BatteryProtocol=Pending");
                            }
                            if (knownBatteryProtocol)
                            {
                                interfaces.Add(info);
                            }
                        }
                        catch
                        {
                            // DeviceClasses also retains disconnected interfaces.
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine("  枚举通用 2.4G HID 接口失败：" + ex.Message);
            }
            return interfaces;
        }

        private static bool LooksLikeReceiver(string product)
        {
            string value = (product ?? string.Empty).ToLowerInvariant();
            return value.Contains("receiver") || value.Contains("dongle") ||
                   value.Contains("2.4g") || value.Contains("mouse") || value.Contains("接收器");
        }

        private static bool IsFeaulleShine(HidInterfaceInfo info)
        {
            if (info == null || info.VendorId != 0xBE57 || info.ProductId != 0x1213) return false;
            return (info.Product ?? string.Empty).IndexOf("Feaulle SHINE", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static HidInterfaceInfo Probe(string path)
        {
            using (SafeFileHandle handle = CreateFile(path, 0, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
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

        private static byte[] GetFeature(string path, byte reportId, int length)
        {
            using (SafeFileHandle handle = CreateFile(path, 0, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                byte[] report = new byte[length];
                report[0] = reportId;
                if (!HidD_GetFeature(handle, report, report.Length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                return report;
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
            return bytes == null ? "<null>" : string.Join(" ", bytes.Select(delegate(byte value) { return value.ToString("X2"); }));
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

        private const uint FileShareRead = 1;
        private const uint FileShareWrite = 2;
        private const uint OpenExisting = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetManufacturerString(SafeFileHandle handle, IntPtr buffer, int length);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetProductString(SafeFileHandle handle, IntPtr buffer, int length);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] reportBuffer, int reportBufferLength);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsed);
        [DllImport("hid.dll")]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsed);
        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsed, ref HidCaps caps);
    }
}
