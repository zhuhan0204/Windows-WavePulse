using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace BluetoothBatteryMonitor
{
    internal sealed class DiagnosticLogger
    {
        private readonly object _sync = new object();
        private readonly string _logPath;

        public DiagnosticLogger(string dataDirectory)
        {
            _logPath = Path.Combine(dataDirectory, "diagnostic.log");
            string legacyPrevious = Path.Combine(dataDirectory, "diagnostic-previous.log");
            try { if (File.Exists(legacyPrevious)) File.Delete(legacyPrevious); }
            catch { }
            PruneToLastDay();
        }

        public string LogPath
        {
            get { return _logPath; }
        }

        public void LogStartup()
        {
            Append(
                "程序启动 | Version=" + GetVersion() +
                " | OS=" + Environment.OSVersion.VersionString +
                " | Runtime=" + Environment.Version +
                " | 64Bit=" + Environment.Is64BitProcess +
                " | Exe=" + RuntimeCompat.ExecutablePath);
        }

        public void LogScan(string readerDetails, IList<BluetoothDeviceBattery> devices)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("扫描完成 | DeviceCount=" + (devices == null ? 0 : devices.Count));
            if (!string.IsNullOrWhiteSpace(readerDetails))
            {
                builder.AppendLine(readerDetails.TrimEnd());
            }
            if (devices != null && devices.Count > 0)
            {
                builder.AppendLine("应用解析后的设备电量快照：");
                foreach (BluetoothDeviceBattery device in devices)
                {
                    builder.AppendLine(
                        "  DeviceBattery | StableId=" + device.StableId +
                        " | Name=" + device.Name +
                        " | Connected=" + device.IsConnected +
                        " | Charging=" + device.IsCharging +
                        " | MainBattery=" + BatteryValue(device.BatteryPercent) +
                        " | LeftEarBattery=" + BatteryValue(device.LeftBatteryPercent) +
                        " | RightEarBattery=" + BatteryValue(device.RightBatteryPercent) +
                        " | CaseBattery=" + BatteryValue(device.CaseBatteryPercent) +
                        " | EarbudSidesConfirmed=" + device.EarbudSidesConfirmed +
                        " | EarbudSidesInferred=" + device.EarbudSidesInferred +
                        " | Source=" + device.BatterySource);
                }
            }
            Append(builder.ToString().TrimEnd());
        }

        private static string BatteryValue(int? value)
        {
            return value.HasValue ? value.Value + "%" : "null";
        }

        public void LogError(string context, Exception exception)
        {
            Append("错误 | Context=" + context + Environment.NewLine + (exception == null ? "未知错误" : exception.ToString()));
        }

        public void LogMessage(string message)
        {
            Append(message);
        }

        public IList<int> GetRecentHfpBatterySamples(string address)
        {
            List<int> values = new List<int>();
            if (string.IsNullOrWhiteSpace(address)) return values;
            try
            {
                lock (_sync)
                {
                    PruneToLastDay();
                    if (!File.Exists(_logPath)) return values;
                    string content = File.ReadAllText(_logPath, Encoding.UTF8);
                    MatchCollection matches = Regex.Matches(
                        content,
                        @"HFP/PnP 电量节点 \|[^\r\n]*?Battery=(?<battery>\d{1,3}) \| Address=" +
                        Regex.Escape(address),
                        RegexOptions.IgnoreCase);
                    for (int index = matches.Count - 1; index >= 0 && values.Count < 4; index--)
                    {
                        int value;
                        if (!int.TryParse(matches[index].Groups["battery"].Value, out value) || value < 0 || value > 100)
                        {
                            continue;
                        }
                        if (!values.Contains(value)) values.Add(value);
                    }
                }
            }
            catch
            {
            }
            return values;
        }

        public void Export(
            string destinationPath,
            IList<BluetoothDeviceBattery> devices,
            AppSettings settings,
            string latestReaderDetails)
        {
            StringBuilder export = new StringBuilder();
            export.AppendLine("WavePulse 无线设备电量助手诊断日志");
            export.AppendLine("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            export.AppendLine("程序版本：" + GetVersion());
            export.AppendLine("系统：" + Environment.OSVersion.VersionString);
            export.AppendLine("运行时：" + Environment.Version);
            export.AppendLine("程序路径：" + RuntimeCompat.ExecutablePath);
            export.AppendLine("数据目录：" + Path.GetDirectoryName(_logPath));
            export.AppendLine(
                "合并悬浮：" + settings.MergeFloatingWindows +
                " | Topmost=" + settings.MergedAlwaysOnTop +
                " | Locked=" + settings.MergedLocked +
                " | Opacity=" + settings.MergedOpacityPercent + "%" +
                " | Monitor=" + settings.MergedMonitorDeviceName +
                " | MonitorWork=" + settings.MergedMonitorWorkWidth + "x" + settings.MergedMonitorWorkHeight +
                " | MonitorCenter=" + settings.MergedMonitorCenterXRatio + "," + settings.MergedMonitorCenterYRatio);
            export.AppendLine();
            export.AppendLine("========== 当前设备快照 ==========");
            if (devices == null || devices.Count == 0)
            {
                export.AppendLine("当前没有扫描结果。");
            }
            else
            {
                foreach (BluetoothDeviceBattery device in devices)
                {
                    DevicePreference preference = settings.Devices.Find(delegate(DevicePreference item)
                    {
                        return string.Equals(item.StableId, device.StableId, StringComparison.OrdinalIgnoreCase);
                    });
                    export.AppendLine(
                        "StableId=" + device.StableId +
                        " | DisplayName=" + device.Name +
                        " | DetectedName=" + device.DetectedName +
                        " | Kind=" + device.Kind +
                        " | Transport=" + device.Transport +
                        " | Connected=" + device.IsConnected +
                        " | Charging=" + device.IsCharging +
                        " | Paired=" + device.IsPaired +
                        " | BLE=" + device.IsLowEnergy +
                        " | Battery=" + (device.BatteryPercent.HasValue ? device.BatteryPercent.Value.ToString() : "null") +
                        " | LeftBattery=" + (device.LeftBatteryPercent.HasValue ? device.LeftBatteryPercent.Value.ToString() : "null") +
                        " | RightBattery=" + (device.RightBatteryPercent.HasValue ? device.RightBatteryPercent.Value.ToString() : "null") +
                        " | CaseBattery=" + (device.CaseBatteryPercent.HasValue ? device.CaseBatteryPercent.Value.ToString() : "null") +
                        " | LastRefreshedAt=" + (device.LastRefreshedAt == default(DateTime) ? "null" : device.LastRefreshedAt.ToString("yyyy-MM-dd HH:mm:ss.fff zzz")) +
                        " | Source=" + device.BatterySource +
                        " | AutoRefresh=" + (preference == null ? "unknown" : preference.AutoRefresh.ToString()) +
                        " | RefreshSeconds=" + (preference == null ? "unknown" : preference.RefreshSeconds.ToString()) +
                        " | EarbudSampleA=" + (preference == null ? "unknown" : preference.EarbudSampleA.ToString()) +
                        " | EarbudSampleB=" + (preference == null ? "unknown" : preference.EarbudSampleB.ToString()) +
                        " | FloatingTopmost=" + (preference == null ? "unknown" : preference.AlwaysOnTop.ToString()) +
                        " | FloatingLocked=" + (preference == null ? "unknown" : preference.FloatingLocked.ToString()) +
                        " | FloatingMonitor=" + (preference == null ? "unknown" : preference.MonitorDeviceName) +
                        " | FloatingMonitorWork=" + (preference == null ? "unknown" : preference.MonitorWorkWidth + "x" + preference.MonitorWorkHeight) +
                        " | FloatingMonitorCenter=" + (preference == null ? "unknown" : preference.MonitorCenterXRatio + "," + preference.MonitorCenterYRatio) +
                        " | IncludedInMerge=" + (preference == null ? "unknown" : preference.IncludeInMergedFloating.ToString()) +
                        " | Hidden=" + (preference == null ? "unknown" : preference.IsHidden.ToString()) +
                        " | MergedDisplayOrder=" + (preference == null ? "unknown" : preference.MergedDisplayOrder.ToString()) +
                        " | LowBatteryThreshold=" + (preference == null ? "unknown" : preference.LowBatteryThreshold.ToString()) +
                        " | DisconnectedColor=" + (preference == null ? "unknown" : preference.DisconnectedColor) +
                        " | Address=" + device.Address +
                        " | DeviceId=" + device.DeviceId +
                        " | Error=" + device.Error);
                }
            }
            export.AppendLine();
            export.AppendLine("========== 最近一次底层候选扫描（含未适配设备） ==========");
            export.AppendLine(string.IsNullOrWhiteSpace(latestReaderDetails) ? "没有底层扫描记录。" : latestReaderDetails.TrimEnd());
            export.AppendLine();
            export.AppendLine("========== 程序运行日志 ==========");
            lock (_sync)
            {
                PruneToLastDay();
                if (File.Exists(_logPath))
                {
                    export.AppendLine(File.ReadAllText(_logPath, Encoding.UTF8));
                }
            }
            File.WriteAllText(destinationPath, export.ToString(), new UTF8Encoding(true));
        }

        private void Append(string message)
        {
            try
            {
                lock (_sync)
                {
                    string directory = Path.GetDirectoryName(_logPath);
                    if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                    PruneToLastDay();
                    File.AppendAllText(
                        _logPath,
                        "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " + message + Environment.NewLine + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        private void PruneToLastDay()
        {
            try
            {
                if (!File.Exists(_logPath)) return;
                string content = File.ReadAllText(_logPath, Encoding.UTF8);
                MatchCollection entries = Regex.Matches(
                    content,
                    @"(?ms)^\[(?<stamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\] .*?(?=^\[\d{4}-\d{2}-\d{2} |\z)");
                DateTime cutoff = DateTime.Now.AddDays(-1);
                StringBuilder retained = new StringBuilder();
                foreach (Match entry in entries)
                {
                    DateTime timestamp;
                    if (DateTime.TryParseExact(
                        entry.Groups["stamp"].Value,
                        "yyyy-MM-dd HH:mm:ss.fff",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out timestamp) && timestamp >= cutoff)
                    {
                        retained.AppendLine(entry.Value.TrimEnd());
                        retained.AppendLine();
                    }
                }
                File.WriteAllText(_logPath, retained.ToString(), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string GetVersion()
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null ? "未知" : version.ToString();
        }
    }
}
