using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BluetoothBatteryMonitor
{
    internal sealed class SettingsStore
    {
        private readonly string _dataDirectory;
        private readonly string _settingsPath;
        private readonly string _signaturePath;
        private static readonly byte[] IntegrityKey =
        {
            0x42, 0x6C, 0x75, 0x65, 0x50, 0x75, 0x6C, 0x73,
            0x65, 0x2D, 0x79, 0x75, 0x62, 0x61, 0x6E, 0x2D,
            0x32, 0x30, 0x32, 0x36, 0x2D, 0x49, 0x6E, 0x74,
            0x65, 0x67, 0x72, 0x69, 0x74, 0x79, 0x21, 0x5A
        };

        public SettingsStore()
        {
            string executableDirectory = RuntimeCompat.PortableDirectory;
            string preferredDataDirectory = Path.Combine(executableDirectory, "data");

            try
            {
                Directory.CreateDirectory(preferredDataDirectory);
                string probe = Path.Combine(preferredDataDirectory, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                _dataDirectory = preferredDataDirectory;
            }
            catch
            {
                _dataDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BluetoothBatteryMonitor");
                Directory.CreateDirectory(_dataDirectory);
            }

            _settingsPath = Path.Combine(_dataDirectory, "settings.json");
            _signaturePath = Path.Combine(_dataDirectory, "settings.sig");
        }

        public string DataDirectory
        {
            get { return _dataDirectory; }
        }

        public string IntegrityWarning { get; private set; }

        public AppSettings Load()
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            try
            {
                byte[] settingsBytes = File.ReadAllBytes(_settingsPath);
                if (File.Exists(_signaturePath))
                {
                    string expectedSignature = File.ReadAllText(_signaturePath).Trim();
                    string actualSignature = ComputeSignature(settingsBytes);
                    if (!FixedTimeEquals(expectedSignature, actualSignature))
                    {
                        string backupPath = Path.Combine(
                            _dataDirectory,
                            "settings.tampered-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                        File.Copy(_settingsPath, backupPath, true);
                        IntegrityWarning = "检测到本地设置文件被外部修改，已阻止加载并保留备份。";
                        return new AppSettings();
                    }
                }

                using (MemoryStream stream = new MemoryStream(settingsBytes, false))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    AppSettings settings = serializer.ReadObject(stream) as AppSettings;
                    if (settings == null)
                    {
                        return new AppSettings();
                    }

                    if (settings.Devices == null)
                    {
                        settings.Devices = new List<DevicePreference>();
                    }
                    if (settings.SettingsVersion < 2)
                    {
                        settings.ConnectedOnly = false;
                        settings.SettingsVersion = 2;
                    }
                    settings.RefreshSeconds = NormalizeRefreshSeconds(settings.RefreshSeconds);
                    if (settings.SettingsVersion < 3)
                    {
                        foreach (DevicePreference preference in settings.Devices)
                        {
                            preference.AutoRefresh = settings.AutoRefresh;
                            preference.RefreshSeconds = settings.RefreshSeconds;
                        }
                        settings.MergeFloatingWindows = false;
                        settings.MergedLeft = double.NaN;
                        settings.MergedTop = double.NaN;
                        settings.SettingsVersion = 3;
                    }
                    if (settings.SettingsVersion < 4)
                    {
                        foreach (DevicePreference preference in settings.Devices)
                        {
                            preference.AlwaysOnTop = settings.AlwaysOnTop;
                            preference.FloatingLocked = false;
                        }
                        settings.MergedAlwaysOnTop = settings.AlwaysOnTop;
                        settings.MergedLocked = false;
                        settings.MergedOpacityPercent = 100;
                        settings.SettingsVersion = 4;
                    }
                    if (settings.SettingsVersion < 5)
                    {
                        foreach (DevicePreference preference in settings.Devices)
                        {
                            preference.IncludeInMergedFloating = true;
                            preference.IsExpanded = false;
                            preference.DisconnectedColor = FloatingColorDefaults.Disconnected;
                        }
                        settings.SettingsVersion = 5;
                    }
                    if (settings.SettingsVersion < 6)
                    {
                        foreach (DevicePreference preference in settings.Devices)
                        {
                            preference.LowBatteryThreshold = settings.LowBatteryThreshold;
                        }
                        settings.SettingsVersion = 6;
                    }
                    if (settings.SettingsVersion < 7)
                    {
                        foreach (DevicePreference preference in settings.Devices)
                        {
                            preference.KeepFloatingWhenDisconnected = true;
                        }
                        settings.SettingsVersion = 7;
                    }
                    if (settings.SettingsVersion < 8)
                    {
                        settings.RefreshSeconds = NormalizeRefreshSeconds(settings.RefreshSeconds);
                        foreach (DevicePreference preference in settings.Devices)
                        {
                            preference.RefreshSeconds = NormalizeRefreshSeconds(preference.RefreshSeconds);
                        }
                        settings.SettingsVersion = 8;
                    }
                    if (settings.SettingsVersion < 9)
                    {
                        foreach (DevicePreference preference in settings.Devices)
                        {
                            preference.EarbudSampleA = -1;
                            preference.EarbudSampleB = -1;
                            preference.EarbudSamplesUtcTicks = 0;
                        }
                        settings.SettingsVersion = 9;
                    }
                    if (settings.SettingsVersion < 10)
                    {
                        // v0.1 旧设置中只有蓝牙设备，缺省值即蓝牙；
                        // 从这一版起开始持久化 2.4G 接收器设备的传输类型。
                        foreach (DevicePreference preference in settings.Devices)
                        {
                            preference.LastKnownTransport = DeviceTransport.Bluetooth;
                        }
                        settings.SettingsVersion = 10;
                    }
                    if (settings.SettingsVersion < 11)
                    {
                        foreach (DevicePreference preference in settings.Devices)
                        {
                            preference.IsHidden = false;
                        }
                        settings.SettingsVersion = 11;
                    }
                    if (settings.SettingsVersion < 12)
                    {
                        for (int index = 0; index < settings.Devices.Count; index++)
                        {
                            settings.Devices[index].MergedDisplayOrder = index;
                        }
                        settings.SettingsVersion = 12;
                    }
                    if (settings.SettingsVersion < 13)
                    {
                        settings.MergedMonitorDeviceName = string.Empty;
                        settings.MergedMonitorWorkWidth = 0;
                        settings.MergedMonitorWorkHeight = 0;
                        settings.MergedMonitorCenterXRatio = double.NaN;
                        settings.MergedMonitorCenterYRatio = double.NaN;
                        foreach (DevicePreference preference in settings.Devices)
                        {
                            preference.MonitorDeviceName = string.Empty;
                            preference.MonitorWorkWidth = 0;
                            preference.MonitorWorkHeight = 0;
                            preference.MonitorCenterXRatio = double.NaN;
                            preference.MonitorCenterYRatio = double.NaN;
                        }
                        settings.SettingsVersion = 13;
                    }
                    if (settings.SettingsVersion < 14)
                    {
                        settings.MergedMonitorIdentity = string.Empty;
                        foreach (DevicePreference preference in settings.Devices)
                            preference.MonitorIdentity = string.Empty;
                        settings.SettingsVersion = 14;
                    }
                    if (settings.LowBatteryThreshold < 1 || settings.LowBatteryThreshold > 99)
                    {
                        settings.LowBatteryThreshold = 20;
                    }
                    foreach (DevicePreference preference in settings.Devices)
                    {
                        NormalizePreference(preference);
                    }
                    settings.MergedOpacityPercent = FloatingAppearanceLimits.ClampOpacity(
                        settings.MergedOpacityPercent == 0 ? 100 : settings.MergedOpacityPercent);
                    if (settings.MergedMonitorDeviceName == null) settings.MergedMonitorDeviceName = string.Empty;
                    if (settings.MergedMonitorIdentity == null) settings.MergedMonitorIdentity = string.Empty;
                    settings.MergedMonitorCenterXRatio = NormalizePlacementRatio(settings.MergedMonitorCenterXRatio);
                    settings.MergedMonitorCenterYRatio = NormalizePlacementRatio(settings.MergedMonitorCenterYRatio);
                    if (!File.Exists(_signaturePath)) Save(settings);
                    return settings;
                }
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(_dataDirectory);
                string temporaryPath = _settingsPath + ".tmp";
                string temporarySignaturePath = _signaturePath + ".tmp";
                byte[] serialized;
                using (MemoryStream stream = new MemoryStream())
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    serializer.WriteObject(stream, settings);
                    serialized = stream.ToArray();
                }
                File.WriteAllBytes(temporaryPath, serialized);
                File.WriteAllText(temporarySignaturePath, ComputeSignature(serialized), new UTF8Encoding(false));

                if (File.Exists(_settingsPath))
                {
                    File.Delete(_settingsPath);
                }
                if (File.Exists(_signaturePath)) File.Delete(_signaturePath);
                File.Move(temporaryPath, _settingsPath);
                File.Move(temporarySignaturePath, _signaturePath);
            }
            catch
            {
                // 设置保存失败不应影响电量查看。
            }
        }

        private static string ComputeSignature(byte[] data)
        {
            using (HMACSHA256 hmac = new HMACSHA256(IntegrityKey))
            {
                byte[] hash = hmac.ComputeHash(data);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) text.Append(value.ToString("X2"));
                return text.ToString();
            }
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }

        public DevicePreference GetOrCreatePreference(AppSettings settings, BluetoothDeviceBattery device)
        {
            DevicePreference preference = settings.Devices.Find(
                delegate(DevicePreference item)
                {
                    return string.Equals(item.StableId, device.StableId, StringComparison.OrdinalIgnoreCase);
                });

            if (preference == null)
            {
                preference = new DevicePreference
                {
                    StableId = device.StableId,
                    LastKnownName = device.Name,
                    MergedDisplayOrder = NextMergedDisplayOrder(settings)
                };
                settings.Devices.Add(preference);
            }
            else
            {
                preference.LastKnownName = string.IsNullOrWhiteSpace(device.DetectedName)
                    ? device.Name
                    : device.DetectedName;
            }
            preference.LastKnownAddress = device.Address ?? string.Empty;
            preference.LastKnownDeviceId = device.DeviceId ?? string.Empty;
            preference.LastKnownIsLowEnergy = device.IsLowEnergy;
            preference.LastKnownTransport = device.Transport;
            preference.LastKnownKind = device.DetectedKind == DeviceKind.Unknown ? device.Kind : device.DetectedKind;
            NormalizePreference(preference);
            return preference;
        }

        private static int NextMergedDisplayOrder(AppSettings settings)
        {
            int highest = -1;
            foreach (DevicePreference preference in settings.Devices)
            {
                if (preference.MergedDisplayOrder >= 0 &&
                    preference.MergedDisplayOrder < int.MaxValue &&
                    preference.MergedDisplayOrder > highest)
                {
                    highest = preference.MergedDisplayOrder;
                }
            }
            return highest == int.MaxValue - 1 ? int.MaxValue - 1 : highest + 1;
        }

        public static int NormalizeRefreshSeconds(int seconds)
        {
            int[] allowed = { 600, 1800, 3600, 10800, 18000 };
            int best = allowed[0];
            int bestDistance = Math.Abs(seconds - best);
            foreach (int candidate in allowed)
            {
                int distance = Math.Abs(seconds - candidate);
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static void NormalizePreference(DevicePreference preference)
        {
            if (preference == null) return;
            if (preference.MergedDisplayOrder < 0) preference.MergedDisplayOrder = int.MaxValue;
            if (preference.FloatingWidth == 0)
            {
                preference.FloatingWidth = FloatingAppearanceLimits.DefaultWidth;
            }
            if (preference.FloatingHeight == 0)
            {
                preference.FloatingHeight = FloatingAppearanceLimits.DefaultHeight;
            }
            if (preference.OpacityPercent == 0)
            {
                preference.OpacityPercent = FloatingAppearanceLimits.DefaultOpacityPercent;
            }
            preference.FloatingWidth = FloatingAppearanceLimits.ClampWidth(preference.FloatingWidth);
            preference.FloatingHeight = FloatingAppearanceLimits.ClampHeight(preference.FloatingHeight);
            preference.OpacityPercent = FloatingAppearanceLimits.ClampOpacity(preference.OpacityPercent);
            preference.RefreshSeconds = NormalizeRefreshSeconds(preference.RefreshSeconds == 0 ? 600 : preference.RefreshSeconds);
            if (preference.LowBatteryThreshold < 1 || preference.LowBatteryThreshold > 99)
            {
                preference.LowBatteryThreshold = 20;
            }
            if (preference.CustomName == null) preference.CustomName = string.Empty;
            if (preference.CustomName.Length > 20) preference.CustomName = preference.CustomName.Substring(0, 20);
            if (preference.LastKnownName == null) preference.LastKnownName = string.Empty;
            if (preference.LastKnownAddress == null) preference.LastKnownAddress = string.Empty;
            if (preference.LastKnownDeviceId == null) preference.LastKnownDeviceId = string.Empty;
            if (preference.CustomIconPath == null) preference.CustomIconPath = string.Empty;
            if (preference.MonitorDeviceName == null) preference.MonitorDeviceName = string.Empty;
            if (preference.MonitorIdentity == null) preference.MonitorIdentity = string.Empty;
            preference.MonitorCenterXRatio = NormalizePlacementRatio(preference.MonitorCenterXRatio);
            preference.MonitorCenterYRatio = NormalizePlacementRatio(preference.MonitorCenterYRatio);
            if (preference.EarbudSampleA < -1 || preference.EarbudSampleA > 100) preference.EarbudSampleA = -1;
            if (preference.EarbudSampleB < -1 || preference.EarbudSampleB > 100) preference.EarbudSampleB = -1;
            if (preference.EarbudSampleA == preference.EarbudSampleB) preference.EarbudSampleB = -1;
            if (preference.HasCustomKind && !IsSupportedKind(preference.CustomKind))
            {
                preference.HasCustomKind = false;
                preference.CustomKind = DeviceKind.Unknown;
            }
            preference.BackgroundColor = NormalizeColor(preference.BackgroundColor, FloatingColorDefaults.Background);
            preference.LowBatteryColor = NormalizeColor(preference.LowBatteryColor, FloatingColorDefaults.LowBattery);
            preference.DisconnectedColor = NormalizeColor(preference.DisconnectedColor, FloatingColorDefaults.Disconnected);
        }

        private static double NormalizePlacementRatio(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1
                ? double.NaN
                : value;
        }

        private static bool IsSupportedKind(DeviceKind kind)
        {
            return kind == DeviceKind.Mouse || kind == DeviceKind.Keyboard ||
                   kind == DeviceKind.Headphones || kind == DeviceKind.Gamepad ||
                   kind == DeviceKind.Other;
        }

        private static string NormalizeColor(string value, string fallback)
        {
            string candidate = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (Regex.IsMatch(candidate, "^#[0-9A-F]{6}$")) return candidate;
            return fallback;
        }

        public string ImportIcon(string sourcePath, string stableId)
        {
            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            string iconDirectory = Path.Combine(_dataDirectory, "icons");
            Directory.CreateDirectory(iconDirectory);
            string targetPath = Path.Combine(iconDirectory, Hash(stableId) + extension);
            File.Copy(sourcePath, targetPath, true);
            return targetPath;
        }

        public void DeleteImportedIcon(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                string fullPath = Path.GetFullPath(path);
                string iconDirectory = Path.GetFullPath(Path.Combine(_dataDirectory, "icons"));
                string prefix = iconDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch
            {
            }
        }

        public void WriteScanError(string message)
        {
            try
            {
                Directory.CreateDirectory(_dataDirectory);
                File.WriteAllText(
                    Path.Combine(_dataDirectory, "last-error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + message,
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        public void ClearScanError()
        {
            try
            {
                string path = Path.Combine(_dataDirectory, "last-error.log");
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                StringBuilder builder = new StringBuilder();
                for (int index = 0; index < 8; index++)
                {
                    builder.Append(bytes[index].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
