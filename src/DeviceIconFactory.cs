using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BluetoothBatteryMonitor
{
    internal static class DeviceIconFactory
    {
        public static FrameworkElement Create(DeviceKind kind, string customIconPath, double size, bool lightForeground)
        {
            if (!string.IsNullOrWhiteSpace(customIconPath) && File.Exists(customIconPath))
            {
                try
                {
                    BitmapImage bitmap = LoadBitmap(customIconPath);
                    return new Image
                    {
                        Source = bitmap,
                        Width = size,
                        Height = size,
                        Stretch = Stretch.Uniform
                    };
                }
                catch
                {
                }
            }

            return new TextBlock
            {
                Text = Theme.KindGlyph(kind),
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = size * 0.66,
                Foreground = Theme.Brush(lightForeground ? Colors.White : Theme.AccentColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
        }

        public static BitmapImage LoadBitmap(string path)
        {
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
