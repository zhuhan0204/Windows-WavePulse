using System;
using System.Diagnostics;
using System.IO;

namespace BluetoothBatteryMonitor
{
    internal static class RuntimeCompat
    {
        public static string LauncherPath
        {
            get
            {
                string outerLauncher = Environment.GetEnvironmentVariable("WAVEPULSE_LAUNCHER_PATH");
                if (string.IsNullOrWhiteSpace(outerLauncher))
                {
                    outerLauncher = Environment.GetEnvironmentVariable("BLUEPULSE_LAUNCHER_PATH");
                }
                return string.IsNullOrWhiteSpace(outerLauncher)
                    ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WavePulse 无线设备电量助手.exe")
                    : outerLauncher;
            }
        }

        public static string PortableDirectory
        {
            get
            {
                string directory = Environment.GetEnvironmentVariable("WAVEPULSE_PORTABLE_DIR");
                if (string.IsNullOrWhiteSpace(directory))
                {
                    directory = Environment.GetEnvironmentVariable("BLUEPULSE_PORTABLE_DIR");
                }
                if (!string.IsNullOrWhiteSpace(directory)) return directory;
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        public static string ExecutablePath
        {
            get
            {
                try
                {
                    using (Process process = Process.GetCurrentProcess())
                    {
                        if (process.MainModule != null && !string.IsNullOrWhiteSpace(process.MainModule.FileName))
                        {
                            return process.MainModule.FileName;
                        }
                    }
                }
                catch
                {
                }
                return LauncherPath;
            }
        }
    }
}
