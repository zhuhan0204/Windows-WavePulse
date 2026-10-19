using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace BluetoothBatteryMonitor
{
    internal sealed class AppToast : Window
    {
        private AppToast(Window owner, string title, string message, Color accent, Color accentSoft, string glyph)
        {
            Title = title;
            Width = 340;
            Height = 82;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            if (owner != null && owner.IsVisible) Owner = owner;

            Border shadow = new Border
            {
                Margin = new Thickness(10),
                Background = Theme.Brush(Colors.White),
                BorderBrush = Theme.Brush(Color.FromRgb(210, 224, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(14, 11, 16, 11),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 3,
                    Opacity = 0.18,
                    Color = Color.FromRgb(34, 65, 105)
                }
            };
            Grid layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Border icon = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = Theme.Brush(accentSoft),
                Child = new TextBlock
                {
                    Text = glyph,
                    Foreground = Theme.Brush(accent),
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                VerticalAlignment = VerticalAlignment.Center
            };
            layout.Children.Add(icon);
            StackPanel text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Theme.Text(title, 11.5, FontWeights.SemiBold, Theme.TextColor));
            TextBlock detail = Theme.Text(message, 10, FontWeights.Normal, Theme.MutedColor);
            detail.Margin = new Thickness(0, 3, 0, 0);
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(detail);
            Grid.SetColumn(text, 1);
            layout.Children.Add(text);
            shadow.Child = layout;
            Content = shadow;

            Loaded += delegate
            {
                Rect area = SystemParameters.WorkArea;
                Left = area.Right - Width - 18;
                Top = area.Bottom - Height - 18;
            };
        }

        public static void Show(Window owner, string title, string message)
        {
            ShowCore(owner, title, message, Theme.SuccessColor, Color.FromRgb(223, 247, 237), "✓");
        }

        public static void ShowWarning(Window owner, string title, string message)
        {
            ShowCore(owner, title, message, Color.FromRgb(210, 117, 18), Color.FromRgb(255, 244, 219), "!");
        }

        public static void ShowError(Window owner, string title, string message)
        {
            ShowCore(owner, title, message, Theme.DangerColor, Theme.DangerSoftColor, "×");
        }

        private static void ShowCore(Window owner, string title, string message, Color accent, Color accentSoft, string glyph)
        {
            Dispatcher dispatcher = owner != null ? owner.Dispatcher : Application.Current.Dispatcher;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke((Action)delegate { ShowCore(owner, title, message, accent, accentSoft, glyph); });
                return;
            }

            AppToast toast = new AppToast(owner, title, message, accent, accentSoft, glyph);
            toast.Show();
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2400) };
            timer.Tick += delegate
            {
                timer.Stop();
                DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
                fade.Completed += delegate { toast.Close(); };
                toast.BeginAnimation(OpacityProperty, fade);
            };
            timer.Start();
        }
    }
}
