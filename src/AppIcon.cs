using System;
using System.Windows.Media.Imaging;

namespace BluetoothBatteryMonitor
{
    internal static class AppIcon
    {
        private static BitmapImage _image;

        public static BitmapImage Image
        {
            get
            {
                if (_image == null)
                {
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri("pack://application:,,,/assets/app-icon.png", UriKind.Absolute);
                    image.EndInit();
                    image.Freeze();
                    _image = image;
                }
                return _image;
            }
        }
    }
}
