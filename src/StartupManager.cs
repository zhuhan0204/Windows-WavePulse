using System;
using Microsoft.Win32;

namespace BluetoothBatteryMonitor
{
    internal static class StartupManager
    {
        private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WavePulseWirelessBatteryAssistant";
        private const string LegacyValueName = "BluetoothBatteryMonitorPortable";

        public static bool TrySetEnabled(bool enabled, out string error)
        {
            error = string.Empty;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath, true))
                {
                    if (key == null) throw new InvalidOperationException("无法打开当前用户的启动项注册表。");
                    if (enabled)
                    {
                        string executablePath = RuntimeCompat.LauncherPath;
                        if (string.IsNullOrWhiteSpace(executablePath))
                        {
                            throw new InvalidOperationException("无法确定程序路径。");
                        }
                        key.SetValue(ValueName, "\"" + executablePath + "\" --background", RegistryValueKind.String);
                        key.DeleteValue(LegacyValueName, false);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                        key.DeleteValue(LegacyValueName, false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
