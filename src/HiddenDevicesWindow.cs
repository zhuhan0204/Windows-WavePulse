using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BluetoothBatteryMonitor
{
    internal sealed class HiddenDevicesWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly Action<DevicePreference> _restoreDevice;
        private readonly Func<DevicePreference, bool> _deleteDevice;
        private readonly StackPanel _deviceGroups;
        private readonly TextBlock _countText;

        public HiddenDevicesWindow(
            Window owner,
            AppSettings settings,
            Action<DevicePreference> restoreDevice,
            Func<DevicePreference, bool> deleteDevice)
        {
            _settings = settings;
            _restoreDevice = restoreDevice;
            _deleteDevice = deleteDevice;

            Owner = owner;
            Title = "隐藏设备 - WavePulse 无线设备电量助手";
            Icon = AppIcon.Image;
            Width = 720;
            Height = Math.Min(620, SystemParameters.WorkArea.Height - 72);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Theme.Brush(Theme.WindowColor);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SourceInitialized += delegate { WindowVisualHelper.ApplyRoundedCorners(this); };
            Closing += delegate { WindowVisualHelper.BringToFront(owner); };

            Grid root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid heading = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel copy = new StackPanel();
            copy.Children.Add(Theme.Text("隐藏设备", 22, FontWeights.Bold, Theme.TextColor));
            TextBlock note = Theme.Text("隐藏设备不会出现在主列表、单独浮窗或合并浮窗中。", 10.8, FontWeights.Normal, Theme.MutedColor);
            note.Margin = new Thickness(0, 5, 0, 0);
            copy.Children.Add(note);
            heading.Children.Add(copy);
            _countText = Theme.Text(string.Empty, 10.5, FontWeights.SemiBold, Theme.AccentColor);
            _countText.VerticalAlignment = VerticalAlignment.Bottom;
            _countText.Margin = new Thickness(14, 0, 0, 2);
            Grid.SetColumn(_countText, 1);
            heading.Children.Add(_countText);
            root.Children.Add(heading);

            Border listCard = Theme.Card();
            listCard.Padding = new Thickness(7);
            _deviceGroups = new StackPanel();
            ScrollViewer scroll = new ScrollViewer
            {
                Content = _deviceGroups,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(5)
            };
            scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Theme.ThinScrollBarStyle);
            listCard.Child = scroll;
            Grid.SetRow(listCard, 1);
            root.Children.Add(listCard);

            Grid footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock footerNote = Theme.Text("删除记录后，下次点击“刷新列表”仍可重新发现当前存在的设备。", 9.8, FontWeights.Normal, Theme.MutedColor);
            footerNote.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(footerNote);
            Button close = Theme.Button("关闭", true);
            close.MinWidth = 88;
            close.IsCancel = true;
            close.Click += delegate { Close(); };
            Grid.SetColumn(close, 1);
            footer.Children.Add(close);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
            RenderDevices();
        }

        private void RenderDevices()
        {
            IList<DevicePreference> hidden = _settings.Devices
                .Where(delegate(DevicePreference preference) { return preference.IsHidden; })
                .OrderBy(delegate(DevicePreference preference) { return preference.LastKnownTransport; })
                .ThenBy(DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _deviceGroups.Children.Clear();
            _countText.Text = "共 " + hidden.Count + " 台";
            if (hidden.Count == 0)
            {
                StackPanel empty = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 72, 0, 72)
                };
                TextBlock glyph = Theme.Text("\uE890", 30, FontWeights.Normal, Theme.MutedColor);
                glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
                glyph.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Children.Add(glyph);
                TextBlock title = Theme.Text("当前没有隐藏设备", 13, FontWeights.SemiBold, Theme.TextColor);
                title.Margin = new Thickness(0, 10, 0, 0);
                title.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Children.Add(title);
                _deviceGroups.Children.Add(empty);
                return;
            }

            AddTransportGroup(hidden, DeviceTransport.Bluetooth, "\uE702", "蓝牙设备");
            AddTransportGroup(hidden, DeviceTransport.Receiver24G, "\uE88E", "2.4G 设备");
        }

        private void AddTransportGroup(
            IList<DevicePreference> hidden,
            DeviceTransport transport,
            string glyph,
            string title)
        {
            IList<DevicePreference> group = hidden.Where(delegate(DevicePreference preference)
            {
                return preference.LastKnownTransport == transport;
            }).ToList();
            if (group.Count == 0) return;

            StackPanel heading = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, _deviceGroups.Children.Count == 0 ? 5 : 17, 8, 8)
            };
            TextBlock icon = Theme.Text(glyph, 12, FontWeights.Normal, Theme.AccentColor);
            icon.FontFamily = new FontFamily("Segoe MDL2 Assets");
            icon.VerticalAlignment = VerticalAlignment.Center;
            heading.Children.Add(icon);
            TextBlock label = Theme.Text(title + "  ·  " + group.Count, 10.5, FontWeights.SemiBold, Theme.MutedColor);
            label.Margin = new Thickness(7, 0, 0, 0);
            heading.Children.Add(label);
            _deviceGroups.Children.Add(heading);

            foreach (DevicePreference preference in group)
            {
                DevicePreference captured = preference;
                Border row = new Border
                {
                    Background = Theme.Brush(Color.FromRgb(249, 251, 254)),
                    BorderBrush = Theme.Brush(Theme.BorderColor),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(11),
                    Padding = new Thickness(13, 10, 13, 10),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                Grid rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Border iconSurface = new Border
                {
                    Width = 38,
                    Height = 38,
                    CornerRadius = new CornerRadius(11),
                    Background = Theme.Brush(Theme.AccentSoftColor),
                    Child = DeviceIconFactory.Create(DeviceKindFor(captured), captured.CustomIconPath, 26, false)
                };
                rowGrid.Children.Add(iconSurface);
                StackPanel identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                identity.Children.Add(Theme.Text(DisplayName(captured), 11.5, FontWeights.SemiBold, Theme.TextColor));
                TextBlock meta = Theme.Text(
                    Theme.KindLabel(DeviceKindFor(captured)) + " · " +
                    (transport == DeviceTransport.Receiver24G ? "2.4G 接收器" : captured.LastKnownIsLowEnergy ? "BLE" : "蓝牙"),
                    9.5,
                    FontWeights.Normal,
                    Theme.MutedColor);
                meta.Margin = new Thickness(0, 3, 0, 0);
                identity.Children.Add(meta);
                Grid.SetColumn(identity, 1);
                rowGrid.Children.Add(identity);

                StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Button restore = Theme.Button("取消隐藏", false);
                restore.MinWidth = 92;
                restore.Height = 34;
                restore.Click += delegate
                {
                    _restoreDevice(captured);
                    RenderDevices();
                };
                actions.Children.Add(restore);
                Button delete = Theme.Button("删除记录", false);
                delete.MinWidth = 88;
                delete.Height = 34;
                delete.Margin = new Thickness(8, 0, 0, 0);
                delete.Foreground = Theme.Brush(Theme.DangerColor);
                delete.BorderBrush = Theme.Brush(Color.FromRgb(244, 190, 190));
                delete.Click += delegate
                {
                    if (_deleteDevice(captured)) RenderDevices();
                };
                actions.Children.Add(delete);
                Grid.SetColumn(actions, 2);
                rowGrid.Children.Add(actions);
                row.Child = rowGrid;
                _deviceGroups.Children.Add(row);
            }
        }

        private static string DisplayName(DevicePreference preference)
        {
            if (!string.IsNullOrWhiteSpace(preference.CustomName)) return preference.CustomName;
            if (!string.IsNullOrWhiteSpace(preference.LastKnownName)) return preference.LastKnownName;
            return preference.LastKnownTransport == DeviceTransport.Receiver24G ? "未命名 2.4G 设备" : "未命名蓝牙设备";
        }

        private static DeviceKind DeviceKindFor(DevicePreference preference)
        {
            return preference.HasCustomKind ? preference.CustomKind : preference.LastKnownKind;
        }
    }
}
