using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BluetoothBatteryMonitor
{
    internal sealed class AboutWindow : Window
    {
        public AboutWindow(Window owner)
        {
            Owner = owner;
            Title = "关于应用 - WavePulse 无线设备电量助手";
            Icon = AppIcon.Image;
            Width = 610;
            Height = 590;
            MinWidth = 560;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = Theme.Brush(Theme.WindowColor);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SourceInitialized += delegate { WindowVisualHelper.ApplyRoundedCorners(this); };

            Grid root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid heading = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.Children.Add(new Image
            {
                Source = AppIcon.Image,
                Width = 56,
                Height = 56,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left
            });
            StackPanel headingText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            headingText.Children.Add(Theme.Text("WavePulse 无线设备电量助手", 21, FontWeights.Bold, Theme.TextColor));
            TextBlock version = Theme.Text("版本 v0.1", 11.5, FontWeights.SemiBold, Theme.AccentColor);
            version.Margin = new Thickness(0, 5, 0, 0);
            headingText.Children.Add(version);
            Grid.SetColumn(headingText, 1);
            heading.Children.Add(headingText);
            root.Children.Add(heading);

            StackPanel content = new StackPanel();
            content.Children.Add(CreateVersionHistorySection());
            content.Children.Add(CreateSection(
                "作者与版本信息",
                "版权所有人：yuban\n" +
                "Copyright © 2026 yuban. All rights reserved.\n" +
                "作者交流 QQ 群：560010078\n" +
                "版本声明：本发布文件为作者提供的未经修改版本。若经第三方修改、重新打包或附加其他程序，其安全性与行为不再由原作者负责。"));
            content.Children.Add(CreateSection(
                "免责声明",
                "本工具仅展示 Windows、蓝牙设备及其驱动所上报的数据，不保证电量数值实时、持续或完全准确，请勿将其用于安全、医疗或关键业务判断。诊断日志可能包含设备名称、蓝牙地址和设备 ID，分享前请自行确认并妥善处理。使用本程序所产生的结果与风险由使用者自行承担。"));

            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            };
            scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Theme.ThinScrollBarStyle);
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            Button close = Theme.Button("关闭", true);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 14, 0, 0);
            close.Click += delegate { Close(); };
            Grid.SetRow(close, 2);
            root.Children.Add(close);
            Content = root;
        }

        private static Border CreateSection(string title, string body)
        {
            Border card = Theme.Card();
            card.Padding = new Thickness(17, 14, 17, 14);
            StackPanel panel = new StackPanel();
            panel.Children.Add(Theme.Text(title, 13.5, FontWeights.SemiBold, Theme.TextColor));
            TextBlock text = Theme.Text(body, 11, FontWeights.Normal, Theme.MutedColor);
            text.Margin = new Thickness(0, 8, 0, 0);
            text.TextWrapping = TextWrapping.Wrap;
            text.TextTrimming = TextTrimming.None;
            text.LineHeight = 21;
            panel.Children.Add(text);
            card.Child = panel;
            return card;
        }

        private static Border CreateVersionHistorySection()
        {
            Border card = Theme.Card();
            card.Padding = new Thickness(17, 14, 17, 14);
            StackPanel panel = new StackPanel();
            Grid heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel title = new StackPanel();
            title.Children.Add(Theme.Text("版本历史", 13.5, FontWeights.SemiBold, Theme.TextColor));
            TextBlock hint = Theme.Text("选择版本即可查看对应更新记录", 9.5, FontWeights.Normal, Theme.MutedColor);
            hint.Margin = new Thickness(0, 4, 0, 0);
            title.Children.Add(hint);
            heading.Children.Add(title);
            ComboBox selector = new ComboBox
            {
                Width = 112,
                Height = 34,
                ItemsSource = new[] { new SelectionOption<string>("0.1", "v0.1（当前）") },
                DisplayMemberPath = "Label",
                SelectedValuePath = "Value",
                SelectedValue = "0.1",
                Style = Theme.ModernComboBoxStyle,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            System.Windows.Automation.AutomationProperties.SetName(selector, "版本历史");
            Grid.SetColumn(selector, 1);
            heading.Children.Add(selector);
            panel.Children.Add(heading);

            TextBlock details = Theme.Text(VersionDetails("0.1"), 11, FontWeights.Normal, Theme.MutedColor);
            details.Margin = new Thickness(0, 12, 0, 0);
            details.TextWrapping = TextWrapping.Wrap;
            details.TextTrimming = TextTrimming.None;
            details.LineHeight = 21;
            selector.SelectionChanged += delegate
            {
                details.Text = VersionDetails(selector.SelectedValue == null ? "0.1" : selector.SelectedValue.ToString());
            };
            panel.Children.Add(details);
            card.Child = panel;
            return card;
        }

        private static string VersionDetails(string version)
        {
            switch (version)
            {
                default:
                    return "• 支持读取并展示蓝牙与已适配 2.4G 无线设备电量\n" +
                           "• 支持每台设备独立刷新、名称、自动识别/自定义类型、图标与悬浮样式设置\n" +
                           "• 支持设备卡片折叠、连接状态排序、现代色盘与实时透明度滑杆\n" +
                           "• 支持圆环电量、最近刷新时间及部分耳机左右耳/充电盒电量\n" +
                           "• 支持选择设备后合并悬浮、固定、置顶、通知区域驻留及诊断日志";
            }
        }
    }
}
