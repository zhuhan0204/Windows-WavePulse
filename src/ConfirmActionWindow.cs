using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BluetoothBatteryMonitor
{
    internal sealed class ConfirmActionWindow : Window
    {
        public bool Confirmed { get; private set; }

        public ConfirmActionWindow(
            Window owner,
            string title,
            string heading,
            string message,
            string confirmText)
        {
            Owner = owner;
            Title = title + " - WavePulse";
            Icon = AppIcon.Image;
            Width = 480;
            Height = 278;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Theme.Brush(Theme.WindowColor);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SourceInitialized += delegate { WindowVisualHelper.ApplyRoundedCorners(this); };

            Grid root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border content = Theme.Card();
            content.Padding = new Thickness(20);
            Grid contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Border iconSurface = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(13),
                Background = Theme.Brush(Theme.DangerSoftColor),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = "\uE74D",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 18,
                    Foreground = Theme.Brush(Theme.DangerColor),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            contentGrid.Children.Add(iconSurface);
            StackPanel copy = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            copy.Children.Add(Theme.Text(heading, 15, FontWeights.SemiBold, Theme.TextColor));
            TextBlock description = Theme.Text(message, 10.8, FontWeights.Normal, Theme.MutedColor);
            description.Margin = new Thickness(0, 8, 0, 0);
            description.TextWrapping = TextWrapping.Wrap;
            description.LineHeight = 19;
            copy.Children.Add(description);
            Grid.SetColumn(copy, 1);
            contentGrid.Children.Add(copy);
            content.Child = contentGrid;
            root.Children.Add(content);

            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            Button cancel = Theme.Button("取消", false);
            cancel.MinWidth = 88;
            cancel.IsCancel = true;
            cancel.Click += delegate { Close(); };
            actions.Children.Add(cancel);
            Button confirm = Theme.Button(confirmText, false);
            confirm.MinWidth = 104;
            confirm.Margin = new Thickness(10, 0, 0, 0);
            confirm.Background = Theme.Brush(Theme.DangerColor);
            confirm.Foreground = Brushes.White;
            confirm.BorderBrush = Theme.Brush(Theme.DangerColor);
            confirm.Click += delegate
            {
                Confirmed = true;
                DialogResult = true;
            };
            actions.Children.Add(confirm);
            Grid.SetRow(actions, 1);
            root.Children.Add(actions);
            Content = root;
        }
    }
}
