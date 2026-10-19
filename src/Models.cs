using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BluetoothBatteryMonitor
{
    internal sealed class SelectionOption<T>
    {
        public T Value { get; private set; }
        public string Label { get; private set; }

        public SelectionOption(T value, string label)
        {
            Value = value;
            Label = label;
        }

        public override string ToString()
        {
            return Label;
        }
    }

    internal static class FloatingAppearanceLimits
    {
        public const int DefaultWidth = 196;
        public const int DefaultHeight = 66;
        public const int DefaultOpacityPercent = 100;
        public const int MinWidth = 160;
        public const int MaxWidth = 420;
        public const int MinHeight = 56;
        public const int MaxHeight = 160;

        public static int ClampWidth(int value)
        {
            return Math.Max(MinWidth, Math.Min(MaxWidth, value));
        }

        public static int ClampHeight(int value)
        {
            return Math.Max(MinHeight, Math.Min(MaxHeight, value));
        }

        public static int ClampOpacity(int value)
        {
            return Math.Max(1, Math.Min(100, value));
        }
    }

    internal static class FloatingColorDefaults
    {
        public const string Background = "#FFFFFF";
        public const string LowBattery = "#E04343";
        public const string Disconnected = "#3E4856";
    }

    internal enum DeviceKind
    {
        Unknown,
        Mouse,
        Keyboard,
        Headphones,
        Gamepad,
        Other,
        Pen,
        Speaker,
        Phone
    }

    internal enum DeviceTransport
    {
        Bluetooth = 0,
        Receiver24G = 1
    }

    internal sealed class BluetoothDeviceBattery
    {
        public string StableId { get; set; }
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public string DetectedName { get; set; }
        public string Address { get; set; }
        public DeviceTransport Transport { get; set; }
        public bool IsLowEnergy { get; set; }
        public bool IsConnected { get; set; }
        public bool IsPaired { get; set; }
        public int? BatteryPercent { get; set; }
        public bool IsCharging { get; set; }
        public string BatterySource { get; set; }
        public string Error { get; set; }
        public DeviceKind Kind { get; set; }
        public DeviceKind DetectedKind { get; set; }
        public DateTime LastRefreshedAt { get; set; }
        public int? LeftBatteryPercent { get; set; }
        public int? RightBatteryPercent { get; set; }
        public int? CaseBatteryPercent { get; set; }
        public bool EarbudSidesInferred { get; set; }

        public bool EarbudSidesConfirmed { get; set; }

        public BluetoothDeviceBattery()
        {
            StableId = string.Empty;
            DeviceId = string.Empty;
            Name = "未命名蓝牙设备";
            DetectedName = "未命名蓝牙设备";
            Address = string.Empty;
            BatterySource = string.Empty;
            Error = string.Empty;
            Transport = DeviceTransport.Bluetooth;
            Kind = DeviceKind.Unknown;
            DetectedKind = DeviceKind.Unknown;
        }
    }

    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember]
        public int SettingsVersion { get; set; }

        [DataMember]
        public bool ConnectedOnly { get; set; }

        [DataMember]
        public bool AlwaysOnTop { get; set; }

        [DataMember]
        public bool AutoRefresh { get; set; }

        [DataMember]
        public int RefreshSeconds { get; set; }

        [DataMember]
        public bool StartWithWindows { get; set; }

        [DataMember]
        public bool MergeFloatingWindows { get; set; }

        [DataMember]
        public double MergedLeft { get; set; }

        [DataMember]
        public double MergedTop { get; set; }

        [DataMember]
        public string MergedMonitorDeviceName { get; set; }

        [DataMember]
        public string MergedMonitorIdentity { get; set; }

        [DataMember]
        public int MergedMonitorWorkWidth { get; set; }

        [DataMember]
        public int MergedMonitorWorkHeight { get; set; }

        [DataMember]
        public double MergedMonitorCenterXRatio { get; set; }

        [DataMember]
        public double MergedMonitorCenterYRatio { get; set; }

        [DataMember]
        public bool MergedAlwaysOnTop { get; set; }

        [DataMember]
        public bool MergedLocked { get; set; }

        [DataMember]
        public int MergedOpacityPercent { get; set; }

        [DataMember]
        public int LowBatteryThreshold { get; set; }

        [DataMember]
        public List<DevicePreference> Devices { get; set; }

        public AppSettings()
        {
            SettingsVersion = 14;
            ConnectedOnly = false;
            AlwaysOnTop = true;
            AutoRefresh = true;
            RefreshSeconds = 600;
            StartWithWindows = false;
            MergeFloatingWindows = false;
            MergedLeft = double.NaN;
            MergedTop = double.NaN;
            MergedMonitorDeviceName = string.Empty;
            MergedMonitorIdentity = string.Empty;
            MergedMonitorCenterXRatio = double.NaN;
            MergedMonitorCenterYRatio = double.NaN;
            MergedAlwaysOnTop = true;
            MergedLocked = false;
            MergedOpacityPercent = 100;
            LowBatteryThreshold = 20;
            Devices = new List<DevicePreference>();
        }
    }

    [DataContract]
    internal sealed class DevicePreference
    {
        [DataMember]
        public string StableId { get; set; }

        [DataMember]
        public string LastKnownName { get; set; }

        [DataMember]
        public string LastKnownAddress { get; set; }

        [DataMember]
        public string LastKnownDeviceId { get; set; }

        [DataMember]
        public bool LastKnownIsLowEnergy { get; set; }

        [DataMember]
        public DeviceTransport LastKnownTransport { get; set; }

        [DataMember]
        public DeviceKind LastKnownKind { get; set; }

        [DataMember]
        public string CustomName { get; set; }

        [DataMember]
        public bool HasCustomKind { get; set; }

        [DataMember]
        public DeviceKind CustomKind { get; set; }

        [DataMember]
        public bool IsFloating { get; set; }

        [DataMember]
        public bool KeepFloatingWhenDisconnected { get; set; }

        [DataMember]
        public bool AlwaysOnTop { get; set; }

        [DataMember]
        public bool FloatingLocked { get; set; }

        [DataMember]
        public bool IncludeInMergedFloating { get; set; }

        [DataMember]
        public bool IsHidden { get; set; }

        [DataMember]
        public int MergedDisplayOrder { get; set; }

        [DataMember]
        public bool IsExpanded { get; set; }

        [DataMember]
        public bool AutoRefresh { get; set; }

        [DataMember]
        public int RefreshSeconds { get; set; }

        [DataMember]
        public int LowBatteryThreshold { get; set; }

        [DataMember]
        public string CustomIconPath { get; set; }

        [DataMember]
        public double Left { get; set; }

        [DataMember]
        public double Top { get; set; }

        [DataMember]
        public string MonitorDeviceName { get; set; }

        [DataMember]
        public string MonitorIdentity { get; set; }

        [DataMember]
        public int MonitorWorkWidth { get; set; }

        [DataMember]
        public int MonitorWorkHeight { get; set; }

        [DataMember]
        public double MonitorCenterXRatio { get; set; }

        [DataMember]
        public double MonitorCenterYRatio { get; set; }

        [DataMember]
        public int FloatingWidth { get; set; }

        [DataMember]
        public int FloatingHeight { get; set; }

        [DataMember]
        public int OpacityPercent { get; set; }

        [DataMember]
        public string BackgroundColor { get; set; }

        [DataMember]
        public string LowBatteryColor { get; set; }

        [DataMember]
        public string DisconnectedColor { get; set; }

        [DataMember]
        public long LastRefreshUtcTicks { get; set; }

        [DataMember]
        public int EarbudSampleA { get; set; }

        [DataMember]
        public int EarbudSampleB { get; set; }

        [DataMember]
        public long EarbudSamplesUtcTicks { get; set; }

        public DevicePreference()
        {
            StableId = string.Empty;
            LastKnownName = string.Empty;
            LastKnownAddress = string.Empty;
            LastKnownDeviceId = string.Empty;
            LastKnownKind = DeviceKind.Unknown;
            LastKnownTransport = DeviceTransport.Bluetooth;
            CustomName = string.Empty;
            CustomKind = DeviceKind.Unknown;
            CustomIconPath = string.Empty;
            AlwaysOnTop = true;
            FloatingLocked = false;
            KeepFloatingWhenDisconnected = true;
            IncludeInMergedFloating = true;
            IsHidden = false;
            MergedDisplayOrder = int.MaxValue;
            IsExpanded = false;
            AutoRefresh = true;
            RefreshSeconds = 600;
            LowBatteryThreshold = 20;
            Left = double.NaN;
            Top = double.NaN;
            MonitorDeviceName = string.Empty;
            MonitorIdentity = string.Empty;
            MonitorCenterXRatio = double.NaN;
            MonitorCenterYRatio = double.NaN;
            FloatingWidth = FloatingAppearanceLimits.DefaultWidth;
            FloatingHeight = FloatingAppearanceLimits.DefaultHeight;
            OpacityPercent = FloatingAppearanceLimits.DefaultOpacityPercent;
            BackgroundColor = FloatingColorDefaults.Background;
            LowBatteryColor = FloatingColorDefaults.LowBattery;
            DisconnectedColor = FloatingColorDefaults.Disconnected;
            EarbudSampleA = -1;
            EarbudSampleB = -1;
        }
    }

    internal static class MergedDeviceOrdering
    {
        public static bool Move(
            AppSettings settings,
            IList<DevicePreference> candidates,
            DevicePreference source,
            DevicePreference target,
            bool placeAfter)
        {
            if (settings == null || candidates == null || source == null || target == null ||
                ReferenceEquals(source, target))
            {
                return false;
            }
            int sourceIndex = candidates.IndexOf(source);
            if (sourceIndex < 0 || candidates.IndexOf(target) < 0) return false;
            candidates.RemoveAt(sourceIndex);
            int targetIndex = candidates.IndexOf(target);
            int insertIndex = placeAfter ? targetIndex + 1 : targetIndex;
            candidates.Insert(Math.Max(0, Math.Min(candidates.Count, insertIndex)), source);

            int order = 0;
            foreach (DevicePreference preference in candidates)
            {
                preference.MergedDisplayOrder = order++;
            }
            List<DevicePreference> hidden = settings.Devices.FindAll(delegate(DevicePreference preference)
            {
                return preference.IsHidden;
            });
            hidden.Sort(delegate(DevicePreference left, DevicePreference right)
            {
                return left.MergedDisplayOrder.CompareTo(right.MergedDisplayOrder);
            });
            foreach (DevicePreference preference in hidden)
            {
                preference.MergedDisplayOrder = order++;
            }
            return true;
        }
    }
}
