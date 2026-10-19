using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BluetoothBatteryMonitor
{
    internal sealed class ReceiverSupportWindow : Window
    {
        private readonly IList<ReceiverSupportBrand> _groups;
        private readonly ComboBox _brandFilter;
        private readonly ComboBox _typeFilter;
        private readonly TextBox _modelFilter;
        private readonly StackPanel _rows;
        private readonly TextBlock _resultCount;

        public ReceiverSupportWindow(Window owner)
        {
            Owner = owner;
            Title = "2.4G 适配设备 - WavePulse";
            Icon = AppIcon.Image;
            Width = 940;
            Height = 540;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Theme.Brush(Theme.WindowColor);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SourceInitialized += delegate { WindowVisualHelper.ApplyRoundedCorners(this); };

            _groups = ReceiverSupportCatalog.GetDeviceGroups();

            Grid root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid heading = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel headingText = new StackPanel();
            headingText.Children.Add(Theme.Text("2.4G 适配设备", 20, FontWeights.Bold, Theme.TextColor));
            TextBlock subtitle = Theme.Text("按品牌归类查看设备；标注“电量待适配”的型号目前仅完成识别", 10.5, FontWeights.Normal, Theme.MutedColor);
            subtitle.Margin = new Thickness(0, 5, 0, 0);
            headingText.Children.Add(subtitle);
            heading.Children.Add(headingText);
            _resultCount = Theme.Text(string.Empty, 10.5, FontWeights.SemiBold, Theme.AccentColor);
            _resultCount.VerticalAlignment = VerticalAlignment.Bottom;
            _resultCount.Margin = new Thickness(16, 0, 0, 2);
            Grid.SetColumn(_resultCount, 1);
            heading.Children.Add(_resultCount);
            root.Children.Add(heading);

            Border filters = new Border
            {
                Background = Brushes.White,
                BorderBrush = Theme.Brush(Theme.BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid filterGrid = new Grid();
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel brandField = new StackPanel();
            brandField.Children.Add(Theme.Text("品牌", 9.5, FontWeights.SemiBold, Theme.MutedColor));
            _brandFilter = new ComboBox
            {
                Height = 38,
                Margin = new Thickness(0, 6, 0, 0),
                Style = Theme.ModernComboBoxStyle
            };
            _brandFilter.Items.Add("全部品牌");
            foreach (ReceiverSupportBrand group in _groups) _brandFilter.Items.Add(group.Brand);
            _brandFilter.SelectedIndex = 0;
            _brandFilter.SelectionChanged += delegate { RenderRows(); };
            brandField.Children.Add(_brandFilter);
            filterGrid.Children.Add(brandField);

            StackPanel typeField = new StackPanel();
            typeField.Children.Add(Theme.Text("类型", 9.5, FontWeights.SemiBold, Theme.MutedColor));
            _typeFilter = new ComboBox
            {
                Height = 38,
                Margin = new Thickness(0, 6, 0, 0),
                Style = Theme.ModernComboBoxStyle
            };
            _typeFilter.Items.Add("全部类型");
            _typeFilter.Items.Add("鼠标");
            _typeFilter.Items.Add("键盘");
            _typeFilter.Items.Add("耳机");
            _typeFilter.SelectedIndex = 0;
            _typeFilter.SelectionChanged += delegate { RenderRows(); };
            typeField.Children.Add(_typeFilter);
            Grid.SetColumn(typeField, 2);
            filterGrid.Children.Add(typeField);

            StackPanel modelField = new StackPanel();
            modelField.Children.Add(Theme.Text("型号", 9.5, FontWeights.SemiBold, Theme.MutedColor));
            _modelFilter = new TextBox
            {
                Height = 38,
                Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(11, 5, 11, 5),
                VerticalContentAlignment = VerticalAlignment.Center,
                Style = Theme.ModernTextBoxStyle,
                ToolTip = "输入型号关键字，例如 M750"
            };
            _modelFilter.TextChanged += delegate { RenderRows(); };
            modelField.Children.Add(_modelFilter);
            Grid.SetColumn(modelField, 4);
            filterGrid.Children.Add(modelField);
            filters.Child = filterGrid;
            Grid.SetRow(filters, 1);
            root.Children.Add(filters);

            Border table = new Border
            {
                Background = Brushes.White,
                BorderBrush = Theme.Brush(Theme.BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(1)
            };
            Grid tableGrid = new Grid();
            tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Border tableHeader = new Border
            {
                Background = Theme.Brush(Theme.AccentSoftColor),
                CornerRadius = new CornerRadius(11, 11, 0, 0),
                Padding = new Thickness(18, 10, 18, 10)
            };
            Grid headerGrid = CreateColumns();
            headerGrid.Children.Add(Theme.Text("设备品牌", 10.5, FontWeights.SemiBold, Color.FromRgb(20, 88, 180)));
            TextBlock mouseHeader = Theme.Text("鼠标", 10.5, FontWeights.SemiBold, Color.FromRgb(20, 88, 180));
            Grid.SetColumn(mouseHeader, 1);
            headerGrid.Children.Add(mouseHeader);
            TextBlock keyboardHeader = Theme.Text("键盘", 10.5, FontWeights.SemiBold, Color.FromRgb(20, 88, 180));
            Grid.SetColumn(keyboardHeader, 2);
            headerGrid.Children.Add(keyboardHeader);
            TextBlock headphoneHeader = Theme.Text("耳机", 10.5, FontWeights.SemiBold, Color.FromRgb(20, 88, 180));
            Grid.SetColumn(headphoneHeader, 3);
            headerGrid.Children.Add(headphoneHeader);
            tableHeader.Child = headerGrid;
            tableGrid.Children.Add(tableHeader);

            _rows = new StackPanel();
            ScrollViewer rowsScroll = new ScrollViewer
            {
                Content = _rows,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            rowsScroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Theme.ThinScrollBarStyle);
            Grid.SetRow(rowsScroll, 1);
            tableGrid.Children.Add(rowsScroll);
            table.Child = tableGrid;
            Grid.SetRow(table, 2);
            root.Children.Add(table);

            Grid footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock hint = Theme.Text(ReceiverSupportCatalog.UnknownBrandHint, 9.5, FontWeights.Normal, Theme.MutedColor);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.Margin = new Thickness(0, 0, 18, 0);
            footer.Children.Add(hint);
            Button close = Theme.Button("关闭", false);
            close.MinWidth = 88;
            close.IsCancel = true;
            close.Click += delegate { Close(); };
            Grid.SetColumn(close, 1);
            footer.Children.Add(close);
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;
            RenderRows();
        }

        private void RenderRows()
        {
            if (_rows == null || _brandFilter == null || _typeFilter == null || _modelFilter == null) return;
            string selectedBrand = _brandFilter.SelectedItem as string ?? "全部品牌";
            string selectedType = _typeFilter.SelectedItem as string ?? "全部类型";
            string modelQuery = (_modelFilter.Text ?? string.Empty).Trim();
            List<VisibleBrandGroup> visible = new List<VisibleBrandGroup>();

            foreach (ReceiverSupportBrand group in _groups)
            {
                bool brandMatches = selectedBrand == "全部品牌" ||
                                    string.Equals(group.Brand, selectedBrand, StringComparison.CurrentCultureIgnoreCase);
                if (!brandMatches) continue;

                IList<string> mouseModels = selectedType != "全部类型" && selectedType != "鼠标"
                    ? new List<string>()
                    : FilterModels(group.MouseModels, modelQuery);
                IList<string> keyboardModels = selectedType != "全部类型" && selectedType != "键盘"
                    ? new List<string>()
                    : FilterModels(group.KeyboardModels, modelQuery);
                IList<string> headphoneModels = selectedType != "全部类型" && selectedType != "耳机"
                    ? new List<string>()
                    : FilterModels(group.HeadphoneModels, modelQuery);
                if (mouseModels.Count == 0 && keyboardModels.Count == 0 && headphoneModels.Count == 0) continue;

                visible.Add(new VisibleBrandGroup
                {
                    Brand = group.Brand,
                    MouseModels = mouseModels,
                    KeyboardModels = keyboardModels,
                    HeadphoneModels = headphoneModels
                });
            }

            _rows.Children.Clear();
            foreach (VisibleBrandGroup group in visible)
            {
                Border row = new Border
                {
                    BorderBrush = Theme.Brush(Theme.BorderColor),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(18, 13, 18, 13)
                };
                Grid columns = CreateColumns();
                TextBlock brand = Theme.Text(group.Brand, 11, FontWeights.SemiBold, Theme.TextColor);
                brand.VerticalAlignment = VerticalAlignment.Center;
                columns.Children.Add(brand);
                FrameworkElement mouseModels = CreateModelTags(group.MouseModels, DeviceKind.Mouse);
                Grid.SetColumn(mouseModels, 1);
                columns.Children.Add(mouseModels);
                FrameworkElement keyboardModels = CreateModelTags(group.KeyboardModels, DeviceKind.Keyboard);
                Grid.SetColumn(keyboardModels, 2);
                columns.Children.Add(keyboardModels);
                FrameworkElement headphoneModels = CreateModelTags(group.HeadphoneModels, DeviceKind.Headphones);
                Grid.SetColumn(headphoneModels, 3);
                columns.Children.Add(headphoneModels);
                row.Child = columns;
                _rows.Children.Add(row);
            }

            if (visible.Count == 0)
            {
                TextBlock empty = Theme.Text("没有找到匹配的品牌、类型或型号", 11, FontWeights.Normal, Theme.MutedColor);
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Margin = new Thickness(0, 34, 0, 34);
                _rows.Children.Add(empty);
            }

            int modelCount = visible.Sum(delegate(VisibleBrandGroup group)
            {
                return group.MouseModels.Count + group.KeyboardModels.Count + group.HeadphoneModels.Count;
            });
            _resultCount.Text = "共 " + modelCount + " 个型号 · " + visible.Count + " 个品牌";
        }

        private static IList<string> FilterModels(IList<string> models, string query)
        {
            if (models == null) return new List<string>();
            return models.Where(delegate(string model)
            {
                return query.Length == 0 || model.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
            }).ToList();
        }

        private static FrameworkElement CreateModelTags(IList<string> models, DeviceKind kind)
        {
            if (models == null || models.Count == 0)
            {
                TextBlock empty = Theme.Text("—", 11, FontWeights.Normal, Theme.MutedColor);
                empty.VerticalAlignment = VerticalAlignment.Center;
                return empty;
            }

            WrapPanel panel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            Color foreground = kind == DeviceKind.Mouse
                ? Color.FromRgb(28, 99, 203)
                : kind == DeviceKind.Keyboard
                    ? Color.FromRgb(31, 130, 91)
                    : Color.FromRgb(132, 76, 190);
            Color background = kind == DeviceKind.Mouse
                ? Color.FromRgb(234, 243, 255)
                : kind == DeviceKind.Keyboard
                    ? Color.FromRgb(233, 248, 241)
                    : Color.FromRgb(245, 237, 255);
            foreach (string model in models)
            {
                TextBlock label = Theme.Text(
                    model,
                    10.5,
                    FontWeights.SemiBold,
                    foreground);
                panel.Children.Add(new Border
                {
                    Background = Theme.Brush(background),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 2, 7, 2),
                    Child = label
                });
            }
            return panel;
        }

        private static Grid CreateColumns()
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        private sealed class VisibleBrandGroup
        {
            public string Brand { get; set; }
            public IList<string> MouseModels { get; set; }
            public IList<string> KeyboardModels { get; set; }
            public IList<string> HeadphoneModels { get; set; }
        }
    }
}
