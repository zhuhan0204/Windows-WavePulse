using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BluetoothBatteryMonitor
{
    internal sealed class FloatingSizeWindow : Window
    {
        private readonly TextBox _widthBox;
        private readonly TextBox _heightBox;
        private readonly TextBlock _error;

        public int SelectedWidth { get; private set; }
        public int SelectedHeight { get; private set; }

        public FloatingSizeWindow(Window owner, int currentWidth, int currentHeight)
        {
            Owner = owner;
            Title = "自定义悬浮窗尺寸";
            Icon = AppIcon.Image;
            Width = 430;
            Height = 300;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Theme.Brush(Theme.WindowColor);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SourceInitialized += delegate { WindowVisualHelper.ApplyRoundedCorners(this); };

            Grid root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            StackPanel heading = new StackPanel();
            heading.Children.Add(Theme.Text("自定义悬浮窗尺寸", 20, FontWeights.Bold, Theme.TextColor));
            TextBlock note = Theme.Text("宽度 160–420 px，高度 56–160 px。", 11, FontWeights.Normal, Theme.MutedColor);
            note.Margin = new Thickness(0, 5, 0, 16);
            heading.Children.Add(note);
            root.Children.Add(heading);

            Border card = Theme.Card();
            card.Margin = new Thickness(0);
            StackPanel form = new StackPanel();
            Grid fields = new Grid();
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            StackPanel widthField = new StackPanel();
            widthField.Children.Add(Theme.Text("宽度（px）", 10.5, FontWeights.SemiBold, Theme.MutedColor));
            _widthBox = CreateNumberBox(currentWidth);
            _widthBox.Margin = new Thickness(0, 7, 0, 0);
            widthField.Children.Add(_widthBox);
            fields.Children.Add(widthField);
            StackPanel heightField = new StackPanel();
            heightField.Children.Add(Theme.Text("高度（px）", 10.5, FontWeights.SemiBold, Theme.MutedColor));
            _heightBox = CreateNumberBox(currentHeight);
            _heightBox.Margin = new Thickness(0, 7, 0, 0);
            heightField.Children.Add(_heightBox);
            Grid.SetColumn(heightField, 2);
            fields.Children.Add(heightField);
            form.Children.Add(fields);
            _error = Theme.Text(string.Empty, 10, FontWeights.Normal, Theme.DangerColor);
            _error.Margin = new Thickness(0, 8, 0, 0);
            form.Children.Add(_error);
            card.Child = form;
            Grid.SetRow(card, 1);
            root.Children.Add(card);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            Button close = Theme.Button("关闭", false);
            close.Click += delegate { Close(); };
            Button save = Theme.Button("保存", true);
            save.Margin = new Thickness(10, 0, 0, 0);
            save.Click += delegate
            {
                int width;
                int height;
                if (!int.TryParse(_widthBox.Text, out width) || !int.TryParse(_heightBox.Text, out height))
                {
                    _error.Text = "宽度和高度需要填写整数。";
                    return;
                }
                SelectedWidth = FloatingAppearanceLimits.ClampWidth(width);
                SelectedHeight = FloatingAppearanceLimits.ClampHeight(height);
                DialogResult = true;
            };
            actions.Children.Add(close);
            actions.Children.Add(save);
            Grid.SetRow(actions, 2);
            root.Children.Add(actions);
            Content = root;
        }

        private static TextBox CreateNumberBox(int value)
        {
            TextBox box = new TextBox
            {
                Text = value.ToString(),
                Height = 38,
                Padding = new Thickness(10, 5, 10, 5),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 12,
                Foreground = Theme.Brush(Theme.TextColor),
                Background = Brushes.White,
                BorderBrush = Theme.Brush(Theme.BorderColor),
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            box.PreviewTextInput += delegate(object sender, TextCompositionEventArgs e)
            {
                foreach (char valueCharacter in e.Text)
                {
                    if (!char.IsDigit(valueCharacter)) e.Handled = true;
                }
            };
            return box;
        }
    }
}
