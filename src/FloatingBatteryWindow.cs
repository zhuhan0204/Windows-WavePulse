using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BluetoothBatteryMonitor
{
    internal sealed class OpacityPresetEventArgs : EventArgs
    {
        public int OpacityPercent { get; private set; }

        public OpacityPresetEventArgs(int opacityPercent)
        {
            OpacityPercent = opacityPercent;
        }
    }

    internal sealed class BooleanSettingEventArgs : EventArgs
    {
        public bool Enabled { get; private set; }

        public BooleanSettingEventArgs(bool enabled)
        {
            Enabled = enabled;
        }
    }

    internal sealed class FloatingBatteryWindow : Window
    {
        private readonly string _stableId;
        private readonly Border _surface;
        private readonly Grid _iconHost;
        private readonly TextBlock _nameText;
        private readonly BatteryRing _batteryRing;
        private readonly TextBlock _statusText;
        private readonly Dictionary<int, MenuItem> _opacityMenuItems;
        private readonly ContextMenu _contextMenu;
        private MenuItem _lockMenuItem;
        private MenuItem _topmostMenuItem;
        private string _customIconPath;
        private int _opacityPercent;
        private bool _isLocked;
        private bool _positionPersistenceEnabled;

        public event EventHandler HideRequested;
        public event EventHandler RefreshRequested;
        public event EventHandler PositionChangedByUser;
        public event EventHandler<OpacityPresetEventArgs> OpacityPresetRequested;
        public event EventHandler<BooleanSettingEventArgs> LockChangedRequested;
        public event EventHandler<BooleanSettingEventArgs> TopmostChangedRequested;

        public FloatingBatteryWindow(
            BluetoothDeviceBattery device,
            string customIconPath,
            int lowBatteryThreshold,
            bool alwaysOnTop,
            bool isLocked,
            int floatingWidth,
            int floatingHeight,
            int opacityPercent,
            string backgroundColor,
            string lowBatteryColor,
            string disconnectedColor)
        {
            _stableId = device.StableId;
            _customIconPath = customIconPath;
            _opacityMenuItems = new Dictionary<int, MenuItem>();

            Width = FloatingAppearanceLimits.DefaultWidth;
            Height = FloatingAppearanceLimits.DefaultHeight;
            MinWidth = FloatingAppearanceLimits.MinWidth;
            MaxWidth = FloatingAppearanceLimits.MaxWidth;
            MinHeight = FloatingAppearanceLimits.MinHeight;
            MaxHeight = FloatingAppearanceLimits.MaxHeight;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = alwaysOnTop;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            SourceInitialized += delegate { WindowVisualHelper.HideFromAltTab(this); };

            _surface = new Border
            {
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(10, 9, 14, 9),
                BorderThickness = new Thickness(1),
                ClipToBounds = false,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };

            Grid layout = new Grid { ClipToBounds = false };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _iconHost = new Grid
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_iconHost, 0);
            layout.Children.Add(_iconHost);

            StackPanel textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _nameText = Theme.Text(device.Name, 11.5, FontWeights.SemiBold, Theme.TextColor);
            _nameText.MaxWidth = 86;
            _statusText = Theme.Text("已连接", 9.5, FontWeights.Normal, Theme.MutedColor);
            _statusText.Margin = new Thickness(0, 2, 0, 0);
            _statusText.LineHeight = 11;
            textPanel.Children.Add(_nameText);
            textPanel.Children.Add(_statusText);
            Grid.SetColumn(textPanel, 1);
            layout.Children.Add(textPanel);

            _batteryRing = new BatteryRing(42) { Margin = new Thickness(7, 0, 2, 0) };
            Grid.SetColumn(_batteryRing, 2);
            layout.Children.Add(_batteryRing);

            _surface.Child = layout;
            Content = _surface;

            MouseLeftButtonDown += OnMouseLeftButtonDown;
            PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
            PreviewMouseRightButtonUp += OnPreviewMouseRightButtonUp;
            _contextMenu = BuildContextMenu();
            ContextMenu = _contextMenu;
            _surface.ContextMenu = _contextMenu;
            UpdateDevice(
                device,
                customIconPath,
                lowBatteryThreshold,
                alwaysOnTop,
                isLocked,
                floatingWidth,
                floatingHeight,
                opacityPercent,
                backgroundColor,
                lowBatteryColor,
                disconnectedColor);
        }

        public string StableId
        {
            get { return _stableId; }
        }

        public int OpacityPercent
        {
            get { return _opacityPercent; }
        }

        public bool CanPersistPosition
        {
            get { return _positionPersistenceEnabled; }
        }

        public void SetPositionPersistenceEnabled(bool enabled)
        {
            _positionPersistenceEnabled = enabled;
        }

        public void ApplyAppearance(int floatingWidth, int floatingHeight, int opacityPercent)
        {
            int safeWidth = FloatingAppearanceLimits.ClampWidth(floatingWidth);
            int safeHeight = FloatingAppearanceLimits.ClampHeight(floatingHeight);
            int safeOpacity = FloatingAppearanceLimits.ClampOpacity(opacityPercent);

            Width = safeWidth;
            Height = safeHeight;
            Opacity = safeOpacity / 100.0;
            _opacityPercent = safeOpacity;

            _nameText.MaxWidth = Math.Max(48, safeWidth - 122);
            _batteryRing.SetSize(Math.Max(36, Math.Min(78, safeHeight - 22)));
            _surface.CornerRadius = new CornerRadius(Math.Max(12, Math.Min(22, safeHeight * 0.24)));
            UpdateOpacityMenuChecks();
        }

        public void UpdateDevice(
            BluetoothDeviceBattery device,
            string customIconPath,
            int lowBatteryThreshold,
            bool alwaysOnTop,
            bool isLocked,
            int floatingWidth,
            int floatingHeight,
            int opacityPercent,
            string backgroundColor,
            string lowBatteryColor,
            string disconnectedColor)
        {
            _customIconPath = customIconPath;
            Topmost = alwaysOnTop;
            _isLocked = isLocked;
            if (_lockMenuItem != null) _lockMenuItem.IsChecked = _isLocked;
            if (_topmostMenuItem != null) _topmostMenuItem.IsChecked = Topmost;
            _nameText.Text = device.Name;
            ApplyAppearance(floatingWidth, floatingHeight, opacityPercent);
            bool isLow = MainWindow.IsLowBattery(device, lowBatteryThreshold);

            Color background;
            Color border;
            Color foreground;
            Color secondary;
            if (!device.IsConnected)
            {
                background = Theme.ParseColor(disconnectedColor, Color.FromRgb(62, 72, 86));
                border = Theme.Blend(background, Colors.Black, 0.12);
                foreground = Theme.ContrastText(background);
                secondary = Theme.Blend(foreground, background, 0.28);
                _statusText.Text = BuildStatusText(device, "未连接");
            }
            else if (device.IsCharging)
            {
                background = Theme.ParseColor(backgroundColor, Colors.White);
                border = background == Colors.White ? Theme.BorderColor : Theme.Blend(background, Colors.Black, 0.12);
                foreground = Theme.ContrastText(background);
                secondary = foreground == Theme.TextColor ? Theme.SuccessColor : Theme.Blend(foreground, background, 0.22);
                _statusText.Text = BuildStatusText(device, "充电中");
            }
            else if (isLow)
            {
                background = Theme.ParseColor(lowBatteryColor, Theme.DangerColor);
                border = Theme.Blend(background, Colors.Black, 0.12);
                foreground = Theme.ContrastText(background);
                secondary = Theme.Blend(foreground, background, 0.25);
                _statusText.Text = BuildStatusText(device, "电量偏低");
            }
            else
            {
                background = Theme.ParseColor(backgroundColor, Colors.White);
                border = background == Colors.White ? Theme.BorderColor : Theme.Blend(background, Colors.Black, 0.12);
                foreground = Theme.ContrastText(background);
                secondary = foreground == Theme.TextColor ? Theme.MutedColor : Theme.Blend(foreground, background, 0.28);
                _statusText.Text = BuildStatusText(device, MainWindow.HasReportedBattery(device) ? "已连接" : "标准接口未上报");
            }

            _surface.Background = Theme.Brush(background);
            _surface.BorderBrush = Theme.Brush(border);
            _nameText.Foreground = Theme.Brush(foreground);
            _statusText.Foreground = Theme.Brush(secondary);
            _batteryRing.Update(
                MainWindow.PrimaryBatteryPercent(device),
                foreground,
                Theme.Blend(foreground, background, 0.78),
                device.LastRefreshedAt == default(DateTime) ? "未刷新" : device.LastRefreshedAt.ToString("HH:mm"),
                device.IsConnected && !isLow && MainWindow.PrimaryBatteryPercent(device).HasValue
                    ? (Color?)Theme.SuccessColor
                    : null,
                device.IsCharging ? "充电中" : null);

            _iconHost.Children.Clear();
            Border iconBackground = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = Theme.Brush(isLow || !device.IsConnected
                    ? Color.FromArgb(48, 255, 255, 255)
                    : Theme.AccentSoftColor)
            };
            iconBackground.Child = DeviceIconFactory.Create(
                device.Kind,
                _customIconPath,
                32,
                isLow || !device.IsConnected);
            _iconHost.Children.Add(iconBackground);

            ToolTip = device.Name + "\n" +
                      (device.IsCharging
                          ? "正在充电，百分比暂不显示"
                          : MainWindow.PrimaryBatteryPercent(device).HasValue
                          ? "电量 " + MainWindow.PrimaryBatteryPercent(device).Value + "%"
                          : "标准接口未上报电量") +
                      (string.IsNullOrWhiteSpace(MainWindow.AccessoryBatteryText(device)) ? string.Empty : "\n" + MainWindow.AccessoryBatteryText(device)) +
                      "\n" + MainWindow.FormatLastRefresh(device.LastRefreshedAt) +
                      (_isLocked ? "\n位置已固定，右键可解除固定" : "\n拖动可改变位置") +
                      "，右键可调透明度、固定、置顶或关闭悬浮";
        }

        private static string BuildStatusText(BluetoothDeviceBattery device, string state)
        {
            string accessory = MainWindow.CompactAccessoryBatteryText(device);
            string time = device.LastRefreshedAt == default(DateTime) ? "未刷新" : device.LastRefreshedAt.ToString("HH:mm");
            return string.IsNullOrWhiteSpace(accessory)
                ? state + " · " + time
                : accessory;
        }

        private ContextMenu BuildContextMenu()
        {
            ContextMenu menu = new ContextMenu
            {
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                Style = Theme.ModernContextMenuStyle
            };

            MenuItem refresh = new MenuItem
            {
                Header = "刷新电量",
                Style = Theme.ModernMenuItemStyle
            };
            refresh.Click += delegate
            {
                EventHandler handler = RefreshRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            menu.Items.Add(refresh);
            menu.Items.Add(new Separator());

            _lockMenuItem = new MenuItem
            {
                Header = "固定位置",
                IsCheckable = true,
                StaysOpenOnClick = false,
                Style = Theme.ModernMenuItemStyle
            };
            _lockMenuItem.Click += delegate
            {
                _isLocked = _lockMenuItem.IsChecked;
                EventHandler<BooleanSettingEventArgs> handler = LockChangedRequested;
                if (handler != null) handler(this, new BooleanSettingEventArgs(_isLocked));
            };
            menu.Items.Add(_lockMenuItem);

            _topmostMenuItem = new MenuItem
            {
                Header = "置顶显示",
                IsCheckable = true,
                StaysOpenOnClick = false,
                Style = Theme.ModernMenuItemStyle
            };
            _topmostMenuItem.Click += delegate
            {
                Topmost = _topmostMenuItem.IsChecked;
                EventHandler<BooleanSettingEventArgs> handler = TopmostChangedRequested;
                if (handler != null) handler(this, new BooleanSettingEventArgs(Topmost));
            };
            menu.Items.Add(_topmostMenuItem);
            menu.Items.Add(new Separator());

            MenuItem opacityMenu = new MenuItem { Header = "透明度", Style = Theme.ModernMenuItemStyle };
            int[] opacityValues = { 100, 75, 50, 25 };
            foreach (int opacityValue in opacityValues)
            {
                int capturedValue = opacityValue;
                MenuItem item = new MenuItem
                {
                    Header = capturedValue + "%",
                    IsCheckable = true,
                    StaysOpenOnClick = false,
                    Style = Theme.ModernMenuItemStyle
                };
                item.Click += delegate
                {
                    ApplyAppearance((int)Width, (int)Height, capturedValue);
                    EventHandler<OpacityPresetEventArgs> handler = OpacityPresetRequested;
                    if (handler != null) handler(this, new OpacityPresetEventArgs(capturedValue));
                };
                _opacityMenuItems[capturedValue] = item;
                opacityMenu.Items.Add(item);
            }
            menu.Items.Add(opacityMenu);
            menu.Items.Add(new Separator());

            MenuItem hide = new MenuItem { Header = "关闭浮窗", Style = Theme.ModernMenuItemStyle };
            hide.Click += delegate
            {
                EventHandler handler = HideRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            menu.Items.Add(hide);
            return menu;
        }

        private void UpdateOpacityMenuChecks()
        {
            foreach (KeyValuePair<int, MenuItem> item in _opacityMenuItems)
            {
                item.Value.IsChecked = item.Key == _opacityPercent;
            }
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isLocked && _positionPersistenceEnabled && e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch { return; }
                // OS/DPI moves must never be persisted as a user-selected screen.
                if (!_positionPersistenceEnabled || !IsVisible) return;
                EventHandler handler = PositionChangedByUser;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private void OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            OpenContextMenu(e);
        }

        private void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            OpenContextMenu(e);
        }

        private void OpenContextMenu(MouseButtonEventArgs e)
        {
            _contextMenu.PlacementTarget = this;
            _contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            _contextMenu.IsOpen = true;
            e.Handled = true;
        }

    }
}
