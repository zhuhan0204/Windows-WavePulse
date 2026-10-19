using System.Collections.Generic;

namespace BluetoothBatteryMonitor
{
    internal sealed class ReceiverSupportBrand
    {
        public string Brand { get; set; }
        public IList<string> MouseModels { get; set; }
        public IList<string> KeyboardModels { get; set; }
        public IList<string> HeadphoneModels { get; set; }
    }

    internal static class ReceiverSupportCatalog
    {
        public static IList<ReceiverSupportBrand> GetDeviceGroups()
        {
            return new List<ReceiverSupportBrand>
            {
                new ReceiverSupportBrand
                {
                    Brand = "罗技",
                    MouseModels = new List<string> { "M750" },
                    KeyboardModels = new List<string>(),
                    HeadphoneModels = new List<string>()
                },
                new ReceiverSupportBrand
                {
                    Brand = "Compx",
                    MouseModels = new List<string> { "其他" },
                    KeyboardModels = new List<string>(),
                    HeadphoneModels = new List<string>()
                },
                new ReceiverSupportBrand
                {
                    Brand = "迈从",
                    MouseModels = new List<string>(),
                    KeyboardModels = new List<string> { "G98 V2" },
                    HeadphoneModels = new List<string>()
                },
                new ReceiverSupportBrand
                {
                    Brand = "前行者",
                    MouseModels = new List<string> { "S9 Pro" },
                    KeyboardModels = new List<string>(),
                    HeadphoneModels = new List<string>()
                }
            };
        }

        public const string UnknownBrandHint = "其他未知品牌可自行测试；如能识别电量，程序会自动加入 2.4G 设备列表。";
    }
}
