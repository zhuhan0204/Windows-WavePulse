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
    internal sealed class LogitechHidppScanResult
    {
        public IList<BluetoothDeviceBattery> Devices { get; set; }
        public string Diagnostics { get; set; }
        public bool ReceiverDetected { get; set; }

        public LogitechHidppScanResult()
        {
            Devices = new List<BluetoothDeviceBattery>();
            Diagnostics = string.Empty;
        }
    }

    /// <summary>
    /// Reads Logitech HID++ 2.0 devices connected through a USB 2.4G receiver.
    /// The first supported receiver was verified against the local C548 receiver
    /// and its Signature Plus M750 mouse; the protocol discovery remains generic
    /// for other Logitech HID++ 2.0 receiver product ids.
    /// </summary>
    internal static class LogitechHidppReader
    {
        private const ushort LogitechVendorId = 0x046D;
        private const ushort HidppUsagePage = 0xFF00;
        private const ushort HidppLongUsage = 0x0002;
        private const int LongReportLength = 20;
        private const int ExchangeTimeoutMilliseconds = 400;
        private static int _softwareIdCounter;

        public static Task<LogitechHidppScanResult> ScanAsync(CancellationToken cancellationToken)
        {
            return Task.Run(delegate { return Scan(cancellationToken); }, cancellationToken);
        }

        private static LogitechHidppScanResult Scan(CancellationToken cancellationToken)
        {
            LogitechHidppScanResult result = new LogitechHidppScanResult();
            StringBuilder diagnostics = new StringBuilder();
            diagnostics.AppendLine("2.4G/HID++ 扫描：");

            foreach (HidInterfaceInfo hidInterface in EnumerateHidppInterfaces(diagnostics))
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.ReceiverDetected = true;
                diagnostics.AppendLine(
                    "  Logitech 接收器 | VID=" + hidInterface.VendorId.ToString("X4") +
                    " | PID=" + hidInterface.ProductId.ToString("X4") +
                    " | Version=" + hidInterface.VersionNumber.ToString("X4") +
                    " | Path=" + hidInterface.Path);

                for (byte deviceIndex = 1; deviceIndex <= 6; deviceIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        int nameFeature = GetFeatureIndex(hidInterface.Path, deviceIndex, 0x0005);
                        if (nameFeature <= 0) continue;

                        int? battery = ReadBattery(hidInterface.Path, deviceIndex, diagnostics);
                        string name = ReadDeviceName(hidInterface.Path, deviceIndex, (byte)nameFeature);
                        DeviceKind kind = ReadDeviceKind(hidInterface.Path, deviceIndex, (byte)nameFeature);
                        if (string.IsNullOrWhiteSpace(name)) name = "罗技 2.4G 设备";

                        BluetoothDeviceBattery device = new BluetoothDeviceBattery
                        {
                            StableId = "receiver:" + hidInterface.VendorId.ToString("x4") + ":" +
                                       hidInterface.ProductId.ToString("x4") + ":slot:" + deviceIndex.ToString("00"),
                            DeviceId = hidInterface.Path + "|slot=" + deviceIndex,
                            Name = name,
                            DetectedName = name,
                            Address = "USB " + hidInterface.VendorId.ToString("X4") + ":" +
                                      hidInterface.ProductId.ToString("X4") + " · 槽位 " + deviceIndex,
                            Transport = DeviceTransport.Receiver24G,
                            IsLowEnergy = false,
                            IsConnected = true,
                            IsPaired = true,
                            BatteryPercent = battery,
                            BatterySource = battery.HasValue ? "罗技 HID++ 2.4G 接收器" : string.Empty,
                            Error = battery.HasValue ? string.Empty : "设备已连接，但 HID++ 未上报电量",
                            Kind = kind,
                            DetectedKind = kind,
                            LastRefreshedAt = DateTime.Now
                        };
                        result.Devices.Add(device);
                        diagnostics.AppendLine(
                            "    槽位 " + deviceIndex + " | Name=" + name +
                            " | Kind=" + kind +
                            " | Battery=" + (battery.HasValue ? battery.Value + "%" : "null"));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        diagnostics.AppendLine("    槽位 " + deviceIndex + " 读取失败：" + ex.Message);
                    }
                }
            }

            if (!result.ReceiverDetected)
            {
                diagnostics.AppendLine("  未检测到可用的 Logitech HID++ 2.4G 接收器。");
            }
            result.Devices = result.Devices
                .GroupBy(delegate(BluetoothDeviceBattery device) { return device.StableId; }, StringComparer.OrdinalIgnoreCase)
                .Select(delegate(IGrouping<string, BluetoothDeviceBattery> group) { return group.First(); })
                .ToList();
            result.Diagnostics = diagnostics.ToString().TrimEnd();
            return result;
        }

        private static int GetFeatureIndex(string path, byte deviceIndex, ushort featureId)
        {
            byte[] parameters = { (byte)(featureId >> 8), (byte)(featureId & 0xFF) };
            byte[] response = Call(path, deviceIndex, 0, 0, parameters, 4);
            if (response == null || response.Length < 7 || response[2] == 0x8F) return 0;
            return response[4];
        }

        private static string ReadDeviceName(string path, byte deviceIndex, byte featureIndex)
        {
            byte[] lengthResponse = Call(path, deviceIndex, featureIndex, 0, null, 4);
            if (lengthResponse == null || lengthResponse.Length < 5 || lengthResponse[2] == 0x8F) return string.Empty;
            int length = lengthResponse[4];
            if (length <= 0 || length > 96) return string.Empty;

            List<byte> bytes = new List<byte>();
            for (int offset = 0; offset < length; offset += 16)
            {
                byte[] chunk = Call(path, deviceIndex, featureIndex, 1, new[] { (byte)offset }, 4);
                if (chunk == null || chunk.Length <= 4 || chunk[2] == 0x8F) break;
                int count = Math.Min(16, Math.Min(length - offset, chunk.Length - 4));
                for (int index = 0; index < count; index++)
                {
                    if (chunk[4 + index] == 0) break;
                    bytes.Add(chunk[4 + index]);
                }
            }
            return Encoding.UTF8.GetString(bytes.ToArray()).Trim();
        }

        private static DeviceKind ReadDeviceKind(string path, byte deviceIndex, byte featureIndex)
        {
            byte[] response = Call(path, deviceIndex, featureIndex, 2, null, 4);
            if (response == null || response.Length < 5 || response[2] == 0x8F) return DeviceKind.Other;
            switch (response[4])
            {
                case 0:
                case 1:
                case 2:
                    return DeviceKind.Keyboard;
                case 3:
                case 4:
                case 5:
                case 6:
                    return DeviceKind.Mouse;
                case 7:
                    return DeviceKind.Gamepad;
                default:
                    return DeviceKind.Other;
            }
        }

        private static int? ReadBattery(string path, byte deviceIndex, StringBuilder diagnostics)
        {
            int unifiedBatteryFeature = GetFeatureIndex(path, deviceIndex, 0x1004);
            if (unifiedBatteryFeature > 0)
            {
                byte[] response = Call(path, deviceIndex, (byte)unifiedBatteryFeature, 0, null, 3);
                if (response != null && response.Length >= 5 && response[2] != 0x8F && response[4] <= 100)
                {
                    diagnostics.AppendLine("      电量特征=0x1004 | Raw=" + ToHex(response));
                    return response[4];
                }
            }

            int batteryLevelFeature = GetFeatureIndex(path, deviceIndex, 0x1000);
            if (batteryLevelFeature > 0)
            {
                byte[] response = Call(path, deviceIndex, (byte)batteryLevelFeature, 0, null, 3);
                if (response != null && response.Length >= 5 && response[2] != 0x8F && response[4] <= 100)
                {
                    diagnostics.AppendLine("      电量特征=0x1000 | Raw=" + ToHex(response));
                    return response[4];
                }
            }
            return null;
        }

        private static byte[] Call(
            string path,
            byte deviceIndex,
            byte featureIndex,
            byte function,
            byte[] parameters,
            byte attempts)
        {
            // Each logical request gets a different HID++ software id. Receivers can
            // retain a late response briefly; reusing the same id would let a stale
            // name chunk be mistaken for the next chunk.
            byte softwareId = (byte)(((Interlocked.Increment(ref _softwareIdCounter) - 1) % 15) + 1);
            byte[] request = new byte[LongReportLength];
            request[0] = 0x11;
            request[1] = deviceIndex;
            request[2] = featureIndex;
            request[3] = (byte)((function << 4) | (softwareId & 0x0F));
            if (parameters != null)
            {
                Array.Copy(parameters, 0, request, 4, Math.Min(parameters.Length, request.Length - 4));
            }

            int attemptCount = Math.Max(1, Math.Min(4, (int)attempts));
            for (int attempt = 0; attempt < attemptCount; attempt++)
            {
                byte[] response = Exchange(path, request, ExchangeTimeoutMilliseconds);
                if (response == null || response.Length < 4) continue;
                if (response[0] != request[0] || response[1] != request[1]) continue;
                if (response[2] == request[2] && response[3] == request[3]) return response;
                if (response[2] == 0x8F) return response;
            }
            return null;
        }

        private static string ToHex(byte[] bytes)
        {
            return string.Join(" ", bytes.Select(delegate(byte value) { return value.ToString("X2"); }));
        }

        private static IEnumerable<HidInterfaceInfo> EnumerateHidppInterfaces(StringBuilder diagnostics)
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
                        if (subKeyName.IndexOf("vid_046d", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string path = subKeyName.StartsWith("##?#", StringComparison.OrdinalIgnoreCase)
                            ? @"\\?\" + subKeyName.Substring(4)
                            : subKeyName;
                        try
                        {
                            HidInterfaceInfo info = Probe(path);
                            if (info.VendorId == LogitechVendorId &&
                                info.UsagePage == HidppUsagePage &&
                                info.Usage == HidppLongUsage &&
                                info.InputLength == LongReportLength &&
                                info.OutputLength == LongReportLength)
                            {
                                interfaces.Add(info);
                            }
                        }
                        catch (Exception ex)
                        {
                            diagnostics.AppendLine("  HID 接口跳过：" + ex.Message + " | " + path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine("  枚举 HID 接口失败：" + ex.Message);
            }
            return interfaces;
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
                    VersionNumber = attributes.VersionNumber,
                    UsagePage = caps.UsagePage,
                    Usage = caps.Usage,
                    InputLength = caps.InputReportByteLength,
                    OutputLength = caps.OutputReportByteLength
                };
            }
        }

        private static byte[] Exchange(string path, byte[] request, int timeoutMilliseconds)
        {
            using (SafeFileHandle handle = CreateFile(
                path,
                GenericRead | GenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                FileFlagOverlapped,
                IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                IntPtr readEvent = CreateEvent(IntPtr.Zero, true, false, null);
                IntPtr writeEvent = CreateEvent(IntPtr.Zero, true, false, null);
                if (readEvent == IntPtr.Zero || writeEvent == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                byte[] response = new byte[LongReportLength];
                GCHandle responsePin = GCHandle.Alloc(response, GCHandleType.Pinned);
                GCHandle requestPin = GCHandle.Alloc(request, GCHandleType.Pinned);
                try
                {
                    NativeOverlapped readOverlapped = new NativeOverlapped { EventHandle = readEvent };
                    NativeOverlapped writeOverlapped = new NativeOverlapped { EventHandle = writeEvent };
                    int bytesRead;
                    bool readStarted = ReadFile(handle, responsePin.AddrOfPinnedObject(), response.Length, out bytesRead, ref readOverlapped);
                    if (!readStarted && Marshal.GetLastWin32Error() != ErrorIoPending)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    int bytesWritten;
                    bool writeStarted = WriteFile(handle, requestPin.AddrOfPinnedObject(), request.Length, out bytesWritten, ref writeOverlapped);
                    if (!writeStarted && Marshal.GetLastWin32Error() != ErrorIoPending)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    if (!writeStarted)
                    {
                        if (WaitForSingleObject(writeEvent, 800) != WaitObject0) throw new TimeoutException("HID 写入超时");
                        if (!GetOverlappedResult(handle, ref writeOverlapped, out bytesWritten, false))
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        }
                    }

                    uint wait = readStarted ? WaitObject0 : WaitForSingleObject(readEvent, (uint)timeoutMilliseconds);
                    if (wait == WaitTimeout)
                    {
                        CancelIoEx(handle, ref readOverlapped);
                        WaitForSingleObject(readEvent, 100);
                        return null;
                    }
                    if (wait != WaitObject0) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (!readStarted && !GetOverlappedResult(handle, ref readOverlapped, out bytesRead, false))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
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

        private sealed class HidInterfaceInfo
        {
            public string Path { get; set; }
            public ushort VendorId { get; set; }
            public ushort ProductId { get; set; }
            public ushort VersionNumber { get; set; }
            public ushort UsagePage { get; set; }
            public ushort Usage { get; set; }
            public ushort InputLength { get; set; }
            public ushort OutputLength { get; set; }
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
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileFlagOverlapped = 0x40000000;
        private const int ErrorIoPending = 997;
        private const uint WaitObject0 = 0;
        private const uint WaitTimeout = 258;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeFileHandle handle, IntPtr buffer, int count, out int read, ref NativeOverlapped overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(SafeFileHandle handle, IntPtr buffer, int count, out int written, ref NativeOverlapped overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr security, bool manualReset, bool initialState, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(SafeFileHandle handle, ref NativeOverlapped overlapped, out int transferred, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(SafeFileHandle handle, ref NativeOverlapped overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);
        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsed);
        [DllImport("hid.dll")]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsed);
        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsed, ref HidCaps caps);
    }
}
