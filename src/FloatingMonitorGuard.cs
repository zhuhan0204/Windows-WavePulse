using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace BluetoothBatteryMonitor
{
    /// <summary>Hide before Windows relocates a window from a removed screen.</summary>
    internal static class FloatingMonitorGuard
    {
        public static void Attach(Window window, Func<bool> canShow, Action disablePersistence, Action topologyChanged)
        {
            HwndSource source = null;
            bool suppressed = false;
            bool notificationQueued = false;
            Action notify = delegate
            {
                disablePersistence();
                if (notificationQueued || window.Dispatcher.HasShutdownStarted) return;
                notificationQueued = true;
                // Closing inside WINDOWPOSCHANGING is re-entrant. Defer only the
                // reconciliation; native visibility is suppressed immediately.
                window.Dispatcher.BeginInvoke(DispatcherPriority.Send, topologyChanged);
            };
            HwndSourceHook hook = delegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (message == 0x007E) // WM_DISPLAYCHANGE
                {
                    suppressed = true;
                    notify();
                    ShowWindow(hwnd, 0); // SW_HIDE, without activation
                }
                else if (message == 0x0046 && lParam != IntPtr.Zero) // WM_WINDOWPOSCHANGING
                {
                    if (suppressed || !canShow())
                    {
                        suppressed = true;
                        WindowPosition position = (WindowPosition)Marshal.PtrToStructure(lParam, typeof(WindowPosition));
                        position.Flags = (position.Flags & ~0x0040u) | 0x0080u | 0x0010u;
                        Marshal.StructureToPtr(position, lParam, false); // hide, never show/activate
                        notify();
                    }
                }
                return IntPtr.Zero;
            };
            window.SourceInitialized += delegate
            {
                source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
                if (source != null) source.AddHook(hook);
            };
            window.Closed += delegate
            {
                if (source != null && !source.IsDisposed) source.RemoveHook(hook);
                source = null;
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPosition
        {
            public IntPtr Window;
            public IntPtr InsertAfter;
            public int X;
            public int Y;
            public int Width;
            public int Height;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);
    }
}
