using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BluetoothBatteryMonitor
{
    internal sealed class MergedFloatingItem
    {
        public BluetoothDeviceBattery Device { get; private set; }
        public DevicePreference Preference { get; private set; }

        public MergedFloatingItem(BluetoothDeviceBattery device, DevicePreference preference)
        {
            Device = device;
            Preference = preference;
        }
    }

    internal sealed class MergedFloatingWindow : Window
    {
        private readonly Border _surface;
        private readonly StackPanel _rows;
        private readonly TextBlock _heading;
        private readonly Dictionary<int, MenuItem> _opacityMenuItems;
        private readonly ContextMenu _contextMenu;
        private MenuItem _lockMenuItem;
        private MenuItem _topmostMenuItem;
        private int _opacityPercent;
        private bool _isLocked;
        private bool _positionPersistenceEnabled;

        public event EventHandler HideRequested;
        public event EventHandler RefreshRequested;
        public event EventHandler PositionChangedByUser;
        public event EventHandler<OpacityPresetEventArgs> OpacityPresetRequested;
        public event EventHandler<BooleanSettingEventArgs> LockChangedRequested;
        public event EventHandler<BooleanSettingEventArgs> TopmostChangedRequested;

        public MergedFloatingWindow(
            IList<MergedFloatingItem> items,
            int lowBatteryThreshold,
            bool alwaysOnTop,
            bool isLocked,
            int opacityPercent)
        {
            _opacityMenuItems = new Dictionary<int, MenuItem>();
            MinWidth = 260;
            MaxWidth = FloatingAppearanceLimits.MaxWidth;
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
                Background = Theme.Brush(Colors.White),
                BorderBrush = Theme.Brush(Theme.BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(10),
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _heading = Theme.Text("全部设备电量", 12, FontWeights.SemiBold, Theme.TextColor);
            _heading.Margin = new Thickness(7, 2, 7, 9);
            root.Children.Add(_heading);

            _rows = new StackPanel();
            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _rows
            };
            scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Theme.ThinScrollBarStyle);
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            _surface.Child = root;
            Content = _surface;

            MouseLeftButtonDown += OnMouseLeftButtonDown;
            PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
            PreviewMouseRightButtonUp += OnPreviewMouseRightButtonUp;
            _contextMenu = BuildContextMenu();
            ContextMenu = _contextMenu;
            _surface.ContextMenu = _contextMenu;
            UpdateItems(items, lowBatteryThreshold, alwaysOnTop, isLocked, opacityPercent);
        }

        public void UpdateItems(
            IList<MergedFloatingItem> items,
            int lowBatteryThreshold,
            bool alwaysOnTop,
            bool isLocked,
            int opacityPercent)
        {
            IList<MergedFloatingItem> safeItems = items ?? new List<MergedFloatingItem>();
            ApplyWindowOptions(alwaysOnTop, isLocked, opacityPercent);
            int largestWidth = safeItems.Count == 0
                ? 300
                : safeItems.Max(delegate(MergedFloatingItem item) { return item.Preference.FloatingWidth; });
            int largestHeight = safeItems.Count == 0
                ? FloatingAppearanceLimits.DefaultHeight
                : safeItems.Max(delegate(MergedFloatingItem item) { return item.Preference.FloatingHeight; });
            Width = Math.Max(260, Math.Min(FloatingAppearanceLimits.MaxWidth, largestWidth));
            double desiredHeight = 46 + (safeItems.Count * largestHeight) + Math.Max(0, safeItems.Count - 1) * 6 + 20;
            Height = Math.Max(100, Math.Min(SystemParameters.WorkArea.Height * 0.82, desiredHeight));
            _heading.Text = "全部设备电量  ·  " + safeItems.Count + " 台";

            _rows.Children.Clear();
            foreach (MergedFloatingItem item in safeItems)
            {
                _rows.Children.Add(BuildDeviceRow(item, largestHeight, lowBatteryThreshold));
            }
            if (safeItems.Count == 0)
            {
                TextBlock empty = Theme.Text("暂无已记录的蓝牙设备", 11, FontWeights.Normal, Theme.MutedColor);
                empty.Margin = new Thickness(8, 18, 8, 18);
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                _rows.Children.Add(empty);
            }
        }

        public bool CanPersistPosition
        {
            get { return _positionPersistenceEnabled; }
        }

        public void SetPositionPersistenceEnabled(bool enabled)
        {
            _positionPersistenceEnabled = enabled;
        }

        private void ApplyWindowOptions(bool alwaysOnTop, bool isLocked, int opacityPercent)
        {
            Topmost = alwaysOnTop;
            _isLocked = isLocked;
            _opacityPercent = FloatingAppearanceLimits.ClampOpacity(opacityPercent);
            Opacity = _opacityPercent / 100.0;
            if (_lockMenuItem != null) _lockMenuItem.IsChecked = _isLocked;
            if (_topmostMenuItem != null) _topmostMenuItem.IsChecked = Topmost;
            foreach (KeyValuePair<int, MenuItem> item in _opacityMenuItems)
            {
                item.Value.IsChecked = item.Key == _opacityPercent;
            }
            ToolTip = _isLocked
                ? "合并悬浮窗的位置已固定；右键可调整透明度、固定和置顶"
                : "拖动可改变合并悬浮窗位置；右键可调整透明度、固定和置顶";
        }

        private static Border BuildDeviceRow(MergedFloatingItem item, int rowHeight, int lowBatteryThreshold)
        {
            BluetoothDeviceBattery device = item.Device;
            DevicePreference preference = item.Preference;
            bool low = MainWindow.IsLowBattery(device, preference.LowBatteryThreshold);
            Color background = !device.IsConnected
                ? Theme.ParseColor(preference.DisconnectedColor, Color.FromRgb(62, 72, 86))
                : low
                    ? Theme.ParseColor(preference.LowBatteryColor, Theme.DangerColor)
                    : Theme.ParseColor(preference.BackgroundColor, Colors.White);
            Color foreground = Theme.ContrastText(background);
            Color secondary = foreground == Theme.TextColor
                ? Theme.MutedColor
                : Theme.Blend(foreground, background, 0.28);

            Border row = new Border
            {
                Height = rowHeight,
                Background = Theme.Brush(background),
                BorderBrush = Theme.Brush(Theme.Blend(background, Colors.Black, 0.1)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Math.Max(12, Math.Min(20, rowHeight * 0.22))),
                Padding = new Thickness(10, 7, 12, 7),
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border icon = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(11),
                Background = Theme.Brush(low || !device.IsConnected ? Color.FromArgb(48, 255, 255, 255) : Theme.AccentSoftColor),
                Child = DeviceIconFactory.Create(device.Kind, preference.CustomIconPath, 30, low || !device.IsConnected),
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(icon);

            StackPanel text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock name = Theme.Text(device.Name, 11.5, FontWeights.SemiBold, foreground);
            name.ToolTip = device.Name;
            text.Children.Add(name);
            string state = !device.IsConnected
                ? "未连接"
                : device.IsCharging
                    ? "充电中"
                    : MainWindow.HasReportedBattery(device) ? (low ? "电量偏低" : "已连接") : "设备未上报";
            string accessory = MainWindow.AccessoryBatteryText(device);
            string refreshTime = device.LastRefreshedAt == default(DateTime) ? "未刷新" : device.LastRefreshedAt.ToString("HH:mm");
            TextBlock status = Theme.Text(
                (string.IsNullOrWhiteSpace(accessory) ? state : accessory) + " · " + refreshTime,
                9.5,
                FontWeights.Normal,
                secondary);
            status.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(status);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            BatteryRing battery = new BatteryRing(Math.Max(38, Math.Min(76, rowHeight - 16)))
            {
                Margin = new Thickness(8, 0, 0, 0)
            };
            battery.Update(
                MainWindow.PrimaryBatteryPercent(device),
                foreground,
                Theme.Blend(foreground, background, 0.78),
                refreshTime,
                device.IsConnected && !low && MainWindow.PrimaryBatteryPercent(device).HasValue
                    ? (Color?)Theme.SuccessColor
                    : null,
                device.IsCharging ? "充电中" : null);
            Grid.SetColumn(battery, 2);
            grid.Children.Add(battery);
            row.Child = grid;
            return row;
        }

        private ContextMenu BuildContextMenu()
        {
            ContextMenu menu = new ContextMenu { Style = Theme.ModernContextMenuStyle };
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
                    _opacityPercent = capturedValue;
                    Opacity = capturedValue / 100.0;
                    foreach (KeyValuePair<int, MenuItem> pair in _opacityMenuItems)
                    {
                        pair.Value.IsChecked = pair.Key == capturedValue;
                    }
                    EventHandler<OpacityPresetEventArgs> handler = OpacityPresetRequested;
                    if (handler != null) handler(this, new OpacityPresetEventArgs(capturedValue));
                };
                _opacityMenuItems[capturedValue] = item;
                opacityMenu.Items.Add(item);
            }
            menu.Items.Add(opacityMenu);
            menu.Items.Add(new Separator());

            MenuItem hide = new MenuItem { Header = "关闭合并悬浮窗", Style = Theme.ModernMenuItemStyle };
            hide.Click += delegate
            {
                EventHandler handler = HideRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            menu.Items.Add(hide);
            return menu;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isLocked || !_positionPersistenceEnabled || e.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); }
            catch { return; }
            if (!_positionPersistenceEnabled || !IsVisible) return;
            EventHandler handler = PositionChangedByUser;
            if (handler != null) handler(this, EventArgs.Empty);
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
