using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace BluetoothBatteryMonitor
{
    internal sealed class FloatingWindowPlacementSnapshot
    {
        public string MonitorDeviceName { get; set; }
        public string MonitorIdentity { get; set; }
        public int MonitorWorkWidth { get; set; }
        public int MonitorWorkHeight { get; set; }
        public double CenterXRatio { get; set; }
        public double CenterYRatio { get; set; }
    }

    internal sealed class FloatingMonitorOption
    {
        public string DeviceName { get; set; }
        public string MonitorIdentity { get; set; }
        public string DisplayName { get; set; }
        public int ResolutionWidth { get; set; }
        public int ResolutionHeight { get; set; }
        public int WorkLeft { get; set; }
        public int WorkTop { get; set; }
        public int WorkWidth { get; set; }
        public int WorkHeight { get; set; }
        public bool IsPrimary { get; set; }
        public string PositionDescription { get; set; }
    }

    /// <summary>
    /// Persists floating-window positions in physical desktop pixels, tied to
    /// a monitor identity. WPF Left/Top values alone are not stable when the
    /// monitors use different DPI scales (for example 4K + 1080p).
    /// </summary>
    internal static class FloatingWindowPlacement
    {
        private const uint MonitorDefaultToNearest = 2;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        public static bool HasSavedPlacement(
            string monitorDeviceName,
            int monitorWorkWidth,
            int monitorWorkHeight,
            double centerXRatio,
            double centerYRatio)
        {
            return !string.IsNullOrWhiteSpace(monitorDeviceName) &&
                   monitorWorkWidth > 0 && monitorWorkHeight > 0 &&
                   IsRatio(centerXRatio) && IsRatio(centerYRatio);
        }

        public static bool IsSavedMonitorAvailable(
            string monitorDeviceName,
            int savedWorkWidth,
            int savedWorkHeight,
            double centerXRatio,
            double centerYRatio,
            string monitorIdentity = null)
        {
            if (!HasSavedPlacement(
                monitorDeviceName,
                savedWorkWidth,
                savedWorkHeight,
                centerXRatio,
                centerYRatio))
            {
                // Incomplete target metadata must not silently become a request
                // to show on the primary screen.
                return string.IsNullOrWhiteSpace(monitorDeviceName) && string.IsNullOrWhiteSpace(monitorIdentity);
            }
            return FindTargetMonitor(
                EnumerateMonitors(),
                monitorDeviceName,
                savedWorkWidth,
                savedWorkHeight,
                monitorIdentity) != null;
        }

        public static IList<FloatingMonitorOption> GetAvailableMonitors()
        {
            List<MonitorRecord> monitors = EnumerateMonitors();
            monitors.Sort(delegate(MonitorRecord left, MonitorRecord right)
            {
                if (left.IsPrimary != right.IsPrimary) return left.IsPrimary ? -1 : 1;
                int horizontal = left.MonitorArea.Left.CompareTo(right.MonitorArea.Left);
                return horizontal != 0 ? horizontal : left.MonitorArea.Top.CompareTo(right.MonitorArea.Top);
            });
            MonitorRecord primary = monitors.Find(delegate(MonitorRecord item) { return item.IsPrimary; });
            List<FloatingMonitorOption> result = new List<FloatingMonitorOption>();
            int secondaryIndex = 0;
            for (int index = 0; index < monitors.Count; index++)
            {
                MonitorRecord monitor = monitors[index];
                result.Add(new FloatingMonitorOption
                {
                    DeviceName = monitor.DeviceName,
                    MonitorIdentity = monitor.Identity,
                    DisplayName = monitor.IsPrimary ? "主屏幕" : "显示器 " + (++secondaryIndex),
                    ResolutionWidth = monitor.MonitorArea.Width,
                    ResolutionHeight = monitor.MonitorArea.Height,
                    WorkLeft = monitor.WorkArea.Left,
                    WorkTop = monitor.WorkArea.Top,
                    WorkWidth = monitor.WorkArea.Width,
                    WorkHeight = monitor.WorkArea.Height,
                    IsPrimary = monitor.IsPrimary,
                    PositionDescription = DescribePosition(monitor, primary)
                });
            }
            return result;
        }

        public static FloatingWindowPlacementSnapshot Capture(Window window)
        {
            if (window == null) return null;
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return null;

            NativeRect windowRect;
            if (!GetWindowRect(handle, out windowRect)) return null;
            IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            MonitorRecord record = ReadMonitor(monitor);
            if (record == null) return null;

            int workWidth = record.WorkArea.Width;
            int workHeight = record.WorkArea.Height;
            if (workWidth <= 0 || workHeight <= 0) return null;
            double centerX = windowRect.Left + Math.Max(1, windowRect.Width) / 2.0;
            double centerY = windowRect.Top + Math.Max(1, windowRect.Height) / 2.0;
            return new FloatingWindowPlacementSnapshot
            {
                MonitorDeviceName = record.DeviceName,
                MonitorIdentity = record.Identity,
                MonitorWorkWidth = workWidth,
                MonitorWorkHeight = workHeight,
                CenterXRatio = Clamp01((centerX - record.WorkArea.Left) / workWidth),
                CenterYRatio = Clamp01((centerY - record.WorkArea.Top) / workHeight)
            };
        }

        public static bool TryRestore(
            Window window,
            string monitorDeviceName,
            int savedWorkWidth,
            int savedWorkHeight,
            double centerXRatio,
            double centerYRatio,
            string monitorIdentity = null)
        {
            if (window == null || !IsRatio(centerXRatio) || !IsRatio(centerYRatio)) return false;
            List<MonitorRecord> monitors = EnumerateMonitors();
            MonitorRecord target = FindTargetMonitor(monitors, monitorDeviceName, savedWorkWidth, savedWorkHeight, monitorIdentity);
            if (target == null) return false;

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return false;
            NativeRect current;
            if (!GetWindowRect(handle, out current)) return false;
            Rectangle targetBounds = CalculateTargetBounds(
                target.WorkArea,
                Math.Max(1, current.Width),
                Math.Max(1, current.Height),
                centerXRatio,
                centerYRatio);
            return SetWindowPos(
                handle,
                IntPtr.Zero,
                targetBounds.Left,
                targetBounds.Top,
                0,
                0,
                SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }

        internal static Rectangle CalculateTargetBounds(
            Rectangle workArea,
            int windowWidth,
            int windowHeight,
            double centerXRatio,
            double centerYRatio)
        {
            int safeWidth = Math.Max(1, windowWidth);
            int safeHeight = Math.Max(1, windowHeight);
            double centerX = workArea.Left + Clamp01(centerXRatio) * workArea.Width;
            double centerY = workArea.Top + Clamp01(centerYRatio) * workArea.Height;
            int left = (int)Math.Round(centerX - safeWidth / 2.0);
            int top = (int)Math.Round(centerY - safeHeight / 2.0);
            int maxLeft = Math.Max(workArea.Left, workArea.Right - safeWidth);
            int maxTop = Math.Max(workArea.Top, workArea.Bottom - safeHeight);
            left = Math.Max(workArea.Left, Math.Min(maxLeft, left));
            top = Math.Max(workArea.Top, Math.Min(maxTop, top));
            return new Rectangle(left, top, safeWidth, safeHeight);
        }

        internal static MonitorRecord FindTargetMonitor(
            IList<MonitorRecord> monitors,
            string deviceName,
            int savedWorkWidth,
            int savedWorkHeight,
            string monitorIdentity)
        {
            // A physical display interface survives DISPLAY number changes. Once
            // recorded, never substitute another screen with a similar resolution.
            if (!string.IsNullOrWhiteSpace(monitorIdentity))
            {
                foreach (MonitorRecord monitor in monitors)
                {
                    if (string.Equals(monitor.Identity, monitorIdentity, StringComparison.OrdinalIgnoreCase))
                        return monitor;
                }
                return null;
            }
            MonitorRecord exactNameMatch = null;
            foreach (MonitorRecord monitor in monitors)
            {
                if (string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    exactNameMatch = monitor;
                    break;
                }
            }
            if (exactNameMatch != null)
            {
                // Legacy settings have no physical identity. Require both the
                // original device name and a plausible size; do not guess by size.
                if (savedWorkWidth <= 0 || savedWorkHeight <= 0 ||
                    CalculateWorkAreaDifference(exactNameMatch, savedWorkWidth, savedWorkHeight) <= 0.18)
                {
                    return exactNameMatch;
                }
            }

            return null;
        }

        public static string GetTopologySignature()
        {
            List<MonitorRecord> monitors = EnumerateMonitors();
            monitors.Sort(delegate(MonitorRecord a, MonitorRecord b)
            {
                return string.Compare(a.DeviceName, b.DeviceName, StringComparison.OrdinalIgnoreCase);
            });
            StringBuilder signature = new StringBuilder();
            foreach (MonitorRecord monitor in monitors)
            {
                signature.Append(monitor.Identity).Append('|').Append(monitor.DeviceName).Append('|')
                    .Append(monitor.MonitorArea).Append('|').Append(monitor.WorkArea).Append('|')
                    .Append(monitor.IsPrimary).Append(';');
            }
            return signature.ToString();
        }

        private static double CalculateWorkAreaDifference(MonitorRecord monitor, int savedWorkWidth, int savedWorkHeight)
        {
            double widthDifference = Math.Abs(monitor.WorkArea.Width - savedWorkWidth) / (double)savedWorkWidth;
            double heightDifference = Math.Abs(monitor.WorkArea.Height - savedWorkHeight) / (double)savedWorkHeight;
            return widthDifference + heightDifference;
        }

        private static List<MonitorRecord> EnumerateMonitors()
        {
            List<MonitorRecord> monitors = new List<MonitorRecord>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data)
            {
                MonitorRecord record = ReadMonitor(monitor);
                if (record != null) monitors.Add(record);
                return true;
            }, IntPtr.Zero);
            return monitors;
        }

        private static MonitorRecord ReadMonitor(IntPtr monitor)
        {
            if (monitor == IntPtr.Zero) return null;
            MonitorInfoEx info = new MonitorInfoEx();
            info.Size = Marshal.SizeOf(typeof(MonitorInfoEx));
            if (!GetMonitorInfo(monitor, ref info)) return null;
            return new MonitorRecord
            {
                DeviceName = info.DeviceName ?? string.Empty,
                Identity = ReadMonitorIdentity(info.DeviceName),
                MonitorArea = Rectangle.FromLTRB(info.MonitorArea.Left, info.MonitorArea.Top, info.MonitorArea.Right, info.MonitorArea.Bottom),
                WorkArea = Rectangle.FromLTRB(info.WorkArea.Left, info.WorkArea.Top, info.WorkArea.Right, info.WorkArea.Bottom),
                IsPrimary = (info.Flags & 1) != 0
            };
        }

        private static string ReadMonitorIdentity(string deviceName)
        {
            // EDD_GET_DEVICE_INTERFACE_NAME returns a monitor interface path,
            // not the transient \\.\DISPLAYn adapter label.
            for (uint index = 0; index < 32; index++)
            {
                DisplayDevice device = new DisplayDevice();
                device.Size = Marshal.SizeOf(typeof(DisplayDevice));
                if (!EnumDisplayDevices(deviceName, index, ref device, 1)) break;
                if ((device.StateFlags & 1) == 0) continue;
                if (!string.IsNullOrWhiteSpace(device.DeviceId)) return device.DeviceId;
                if (!string.IsNullOrWhiteSpace(device.DeviceKey)) return "key:" + device.DeviceKey;
            }
            return string.Empty;
        }

        private static string DescribePosition(MonitorRecord monitor, MonitorRecord primary)
        {
            if (monitor.IsPrimary) return "Windows 主屏";
            if (primary == null) return "扩展屏幕";
            double centerX = monitor.MonitorArea.Left + monitor.MonitorArea.Width / 2.0;
            double centerY = monitor.MonitorArea.Top + monitor.MonitorArea.Height / 2.0;
            double primaryCenterX = primary.MonitorArea.Left + primary.MonitorArea.Width / 2.0;
            double primaryCenterY = primary.MonitorArea.Top + primary.MonitorArea.Height / 2.0;
            double horizontalDistance = centerX - primaryCenterX;
            double verticalDistance = centerY - primaryCenterY;
            if (Math.Abs(horizontalDistance) >= Math.Abs(verticalDistance))
            {
                return horizontalDistance < 0 ? "位于主屏左侧" : "位于主屏右侧";
            }
            return verticalDistance < 0 ? "位于主屏上方" : "位于主屏下方";
        }

        private static bool IsRatio(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1;
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0.5;
            return Math.Max(0, Math.Min(1, value));
        }

        internal sealed class MonitorRecord
        {
            public string DeviceName { get; set; }
            public string Identity { get; set; }
            public Rectangle MonitorArea { get; set; }
            public Rectangle WorkArea { get; set; }
            public bool IsPrimary { get; set; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string deviceName, uint index, ref DisplayDevice device, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfoEx
        {
            public int Size;
            public NativeRect MonitorArea;
            public NativeRect WorkArea;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        private delegate bool MonitorEnumProcedure(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(
            IntPtr dc,
            IntPtr clip,
            MonitorEnumProcedure callback,
            IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}
