using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BluetoothBatteryMonitor
{
    internal sealed class MonitorSelectionWindow : Window
    {
        public FloatingMonitorOption SelectedMonitor { get; private set; }

        public MonitorSelectionWindow(
            Window owner,
            IList<FloatingMonitorOption> monitors,
            string deviceName)
        {
            Owner = owner;
            Title = "选择悬浮窗屏幕 - WavePulse";
            try { Icon = AppIcon.Image; } catch { }
            Width = 520;
            Height = Math.Max(340, Math.Min(580, 220 + ((monitors == null ? 0 : monitors.Count) * 82)));
            MinWidth = 520;
            MaxWidth = 520;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = Theme.Brush(Theme.WindowColor);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SourceInitialized += delegate { WindowVisualHelper.ApplyRoundedCorners(this); };

            Grid root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel heading = new StackPanel();
            heading.Children.Add(Theme.Text("选择悬浮窗显示屏幕", 20, FontWeights.Bold, Theme.TextColor));
            TextBlock note = Theme.Text(
                "“" + (string.IsNullOrWhiteSpace(deviceName) ? "当前设备" : deviceName) +
                "”将显示在所选屏幕中央，显示后仍可自由拖动。",
                10.8,
                FontWeights.Normal,
                Theme.MutedColor);
            note.Margin = new Thickness(0, 6, 0, 16);
            note.TextWrapping = TextWrapping.Wrap;
            heading.Children.Add(note);
            root.Children.Add(heading);

            StackPanel monitorList = new StackPanel();
            if (monitors != null)
            {
                foreach (FloatingMonitorOption monitor in monitors)
                {
                    monitorList.Children.Add(BuildMonitorCard(monitor));
                }
            }
            ScrollViewer scroll = new ScrollViewer
            {
                Content = monitorList,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Theme.ThinScrollBarStyle);
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            Button cancel = Theme.Button("取消", false);
            cancel.MinWidth = 88;
            cancel.IsCancel = true;
            cancel.Click += delegate { Close(); };
            actions.Children.Add(cancel);
            Grid.SetRow(actions, 2);
            root.Children.Add(actions);

            Content = root;
        }

        private Button BuildMonitorCard(FloatingMonitorOption monitor)
        {
            Button card = new Button
            {
                Style = Theme.ModernButtonStyle,
                Background = Brushes.White,
                BorderBrush = Theme.Brush(Theme.BorderColor),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 11, 14, 11),
                Margin = new Thickness(0, 0, 0, 9),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            System.Windows.Automation.AutomationProperties.SetName(
                card,
                monitor.DisplayName + " " + monitor.ResolutionWidth + " × " + monitor.ResolutionHeight);

            Grid row = new Grid { Width = 402 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border iconSurface = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(12),
                Background = Theme.Brush(Theme.AccentSoftColor),
                Child = new TextBlock
                {
                    Text = "\uE7F4",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 18,
                    Foreground = Theme.Brush(Theme.AccentColor),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            row.Children.Add(iconSurface);

            StackPanel copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            string title = monitor.DisplayName;
            copy.Children.Add(Theme.Text(title, 12.2, FontWeights.SemiBold, Theme.TextColor));
            TextBlock details = Theme.Text(
                monitor.ResolutionWidth + " × " + monitor.ResolutionHeight + "  ·  " + monitor.PositionDescription,
                10.2,
                FontWeights.Normal,
                Theme.MutedColor);
            details.Margin = new Thickness(0, 3, 0, 0);
            copy.Children.Add(details);
            Grid.SetColumn(copy, 1);
            row.Children.Add(copy);

            TextBlock choose = Theme.Text("选择", 10.8, FontWeights.SemiBold, Theme.AccentColor);
            choose.VerticalAlignment = VerticalAlignment.Center;
            choose.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(choose, 2);
            row.Children.Add(choose);
            card.Content = row;
            card.Click += delegate
            {
                SelectedMonitor = monitor;
                DialogResult = true;
            };
            return card;
        }
    }
}
