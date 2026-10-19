using System;
using System.Threading;
using System.Windows;

namespace BluetoothBatteryMonitor
{
    internal static class Program
    {
        private static Mutex _singleInstanceMutex;

        [STAThread]
        public static void Main()
        {
            bool startInBackground = HasCommandLineArgument("--background");
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "BluetoothBatteryMonitor.SingleInstance.2026", out createdNew);
            if (!createdNew)
            {
                if (!startInBackground)
                {
                    MessageBox.Show("WavePulse 无线设备电量助手已经在运行。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            MainWindow window = new MainWindow();
            app.MainWindow = window;
            app.SessionEnding += delegate(object sender, SessionEndingCancelEventArgs e) { window.RequestExit(); };
            if (!startInBackground)
            {
                window.Show();
            }
            window.QueueInitialization(startInBackground);
            app.Run();
            GC.KeepAlive(_singleInstanceMutex);
        }

        private static bool HasCommandLineArgument(string expected)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 1; index < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], expected, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                Exception ex = e.ExceptionObject as Exception;
                MessageBox.Show(
                    "程序遇到未处理的问题：\n\n" + (ex == null ? "未知错误" : ex.Message),
                    "WavePulse 无线设备电量助手",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
            }
        }
    }
}
