using System;
using System.Collections.Generic;

namespace BluetoothBatteryMonitor
{
    internal static class EarbudSampleInference
    {
        private const string ConfirmedLeftRightDeviceId = "";

        public static void Apply(
            BluetoothDeviceBattery device,
            DevicePreference preference,
            IList<int> historicValues,
            DateTime nowUtc)
        {
            if (device == null || preference == null || !device.BatteryPercent.HasValue) return;

            DateTime previousSampleTime;
            bool validPreviousTime = preference.EarbudSamplesUtcTicks > 0;
            try { previousSampleTime = new DateTime(preference.EarbudSamplesUtcTicks, DateTimeKind.Utc); }
            catch { validPreviousTime = false; previousSampleTime = default(DateTime); }
            if (!validPreviousTime || nowUtc - previousSampleTime > TimeSpan.FromHours(24))
            {
                preference.EarbudSampleA = -1;
                preference.EarbudSampleB = -1;
            }

            AddSample(preference, device.BatteryPercent.Value);
            if (historicValues != null)
            {
                foreach (int historicValue in historicValues) AddSample(preference, historicValue);
            }
            preference.EarbudSamplesUtcTicks = nowUtc.Ticks;

            if (preference.EarbudSampleA < 0 || preference.EarbudSampleB < 0 ||
                preference.EarbudSampleA == preference.EarbudSampleB)
            {
                return;
            }

            // 公开版本不内置任何个人设备地址或专属左右耳映射。
            // 只对这个稳定设备 ID 使用左右耳标识，其他 HFP 设备仍保持中性标签。
            device.LeftBatteryPercent = preference.EarbudSampleA;
            device.RightBatteryPercent = preference.EarbudSampleB;
            device.BatteryPercent = null;
            device.EarbudSidesConfirmed = string.Equals(
                device.StableId,
                ConfirmedLeftRightDeviceId,
                StringComparison.OrdinalIgnoreCase);
            device.EarbudSidesInferred = !device.EarbudSidesConfirmed;
            device.BatterySource = device.EarbudSidesConfirmed
                ? "Windows HFP/PnP 双值采样（左右耳已确认）"
                : "Windows HFP/PnP 双值采样（未区分左右）";
            device.Error = string.Empty;
        }

        private static void AddSample(DevicePreference preference, int value)
        {
            if (value < 0 || value > 100) return;
            if (preference.EarbudSampleA < 0)
            {
                preference.EarbudSampleA = value;
                return;
            }
            if (preference.EarbudSampleA == value || preference.EarbudSampleB == value) return;
            if (preference.EarbudSampleB < 0)
            {
                preference.EarbudSampleB = value;
                return;
            }

            int distanceA = Math.Abs(preference.EarbudSampleA - value);
            int distanceB = Math.Abs(preference.EarbudSampleB - value);
            if (distanceA <= distanceB) preference.EarbudSampleA = value;
            else preference.EarbudSampleB = value;
        }
    }
}
