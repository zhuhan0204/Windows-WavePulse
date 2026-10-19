using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BluetoothBatteryMonitor
{
    internal sealed class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly SettingsStore _settingsStore;
        private readonly Action _exportLogs;
        private readonly Action<bool> _mergeChanged;
        private readonly Action _mergeOptionsChanged;
        private readonly ToggleSwitch _startupToggle;
        private readonly ToggleSwitch _connectedOnlyToggle;
        private readonly ToggleSwitch _mergeToggle;
        private readonly ToggleSwitch _mergeTopmostToggle;
        private bool _updating;

        public SettingsWindow(
            Window owner,
            AppSettings settings,
            SettingsStore settingsStore,
            Action exportLogs,
            Action<bool> mergeChanged,
            Action mergeOptionsChanged)
        {
            _settings = settings;
            _settingsStore = settingsStore;
            _exportLogs = exportLogs;
            _mergeChanged = mergeChanged;
            _mergeOptionsChanged = mergeOptionsChanged;

            Owner = owner;
            Title = "设置 - WavePulse 无线设备电量助手";
            Icon = AppIcon.Image;
            Width = 610;
            Height = Math.Min(650, SystemParameters.WorkArea.Height - 72);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = Theme.Brush(Theme.WindowColor);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            SourceInitialized += delegate { WindowVisualHelper.ApplyRoundedCorners(this); };
            Closing += delegate { WindowVisualHelper.BringToFront(owner); };

            Grid root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel heading = new StackPanel();
            heading.Children.Add(Theme.Text("应用设置", 22, FontWeights.Bold, Theme.TextColor));
            TextBlock subtitle = Theme.Text("设备偏好和应用设置会自动保存在本地 data 文件夹中。", 11.5, FontWeights.Normal, Theme.MutedColor);
            subtitle.Margin = new Thickness(0, 5, 0, 18);
            heading.Children.Add(subtitle);
            root.Children.Add(heading);

            StackPanel content = new StackPanel();
            Border startupCard = CreateSettingCard();
            Grid startupGrid = new Grid();
            startupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            startupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel startupText = new StackPanel();
            startupText.Children.Add(Theme.Text("开机自动启动", 13, FontWeights.SemiBold, Theme.TextColor));
            TextBlock startupNote = Theme.Text("使用当前 EXE 路径写入当前 Windows 用户的启动项。", 10.5, FontWeights.Normal, Theme.MutedColor);
            startupNote.Margin = new Thickness(0, 4, 0, 0);
            startupText.Children.Add(startupNote);
            startupGrid.Children.Add(startupText);
            _startupToggle = new ToggleSwitch { IsChecked = _settings.StartWithWindows, VerticalAlignment = VerticalAlignment.Center };
            _startupToggle.Checked += OnStartupChanged;
            _startupToggle.Unchecked += OnStartupChanged;
            Grid.SetColumn(_startupToggle, 1);
            startupGrid.Children.Add(_startupToggle);
            startupCard.Child = startupGrid;
            content.Children.Add(startupCard);

            Border filterCard = CreateSettingCard();
            Grid filterGrid = new Grid();
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel filterText = new StackPanel();
            filterText.Children.Add(Theme.Text("仅显示已连接设备", 13, FontWeights.SemiBold, Theme.TextColor));
            TextBlock filterNote = Theme.Text("开启后，主列表隐藏当前未连接的已配对设备。", 10.5, FontWeights.Normal, Theme.MutedColor);
            filterNote.Margin = new Thickness(0, 4, 0, 0);
            filterText.Children.Add(filterNote);
            filterGrid.Children.Add(filterText);
            _connectedOnlyToggle = new ToggleSwitch { IsChecked = _settings.ConnectedOnly, VerticalAlignment = VerticalAlignment.Center };
            _connectedOnlyToggle.Checked += OnConnectedOnlyChanged;
            _connectedOnlyToggle.Unchecked += OnConnectedOnlyChanged;
            Grid.SetColumn(_connectedOnlyToggle, 1);
            filterGrid.Children.Add(_connectedOnlyToggle);
            filterCard.Child = filterGrid;
            content.Children.Add(filterCard);

            Border mergeCard = CreateSettingCard();
            Grid mergeGrid = new Grid();
            mergeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mergeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel mergeText = new StackPanel();
            mergeText.Children.Add(Theme.Text("合并展示设备", 13, FontWeights.SemiBold, Theme.TextColor));
            TextBlock mergeNote = Theme.Text("仅把下方勾选的设备放进合并悬浮窗；按住右侧手柄可拖动排序。各设备独立悬浮窗不受影响，隐藏设备不在此处展示。", 10.5, FontWeights.Normal, Theme.MutedColor);
            mergeNote.Margin = new Thickness(0, 4, 12, 0);
            mergeNote.TextWrapping = TextWrapping.Wrap;
            mergeText.Children.Add(mergeNote);
            mergeGrid.Children.Add(mergeText);
            StackPanel mergeOptions = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            StackPanel mergeSwitchRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            TextBlock mergeSwitchLabel = Theme.Text("合并显示", 10.5, FontWeights.Normal, Theme.MutedColor);
            mergeSwitchLabel.Margin = new Thickness(0, 0, 8, 0);
            _mergeToggle = new ToggleSwitch { IsChecked = _settings.MergeFloatingWindows, VerticalAlignment = VerticalAlignment.Center };
            _mergeToggle.Checked += OnMergeChanged;
            _mergeToggle.Unchecked += OnMergeChanged;
            mergeSwitchRow.Children.Add(mergeSwitchLabel);
            mergeSwitchRow.Children.Add(_mergeToggle);
            mergeOptions.Children.Add(mergeSwitchRow);
            StackPanel topmostSwitchRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            TextBlock topmostSwitchLabel = Theme.Text("合并窗置顶", 10.5, FontWeights.Normal, Theme.MutedColor);
            topmostSwitchLabel.Margin = new Thickness(0, 0, 8, 0);
            _mergeTopmostToggle = new ToggleSwitch { IsChecked = _settings.MergedAlwaysOnTop, VerticalAlignment = VerticalAlignment.Center };
            _mergeTopmostToggle.Checked += OnMergeTopmostChanged;
            _mergeTopmostToggle.Unchecked += OnMergeTopmostChanged;
            topmostSwitchRow.Children.Add(topmostSwitchLabel);
            topmostSwitchRow.Children.Add(_mergeTopmostToggle);
            mergeOptions.Children.Add(topmostSwitchRow);
            Grid.SetColumn(mergeOptions, 1);
            mergeGrid.Children.Add(mergeOptions);
            StackPanel mergeCardContent = new StackPanel();
            mergeCardContent.Children.Add(mergeGrid);
            IList<DevicePreference> mergeCandidates = _settings.Devices
                .Where(delegate(DevicePreference preference) { return !preference.IsHidden; })
                .OrderBy(delegate(DevicePreference preference) { return preference.MergedDisplayOrder; })
                .ThenBy(DeviceDisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            Grid deviceSelectionHeading = new Grid { Margin = new Thickness(0, 13, 0, 8) };
            deviceSelectionHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            deviceSelectionHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            deviceSelectionHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            deviceSelectionHeading.Children.Add(Theme.Text("选择要合并的设备", 10.5, FontWeights.SemiBold, Theme.TextColor));
            TextBlock selectedCount = Theme.Text(string.Empty, 9.5, FontWeights.SemiBold, Theme.AccentColor);
            selectedCount.Margin = new Thickness(0, 0, 8, 0);
            selectedCount.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(selectedCount, 1);
            deviceSelectionHeading.Children.Add(selectedCount);
            Button selectionToggle = new Button
            {
                Content = "\uE70D",
                Width = 28,
                Height = 28,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Style = Theme.IconButtonStyle,
                ToolTip = "展开合并设备选择"
            };
            System.Windows.Automation.AutomationProperties.SetName(selectionToggle, "展开合并设备选择");
            Grid.SetColumn(selectionToggle, 2);
            deviceSelectionHeading.Children.Add(selectionToggle);
            mergeCardContent.Children.Add(deviceSelectionHeading);

            StackPanel deviceSelection = new StackPanel();
            Action updateSelectedCount = delegate
            {
                int selected = mergeCandidates.Count(delegate(DevicePreference preference)
                {
                    return preference.IncludeInMergedFloating;
                });
                selectedCount.Text = "已选择 " + selected + " / " + mergeCandidates.Count;
            };
            Action renderDeviceSelection = null;
            renderDeviceSelection = delegate
            {
                deviceSelection.Children.Clear();
                if (mergeCandidates.Count == 0)
                {
                    TextBlock emptySelection = Theme.Text(
                        _settings.Devices.Any(delegate(DevicePreference preference) { return preference.IsHidden; })
                            ? "没有可选择的设备；隐藏设备不会加入合并展示。"
                            : "刷新设备列表后，可在这里选择设备。",
                        10.5,
                        FontWeights.Normal,
                        Theme.MutedColor);
                    emptySelection.Margin = new Thickness(10, 16, 10, 16);
                    emptySelection.TextAlignment = TextAlignment.Center;
                    deviceSelection.Children.Add(emptySelection);
                }
                else
                {
                    AddMergeDeviceRows(deviceSelection, mergeCandidates, updateSelectedCount, renderDeviceSelection);
                }
                updateSelectedCount();
            };
            Border deviceSelectionSurface = new Border
            {
                Background = Theme.Brush(Color.FromRgb(248, 250, 253)),
                BorderBrush = Theme.Brush(Theme.BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(6),
                Visibility = Visibility.Collapsed
            };
            ScrollViewer deviceSelectionScroll = new ScrollViewer
            {
                Content = deviceSelection,
                MaxHeight = 238,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            deviceSelectionScroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Theme.ThinScrollBarStyle);
            deviceSelectionSurface.Child = deviceSelectionScroll;
            mergeCardContent.Children.Add(deviceSelectionSurface);
            bool selectionExpanded = false;
            selectionToggle.Click += delegate
            {
                selectionExpanded = !selectionExpanded;
                deviceSelectionSurface.Visibility = selectionExpanded ? Visibility.Visible : Visibility.Collapsed;
                selectionToggle.Content = selectionExpanded ? "\uE70E" : "\uE70D";
                selectionToggle.ToolTip = selectionExpanded ? "关闭合并设备选择" : "展开合并设备选择";
                System.Windows.Automation.AutomationProperties.SetName(
                    selectionToggle,
                    selectionExpanded ? "关闭合并设备选择" : "展开合并设备选择");
            };
            renderDeviceSelection();
            mergeCard.Child = mergeCardContent;
            content.Children.Add(mergeCard);

            Border logCard = CreateSettingCard();
            Grid logGrid = new Grid();
            logGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            logGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel logText = new StackPanel();
            logText.Children.Add(Theme.Text("诊断日志", 13, FontWeights.SemiBold, Theme.TextColor));
            TextBlock logNote = Theme.Text("仅保留并导出最近 24 小时的设备 ID、设备地址、电量来源及底层读取结果。", 10.5, FontWeights.Normal, Theme.MutedColor);
            logNote.Margin = new Thickness(0, 4, 12, 0);
            logNote.TextWrapping = TextWrapping.Wrap;
            logText.Children.Add(logNote);
            logGrid.Children.Add(logText);
            Button export = Theme.Button("导出日志", false);
            export.MinWidth = 94;
            export.Click += delegate { _exportLogs(); };
            Grid.SetColumn(export, 1);
            logGrid.Children.Add(export);
            logCard.Child = logGrid;
            content.Children.Add(logCard);

            Border aboutCard = CreateSettingCard();
            Grid aboutGrid = new Grid();
            aboutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            aboutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel aboutText = new StackPanel();
            aboutText.Children.Add(Theme.Text("关于应用", 13, FontWeights.SemiBold, Theme.TextColor));
            TextBlock aboutNote = Theme.Text("查看当前版本、历史更新记录、作者信息、版本声明和免责声明。", 10.5, FontWeights.Normal, Theme.MutedColor);
            aboutNote.Margin = new Thickness(0, 4, 12, 0);
            aboutText.Children.Add(aboutNote);
            aboutGrid.Children.Add(aboutText);
            Button about = Theme.Button("查看关于", false);
            about.MinWidth = 94;
            about.Click += delegate
            {
                new AboutWindow(this).ShowDialog();
            };
            Grid.SetColumn(about, 1);
            aboutGrid.Children.Add(about);
            aboutCard.Child = aboutGrid;
            content.Children.Add(aboutCard);

            ScrollViewer contentScroll = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            contentScroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Theme.ThinScrollBarStyle);
            Grid.SetRow(contentScroll, 1);
            root.Children.Add(contentScroll);

            Grid actions = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Button save = Theme.Button("关闭", true);
            save.Click += delegate
            {
                _settingsStore.Save(_settings);
                Close();
            };
            Grid.SetColumn(save, 1);
            actions.Children.Add(save);
            Grid.SetRow(actions, 2);
            root.Children.Add(actions);

            Content = root;
        }

        private void AddMergeDeviceRows(
            StackPanel host,
            IList<DevicePreference> candidates,
            Action updateSelectedCount,
            Action renderRows)
        {
            foreach (DevicePreference preference in candidates)
            {
                DevicePreference capturedPreference = preference;
                DeviceKind kind = preference.HasCustomKind ? preference.CustomKind : preference.LastKnownKind;
                Grid content = new Grid { MinWidth = 442 };
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

                CheckBox deviceCheck = new CheckBox
                {
                    IsChecked = preference.IncludeInMergedFloating,
                    Style = Theme.ModernCheckBoxStyle,
                    ToolTip = "勾选后加入合并悬浮窗",
                    VerticalAlignment = VerticalAlignment.Center
                };
                content.Children.Add(deviceCheck);
                Border iconSurface = new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(9),
                    Background = Theme.Brush(Theme.AccentSoftColor),
                    Child = DeviceIconFactory.Create(kind, preference.CustomIconPath, 23, false)
                };
                Grid.SetColumn(iconSurface, 1);
                content.Children.Add(iconSurface);
                StackPanel identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                identity.Children.Add(Theme.Text(DeviceDisplayName(preference), 10.5, FontWeights.SemiBold, Theme.TextColor));
                TextBlock meta = Theme.Text(
                    Theme.KindLabel(kind),
                    9,
                    FontWeights.Normal,
                    Theme.MutedColor);
                meta.Margin = new Thickness(0, 2, 0, 0);
                identity.Children.Add(meta);
                Grid.SetColumn(identity, 2);
                content.Children.Add(identity);

                bool receiver = preference.LastKnownTransport == DeviceTransport.Receiver24G;
                Color tagBackground = receiver ? Color.FromRgb(255, 241, 220) : Theme.AccentSoftColor;
                Color tagForeground = receiver ? Color.FromRgb(194, 103, 8) : Theme.AccentColor;
                Border transportTag = new Border
                {
                    Background = Theme.Brush(tagBackground),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(10, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = Theme.Text(receiver ? "2.4G" : "蓝牙", 8.8, FontWeights.SemiBold, tagForeground)
                };
                Grid.SetColumn(transportTag, 3);
                content.Children.Add(transportTag);

                TextBlock dragGlyph = Theme.Text("⠿", 16, FontWeights.SemiBold, Theme.MutedColor);
                dragGlyph.HorizontalAlignment = HorizontalAlignment.Center;
                dragGlyph.VerticalAlignment = VerticalAlignment.Center;
                dragGlyph.IsHitTestVisible = false;
                Thumb dragThumb = new Thumb
                {
                    Width = 34,
                    Height = 36,
                    Opacity = 0,
                    Background = Brushes.Transparent,
                    Cursor = Cursors.SizeAll
                };
                System.Windows.Automation.AutomationProperties.SetName(
                    dragThumb,
                    "拖动排序 " + DeviceDisplayName(preference));
                Grid dragHandleContent = new Grid();
                dragHandleContent.Children.Add(dragThumb);
                dragHandleContent.Children.Add(dragGlyph);
                Border dragHandle = new Border
                {
                    Width = 34,
                    Height = 36,
                    CornerRadius = new CornerRadius(9),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.SizeAll,
                    ToolTip = "按住并拖到目标设备的上半部或下半部",
                    Child = dragHandleContent
                };
                System.Windows.Automation.AutomationProperties.SetName(
                    dragHandle,
                    "拖动排序 " + DeviceDisplayName(preference));
                System.Windows.Automation.AutomationProperties.SetName(
                    dragGlyph,
                    "拖动排序 " + DeviceDisplayName(preference));
                Grid.SetColumn(dragHandle, 4);
                content.Children.Add(dragHandle);

                Border row = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = Theme.Brush(Theme.BorderColor),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 7),
                    Child = content,
                    ToolTip = "拖动右侧手柄可调整合并浮窗中的顺序"
                };
                deviceCheck.Checked += delegate
                {
                    capturedPreference.IncludeInMergedFloating = true;
                    _settingsStore.Save(_settings);
                    updateSelectedCount();
                    if (_mergeOptionsChanged != null) _mergeOptionsChanged();
                };
                deviceCheck.Unchecked += delegate
                {
                    capturedPreference.IncludeInMergedFloating = false;
                    _settingsStore.Save(_settings);
                    updateSelectedCount();
                    if (_mergeOptionsChanged != null) _mergeOptionsChanged();
                };
                bool dragInProgress = false;
                Border activeTargetRow = null;
                DevicePreference activeTargetPreference = null;
                bool activePlaceAfter = false;
                dragHandle.MouseEnter += delegate
                {
                    if (!dragInProgress) dragHandle.Background = Theme.Brush(Theme.AccentSoftColor);
                };
                dragHandle.MouseLeave += delegate
                {
                    if (!dragInProgress) dragHandle.Background = Brushes.Transparent;
                };
                dragThumb.DragStarted += delegate
                {
                    dragInProgress = true;
                    dragHandle.Background = Theme.Brush(Theme.AccentSoftColor);
                    row.Opacity = 0.62;
                };
                dragThumb.DragDelta += delegate
                {
                    AutoScrollMergeSelection(host);
                    Border targetRow;
                    DevicePreference targetPreference;
                    bool placeAfter;
                    if (!TryFindMergeDropTarget(
                        host,
                        candidates,
                        capturedPreference,
                        Mouse.GetPosition(host),
                        out targetRow,
                        out targetPreference,
                        out placeAfter))
                    {
                        if (activeTargetRow != null) ResetMergeDropTarget(activeTargetRow);
                        activeTargetRow = null;
                        activeTargetPreference = null;
                        return;
                    }
                    if (activeTargetRow != null && !ReferenceEquals(activeTargetRow, targetRow))
                    {
                        ResetMergeDropTarget(activeTargetRow);
                    }
                    activeTargetRow = targetRow;
                    activeTargetPreference = targetPreference;
                    activePlaceAfter = placeAfter;
                    UpdateMergeDropTarget(activeTargetRow, activePlaceAfter);
                };
                dragThumb.DragCompleted += delegate(object sender, DragCompletedEventArgs args)
                {
                    if (activeTargetRow != null) ResetMergeDropTarget(activeTargetRow);
                    row.Opacity = 1;
                    dragHandle.Background = Brushes.Transparent;
                    dragInProgress = false;
                    DevicePreference targetPreference = activeTargetPreference;
                    bool placeAfter = activePlaceAfter;
                    activeTargetRow = null;
                    activeTargetPreference = null;
                    if (!args.Canceled && targetPreference != null &&
                        MoveMergedDevice(candidates, capturedPreference, targetPreference, placeAfter))
                    {
                        Dispatcher.BeginInvoke(new Action(renderRows), DispatcherPriority.Background);
                    }
                };
                host.Children.Add(row);
            }
        }

        private static bool TryFindMergeDropTarget(
            StackPanel host,
            IList<DevicePreference> candidates,
            DevicePreference source,
            Point position,
            out Border targetRow,
            out DevicePreference targetPreference,
            out bool placeAfter)
        {
            targetRow = null;
            targetPreference = null;
            placeAfter = false;
            int count = Math.Min(host.Children.Count, candidates.Count);
            for (int index = 0; index < count; index++)
            {
                DevicePreference candidate = candidates[index];
                if (ReferenceEquals(candidate, source)) continue;
                Border candidateRow = host.Children[index] as Border;
                if (candidateRow == null || candidateRow.ActualHeight <= 0) continue;
                Point origin = candidateRow.TranslatePoint(new Point(0, 0), host);
                double top = origin.Y - 4;
                double bottom = origin.Y + candidateRow.ActualHeight + 4;
                if (position.Y < top || position.Y > bottom) continue;
                targetRow = candidateRow;
                targetPreference = candidate;
                placeAfter = position.Y >= origin.Y + (candidateRow.ActualHeight / 2.0);
                return true;
            }
            return false;
        }

        private static void UpdateMergeDropTarget(Border row, bool placeAfter)
        {
            row.BorderBrush = Theme.Brush(Theme.AccentColor);
            row.BorderThickness = placeAfter
                ? new Thickness(1, 1, 1, 3)
                : new Thickness(1, 3, 1, 1);
            row.Background = Theme.Brush(Color.FromRgb(244, 249, 255));
        }

        private static void ResetMergeDropTarget(Border row)
        {
            row.BorderBrush = Theme.Brush(Theme.BorderColor);
            row.BorderThickness = new Thickness(1);
            row.Background = Brushes.White;
        }

        private static void AutoScrollMergeSelection(DependencyObject origin)
        {
            DependencyObject current = origin;
            ScrollViewer scroll = null;
            while (current != null)
            {
                scroll = current as ScrollViewer;
                if (scroll != null) break;
                current = VisualTreeHelper.GetParent(current);
            }
            if (scroll == null || scroll.ScrollableHeight <= 0) return;
            Point position = Mouse.GetPosition(scroll);
            const double edge = 34;
            const double step = 18;
            if (position.Y < edge)
            {
                scroll.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset - step));
            }
            else if (position.Y > scroll.ViewportHeight - edge)
            {
                scroll.ScrollToVerticalOffset(Math.Min(scroll.ScrollableHeight, scroll.VerticalOffset + step));
            }
        }

        private bool MoveMergedDevice(
            IList<DevicePreference> candidates,
            DevicePreference source,
            DevicePreference target,
            bool placeAfter)
        {
            if (!MergedDeviceOrdering.Move(_settings, candidates, source, target, placeAfter)) return false;
            _settingsStore.Save(_settings);
            if (_mergeOptionsChanged != null) _mergeOptionsChanged();
            return true;
        }

        private static string DeviceDisplayName(DevicePreference preference)
        {
            if (!string.IsNullOrWhiteSpace(preference.CustomName)) return preference.CustomName;
            if (!string.IsNullOrWhiteSpace(preference.LastKnownName)) return preference.LastKnownName;
            return preference.LastKnownTransport == DeviceTransport.Receiver24G
                ? "未命名 2.4G 设备"
                : "未命名蓝牙设备";
        }

        private static Border CreateSettingCard()
        {
            Border card = Theme.Card();
            card.Padding = new Thickness(16, 13, 16, 13);
            card.Margin = new Thickness(0, 0, 0, 10);
            return card;
        }

        private void OnStartupChanged(object sender, RoutedEventArgs e)
        {
            if (_updating) return;
            bool enabled = _startupToggle.IsChecked == true;
            string error;
            if (!StartupManager.TrySetEnabled(enabled, out error))
            {
                _updating = true;
                _startupToggle.IsChecked = !enabled;
                _updating = false;
                AppToast.ShowError(this, "开机自启设置失败", error);
                return;
            }
            _settings.StartWithWindows = enabled;
            _settingsStore.Save(_settings);
        }

        private void OnMergeChanged(object sender, RoutedEventArgs e)
        {
            if (_updating) return;
            _settings.MergeFloatingWindows = _mergeToggle.IsChecked == true;
            _settingsStore.Save(_settings);
            if (_mergeChanged != null) _mergeChanged(_settings.MergeFloatingWindows);
        }

        private void OnConnectedOnlyChanged(object sender, RoutedEventArgs e)
        {
            if (_updating) return;
            _settings.ConnectedOnly = _connectedOnlyToggle.IsChecked == true;
            _settingsStore.Save(_settings);
        }

        private void OnMergeTopmostChanged(object sender, RoutedEventArgs e)
        {
            if (_updating) return;
            _settings.MergedAlwaysOnTop = _mergeTopmostToggle.IsChecked == true;
            _settingsStore.Save(_settings);
            if (_mergeOptionsChanged != null) _mergeOptionsChanged();
        }
    }
}
