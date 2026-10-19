using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BluetoothBatteryMonitor
{
    internal sealed class ColorPickerWindow : Window
    {
        private readonly TextBox _hexBox;
        private readonly Border _preview;
        private readonly TextBlock _error;

        public string SelectedColor { get; private set; }

        public ColorPickerWindow(Window owner, string title, string currentColor)
        {
            Owner = owner;
            Title = title;
            Icon = AppIcon.Image;
            Width = 470;
            Height = 430;
            MinWidth = 470;
            MinHeight = 430;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Theme.Brush(Theme.WindowColor);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SourceInitialized += delegate { WindowVisualHelper.ApplyRoundedCorners(this); };
            SelectedColor = Normalize(currentColor) ?? FloatingColorDefaults.Background;

            Grid root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            StackPanel heading = new StackPanel();
            heading.Children.Add(Theme.Text(title, 20, FontWeights.Bold, Theme.TextColor));
            TextBlock note = Theme.Text("选择预设颜色，或在下方输入 HEX 颜色值。", 11, FontWeights.Normal, Theme.MutedColor);
            note.Margin = new Thickness(0, 5, 0, 16);
            heading.Children.Add(note);
            root.Children.Add(heading);

            Border card = Theme.Card();
            card.Margin = new Thickness(0);
            StackPanel content = new StackPanel();
            UniformGrid palette = new UniformGrid { Columns = 8, Rows = 3 };
            foreach (string color in PaletteColors())
            {
                string captured = color;
                Button swatch = new Button
                {
                    Height = 36,
                    Margin = new Thickness(4),
                    Background = Theme.Brush(Theme.ParseColor(color, Colors.White)),
                    BorderBrush = Theme.Brush(Color.FromRgb(205, 216, 229)),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = color,
                    Style = Theme.ColorSwatchButtonStyle
                };
                swatch.Click += delegate { SetColor(captured); };
                palette.Children.Add(swatch);
            }
            content.Children.Add(palette);

            Grid inputRow = new Grid { Margin = new Thickness(4, 16, 4, 0) };
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _preview = new Border
            {
                Width = 46,
                Height = 36,
                CornerRadius = new CornerRadius(9),
                Background = Theme.Brush(Theme.ParseColor(SelectedColor, Colors.White)),
                BorderBrush = Theme.Brush(Theme.BorderColor),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 12, 0)
            };
            inputRow.Children.Add(_preview);
            _hexBox = new TextBox
            {
                Text = SelectedColor,
                Height = 36,
                Padding = new Thickness(12, 5, 12, 5),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = Theme.Brush(Theme.TextColor),
                Background = Brushes.White,
                BorderBrush = Theme.Brush(Theme.BorderColor),
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            System.Windows.Automation.AutomationProperties.SetName(_hexBox, "HEX 颜色值");
            _hexBox.TextChanged += delegate
            {
                string normalized = Normalize(_hexBox.Text);
                if (normalized != null) _preview.Background = Theme.Brush(Theme.ParseColor(normalized, Colors.White));
            };
            Grid.SetColumn(_hexBox, 1);
            inputRow.Children.Add(_hexBox);
            content.Children.Add(inputRow);
            _error = Theme.Text(string.Empty, 10, FontWeights.Normal, Theme.DangerColor);
            _error.Margin = new Thickness(4, 7, 4, 0);
            content.Children.Add(_error);
            card.Child = content;
            Grid.SetRow(card, 1);
            root.Children.Add(card);

            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            Button close = Theme.Button("关闭", false);
            close.Click += delegate { Close(); };
            Button save = Theme.Button("保存", true);
            save.Margin = new Thickness(10, 0, 0, 0);
            save.Click += delegate
            {
                string normalized = Normalize(_hexBox.Text);
                if (normalized == null)
                {
                    _error.Text = "请输入 #RRGGBB 格式，例如 #FFFFFF。";
                    return;
                }
                SelectedColor = normalized;
                DialogResult = true;
            };
            actions.Children.Add(close);
            actions.Children.Add(save);
            Grid.SetRow(actions, 2);
            root.Children.Add(actions);
            Content = root;
        }

        private void SetColor(string color)
        {
            _hexBox.Text = color;
            _preview.Background = Theme.Brush(Theme.ParseColor(color, Colors.White));
            _error.Text = string.Empty;
        }

        private static string Normalize(string value)
        {
            string candidate = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (candidate.Length == 6 && candidate[0] != '#') candidate = "#" + candidate;
            int parsed;
            return candidate.Length == 7 && candidate[0] == '#' &&
                   int.TryParse(candidate.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed)
                ? candidate
                : null;
        }

        private static IEnumerable<string> PaletteColors()
        {
            return new[]
            {
                "#FFFFFF", "#F1F5F9", "#CBD5E1", "#64748B", "#334155", "#1E293B", "#111827", "#000000",
                "#FEE2E2", "#EF4444", "#F97316", "#F59E0B", "#FACC15", "#22C55E", "#14B8A6", "#06B6D4",
                "#DBEAFE", "#3B82F6", "#2563EB", "#4F46E5", "#8B5CF6", "#D946EF", "#EC4899", "#E04343"
            };
        }
    }
}
