using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace BluetoothBatteryMonitor
{
    internal sealed class TrayMenuWindow : Window
    {
        private Action _pendingAction;
        private bool _actionSelected;
        private bool _closeRequested;

        public TrayMenuWindow(Action showMain, Action refresh, Action exit)
        {
            Title = "WavePulse 无线设备电量助手菜单";
            Width = 228;
            Height = 190;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            Border surface = new Border
            {
                Margin = new Thickness(10),
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(14),
                Background = Theme.Brush(Colors.White),
                BorderBrush = Theme.Brush(Color.FromRgb(210, 223, 238)),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 5,
                    Opacity = 0.22,
                    Color = Color.FromRgb(31, 56, 88)
                }
            };
            StackPanel content = new StackPanel();
            TextBlock heading = Theme.Text("WavePulse 无线设备电量助手", 10, FontWeights.SemiBold, Theme.MutedColor);
            heading.Margin = new Thickness(10, 3, 10, 6);
            content.Children.Add(heading);
            content.Children.Add(CreateItem("\uE8A7", "显示主窗口", showMain, false));
            content.Children.Add(CreateItem("\uE72C", "刷新设备列表", refresh, false));
            content.Children.Add(new Border
            {
                Height = 1,
                Background = Theme.Brush(Theme.BorderColor),
                Margin = new Thickness(10, 4, 10, 4)
            });
            content.Children.Add(CreateItem("\uE8BB", "退出程序", exit, true));
            surface.Child = content;
            Content = surface;

            Loaded += delegate { PositionNearTray(); };
            Deactivated += delegate
            {
                // A tray-menu click deactivates this window before WPF dispatches the
                // button Click event. Closing synchronously here races with that event.
                Dispatcher.BeginInvoke((Action)delegate
                {
                    if (!_actionSelected) Dismiss();
                }, DispatcherPriority.Background);
            };
            Closed += delegate
            {
                Action action = _pendingAction;
                _pendingAction = null;
                if (action == null) return;
                Dispatcher.BeginInvoke((Action)delegate { action(); }, DispatcherPriority.Background);
            };
        }

        internal void Dismiss()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke((Action)Dismiss);
                return;
            }
            if (_closeRequested) return;
            _closeRequested = true;
            Close();
        }

        private void SelectAction(Action action)
        {
            if (_actionSelected) return;
            _actionSelected = true;
            _pendingAction = action;
            Dismiss();
        }

        private Button CreateItem(string glyph, string text, Action action, bool danger)
        {
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock icon = Theme.Text(glyph, 14, FontWeights.Normal, danger ? Theme.DangerColor : Theme.AccentColor);
            icon.FontFamily = new FontFamily("Segoe MDL2 Assets");
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            row.Children.Add(icon);
            TextBlock label = Theme.Text(text, 11.5, FontWeights.SemiBold, danger ? Theme.DangerColor : Theme.TextColor);
            label.Margin = new Thickness(7, 0, 0, 0);
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            Button button = new Button { Content = row, Style = Theme.TrayMenuButtonStyle };
            System.Windows.Automation.AutomationProperties.SetName(button, text);
            button.Click += delegate { SelectAction(action); };
            return button;
        }

        private void PositionNearTray()
        {
            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            System.Drawing.Rectangle area = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            double scaleX = Math.Max(1, dpi.DpiScaleX);
            double scaleY = Math.Max(1, dpi.DpiScaleY);
            double workLeft = area.Left / scaleX;
            double workTop = area.Top / scaleY;
            double workRight = area.Right / scaleX;
            double workBottom = area.Bottom / scaleY;
            Left = Math.Max(workLeft + 12, Math.Min(workRight - Width - 12, (cursor.X / scaleX) - Width + 18));
            Top = Math.Max(workTop + 12, Math.Min(workBottom - Height - 12, (cursor.Y / scaleY) - Height + 10));
        }
    }
}
