using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BluetoothBatteryMonitor
{
    internal static class WindowVisualHelper
    {
        private const int GwlExStyle = -20;
        private const long WsExToolWindow = 0x00000080L;
        private const long WsExAppWindow = 0x00040000L;
        private const int DwmWindowCornerPreference = 33;
        private const int DwmWindowCornerRound = 2;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private static readonly IntPtr HwndTop = IntPtr.Zero;

        public static void ApplyRoundedCorners(Window window)
        {
            if (window == null) return;
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;
                int preference = DwmWindowCornerRound;
                DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref preference, sizeof(int));
            }
            catch
            {
                // Windows 10 或不支持 DWM 圆角时保留系统默认边框。
            }
        }

        public static void HideFromAltTab(Window window)
        {
            if (window == null) return;
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;
                long style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
                style |= WsExToolWindow;
                style &= ~WsExAppWindow;
                SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
            }
            catch
            {
                // 样式调整失败不影响悬浮窗本身显示。
            }
        }

        public static void BringToFront(Window window)
        {
            if (window == null || !window.IsVisible) return;
            try
            {
                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;
                uint flags = SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow;
                SetWindowPos(handle, HwndTop, 0, 0, 0, 0, flags);
                BringWindowToTop(handle);
                window.Activate();
                window.Focus();
                SetForegroundWindow(handle);
            }
            catch
            {
                // 恢复层级失败时保持窗口当前状态。
            }
        }

        private static IntPtr GetWindowLongPtr(IntPtr handle, int index)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(handle, index)
                : new IntPtr(GetWindowLong32(handle, index));
        }

        private static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(handle, index, value)
                : new IntPtr(SetWindowLong32(handle, index, value.ToInt32()));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int valueSize);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr handle, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr handle, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BringWindowToTop(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr handle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}
