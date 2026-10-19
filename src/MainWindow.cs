using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BluetoothBatteryMonitor
{
    internal sealed class MainWindow : Window
    {
        private readonly BluetoothBatteryReader _reader;
        private readonly SettingsStore _settingsStore;
        private readonly DiagnosticLogger _diagnosticLogger;
        private readonly AppSettings _settings;
        private readonly Dictionary<string, FloatingBatteryWindow> _floatingWindows;
        private readonly Dictionary<string, DispatcherTimer> _deviceRefreshTimers;
        private readonly Dictionary<string, Border> _deviceCards;
        private readonly HashSet<string> _deviceRefreshInProgress;
        private readonly SemaphoreSlim _readerGate;
        private readonly DispatcherTimer _displayMonitorTimer;
        private string _displayTopologySignature;
        private bool _displayTopologySettling;
        private int _displayStableTicks;
        private IList<BluetoothDeviceBattery> _devices;
        private MergedFloatingWindow _mergedFloatingWindow;
        private CancellationTokenSource _scanCancellation;
        private bool _isScanning;
        private bool _isClosing;
        private bool _allowExit;
        private bool _initializationQueued;
        private System.Windows.Forms.NotifyIcon _trayIcon;
        private TrayMenuWindow _trayMenuWindow;

        private StackPanel _devicePanel;
        private TextBlock _statusText;
        private TextBlock _summaryText;
        private Button _refreshButton;
        private SettingsWindow _settingsWindow;
        private HiddenDevicesWindow _hiddenDevicesWindow;
        private DeviceTransport _selectedTransport = DeviceTransport.Bluetooth;
        private Button _bluetoothTabButton;
        private Button _receiverTabButton;
        private TextBlock _bluetoothTabText;
        private TextBlock _receiverTabText;
        private Button _supportedDevicesButton;
        private Button _hiddenDevicesButton;

        public MainWindow()
        {
            _reader = new BluetoothBatteryReader();
            _settingsStore = new SettingsStore();
            _settings = _settingsStore.Load();
            _diagnosticLogger = new DiagnosticLogger(_settingsStore.DataDirectory);
            _diagnosticLogger.LogStartup();
            _devices = new List<BluetoothDeviceBattery>();
            _floatingWindows = new Dictionary<string, FloatingBatteryWindow>(StringComparer.OrdinalIgnoreCase);
            _deviceRefreshTimers = new Dictionary<string, DispatcherTimer>(StringComparer.OrdinalIgnoreCase);
            _deviceCards = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
            _deviceRefreshInProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _readerGate = new SemaphoreSlim(1, 1);
            _displayTopologySignature = FloatingWindowPlacement.GetTopologySignature();
            _displayMonitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _displayMonitorTimer.Tick += OnDisplayMonitorTick;

            Title = "WavePulse 无线设备电量助手";
            Icon = AppIcon.Image;
            Width = 1080;
            Height = 780;
            MinWidth = 920;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Theme.Brush(Theme.WindowColor);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            ShowInTaskbar = true;
            SourceInitialized += delegate { WindowVisualHelper.ApplyRoundedCorners(this); };

            Content = BuildInterface();
            InitializeTrayIcon();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanging += OnDisplaySettingsChanged;

            Closing += OnClosing;
        }

        internal void QueueInitialization(bool startedInBackground)
        {
            if (_initializationQueued) return;
            _initializationQueued = true;
            _displayMonitorTimer.Start();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async delegate
            {
                if (!string.IsNullOrWhiteSpace(_settingsStore.IntegrityWarning) && IsVisible)
                {
                    AppToast.ShowWarning(this, "设置完整性保护", _settingsStore.IntegrityWarning);
                }
                if (_settings.StartWithWindows)
                {
                    string startupError;
                    if (!StartupManager.TrySetEnabled(true, out startupError))
                    {
                        _diagnosticLogger.LogMessage("刷新开机启动项失败：" + startupError);
                    }
                }
                RestoreRememberedFloatingWindows();
                UpdateMergedFloatingWindow();
                if (startedInBackground)
                {
                    _diagnosticLogger.LogMessage("开机自启：立即刷新一次电量，不等待设备的定时刷新周期。");
                }
                await RefreshDevicesAsync(!startedInBackground);
            }));
        }

        private UIElement BuildInterface()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(126) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Border header = BuildHeader();
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            UIElement transportSwitch = BuildTransportSwitch();
            Grid.SetRow(transportSwitch, 1);
            root.Children.Add(transportSwitch);

            ScrollViewer scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(28, 14, 24, 20)
            };
            scrollViewer.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Theme.ThinScrollBarStyle);
            _devicePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 72) };
            scrollViewer.Content = _devicePanel;
            Grid.SetRow(scrollViewer, 2);
            root.Children.Add(scrollViewer);

            StackPanel floatingActions = BuildFloatingActions();
            Grid.SetRow(floatingActions, 2);
            Panel.SetZIndex(floatingActions, 20);
            root.Children.Add(floatingActions);

            return root;
        }

        private Grid BuildTransportSwitch()
        {
            Grid row = new Grid { Margin = new Thickness(28, 16, 28, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border host = new Border
            {
                Background = Theme.Brush(Color.FromRgb(233, 239, 247)),
                BorderBrush = Theme.Brush(Color.FromRgb(218, 227, 238)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(15),
                Padding = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            StackPanel tabs = new StackPanel { Orientation = Orientation.Horizontal };
            _bluetoothTabButton = CreateTransportTab(DeviceTransport.Bluetooth, "\uE702", out _bluetoothTabText);
            _receiverTabButton = CreateTransportTab(DeviceTransport.Receiver24G, "\uE88E", out _receiverTabText);
            _receiverTabButton.Margin = new Thickness(4, 0, 0, 0);
            tabs.Children.Add(_bluetoothTabButton);
            tabs.Children.Add(_receiverTabButton);
            host.Child = tabs;
            row.Children.Add(host);

            _supportedDevicesButton = Theme.Button("查看适配设备", false);
            _supportedDevicesButton.MinWidth = 132;
            _supportedDevicesButton.Height = 42;
            _supportedDevicesButton.Padding = new Thickness(16, 0, 16, 0);
            _supportedDevicesButton.ToolTip = "按品牌和型号查看已适配的 2.4G 无线设备";
            _supportedDevicesButton.Click += delegate { ShowReceiverSupportWindow(); };
            System.Windows.Automation.AutomationProperties.SetName(_supportedDevicesButton, "查看适配设备");
            Grid.SetColumn(_supportedDevicesButton, 2);
            row.Children.Add(_supportedDevicesButton);
            UpdateTransportSwitch();
            return row;
        }

        private Button CreateTransportTab(DeviceTransport transport, string glyphText, out TextBlock label)
        {
            StackPanel content = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock glyph = Theme.Text(glyphText, 14, FontWeights.Normal, Theme.AccentColor);
            glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
            glyph.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(glyph);
            label = Theme.Text(string.Empty, 11.5, FontWeights.SemiBold, Theme.TextColor);
            label.Margin = new Thickness(8, 0, 0, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(label);

            Button button = new Button
            {
                Content = content,
                MinWidth = 142,
                Height = 42,
                Padding = new Thickness(16, 0, 16, 0),
                BorderThickness = new Thickness(1),
                Style = Theme.ModernButtonStyle
            };
            System.Windows.Automation.AutomationProperties.SetName(
                button,
                transport == DeviceTransport.Bluetooth ? "蓝牙设备" : "2.4G 设备");
            button.Click += delegate
            {
                if (_selectedTransport == transport) return;
                _selectedTransport = transport;
                UpdateTransportSwitch();
                RenderDevices();
            };
            return button;
        }

        private void UpdateTransportSwitch()
        {
            int bluetoothCount = _devices == null ? 0 : _devices.Count(delegate(BluetoothDeviceBattery item)
            {
                return item.Transport == DeviceTransport.Bluetooth && !IsDeviceHidden(item.StableId);
            });
            int receiverCount = _devices == null ? 0 : _devices.Count(delegate(BluetoothDeviceBattery item)
            {
                return item.Transport == DeviceTransport.Receiver24G && !IsDeviceHidden(item.StableId);
            });
            if (_bluetoothTabText != null) _bluetoothTabText.Text = "蓝牙设备（" + bluetoothCount + "）";
            if (_receiverTabText != null) _receiverTabText.Text = "2.4G 设备（" + receiverCount + "）";
            ApplyTransportTabState(_bluetoothTabButton, _selectedTransport == DeviceTransport.Bluetooth);
            ApplyTransportTabState(_receiverTabButton, _selectedTransport == DeviceTransport.Receiver24G);
            if (_supportedDevicesButton != null)
            {
                _supportedDevicesButton.Visibility = _selectedTransport == DeviceTransport.Receiver24G
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private static void ApplyTransportTabState(Button button, bool selected)
        {
            if (button == null) return;
            button.Background = Theme.Brush(selected ? Colors.White : Color.FromArgb(0, 255, 255, 255));
            button.BorderBrush = Theme.Brush(selected ? Color.FromRgb(204, 219, 237) : Color.FromArgb(0, 255, 255, 255));
        }

        private Border BuildHeader()
        {
            LinearGradientBrush gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(7, 27, 62), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(13, 65, 137), 0.58));
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(24, 119, 221), 1));

            Border header = new Border
            {
                Background = gradient,
                Padding = new Thickness(30, 20, 32, 20)
            };
            Grid grid = new Grid();
            grid.Background = Brushes.Transparent;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border logo = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(20),
                Background = Theme.Brush(Color.FromArgb(28, 255, 255, 255)),
                BorderBrush = Theme.Brush(Color.FromArgb(55, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 18, 0),
                Child = new Image
                {
                    Source = AppIcon.Image,
                    Width = 54,
                    Height = 54,
                    Stretch = Stretch.Uniform
                }
            };
            grid.Children.Add(logo);

            StackPanel titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock eyebrow = Theme.Text("WAVEPULSE  ·  WIRELESS BATTERY", 9.5, FontWeights.SemiBold, Color.FromRgb(142, 196, 255));
            eyebrow.Margin = new Thickness(0, 0, 0, 4);
            titles.Children.Add(eyebrow);
            titles.Children.Add(Theme.Text("WavePulse 无线设备电量助手", 24, FontWeights.Bold, Colors.White));
            TextBlock subtitle = Theme.Text("统一查看蓝牙与 2.4G 设备电量，让状态清晰地留在桌面上", 11.5, FontWeights.Normal, Color.FromRgb(207, 226, 249));
            subtitle.Margin = new Thickness(0, 5, 0, 0);
            titles.Children.Add(subtitle);
            Grid.SetColumn(titles, 1);
            grid.Children.Add(titles);

            Border versionCard = new Border
            {
                Background = Theme.Brush(Color.FromArgb(25, 255, 255, 255)),
                BorderBrush = Theme.Brush(Color.FromArgb(48, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 9, 14, 9),
                VerticalAlignment = VerticalAlignment.Center
            };
            StackPanel versionText = new StackPanel();
            versionText.Children.Add(Theme.Text("DESKTOP STATUS", 8.5, FontWeights.SemiBold, Color.FromRgb(142, 196, 255)));
            TextBlock versionLine = Theme.Text("设备电量中心  ·  v0.1", 10.5, FontWeights.SemiBold, Colors.White);
            versionLine.Margin = new Thickness(0, 3, 0, 0);
            versionText.Children.Add(versionLine);
            versionCard.Child = versionText;
            Grid.SetColumn(versionCard, 2);
            grid.Children.Add(versionCard);

            header.Child = grid;
            return header;
        }

        private StackPanel BuildFloatingActions()
        {
            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 34, 30),
                Background = Brushes.Transparent
            };
            _hiddenDevicesButton = new Button
            {
                Content = "\uE890",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                Style = Theme.FloatingActionButtonStyle,
                ToolTip = "查看隐藏设备"
            };
            System.Windows.Automation.AutomationProperties.SetName(_hiddenDevicesButton, "查看隐藏设备");
            _hiddenDevicesButton.Click += delegate { ShowHiddenDevicesWindow(); };
            actions.Children.Add(_hiddenDevicesButton);

            Button settingsButton = new Button
            {
                Content = "\uE713",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                Style = Theme.FloatingActionButtonStyle,
                ToolTip = "设置",
                Margin = new Thickness(12, 0, 0, 0)
            };
            System.Windows.Automation.AutomationProperties.SetName(settingsButton, "设置");
            settingsButton.Click += delegate { ShowSettingsWindow(); };
            actions.Children.Add(settingsButton);

            _refreshButton = new Button
            {
                Content = "\uE72C",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                Style = Theme.FloatingActionButtonStyle,
                ToolTip = "刷新列表",
                Margin = new Thickness(12, 0, 0, 0)
            };
            System.Windows.Automation.AutomationProperties.SetName(_refreshButton, "刷新列表");
            _refreshButton.Click += async delegate { await RefreshDevicesAsync(true); };
            actions.Children.Add(_refreshButton);
            return actions;
        }

        private async Task RefreshDevicesAsync(bool userInitiated)
        {
            if (_isScanning) return;
            _isScanning = true;
            _refreshButton.IsEnabled = false;
            _refreshButton.ToolTip = "正在刷新设备列表…";
            ShowLoadingState();
            _scanCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                await _readerGate.WaitAsync(_scanCancellation.Token);
                try
                {
                    _devices = await _reader.ScanAsync(_scanCancellation.Token);
                }
                finally
                {
                    _readerGate.Release();
                }
                ApplyPreferencesToDevices();
                MergeRememberedDevicesIntoScan();
                RecordRefreshAttemptForRememberedDevices(DateTime.Now);
                _settingsStore.ClearScanError();
                _diagnosticLogger.LogScan(_reader.LastDiagnosticDetails, _devices);
                RenderDevices();
                RestoreAndUpdateFloatingWindows();
                ConfigureAllDeviceTimers();
                UpdateMergedFloatingWindow();
            }
            catch (OperationCanceledException)
            {
                if (!_isClosing)
                {
                    _settingsStore.WriteScanError("无线设备扫描超时（30 秒）。");
                    _diagnosticLogger.LogMessage("无线设备扫描超时（30 秒）。");
                    MarkAllDevicesOffline("设备读取超时", DateTime.Now);
                    ShowErrorState("设备读取超时", "设备响应较慢，请唤醒设备、检查蓝牙或 2.4G 接收器后再刷新。");
                }
            }
            catch (Exception ex)
            {
                _settingsStore.WriteScanError(ex.ToString());
                _diagnosticLogger.LogError("扫描无线设备", ex);
                MarkAllDevicesOffline(FriendlyScanError(ex), DateTime.Now);
                ShowErrorState("无法读取无线设备", FriendlyScanError(ex));
            }
            finally
            {
                _isScanning = false;
                _refreshButton.IsEnabled = true;
                _refreshButton.ToolTip = "刷新列表";
                if (_scanCancellation != null)
                {
                    _scanCancellation.Dispose();
                    _scanCancellation = null;
                }
            }
        }

        private void ApplyPreferencesToDevices()
        {
            foreach (BluetoothDeviceBattery device in _devices)
            {
                ApplyPreferenceToDevice(device);
            }
            _settingsStore.Save(_settings);
        }

        private void MergeRememberedDevicesIntoScan()
        {
            foreach (DevicePreference remembered in _settings.Devices)
            {
                bool alreadyPresent = _devices.Any(delegate(BluetoothDeviceBattery item)
                {
                    return string.Equals(item.StableId, remembered.StableId, StringComparison.OrdinalIgnoreCase);
                });
                if (!alreadyPresent) _devices.Add(CreateRememberedDevice(remembered));
            }
        }

        private DevicePreference ApplyPreferenceToDevice(BluetoothDeviceBattery device)
        {
            if (string.IsNullOrWhiteSpace(device.DetectedName)) device.DetectedName = device.Name;
            if (device.DetectedKind == DeviceKind.Unknown) device.DetectedKind = device.Kind;
            DevicePreference preference = _settingsStore.GetOrCreatePreference(_settings, device);
            device.Name = string.IsNullOrWhiteSpace(preference.CustomName)
                ? device.DetectedName
                : preference.CustomName;
            device.Kind = preference.HasCustomKind ? preference.CustomKind : device.DetectedKind;
            ApplyEarbudSampleHistory(device, preference);
            if (device.LastRefreshedAt != default(DateTime))
            {
                preference.LastRefreshUtcTicks = device.LastRefreshedAt.ToUniversalTime().Ticks;
            }
            else if (preference.LastRefreshUtcTicks > 0)
            {
                device.LastRefreshedAt = new DateTime(preference.LastRefreshUtcTicks, DateTimeKind.Utc).ToLocalTime();
            }
            return preference;
        }

        private DevicePreference FindPreference(string stableId)
        {
            return _settings.Devices.Find(delegate(DevicePreference preference)
            {
                return string.Equals(preference.StableId, stableId, StringComparison.OrdinalIgnoreCase);
            });
        }

        private bool IsDeviceHidden(string stableId)
        {
            DevicePreference preference = FindPreference(stableId);
            return preference != null && preference.IsHidden;
        }

        private void UpdateHiddenDevicesButton()
        {
            if (_hiddenDevicesButton == null) return;
            int hiddenCount = _settings.Devices.Count(delegate(DevicePreference preference) { return preference.IsHidden; });
            _hiddenDevicesButton.ToolTip = hiddenCount == 0
                ? "查看隐藏设备"
                : "查看隐藏设备（" + hiddenCount + "）";
        }

        private void ApplyEarbudSampleHistory(BluetoothDeviceBattery device, DevicePreference preference)
        {
            if (device == null || preference == null || !device.IsConnected ||
                device.Kind != DeviceKind.Headphones || !device.BatteryPercent.HasValue ||
                string.IsNullOrWhiteSpace(device.BatterySource) ||
                device.BatterySource.IndexOf("Windows HFP/PnP", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            EarbudSampleInference.Apply(
                device,
                preference,
                _diagnosticLogger.GetRecentHfpBatterySamples(device.Address),
                DateTime.UtcNow);
        }

        private void RecordRefreshAttemptForRememberedDevices(DateTime refreshedAt)
        {
            long ticks = refreshedAt.ToUniversalTime().Ticks;
            foreach (DevicePreference preference in _settings.Devices)
            {
                preference.LastRefreshUtcTicks = ticks;
            }
            _settingsStore.Save(_settings);
        }

        private void MarkAllDevicesOffline(string reason, DateTime refreshedAt)
        {
            foreach (DevicePreference remembered in _settings.Devices)
            {
                bool alreadyPresent = _devices.Any(delegate(BluetoothDeviceBattery item)
                {
                    return string.Equals(item.StableId, remembered.StableId, StringComparison.OrdinalIgnoreCase);
                });
                if (!alreadyPresent) _devices.Add(CreateRememberedDevice(remembered));
            }
            foreach (BluetoothDeviceBattery device in _devices)
            {
                device.IsConnected = false;
                device.BatteryPercent = null;
                device.IsCharging = false;
                device.LeftBatteryPercent = null;
                device.RightBatteryPercent = null;
                device.CaseBatteryPercent = null;
                device.BatterySource = string.Empty;
                device.Error = reason;
                device.LastRefreshedAt = refreshedAt;
                DevicePreference preference = ApplyPreferenceToDevice(device);
                preference.LastRefreshUtcTicks = refreshedAt.ToUniversalTime().Ticks;
            }
            RecordRefreshAttemptForRememberedDevices(refreshedAt);
            RenderDevices();
            RestoreAndUpdateFloatingWindows();
            UpdateMergedFloatingWindow();
        }

        private void ConfigureAllDeviceTimers()
        {
            HashSet<string> active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BluetoothDeviceBattery device in _devices)
            {
                DevicePreference preference = _settingsStore.GetOrCreatePreference(_settings, device);
                if (preference.IsHidden) continue;
                active.Add(device.StableId);
                ConfigureDeviceTimer(device.StableId, preference);
            }
            foreach (string stableId in _deviceRefreshTimers.Keys.ToList())
            {
                if (active.Contains(stableId)) continue;
                _deviceRefreshTimers[stableId].Stop();
                _deviceRefreshTimers.Remove(stableId);
            }
        }

        private void ConfigureDeviceTimer(string stableId, DevicePreference preference)
        {
            DispatcherTimer timer;
            if (!_deviceRefreshTimers.TryGetValue(stableId, out timer))
            {
                string capturedStableId = stableId;
                timer = new DispatcherTimer();
                timer.Tick += async delegate { await RefreshSingleDeviceAsync(capturedStableId, false); };
                _deviceRefreshTimers[stableId] = timer;
            }
            timer.Stop();
            timer.Interval = TimeSpan.FromSeconds(SettingsStore.NormalizeRefreshSeconds(preference.RefreshSeconds));
            if (preference.AutoRefresh && !_isClosing) timer.Start();
        }

        private async Task RefreshSingleDeviceAsync(string stableId, bool userInitiated)
        {
            if (_isClosing) return;
            if (IsDeviceHidden(stableId)) return;
            if (_isScanning)
            {
                if (userInitiated)
                {
                    AppToast.Show(this, "正在刷新列表", "请稍后再刷新单台设备");
                }
                return;
            }
            if (_deviceRefreshInProgress.Contains(stableId)) return;
            BluetoothDeviceBattery current = _devices.FirstOrDefault(delegate(BluetoothDeviceBattery item)
            {
                return string.Equals(item.StableId, stableId, StringComparison.OrdinalIgnoreCase);
            });
            if (current == null) return;

            _deviceRefreshInProgress.Add(stableId);
            if (userInitiated) ShowStatusMessage("正在刷新“" + current.Name + "”的电量…");
            using (CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                try
                {
                    IList<BluetoothDeviceBattery> scanned;
                    await _readerGate.WaitAsync(cancellation.Token);
                    try
                    {
                        scanned = await _reader.ScanAsync(cancellation.Token);
                    }
                    finally
                    {
                        _readerGate.Release();
                    }
                    BluetoothDeviceBattery refreshed = scanned.FirstOrDefault(delegate(BluetoothDeviceBattery item)
                    {
                        return string.Equals(item.StableId, stableId, StringComparison.OrdinalIgnoreCase);
                    });
                    bool wasConnected = current.IsConnected;
                    if (refreshed == null)
                    {
                        refreshed = current;
                        refreshed.IsConnected = false;
                        refreshed.BatteryPercent = null;
                        refreshed.IsCharging = false;
                        refreshed.BatterySource = string.Empty;
                        refreshed.Error = "本次刷新未找到设备";
                        refreshed.LastRefreshedAt = DateTime.Now;
                    }
                    bool connectionOrderChanged = wasConnected != refreshed.IsConnected;
                    DevicePreference preference = ApplyPreferenceToDevice(refreshed);
                    _diagnosticLogger.LogScan(_reader.LastDiagnosticDetails, scanned);
                    int index = _devices.IndexOf(current);
                    if (index >= 0) _devices[index] = refreshed;
                    _settingsStore.Save(_settings);
                    _settingsStore.ClearScanError();
                    if (connectionOrderChanged) RenderDevices();
                    else UpdateDeviceCard(refreshed);
                    UpdateFloatingForRefreshedDevice(refreshed, preference);
                    UpdateMergedFloatingWindow();
                    UpdateSummaryText("已刷新“" + refreshed.Name + "”的电量");
                    if (userInitiated)
                    {
                        string result = !refreshed.IsConnected
                            ? "设备当前未连接。"
                            : refreshed.IsCharging
                                ? "设备正在充电，充电期间暂不显示不准确的百分比。"
                            : HasReportedBattery(refreshed)
                                ? "当前电量：" + (string.IsNullOrWhiteSpace(AccessoryBatteryText(refreshed))
                                    ? PrimaryBatteryPercent(refreshed).Value + "% 。"
                                    : AccessoryBatteryText(refreshed) + "。")
                                : "设备已连接，但本次仍未上报电量。";
                        AppToast.Show(this, "电量已刷新", refreshed.Name + "：" + result);
                    }
                }
                catch (OperationCanceledException)
                {
                    _diagnosticLogger.LogMessage("单设备电量刷新超时：" + stableId);
                    MarkSingleDeviceOffline(current, "蓝牙读取超时", DateTime.Now);
                    if (userInitiated)
                    {
                        AppToast.ShowWarning(this, "刷新超时", "请唤醒设备后重试");
                    }
                }
                catch (Exception ex)
                {
                    _diagnosticLogger.LogError("刷新单设备电量 " + stableId, ex);
                    MarkSingleDeviceOffline(current, FriendlyScanError(ex), DateTime.Now);
                    if (userInitiated)
                    {
                        AppToast.ShowError(this, "刷新失败", ex.Message);
                    }
                }
                finally
                {
                    _deviceRefreshInProgress.Remove(stableId);
                }
            }
        }

        private void MarkSingleDeviceOffline(BluetoothDeviceBattery device, string reason, DateTime refreshedAt)
        {
            if (device == null) return;
            device.IsConnected = false;
            device.BatteryPercent = null;
            device.IsCharging = false;
            device.LeftBatteryPercent = null;
            device.RightBatteryPercent = null;
            device.CaseBatteryPercent = null;
            device.BatterySource = string.Empty;
            device.Error = reason;
            device.LastRefreshedAt = refreshedAt;
            DevicePreference preference = ApplyPreferenceToDevice(device);
            preference.LastRefreshUtcTicks = refreshedAt.ToUniversalTime().Ticks;
            _settingsStore.Save(_settings);
            RenderDevices();
            UpdateFloatingForRefreshedDevice(device, preference);
            UpdateMergedFloatingWindow();
        }

        private void UpdateDeviceCard(BluetoothDeviceBattery device)
        {
            bool shouldBeVisible = !IsDeviceHidden(device.StableId) &&
                                   device.Transport == _selectedTransport &&
                                   (!_settings.ConnectedOnly || device.IsConnected);
            Border oldCard;
            if (!_deviceCards.TryGetValue(device.StableId, out oldCard))
            {
                if (shouldBeVisible) RenderDevices();
                return;
            }
            if (!shouldBeVisible)
            {
                RenderDevices();
                return;
            }
            int index = _devicePanel.Children.IndexOf(oldCard);
            if (index < 0) return;
            Border newCard = BuildDeviceRow(device);
            _devicePanel.Children.RemoveAt(index);
            _devicePanel.Children.Insert(index, newCard);
            _deviceCards[device.StableId] = newCard;
        }

        private void UpdateSummaryText(string status)
        {
            IEnumerable<BluetoothDeviceBattery> selectedDevices = _devices.Where(delegate(BluetoothDeviceBattery device)
            {
                return device.Transport == _selectedTransport && !IsDeviceHidden(device.StableId);
            });
            int connectedCount = selectedDevices.Count(delegate(BluetoothDeviceBattery device) { return device.IsConnected; });
            int readableCount = selectedDevices.Count(delegate(BluetoothDeviceBattery device)
            {
                return HasReportedBattery(device);
            });
            if (_summaryText != null)
            {
                _summaryText.Text = BuildDeviceSummaryText(connectedCount, readableCount);
            }
            if (_statusText != null) _statusText.Text = "✓ " + status;
        }

        private void UpdateFloatingForRefreshedDevice(BluetoothDeviceBattery device, DevicePreference preference)
        {
            if (!preference.IsHidden && preference.IsFloating &&
                (device.IsConnected || preference.KeepFloatingWhenDisconnected))
            {
                ShowFloatingWindow(device, preference);
            }
            else
            {
                CloseFloatingWindow(device.StableId);
            }
        }

        private void ShowLoadingState()
        {
            _devicePanel.Children.Clear();
            _deviceCards.Clear();
            Border card = Theme.Card();
            card.Padding = new Thickness(28);
            StackPanel panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            TextBlock glyph = Theme.Text("\uE895", 30, FontWeights.Normal, Theme.AccentColor);
            glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(glyph);
            TextBlock title = Theme.Text("正在读取蓝牙与 2.4G 设备…", 14, FontWeights.SemiBold, Theme.TextColor);
            title.Margin = new Thickness(0, 10, 0, 0);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(title);
            TextBlock note = Theme.Text("部分休眠中的鼠标或键盘可能需要先移动或按键唤醒", 11.5, FontWeights.Normal, Theme.MutedColor);
            note.Margin = new Thickness(0, 5, 0, 0);
            note.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(note);
            card.Child = panel;
            _devicePanel.Children.Add(card);
        }

        private void RenderDevices()
        {
            _devicePanel.Children.Clear();
            _deviceCards.Clear();
            UpdateTransportSwitch();
            UpdateHiddenDevicesButton();
            IEnumerable<BluetoothDeviceBattery> selectedDevices = _devices.Where(delegate(BluetoothDeviceBattery device)
            {
                return device.Transport == _selectedTransport && !IsDeviceHidden(device.StableId);
            });
            IEnumerable<BluetoothDeviceBattery> visible = selectedDevices;
            if (_settings.ConnectedOnly)
            {
                visible = visible.Where(delegate(BluetoothDeviceBattery device) { return device.IsConnected; });
            }
            List<BluetoothDeviceBattery> visibleList = visible
                .OrderByDescending(delegate(BluetoothDeviceBattery device) { return device.IsConnected; })
                .ThenBy(delegate(BluetoothDeviceBattery device) { return device.Name; }, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            int connectedCount = selectedDevices.Count(delegate(BluetoothDeviceBattery device) { return device.IsConnected; });
            int readableCount = selectedDevices.Count(delegate(BluetoothDeviceBattery device)
            {
                return HasReportedBattery(device);
            });

            Border summary = new Border
            {
                Background = Theme.Brush(Theme.AccentSoftColor),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 9, 14, 9),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid summaryGrid = new Grid();
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _summaryText = Theme.Text(
                BuildDeviceSummaryText(connectedCount, readableCount),
                12,
                FontWeights.SemiBold,
                Color.FromRgb(20, 88, 180));
            summaryGrid.Children.Add(_summaryText);
            _statusText = Theme.Text("更新于 " + DateTime.Now.ToString("HH:mm:ss"), 11, FontWeights.Normal, Theme.MutedColor);
            Grid.SetColumn(_statusText, 1);
            summaryGrid.Children.Add(_statusText);
            summary.Child = summaryGrid;
            _devicePanel.Children.Add(summary);

            if (visibleList.Count == 0)
            {
                _devicePanel.Children.Add(BuildEmptyState());
                return;
            }

            foreach (BluetoothDeviceBattery device in visibleList)
            {
                Border card = BuildDeviceRow(device);
                _deviceCards[device.StableId] = card;
                _devicePanel.Children.Add(card);
            }
        }

        private string BuildDeviceSummaryText(int connectedCount, int readableCount)
        {
            string summary = "已连接 " + connectedCount + " 台，其中 " + readableCount + " 台上报电量";
            return _selectedTransport == DeviceTransport.Receiver24G
                ? summary + "（部分 2.4G 设备充电时上报的电量可能不准确，属于正常现象）"
                : summary;
        }

        private Border BuildEmptyState()
        {
            Border card = Theme.Card();
            card.Padding = new Thickness(28);
            StackPanel panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            bool receiver = _selectedTransport == DeviceTransport.Receiver24G;
            TextBlock icon = Theme.Text(receiver ? "\uE88E" : "\uE702", 34, FontWeights.Normal, Theme.MutedColor);
            icon.FontFamily = new FontFamily("Segoe MDL2 Assets");
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(icon);
            TextBlock title = Theme.Text(
                receiver
                    ? (_settings.ConnectedOnly ? "没有检测到已连接的 2.4G 设备" : "没有找到支持的 2.4G 接收器设备")
                    : (_settings.ConnectedOnly ? "没有检测到已连接的蓝牙设备" : "没有找到已配对的蓝牙设备"),
                15,
                FontWeights.SemiBold,
                Theme.TextColor);
            title.Margin = new Thickness(0, 11, 0, 0);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(title);
            TextBlock note = Theme.Text(
                receiver
                    ? (_settings.ConnectedOnly
                        ? "请插入接收器并移动鼠标或按键唤醒设备。"
                        : "请插入 2.4G 接收器并唤醒设备；当前已适配罗技、Compx、迈从 G98 V2 与前行者 S9 Pro。")
                    : (_settings.ConnectedOnly
                        ? "先唤醒鼠标/键盘或连接耳机；也可以在“设置”中关闭连接筛选。"
                        : "请先在 Windows 设置 → 蓝牙和设备中完成配对。"),
                11.5,
                FontWeights.Normal,
                Theme.MutedColor);
            note.Margin = new Thickness(0, 7, 0, 0);
            note.TextWrapping = TextWrapping.Wrap;
            note.TextAlignment = TextAlignment.Center;
            panel.Children.Add(note);
            card.Child = panel;
            return card;
        }

        private Border BuildDeviceRow(BluetoothDeviceBattery device)
        {
            DevicePreference preference = _settingsStore.GetOrCreatePreference(_settings, device);
            Border card = Theme.Card();
            card.Padding = new Thickness(16, 12, 16, 12);
            card.Background = Theme.Brush(device.IsConnected ? Colors.White : Color.FromRgb(239, 243, 247));
            card.BorderBrush = Theme.Brush(device.IsConnected ? Color.FromRgb(185, 229, 208) : Color.FromRgb(198, 208, 220));
            StackPanel cardContent = new StackPanel();
            StackPanel expandedContent = new StackPanel
            {
                Visibility = preference.IsExpanded ? Visibility.Visible : Visibility.Collapsed
            };
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(234) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

            grid.Children.Add(CreateEditableDeviceIcon(device, preference));

            StackPanel identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            identity.Children.Add(Theme.Text(device.Name, 14, FontWeights.SemiBold, Theme.TextColor));
            StackPanel details = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 5, 0, 0)
            };
            Border connectionBadge = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = Theme.Brush(device.IsConnected ? Color.FromRgb(222, 247, 237) : Color.FromRgb(218, 225, 233)),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 7, 0),
                Child = Theme.Text(device.IsConnected ? "已连接" : "未连接", 9.5, FontWeights.SemiBold,
                    device.IsConnected ? Theme.SuccessColor : Color.FromRgb(76, 90, 107))
            };
            details.Children.Add(connectionBadge);
            details.Children.Add(Theme.Text(
                Theme.KindLabel(device.Kind) +
                (device.Transport == DeviceTransport.Receiver24G
                    ? " · 2.4G 接收器"
                    : device.IsLowEnergy ? " · BLE" : " · 蓝牙"),
                10.5,
                FontWeights.Normal,
                Theme.MutedColor));
            identity.Children.Add(details);
            TextBlock refreshedTime = Theme.Text(FormatLastRefresh(device.LastRefreshedAt), 9.5, FontWeights.Normal, Theme.MutedColor);
            refreshedTime.Margin = new Thickness(0, 4, 0, 0);
            identity.Children.Add(refreshedTime);
            Grid.SetColumn(identity, 1);
            grid.Children.Add(identity);

            StackPanel batteryPanel = BuildBatteryPanel(device, preference);
            StackPanel batteryHost = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            batteryHost.Children.Add(batteryPanel);
            Button refreshBatteryIcon = new Button
            {
                Content = "\uE72C",
                Width = 32,
                Height = 32,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Style = Theme.IconButtonStyle,
                ToolTip = "手动刷新这个设备的电量"
            };
            System.Windows.Automation.AutomationProperties.SetName(refreshBatteryIcon, "刷新电量");
            refreshBatteryIcon.Click += async delegate { await RefreshSingleDeviceAsync(device.StableId, true); };
            batteryHost.Children.Add(refreshBatteryIcon);
            Grid.SetColumn(batteryHost, 2);
            grid.Children.Add(batteryHost);

            StackPanel floatingPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToggleSwitch floating = new ToggleSwitch
            {
                IsChecked = preference.IsFloating,
                IsEnabled = device.IsConnected || preference.KeepFloatingWhenDisconnected,
                ToolTip = device.IsConnected || preference.KeepFloatingWhenDisconnected
                    ? "开启或关闭这个设备的桌面悬浮窗"
                    : "先开启下方的“离线保留浮窗”，即可显示未连接设备的悬浮窗"
            };
            System.Windows.Automation.AutomationProperties.SetName(floating, device.Name + " 显示悬浮窗");
            TextBlock floatingLabel = Theme.Text("显示悬浮窗", 11.5, FontWeights.Normal, Theme.TextColor);
            floatingLabel.Margin = new Thickness(8, 0, 0, 0);
            floatingPanel.Children.Add(floating);
            floatingPanel.Children.Add(floatingLabel);
            floating.Checked += delegate
            {
                if (!TrySelectMonitorForFloating(device, preference))
                {
                    floating.IsChecked = false;
                    return;
                }
                preference.IsFloating = true;
                _settingsStore.Save(_settings);
                ShowFloatingWindow(device, preference);
            };
            floating.Unchecked += delegate
            {
                preference.IsFloating = false;
                CloseFloatingWindow(device.StableId);
                _settingsStore.Save(_settings);
            };
            Grid.SetColumn(floatingPanel, 3);
            grid.Children.Add(floatingPanel);

            Button expandButton = new Button
            {
                Content = preference.IsExpanded ? "\uE70E" : "\uE70D",
                Width = 34,
                Height = 34,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = Theme.Brush(Theme.MutedColor),
                Style = Theme.IconButtonStyle,
                ToolTip = preference.IsExpanded ? "收起设备设置" : "展开设备设置"
            };
            System.Windows.Automation.AutomationProperties.SetName(expandButton, preference.IsExpanded ? "收起设备设置" : "展开设备设置");
            Action toggleExpanded = delegate
            {
                preference.IsExpanded = !preference.IsExpanded;
                expandedContent.Visibility = preference.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
                expandButton.Content = preference.IsExpanded ? "\uE70E" : "\uE70D";
                expandButton.ToolTip = preference.IsExpanded ? "收起设备设置" : "展开设备设置";
                System.Windows.Automation.AutomationProperties.SetName(expandButton, preference.IsExpanded ? "收起设备设置" : "展开设备设置");
                _settingsStore.Save(_settings);
            };
            expandButton.Click += delegate { toggleExpanded(); };
            Grid.SetColumn(expandButton, 4);
            grid.Children.Add(expandButton);
            grid.Cursor = Cursors.Hand;
            grid.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;
                if (IsInteractiveRowSource(e.OriginalSource as DependencyObject, grid)) return;
                toggleExpanded();
                e.Handled = true;
            };

            cardContent.Children.Add(grid);
            cardContent.Children.Add(expandedContent);

            Border devicePanel = new Border
            {
                Background = Theme.Brush(Color.FromRgb(249, 251, 254)),
                BorderBrush = Theme.Brush(Theme.BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 12, 0, 0)
            };
            StackPanel deviceSettingsContent = new StackPanel();
            Grid deviceHeading = new Grid();
            deviceHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            deviceHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel deviceHeadingCopy = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            deviceHeadingCopy.Children.Add(Theme.Text("设备信息", 12.5, FontWeights.SemiBold, Theme.TextColor));
            TextBlock iconHint = Theme.Text("将鼠标移到上方设备图标可更换；自定义图标可一键恢复", 9.5, FontWeights.Normal, Theme.MutedColor);
            iconHint.Margin = new Thickness(12, 1, 0, 0);
            deviceHeadingCopy.Children.Add(iconHint);
            deviceHeading.Children.Add(deviceHeadingCopy);
            StackPanel deviceActions = new StackPanel { Orientation = Orientation.Horizontal };
            Button hideDevice = Theme.Button("隐藏设备", false);
            hideDevice.Height = 32;
            hideDevice.MinWidth = 86;
            hideDevice.ToolTip = "从列表和所有悬浮窗中隐藏这个设备";
            hideDevice.Click += delegate { HideDevice(device, preference); };
            deviceActions.Children.Add(hideDevice);
            Button deleteDevice = Theme.Button("删除记录", false);
            deleteDevice.Height = 32;
            deleteDevice.MinWidth = 86;
            deleteDevice.Margin = new Thickness(8, 0, 0, 0);
            deleteDevice.Foreground = Theme.Brush(Theme.DangerColor);
            deleteDevice.BorderBrush = Theme.Brush(Color.FromRgb(244, 190, 190));
            deleteDevice.ToolTip = "删除此设备的本地名称、类型、图标和悬浮窗设置";
            deleteDevice.Click += delegate { DeleteDeviceRecord(preference); };
            deviceActions.Children.Add(deleteDevice);
            Grid.SetColumn(deviceActions, 1);
            deviceHeading.Children.Add(deviceActions);
            deviceSettingsContent.Children.Add(deviceHeading);
            Grid deviceGrid = new Grid { Margin = new Thickness(0, 11, 0, 0) };
            deviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            deviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            StackPanel nameField = new StackPanel();
            TextBlock nameLabel = Theme.Text("设备名称", 10, FontWeights.SemiBold, Theme.MutedColor);
            nameLabel.ToolTip = "系统名称：" + preference.LastKnownName;
            nameField.Children.Add(nameLabel);
            Grid nameRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid nameInput = new Grid { Height = 34 };
            TextBox nameBox = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(preference.CustomName) ? device.Name : preference.CustomName,
                Height = 34,
                MaxLength = 20,
                Padding = new Thickness(11, 4, 52, 4),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 11.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderBrush = Theme.Brush(Theme.BorderColor),
                ToolTip = "最多 20 个字；清空后保存可恢复系统名称",
                Style = Theme.ModernTextBoxStyle
            };
            nameInput.Children.Add(nameBox);
            TextBlock nameCount = Theme.Text(nameBox.Text.Length + "/20", 9.5, FontWeights.Normal, Theme.MutedColor);
            nameCount.HorizontalAlignment = HorizontalAlignment.Right;
            nameCount.VerticalAlignment = VerticalAlignment.Center;
            nameCount.Margin = new Thickness(0, 0, 11, 0);
            nameCount.IsHitTestVisible = false;
            nameInput.Children.Add(nameCount);
            nameBox.TextChanged += delegate { nameCount.Text = nameBox.Text.Length + "/20"; };
            nameRow.Children.Add(nameInput);
            Button saveName = Theme.Button("保存名称", false);
            saveName.MinWidth = 82;
            saveName.Height = 34;
            saveName.Margin = new Thickness(8, 0, 0, 0);
            saveName.Click += delegate { SaveCustomName(device, preference, nameBox.Text); };
            Grid.SetColumn(saveName, 1);
            nameRow.Children.Add(saveName);
            nameField.Children.Add(nameRow);
            Border nameSurface = CreateDetailSurface(nameField);
            nameSurface.Margin = new Thickness(0, 0, 7, 0);
            deviceGrid.Children.Add(nameSurface);

            StackPanel typeField = new StackPanel();
            typeField.Children.Add(Theme.Text("自定义设备类型", 10, FontWeights.SemiBold, Theme.MutedColor));
            ComboBox kindCombo = CreateDeviceKindCombo(preference.HasCustomKind ? preference.CustomKind : DeviceKind.Unknown);
            kindCombo.Margin = new Thickness(0, 6, 0, 0);
            kindCombo.SelectionChanged += delegate
            {
                if (kindCombo.SelectedValue == null) return;
                DeviceKind selected = (DeviceKind)kindCombo.SelectedValue;
                preference.HasCustomKind = selected != DeviceKind.Unknown;
                preference.CustomKind = selected;
                device.Kind = preference.HasCustomKind ? selected : preference.LastKnownKind;
                _settingsStore.Save(_settings);
                UpdateFloatingForDevice(device, preference);
                RenderDevices();
                AppToast.Show(
                    this,
                    "设备类型已更新",
                    preference.HasCustomKind ? "当前类型：" + Theme.KindLabel(device.Kind) : "已恢复自动识别");
            };
            typeField.Children.Add(kindCombo);
            Border typeSurface = CreateDetailSurface(typeField);
            typeSurface.Margin = new Thickness(7, 0, 0, 0);
            Grid.SetColumn(typeSurface, 1);
            deviceGrid.Children.Add(typeSurface);

            deviceSettingsContent.Children.Add(deviceGrid);

            Grid refreshControls = new Grid
            {
                Margin = new Thickness(0, 13, 0, 0),
                Background = Brushes.White
            };
            refreshControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            refreshControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel refreshDescription = new StackPanel();
            refreshDescription.Children.Add(Theme.Text("电量刷新", 10.5, FontWeights.SemiBold, Theme.TextColor));
            TextBlock refreshHint = Theme.Text("按这台设备单独设定自动读取周期", 9.5, FontWeights.Normal, Theme.MutedColor);
            refreshHint.Margin = new Thickness(0, 3, 0, 0);
            refreshDescription.Children.Add(refreshHint);
            refreshControls.Children.Add(refreshDescription);
            StackPanel refreshActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToggleSwitch autoRefresh = new ToggleSwitch
            {
                IsChecked = preference.AutoRefresh,
                ToolTip = "只按设定周期刷新这个设备的电量"
            };
            TextBlock autoLabel = Theme.Text("自动刷新", 10.5, FontWeights.Normal, Theme.TextColor);
            autoLabel.Margin = new Thickness(7, 0, 10, 0);
            refreshActions.Children.Add(autoRefresh);
            refreshActions.Children.Add(autoLabel);
            ComboBox refreshInterval = CreateRefreshIntervalCombo(preference.RefreshSeconds);
            refreshInterval.IsEnabled = preference.AutoRefresh;
            refreshActions.Children.Add(refreshInterval);
            Grid.SetColumn(refreshActions, 1);
            refreshControls.Children.Add(refreshActions);
            autoRefresh.Checked += delegate
            {
                preference.AutoRefresh = true;
                refreshInterval.IsEnabled = true;
                _settingsStore.Save(_settings);
                ConfigureDeviceTimer(device.StableId, preference);
                AppToast.Show(this, "自动刷新已开启", device.Name + " · " + RefreshIntervalLabel(preference.RefreshSeconds));
            };
            autoRefresh.Unchecked += delegate
            {
                preference.AutoRefresh = false;
                refreshInterval.IsEnabled = false;
                _settingsStore.Save(_settings);
                ConfigureDeviceTimer(device.StableId, preference);
                AppToast.Show(this, "自动刷新已关闭", device.Name + "将不再按周期自动读取电量");
            };
            refreshInterval.SelectionChanged += delegate
            {
                if (refreshInterval.SelectedValue == null) return;
                preference.RefreshSeconds = SettingsStore.NormalizeRefreshSeconds(Convert.ToInt32(refreshInterval.SelectedValue));
                _settingsStore.Save(_settings);
                ConfigureDeviceTimer(device.StableId, preference);
                AppToast.Show(this, "刷新周期已更新", device.Name + " · " + RefreshIntervalLabel(preference.RefreshSeconds));
            };
            Border refreshSurface = CreateDetailSurface(refreshControls);
            refreshSurface.Margin = new Thickness(0, 13, 0, 0);
            refreshControls.Margin = new Thickness(0);
            deviceSettingsContent.Children.Add(refreshSurface);
            devicePanel.Child = deviceSettingsContent;
            expandedContent.Children.Add(devicePanel);

            Border appearancePanel = new Border
            {
                Background = Theme.Brush(Color.FromRgb(247, 249, 252)),
                BorderBrush = Theme.Brush(Theme.BorderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 12, 0, 0)
            };
            StackPanel appearanceContent = new StackPanel();
            appearanceContent.Children.Add(Theme.Text("悬浮窗信息", 12.5, FontWeights.SemiBold, Theme.TextColor));
            Grid controls = new Grid { Margin = new Thickness(0, 11, 0, 0) };
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            StackPanel sizePanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            sizePanel.Children.Add(Theme.Text("悬浮窗尺寸", 10, FontWeights.SemiBold, Theme.MutedColor));
            StackPanel sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            ComboBox sizeCombo = CreateSizePresetCombo(preference.FloatingWidth, preference.FloatingHeight);
            sizeCombo.SelectionChanged += delegate
            {
                if (sizeCombo.SelectedValue == null) return;
                string selected = sizeCombo.SelectedValue.ToString();
                if (selected == "small") { preference.FloatingWidth = 160; preference.FloatingHeight = 56; }
                else if (selected == "medium") { preference.FloatingWidth = 196; preference.FloatingHeight = 66; }
                else if (selected == "large") { preference.FloatingWidth = 280; preference.FloatingHeight = 96; }
                ApplyFloatingRuntime(device, preference);
            };
            sizeRow.Children.Add(sizeCombo);
            Button customSize = Theme.Button("自定义", false);
            customSize.MinWidth = 74;
            customSize.Height = 32;
            customSize.Margin = new Thickness(7, 0, 0, 0);
            customSize.Click += delegate
            {
                FloatingSizeWindow dialog = new FloatingSizeWindow(this, preference.FloatingWidth, preference.FloatingHeight);
                if (dialog.ShowDialog() != true) return;
                preference.FloatingWidth = dialog.SelectedWidth;
                preference.FloatingHeight = dialog.SelectedHeight;
                ApplyFloatingRuntime(device, preference);
                AppToast.Show(this, "尺寸已保存", preference.FloatingWidth + " × " + preference.FloatingHeight + " px");
            };
            sizeRow.Children.Add(customSize);
            sizePanel.Children.Add(sizeRow);
            Border sizeSurface = CreateDetailSurface(sizePanel);
            sizeSurface.Margin = new Thickness(0, 0, 6, 0);
            controls.Children.Add(sizeSurface);

            StackPanel opacityPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock opacityLabel = Theme.Text("透明度", 10, FontWeights.SemiBold, Theme.MutedColor);
            opacityPanel.Children.Add(opacityLabel);
            StackPanel opacityRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            Slider opacitySlider = new Slider
            {
                Minimum = 1,
                Maximum = 100,
                Value = preference.OpacityPercent,
                Width = 126,
                Margin = new Thickness(5, 0, 4, 0),
                IsSnapToTickEnabled = false,
                Style = Theme.ModernSliderStyle,
                ToolTip = "拖动设置 1–100% 的任意透明度"
            };
            System.Windows.Automation.AutomationProperties.SetName(opacitySlider, device.Name + " 透明度");
            TextBlock opacityValue = Theme.Text(((int)Math.Round(opacitySlider.Value)) + "%", 10.5, FontWeights.SemiBold, Theme.TextColor);
            opacityValue.Width = 38;
            opacityValue.TextAlignment = TextAlignment.Right;
            DispatcherTimer opacitySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            opacitySaveTimer.Tick += delegate
            {
                opacitySaveTimer.Stop();
                _settingsStore.Save(_settings);
            };
            opacitySlider.ValueChanged += delegate
            {
                preference.OpacityPercent = FloatingAppearanceLimits.ClampOpacity((int)Math.Round(opacitySlider.Value));
                opacityValue.Text = preference.OpacityPercent + "%";
                UpdateFloatingForDevice(device, preference);
                opacitySaveTimer.Stop();
                opacitySaveTimer.Start();
            };
            opacityRow.Children.Add(opacitySlider);
            opacityRow.Children.Add(opacityValue);
            opacityPanel.Children.Add(opacityRow);
            Border opacitySurface = CreateDetailSurface(opacityPanel);
            opacitySurface.Margin = new Thickness(6, 0, 6, 0);
            Grid.SetColumn(opacitySurface, 1);
            controls.Children.Add(opacitySurface);

            StackPanel thresholdPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            thresholdPanel.Children.Add(Theme.Text("低电量阈值", 10, FontWeights.SemiBold, Theme.MutedColor));
            StackPanel thresholdRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            Slider thresholdSlider = new Slider
            {
                Minimum = 1,
                Maximum = 99,
                Value = preference.LowBatteryThreshold,
                Width = 116,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                Style = Theme.ModernSliderStyle
            };
            System.Windows.Automation.AutomationProperties.SetName(thresholdSlider, device.Name + " 低电量阈值");
            TextBlock thresholdValue = Theme.Text(preference.LowBatteryThreshold + "%", 10.5, FontWeights.SemiBold, Theme.TextColor);
            thresholdValue.Width = 38;
            thresholdValue.TextAlignment = TextAlignment.Right;
            DispatcherTimer thresholdSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            thresholdSaveTimer.Tick += delegate
            {
                thresholdSaveTimer.Stop();
                _settingsStore.Save(_settings);
            };
            thresholdSlider.ValueChanged += delegate
            {
                preference.LowBatteryThreshold = Math.Max(1, Math.Min(99, (int)Math.Round(thresholdSlider.Value)));
                thresholdValue.Text = preference.LowBatteryThreshold + "%";
                StackPanel updatedBatteryPanel = BuildBatteryPanel(device, preference);
                Grid.SetColumn(updatedBatteryPanel, 2);
                batteryHost.Children.Remove(batteryPanel);
                batteryHost.Children.Insert(0, updatedBatteryPanel);
                batteryPanel = updatedBatteryPanel;
                UpdateFloatingForDevice(device, preference);
                thresholdSaveTimer.Stop();
                thresholdSaveTimer.Start();
            };
            thresholdRow.Children.Add(thresholdSlider);
            thresholdRow.Children.Add(thresholdValue);
            thresholdPanel.Children.Add(thresholdRow);
            Border thresholdSurface = CreateDetailSurface(thresholdPanel);
            thresholdSurface.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(thresholdSurface, 2);
            controls.Children.Add(thresholdSurface);
            appearanceContent.Children.Add(controls);

            Grid appearanceBottom = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            appearanceBottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            appearanceBottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            WrapPanel colorControls = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            Button backgroundButton = CreateColorSwatchButton(preference.BackgroundColor, "选择正常底色");
            colorControls.Children.Add(CreateColorControl("正常底色", backgroundButton));
            Button lowColorButton = CreateColorSwatchButton(preference.LowBatteryColor, "选择低电量颜色");
            colorControls.Children.Add(CreateColorControl("低电量色", lowColorButton));
            Button disconnectedColorButton = CreateColorSwatchButton(preference.DisconnectedColor, "选择未连接颜色");
            colorControls.Children.Add(CreateColorControl("未连接色", disconnectedColorButton));
            backgroundButton.Click += delegate { PickDeviceColor(device, preference, "选择正常底色", backgroundButton, 0); };
            lowColorButton.Click += delegate { PickDeviceColor(device, preference, "选择低电量颜色", lowColorButton, 1); };
            disconnectedColorButton.Click += delegate { PickDeviceColor(device, preference, "选择未连接颜色", disconnectedColorButton, 2); };
            TextBlock resetAppearance = Theme.Text("↺  恢复默认配色", 10.5, FontWeights.SemiBold, Theme.AccentColor);
            resetAppearance.Margin = new Thickness(8, 17, 6, 8);
            resetAppearance.Cursor = Cursors.Hand;
            resetAppearance.ToolTip = "恢复白色底色、红色低电量色和默认未连接颜色";
            System.Windows.Automation.AutomationProperties.SetName(resetAppearance, "恢复默认配色");
            resetAppearance.MouseLeftButtonUp += delegate
            {
                preference.BackgroundColor = FloatingColorDefaults.Background;
                preference.LowBatteryColor = FloatingColorDefaults.LowBattery;
                preference.DisconnectedColor = FloatingColorDefaults.Disconnected;
                SetColorSwatch(backgroundButton, preference.BackgroundColor);
                SetColorSwatch(lowColorButton, preference.LowBatteryColor);
                SetColorSwatch(disconnectedColorButton, preference.DisconnectedColor);
                ApplyFloatingRuntime(device, preference);
                AppToast.Show(this, "已恢复默认配色", "白色底色、红色低电量色和默认未连接色");
            };
            colorControls.Children.Add(resetAppearance);

            Border colorSurface = CreateDetailSurface(colorControls);
            colorSurface.Margin = new Thickness(0, 0, 6, 0);
            appearanceBottom.Children.Add(colorSurface);

            StackPanel toggleControls = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            StackPanel keepPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToggleSwitch keepFloating = new ToggleSwitch
            {
                IsChecked = preference.KeepFloatingWhenDisconnected,
                ToolTip = "记住这个设备；未连接或暂时扫描不到时也继续显示悬浮窗"
            };
            TextBlock keepLabel = Theme.Text("离线保留浮窗", 10.5, FontWeights.Normal, Theme.TextColor);
            keepLabel.Margin = new Thickness(7, 0, 0, 0);
            keepPanel.Children.Add(keepFloating);
            keepPanel.Children.Add(keepLabel);
            keepFloating.Checked += delegate
            {
                preference.KeepFloatingWhenDisconnected = true;
                _settingsStore.Save(_settings);
                if (preference.IsFloating) ShowFloatingWindow(device, preference);
                RenderDevices();
                AppToast.Show(this, "离线保留浮窗已开启", device.Name + "离线后仍会保留浮窗");
            };
            keepFloating.Unchecked += delegate
            {
                preference.KeepFloatingWhenDisconnected = false;
                if (!device.IsConnected) CloseFloatingWindow(device.StableId);
                _settingsStore.Save(_settings);
                RenderDevices();
                AppToast.Show(this, "离线保留浮窗已关闭", device.Name + "离线后将关闭浮窗");
            };
            keepPanel.Margin = new Thickness(0, 0, 0, 12);
            toggleControls.Children.Add(keepPanel);

            StackPanel topmostPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0)
            };
            ToggleSwitch topmostToggle = new ToggleSwitch
            {
                IsChecked = preference.AlwaysOnTop,
                ToolTip = "只控制这个设备的悬浮窗是否保持在其他窗口上方"
            };
            TextBlock topmostLabel = Theme.Text("置顶显示", 10.5, FontWeights.Normal, Theme.TextColor);
            topmostLabel.Margin = new Thickness(7, 0, 0, 0);
            topmostPanel.Children.Add(topmostToggle);
            topmostPanel.Children.Add(topmostLabel);
            topmostToggle.Checked += delegate
            {
                preference.AlwaysOnTop = true;
                _settingsStore.Save(_settings);
                UpdateFloatingForDevice(device, preference);
                AppToast.Show(this, "置顶显示已开启", device.Name + "的浮窗将保持置顶");
            };
            topmostToggle.Unchecked += delegate
            {
                preference.AlwaysOnTop = false;
                _settingsStore.Save(_settings);
                UpdateFloatingForDevice(device, preference);
                AppToast.Show(this, "置顶显示已关闭", device.Name + "的浮窗不再保持置顶");
            };
            toggleControls.Children.Add(topmostPanel);
            Border toggleSurface = CreateDetailSurface(toggleControls);
            toggleSurface.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(toggleSurface, 1);
            appearanceBottom.Children.Add(toggleSurface);
            appearanceContent.Children.Add(appearanceBottom);
            appearancePanel.Child = appearanceContent;
            expandedContent.Children.Add(appearancePanel);
            card.Child = cardContent;
            card.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;
                DependencyObject source = e.OriginalSource as DependencyObject;
                if (IsInteractiveRowSource(source, card)) return;
                if (IsWithinElement(source, grid)) return;
                if (IsWithinElement(source, expandedContent)) return;
                toggleExpanded();
                e.Handled = true;
            };
            return card;
        }

        private static ComboBox CreateSizePresetCombo(int width, int height)
        {
            string selected = width == 160 && height == 56
                ? "small"
                : width == 280 && height == 96
                    ? "large"
                    : "medium";
            return new ComboBox
            {
                Width = 94,
                Height = 32,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 11.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                ItemsSource = new List<SelectionOption<string>>
                {
                    new SelectionOption<string>("small", "小"),
                    new SelectionOption<string>("medium", "中"),
                    new SelectionOption<string>("large", "大")
                },
                DisplayMemberPath = "Label",
                SelectedValuePath = "Value",
                SelectedValue = selected,
                Style = Theme.ModernComboBoxStyle
            };
        }

        private void ApplyFloatingRuntime(BluetoothDeviceBattery device, DevicePreference preference)
        {
            _settingsStore.Save(_settings);
            UpdateFloatingForDevice(device, preference);
        }

        private static Button CreateColorSwatchButton(string color, string tooltip)
        {
            Button button = new Button
            {
                Width = 52,
                Height = 32,
                Padding = new Thickness(5),
                Background = Brushes.White,
                BorderBrush = Theme.Brush(Theme.BorderColor),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = tooltip + " · " + color,
                Style = Theme.ColorSwatchButtonStyle
            };
            button.Content = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = Theme.Brush(Theme.ParseColor(color, Colors.White))
            };
            System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
            return button;
        }

        private static StackPanel CreateColorControl(string label, Button button)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 16, 8) };
            panel.Children.Add(Theme.Text(label, 10, FontWeights.SemiBold, Theme.MutedColor));
            button.Margin = new Thickness(0, 6, 0, 0);
            panel.Children.Add(button);
            return panel;
        }

        private static Border CreateDetailSurface(UIElement content)
        {
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Theme.Brush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(11, 9, 11, 9),
                Child = content
            };
        }

        private static void SetColorSwatch(Button button, string color)
        {
            Border preview = button.Content as Border;
            if (preview != null) preview.Background = Theme.Brush(Theme.ParseColor(color, Colors.White));
            button.ToolTip = color;
        }

        private void PickDeviceColor(
            BluetoothDeviceBattery device,
            DevicePreference preference,
            string title,
            Button swatch,
            int colorKind)
        {
            string current = colorKind == 0
                ? preference.BackgroundColor
                : colorKind == 1 ? preference.LowBatteryColor : preference.DisconnectedColor;
            ColorPickerWindow dialog = new ColorPickerWindow(this, title, current);
            if (dialog.ShowDialog() != true) return;
            if (colorKind == 0) preference.BackgroundColor = dialog.SelectedColor;
            else if (colorKind == 1) preference.LowBatteryColor = dialog.SelectedColor;
            else preference.DisconnectedColor = dialog.SelectedColor;
            SetColorSwatch(swatch, dialog.SelectedColor);
            ApplyFloatingRuntime(device, preference);
            AppToast.Show(this, "颜色已保存", dialog.SelectedColor);
        }

        private static ComboBox CreateDeviceKindCombo(DeviceKind selectedKind)
        {
            ComboBox combo = new ComboBox
            {
                Height = 32,
                MinWidth = 188,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 11.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                SelectedValuePath = "Tag",
                Style = Theme.ModernComboBoxStyle
            };
            combo.Items.Add(CreateDeviceKindItem(DeviceKind.Unknown, "自动识别", "\uE9D9"));
            combo.Items.Add(new Separator
            {
                Margin = new Thickness(8, 4, 8, 4),
                Background = Theme.Brush(Theme.BorderColor),
                IsHitTestVisible = false
            });
            combo.Items.Add(CreateDeviceKindItem(DeviceKind.Mouse, "鼠标", Theme.KindGlyph(DeviceKind.Mouse)));
            combo.Items.Add(CreateDeviceKindItem(DeviceKind.Keyboard, "键盘", Theme.KindGlyph(DeviceKind.Keyboard)));
            combo.Items.Add(CreateDeviceKindItem(DeviceKind.Headphones, "耳机", Theme.KindGlyph(DeviceKind.Headphones)));
            combo.Items.Add(CreateDeviceKindItem(DeviceKind.Gamepad, "手柄", Theme.KindGlyph(DeviceKind.Gamepad)));
            combo.Items.Add(CreateDeviceKindItem(DeviceKind.Other, "其他", Theme.KindGlyph(DeviceKind.Other)));
            combo.SelectedValue = selectedKind;
            return combo;
        }

        private static ComboBoxItem CreateDeviceKindItem(DeviceKind kind, string label, string glyph)
        {
            StackPanel content = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock icon = Theme.Text(glyph, 13, FontWeights.Normal, Theme.AccentColor);
            icon.FontFamily = new FontFamily("Segoe MDL2 Assets");
            icon.Width = 24;
            icon.TextAlignment = TextAlignment.Center;
            content.Children.Add(icon);
            content.Children.Add(Theme.Text(label, 11.5, FontWeights.Normal, Theme.TextColor));
            return new ComboBoxItem { Tag = kind, Content = content };
        }

        private static ComboBox CreateRefreshIntervalCombo(int selectedSeconds)
        {
            return new ComboBox
            {
                Width = 106,
                Height = 32,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 11.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                ItemsSource = new List<SelectionOption<int>>
                {
                    new SelectionOption<int>(600, "10 分钟"),
                    new SelectionOption<int>(1800, "30 分钟"),
                    new SelectionOption<int>(3600, "1 小时"),
                    new SelectionOption<int>(10800, "3 小时"),
                    new SelectionOption<int>(18000, "5 小时")
                },
                DisplayMemberPath = "Label",
                SelectedValuePath = "Value",
                SelectedValue = SettingsStore.NormalizeRefreshSeconds(selectedSeconds),
                Style = Theme.ModernComboBoxStyle
            };
        }

        private static string RefreshIntervalLabel(int seconds)
        {
            switch (SettingsStore.NormalizeRefreshSeconds(seconds))
            {
                case 600: return "每 10 分钟";
                case 1800: return "每 30 分钟";
                case 3600: return "每 1 小时";
                case 10800: return "每 3 小时";
                default: return "每 5 小时";
            }
        }

        private void SaveCustomName(BluetoothDeviceBattery device, DevicePreference preference, string requestedName)
        {
            string trimmed = (requestedName ?? string.Empty).Trim();
            if (trimmed.Length > 20)
            {
                trimmed = trimmed.Substring(0, 20);
            }
            preference.CustomName = string.Equals(trimmed, preference.LastKnownName, StringComparison.CurrentCulture)
                ? string.Empty
                : trimmed;
            device.Name = string.IsNullOrWhiteSpace(preference.CustomName)
                ? (string.IsNullOrWhiteSpace(preference.LastKnownName) ? "未命名蓝牙设备" : preference.LastKnownName)
                : preference.CustomName;
            _settingsStore.Save(_settings);
            UpdateFloatingForDevice(device, preference);
            AppToast.Show(
                this,
                "名称已保存",
                string.IsNullOrWhiteSpace(preference.CustomName)
                    ? "已恢复系统名称：" + device.Name
                    : "当前名称：" + device.Name);
            RenderDevices();
        }

        private Grid CreateEditableDeviceIcon(BluetoothDeviceBattery device, DevicePreference preference)
        {
            Grid host = new Grid
            {
                Width = 50,
                Height = 50,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid visual = new Grid { Width = 46, Height = 46 };
            visual.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(15),
                Background = Theme.Brush(device.IsConnected ? Theme.AccentSoftColor : Color.FromRgb(218, 225, 233)),
                Child = DeviceIconFactory.Create(device.Kind, preference.CustomIconPath, 36, false)
            });
            Border hoverOverlay = new Border
            {
                CornerRadius = new CornerRadius(15),
                Background = Theme.Brush(Color.FromArgb(126, 20, 46, 82)),
                Opacity = 0,
                Child = new TextBlock
                {
                    Text = "\uE898",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 17,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            visual.Children.Add(hoverOverlay);

            Button upload = new Button
            {
                Content = visual,
                Width = 50,
                Height = 50,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Style = Theme.IconButtonStyle,
                ToolTip = string.IsNullOrWhiteSpace(preference.CustomIconPath) ? "上传自定义图标" : "更换自定义图标"
            };
            System.Windows.Automation.AutomationProperties.SetName(
                upload,
                string.IsNullOrWhiteSpace(preference.CustomIconPath) ? "上传设备图标" : "更换设备图标");
            upload.Click += delegate(object sender, RoutedEventArgs e)
            {
                e.Handled = true;
                ImportIconForDevice(device, preference);
            };
            host.Children.Add(upload);

            Button restore = null;
            if (!string.IsNullOrWhiteSpace(preference.CustomIconPath))
            {
                restore = new Button
                {
                    Content = "\uE777",
                    Width = 22,
                    Height = 22,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 10,
                    Background = Brushes.White,
                    Foreground = Theme.Brush(Theme.DangerColor),
                    Style = Theme.IconButtonStyle,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -4, -4, 0),
                    Visibility = Visibility.Collapsed,
                    ToolTip = "恢复默认图标"
                };
                System.Windows.Automation.AutomationProperties.SetName(restore, "恢复默认图标");
                restore.Click += delegate(object sender, RoutedEventArgs e)
                {
                    e.Handled = true;
                    DeleteIconForDevice(device, preference);
                };
                Panel.SetZIndex(restore, 2);
                host.Children.Add(restore);
            }

            host.MouseEnter += delegate
            {
                hoverOverlay.Opacity = 1;
                if (restore != null) restore.Visibility = Visibility.Visible;
            };
            host.MouseLeave += delegate
            {
                hoverOverlay.Opacity = 0;
                if (restore != null) restore.Visibility = Visibility.Collapsed;
            };
            return host;
        }

        private static bool IsInteractiveRowSource(DependencyObject source, DependencyObject boundary)
        {
            DependencyObject current = source;
            while (current != null && !ReferenceEquals(current, boundary))
            {
                if (current is System.Windows.Controls.Primitives.ButtonBase ||
                    current is ToggleSwitch ||
                    current is TextBox ||
                    current is ComboBox ||
                    current is Slider)
                {
                    return true;
                }
                DependencyObject parent = null;
                try { parent = VisualTreeHelper.GetParent(current); }
                catch { }
                if (parent == null) parent = LogicalTreeHelper.GetParent(current);
                current = parent;
            }
            return false;
        }

        private static bool IsWithinElement(DependencyObject source, DependencyObject target)
        {
            DependencyObject current = source;
            while (current != null)
            {
                if (ReferenceEquals(current, target)) return true;
                DependencyObject parent = null;
                try { parent = VisualTreeHelper.GetParent(current); }
                catch { }
                if (parent == null) parent = LogicalTreeHelper.GetParent(current);
                current = parent;
            }
            return false;
        }

        private StackPanel BuildBatteryPanel(BluetoothDeviceBattery device, DevicePreference preference)
        {
            StackPanel panel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Width = 174,
                Margin = new Thickness(8, 0, 8, 0)
            };
            int? displayBattery = PrimaryBatteryPercent(device);
            bool hasCurrentBattery = device.IsConnected && displayBattery.HasValue;
            bool low = IsLowBattery(device, preference.LowBatteryThreshold);
            Color batteryColor = low ? Theme.DangerColor : Theme.SuccessColor;
            TextBlock value = Theme.Text(
                device.IsCharging ? "充电中" : hasCurrentBattery ? displayBattery.Value + "%" : "--%",
                17,
                FontWeights.Bold,
                device.IsCharging || hasCurrentBattery ? batteryColor : Theme.MutedColor);
            panel.Children.Add(value);

            Grid bar = new Grid
            {
                Width = 174,
                Height = 6,
                Margin = new Thickness(0, 5, 0, 0),
                Background = Brushes.Transparent
            };
            int percent = hasCurrentBattery ? displayBattery.Value : 0;
            bar.Children.Add(new Border
            {
                Background = Theme.Brush(Color.FromRgb(232, 237, 243)),
                CornerRadius = new CornerRadius(3)
            });
            if (percent > 0)
            {
                bar.Children.Add(new Border
                {
                    Width = 174 * percent / 100.0,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = Theme.Brush(batteryColor),
                    CornerRadius = new CornerRadius(3)
                });
            }
            panel.Children.Add(bar);

            string note;
            if (!device.IsConnected)
            {
                note = "未连接";
            }
            else if (hasCurrentBattery)
            {
                string accessory = AccessoryBatteryText(device);
                if (device.BatteryPercent.HasValue)
                {
                    note = low ? "电量偏低" : device.BatterySource;
                    if (!string.IsNullOrWhiteSpace(accessory)) note += " · " + accessory;
                }
                else
                {
                    note = (low ? "电量偏低 · " : string.Empty) + accessory;
                }
            }
            else if (device.IsCharging)
            {
                note = "百分比暂不显示";
            }
            else
            {
                string accessory = AccessoryBatteryText(device);
                note = !string.IsNullOrWhiteSpace(accessory)
                    ? accessory
                    : string.IsNullOrWhiteSpace(device.Error) ? "标准接口未上报" : device.Error;
            }
            TextBlock source = Theme.Text(note, 9.5, FontWeights.Normal, low ? Theme.DangerColor : Theme.MutedColor);
            source.Margin = new Thickness(0, 4, 0, 0);
            source.ToolTip = note;
            source.Width = 174;
            source.TextWrapping = TextWrapping.NoWrap;
            source.TextTrimming = TextTrimming.CharacterEllipsis;
            panel.Children.Add(source);
            return panel;
        }

        internal static string AccessoryBatteryText(BluetoothDeviceBattery device)
        {
            if (device == null) return string.Empty;
            List<string> parts = new List<string>();
            if (device.EarbudSidesInferred)
            {
                if (device.LeftBatteryPercent.HasValue) parts.Add("耳机① " + device.LeftBatteryPercent.Value + "%");
                if (device.RightBatteryPercent.HasValue) parts.Add("耳机② " + device.RightBatteryPercent.Value + "%");
            }
            else
            {
                if (device.LeftBatteryPercent.HasValue) parts.Add("左耳 " + device.LeftBatteryPercent.Value + "%");
                if (device.RightBatteryPercent.HasValue) parts.Add("右耳 " + device.RightBatteryPercent.Value + "%");
            }
            if (device.CaseBatteryPercent.HasValue) parts.Add("充电盒 " + device.CaseBatteryPercent.Value + "%");
            string text = string.Join(" · ", parts.ToArray());
            return device != null && device.EarbudSidesInferred && !string.IsNullOrWhiteSpace(text)
                ? text + "（未分左右）"
                : text;
        }

        internal static string CompactAccessoryBatteryText(BluetoothDeviceBattery device)
        {
            if (device == null) return string.Empty;
            List<string> parts = new List<string>();
            if (device.LeftBatteryPercent.HasValue) parts.Add((device.EarbudSidesInferred ? "耳机① " : "左耳 ") + device.LeftBatteryPercent.Value + "%");
            if (device.RightBatteryPercent.HasValue) parts.Add((device.EarbudSidesInferred ? "耳机② " : "右耳 ") + device.RightBatteryPercent.Value + "%");
            if (device.CaseBatteryPercent.HasValue) parts.Add("充电盒 " + device.CaseBatteryPercent.Value + "%");
            return string.Join("\n", parts.ToArray());
        }

        internal static bool HasReportedBattery(BluetoothDeviceBattery device)
        {
            return device != null && device.IsConnected &&
                   (device.BatteryPercent.HasValue || device.LeftBatteryPercent.HasValue ||
                    device.RightBatteryPercent.HasValue || device.CaseBatteryPercent.HasValue);
        }

        internal static int? PrimaryBatteryPercent(BluetoothDeviceBattery device)
        {
            if (device == null || !device.IsConnected) return null;
            if (device.BatteryPercent.HasValue) return device.BatteryPercent;
            List<int> earbuds = new List<int>();
            if (device.LeftBatteryPercent.HasValue) earbuds.Add(device.LeftBatteryPercent.Value);
            if (device.RightBatteryPercent.HasValue) earbuds.Add(device.RightBatteryPercent.Value);
            if (earbuds.Count > 0) return earbuds.Min();
            return device.CaseBatteryPercent;
        }

        internal static bool IsLowBattery(BluetoothDeviceBattery device, int threshold)
        {
            if (device == null || !device.IsConnected) return false;
            return (device.BatteryPercent.HasValue && device.BatteryPercent.Value < threshold) ||
                   (device.LeftBatteryPercent.HasValue && device.LeftBatteryPercent.Value < threshold) ||
                   (device.RightBatteryPercent.HasValue && device.RightBatteryPercent.Value < threshold) ||
                   (device.CaseBatteryPercent.HasValue && device.CaseBatteryPercent.Value < threshold);
        }

        internal static string FormatLastRefresh(DateTime refreshedAt)
        {
            if (refreshedAt == default(DateTime)) return "尚未刷新";
            return refreshedAt.Date == DateTime.Today
                ? "最近刷新 " + refreshedAt.ToString("HH:mm:ss")
                : "最近刷新 " + refreshedAt.ToString("MM-dd HH:mm");
        }

        private void ShowErrorState(string titleText, string detail)
        {
            if (_devicePanel.Children.Count == 0) RenderDevices();
            Border card = Theme.Card();
            card.BorderBrush = Theme.Brush(Color.FromRgb(246, 184, 184));
            StackPanel panel = new StackPanel();
            panel.Children.Add(Theme.Text(titleText, 15, FontWeights.SemiBold, Theme.DangerColor));
            TextBlock note = Theme.Text(detail, 11.5, FontWeights.Normal, Theme.MutedColor);
            note.Margin = new Thickness(0, 7, 0, 0);
            note.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(note);
            card.Child = panel;
            _devicePanel.Children.Insert(Math.Min(1, _devicePanel.Children.Count), card);
        }

        private static string FriendlyScanError(Exception ex)
        {
            uint code = unchecked((uint)ex.HResult);
            if (code == 0x80070490 || code == 0x800710DF)
            {
                return "Windows 当前没有可用的蓝牙适配器。请确认蓝牙已打开。";
            }
            if (code == 0x80070005)
            {
                return "Windows 拒绝了蓝牙访问。请在“设置 → 隐私和安全性”中检查设备权限。";
            }
            if (code == 0x80004004)
            {
                return "Windows 蓝牙接口当前不可用；如果蓝牙已关闭，请开启后再刷新。";
            }
            if (ex.Message.IndexOf("蓝牙已关闭", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ex.Message.IndexOf("蓝牙当前不可用", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "蓝牙已关闭，请先在 Windows 中开启蓝牙后再刷新。";
            }
            return ex.Message + "\n请确认 Windows 蓝牙已开启，然后点击“刷新列表”。";
        }

        private void ImportIconForDevice(BluetoothDeviceBattery device, DevicePreference preference)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "为 “" + device.Name + "” 选择图标",
                Filter = "支持的图片|*.png;*.jpg;*.jpeg;*.ico;*.bmp|PNG 图片|*.png|JPEG 图片|*.jpg;*.jpeg|ICO 图标|*.ico",
                Multiselect = false,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                FileInfo file = new FileInfo(dialog.FileName);
                if (file.Length > 5 * 1024 * 1024)
                {
                    AppToast.ShowWarning(this, "图标太大", "图标不能超过 5 MB，建议使用透明 PNG");
                    return;
                }
                BitmapImage bitmap = DeviceIconFactory.LoadBitmap(dialog.FileName);
                if (bitmap.PixelWidth < 32 || bitmap.PixelHeight < 32)
                {
                    AppToast.ShowWarning(this, "图标尺寸太小", "最低 32×32，建议使用 128×128 或更大图片");
                    return;
                }

                preference.CustomIconPath = _settingsStore.ImportIcon(dialog.FileName, device.StableId);
                _settingsStore.Save(_settings);
                UpdateFloatingForDevice(device, preference);
                AppToast.Show(this, "图标设置成功", "已应用到“" + device.Name + "”");
                RenderDevices();
            }
            catch (Exception ex)
            {
                AppToast.ShowError(this, "图标导入失败", ex.Message);
            }
        }

        private void DeleteIconForDevice(BluetoothDeviceBattery device, DevicePreference preference)
        {
            _settingsStore.DeleteImportedIcon(preference.CustomIconPath);
            preference.CustomIconPath = string.Empty;
            _settingsStore.Save(_settings);
            UpdateFloatingForDevice(device, preference);
            AppToast.Show(this, "图标已恢复", "当前使用“" + Theme.KindLabel(device.Kind) + "”内置图标");
            RenderDevices();
        }

        private void RestoreAndUpdateFloatingWindows()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BluetoothDeviceBattery device in _devices)
            {
                seen.Add(device.StableId);
                DevicePreference preference = _settingsStore.GetOrCreatePreference(_settings, device);
                if (!preference.IsHidden && preference.IsFloating &&
                    (device.IsConnected || preference.KeepFloatingWhenDisconnected))
                {
                    ShowFloatingWindow(device, preference);
                }
                else
                {
                    CloseFloatingWindow(device.StableId);
                }
            }
            foreach (DevicePreference preference in _settings.Devices)
            {
                if (seen.Contains(preference.StableId)) continue;
                if (!preference.IsHidden && preference.IsFloating && preference.KeepFloatingWhenDisconnected)
                {
                    ShowFloatingWindow(CreateRememberedDevice(preference), preference);
                }
                else
                {
                    CloseFloatingWindow(preference.StableId);
                }
            }
            _settingsStore.Save(_settings);
        }

        private void RestoreRememberedFloatingWindows()
        {
            foreach (DevicePreference preference in _settings.Devices)
            {
                if (!preference.IsHidden && preference.IsFloating && preference.KeepFloatingWhenDisconnected)
                {
                    ShowFloatingWindow(CreateRememberedDevice(preference), preference);
                }
            }
        }

        private static BluetoothDeviceBattery CreateRememberedDevice(DevicePreference preference)
        {
            string detectedName = string.IsNullOrWhiteSpace(preference.LastKnownName)
                ? (preference.LastKnownTransport == DeviceTransport.Receiver24G ? "已记住的 2.4G 设备" : "已记住的蓝牙设备")
                : preference.LastKnownName;
            DeviceKind detectedKind = preference.LastKnownKind;
            return new BluetoothDeviceBattery
            {
                StableId = preference.StableId,
                DeviceId = preference.LastKnownDeviceId,
                Name = string.IsNullOrWhiteSpace(preference.CustomName) ? detectedName : preference.CustomName,
                DetectedName = detectedName,
                Address = preference.LastKnownAddress,
                Transport = preference.LastKnownTransport,
                IsLowEnergy = preference.LastKnownIsLowEnergy,
                IsConnected = false,
                IsPaired = true,
                Kind = preference.HasCustomKind ? preference.CustomKind : detectedKind,
                DetectedKind = detectedKind,
                BatteryPercent = null,
                LastRefreshedAt = preference.LastRefreshUtcTicks > 0
                    ? new DateTime(preference.LastRefreshUtcTicks, DateTimeKind.Utc).ToLocalTime()
                    : default(DateTime),
                BatterySource = string.Empty,
                Error = "设备当前未连接或未被扫描到"
            };
        }

        private void UpdateFloatingForDevice(BluetoothDeviceBattery device, DevicePreference preference)
        {
            if (preference.IsHidden)
            {
                CloseFloatingWindow(device.StableId);
                UpdateMergedFloatingWindow();
                return;
            }
            FloatingBatteryWindow floating;
            if (_floatingWindows.TryGetValue(device.StableId, out floating))
            {
                floating.UpdateDevice(
                    device,
                    preference.CustomIconPath,
                    preference.LowBatteryThreshold,
                    preference.AlwaysOnTop,
                    preference.FloatingLocked,
                    preference.FloatingWidth,
                    preference.FloatingHeight,
                    preference.OpacityPercent,
                    preference.BackgroundColor,
                    preference.LowBatteryColor,
                    preference.DisconnectedColor);
            }
            UpdateMergedFloatingWindow();
        }

        private void UpdateMergedFloatingWindow()
        {
            if (_isClosing || _displayTopologySettling) return;
            if (!_settings.MergeFloatingWindows)
            {
                if (_mergedFloatingWindow != null)
                {
                    _mergedFloatingWindow.Close();
                    _mergedFloatingWindow = null;
                }
                return;
            }
            if (!IsMergedTargetMonitorAvailable())
            {
                if (_mergedFloatingWindow != null)
                {
                    _mergedFloatingWindow.Close();
                    _mergedFloatingWindow = null;
                }
                return;
            }

            List<MergedFloatingItem> items = new List<MergedFloatingItem>();
            HashSet<string> mergedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BluetoothDeviceBattery device in _devices)
            {
                DevicePreference preference = _settingsStore.GetOrCreatePreference(_settings, device);
                mergedSeen.Add(device.StableId);
                if (!preference.IsHidden && preference.IncludeInMergedFloating)
                {
                    items.Add(new MergedFloatingItem(device, preference));
                }
            }
            foreach (DevicePreference preference in _settings.Devices)
            {
                if (mergedSeen.Contains(preference.StableId)) continue;
                if (!preference.IsHidden && preference.IncludeInMergedFloating)
                {
                    items.Add(new MergedFloatingItem(CreateRememberedDevice(preference), preference));
                }
            }
            items = items
                .OrderBy(delegate(MergedFloatingItem item) { return item.Preference.MergedDisplayOrder; })
                .ThenBy(delegate(MergedFloatingItem item) { return item.Device.Name; }, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (items.Count == 0)
            {
                if (_mergedFloatingWindow != null)
                {
                    _mergedFloatingWindow.Close();
                    _mergedFloatingWindow = null;
                }
                return;
            }
            if (_mergedFloatingWindow != null)
            {
                _mergedFloatingWindow.UpdateItems(
                    items,
                    _settings.LowBatteryThreshold,
                    _settings.MergedAlwaysOnTop,
                    _settings.MergedLocked,
                    _settings.MergedOpacityPercent);
                if (!_mergedFloatingWindow.IsVisible) _mergedFloatingWindow.Show();
                return;
            }

            MergedFloatingWindow window = new MergedFloatingWindow(
                items,
                _settings.LowBatteryThreshold,
                _settings.MergedAlwaysOnTop,
                _settings.MergedLocked,
                _settings.MergedOpacityPercent);
            window.HideRequested += delegate
            {
                _settings.MergeFloatingWindows = false;
                _settingsStore.Save(_settings);
                if (_mergedFloatingWindow != null)
                {
                    _mergedFloatingWindow.Close();
                    _mergedFloatingWindow = null;
                }
            };
            window.RefreshRequested += delegate
            {
                _ = RefreshDevicesAsync(false);
            };
            window.PositionChangedByUser += delegate
            {
                if (!CanCaptureFloatingPosition() || !IsMergedTargetMonitorAvailable()) return;
                CaptureMergedPlacement(window);
                _settingsStore.Save(_settings);
            };
            window.OpacityPresetRequested += delegate(object sender, OpacityPresetEventArgs args)
            {
                _settings.MergedOpacityPercent = FloatingAppearanceLimits.ClampOpacity(args.OpacityPercent);
                _settingsStore.Save(_settings);
            };
            window.LockChangedRequested += delegate(object sender, BooleanSettingEventArgs args)
            {
                _settings.MergedLocked = args.Enabled;
                _settingsStore.Save(_settings);
            };
            window.TopmostChangedRequested += delegate(object sender, BooleanSettingEventArgs args)
            {
                _settings.MergedAlwaysOnTop = args.Enabled;
                _settingsStore.Save(_settings);
            };
            PrepareMergedPlacement(window);
            _mergedFloatingWindow = window;
            window.Show();
        }

        private void OnMergeFloatingChanged(bool enabled)
        {
            _settings.MergeFloatingWindows = enabled;
            _settingsStore.Save(_settings);
            UpdateMergedFloatingWindow();
        }

        private void OnMergeOptionsChanged()
        {
            _settingsStore.Save(_settings);
            UpdateMergedFloatingWindow();
        }

        private void ShowFloatingWindow(BluetoothDeviceBattery device, DevicePreference preference)
        {
            if (_isClosing || _displayTopologySettling) return;
            if (preference.IsHidden)
            {
                CloseFloatingWindow(device.StableId);
                return;
            }
            if (!IsDeviceTargetMonitorAvailable(preference))
            {
                CloseFloatingWindow(device.StableId);
                return;
            }
            FloatingBatteryWindow existing;
            if (_floatingWindows.TryGetValue(device.StableId, out existing))
            {
                existing.UpdateDevice(
                    device,
                    preference.CustomIconPath,
                    preference.LowBatteryThreshold,
                    preference.AlwaysOnTop,
                    preference.FloatingLocked,
                    preference.FloatingWidth,
                    preference.FloatingHeight,
                    preference.OpacityPercent,
                    preference.BackgroundColor,
                    preference.LowBatteryColor,
                    preference.DisconnectedColor);
                if (!existing.IsVisible) existing.Show();
                return;
            }

            FloatingBatteryWindow window = new FloatingBatteryWindow(
                device,
                preference.CustomIconPath,
                preference.LowBatteryThreshold,
                preference.AlwaysOnTop,
                preference.FloatingLocked,
                preference.FloatingWidth,
                preference.FloatingHeight,
                preference.OpacityPercent,
                preference.BackgroundColor,
                preference.LowBatteryColor,
                preference.DisconnectedColor);
            window.HideRequested += delegate
            {
                preference.IsFloating = false;
                CloseFloatingWindow(device.StableId);
                _settingsStore.Save(_settings);
                RenderDevices();
            };
            window.RefreshRequested += delegate
            {
                _ = RefreshSingleDeviceAsync(device.StableId, false);
            };
            window.PositionChangedByUser += delegate
            {
                if (!CanCaptureFloatingPosition() || !IsDeviceTargetMonitorAvailable(preference)) return;
                CaptureDevicePlacement(window, preference);
                _settingsStore.Save(_settings);
            };
            window.OpacityPresetRequested += delegate(object sender, OpacityPresetEventArgs args)
            {
                preference.OpacityPercent = FloatingAppearanceLimits.ClampOpacity(args.OpacityPercent);
                _settingsStore.Save(_settings);
                RenderDevices();
            };
            window.LockChangedRequested += delegate(object sender, BooleanSettingEventArgs args)
            {
                preference.FloatingLocked = args.Enabled;
                _settingsStore.Save(_settings);
                RenderDevices();
            };
            window.TopmostChangedRequested += delegate(object sender, BooleanSettingEventArgs args)
            {
                preference.AlwaysOnTop = args.Enabled;
                _settingsStore.Save(_settings);
                RenderDevices();
            };

            PrepareDevicePlacement(window, preference, _floatingWindows.Count);

            _floatingWindows[device.StableId] = window;
            window.Show();
        }

        private static bool IsDeviceTargetMonitorAvailable(DevicePreference preference)
        {
            if (preference == null) return true;
            return FloatingWindowPlacement.IsSavedMonitorAvailable(
                preference.MonitorDeviceName,
                preference.MonitorWorkWidth,
                preference.MonitorWorkHeight,
                preference.MonitorCenterXRatio,
                preference.MonitorCenterYRatio,
                preference.MonitorIdentity) &&
                (!string.IsNullOrWhiteSpace(preference.MonitorIdentity) ||
                 !HasFinitePosition(preference.Left, preference.Top) || IsValidPosition(preference.Left, preference.Top));
        }

        private bool IsMergedTargetMonitorAvailable()
        {
            return FloatingWindowPlacement.IsSavedMonitorAvailable(
                _settings.MergedMonitorDeviceName,
                _settings.MergedMonitorWorkWidth,
                _settings.MergedMonitorWorkHeight,
                _settings.MergedMonitorCenterXRatio,
                _settings.MergedMonitorCenterYRatio,
                _settings.MergedMonitorIdentity) &&
                (!string.IsNullOrWhiteSpace(_settings.MergedMonitorIdentity) ||
                 !HasFinitePosition(_settings.MergedLeft, _settings.MergedTop) ||
                 IsValidPosition(_settings.MergedLeft, _settings.MergedTop));
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            if (_isClosing || Dispatcher == null || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(SuspendFloatingForDisplayChange));
        }

        private void SuspendFloatingForDisplayChange()
        {
            if (_isClosing) return;
            if (!_displayTopologySettling)
                _diagnosticLogger.LogMessage("显示器连接发生变化：暂停浮窗，保留目标屏幕和位置，等待屏幕配置稳定。");
            _displayTopologySettling = true;
            _displayStableTicks = 0;
            _displayTopologySignature = FloatingWindowPlacement.GetTopologySignature();
            foreach (FloatingBatteryWindow window in _floatingWindows.Values.ToList())
            {
                window.SetPositionPersistenceEnabled(false);
                window.Close();
            }
            _floatingWindows.Clear();
            if (_mergedFloatingWindow != null)
            {
                _mergedFloatingWindow.SetPositionPersistenceEnabled(false);
                _mergedFloatingWindow.Close();
                _mergedFloatingWindow = null;
            }
        }

        private void OnDisplayMonitorTick(object sender, EventArgs e)
        {
            if (_isClosing || !_initializationQueued) return;
            string signature = FloatingWindowPlacement.GetTopologySignature();
            if (!string.Equals(signature, _displayTopologySignature, StringComparison.Ordinal))
            {
                SuspendFloatingForDisplayChange();
                return;
            }
            // A lightweight screen check is independent of battery refresh timers.
            // It covers late display initialization during boot and missed events.
            if (!_displayTopologySettling || ++_displayStableTicks < 2) return;
            _displayTopologySettling = false;
            _diagnosticLogger.LogMessage("显示器配置已稳定：仅恢复目标屏幕仍连接的浮窗，缺失屏幕继续等待重连。");
            RestoreAndUpdateFloatingWindows();
            UpdateMergedFloatingWindow();
        }

        private bool CanCaptureFloatingPosition()
        {
            if (_isClosing || _displayTopologySettling) return false;
            if (string.Equals(FloatingWindowPlacement.GetTopologySignature(), _displayTopologySignature, StringComparison.Ordinal))
                return true;
            // A removal can precede SystemEvents. Do not save Windows' relocation.
            Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(SuspendFloatingForDisplayChange));
            return false;
        }

        private bool TrySelectMonitorForFloating(
            BluetoothDeviceBattery device,
            DevicePreference preference)
        {
            IList<FloatingMonitorOption> monitors = FloatingWindowPlacement.GetAvailableMonitors();
            if (monitors == null || monitors.Count <= 1) return true;

            MonitorSelectionWindow selectionWindow = new MonitorSelectionWindow(
                this,
                monitors,
                device == null ? string.Empty : device.Name);
            bool selected = selectionWindow.ShowDialog() == true && selectionWindow.SelectedMonitor != null;
            WindowVisualHelper.BringToFront(this);
            if (!selected)
            {
                _diagnosticLogger.LogMessage(
                    "用户取消选择悬浮窗显示屏幕 | StableId=" + (device == null ? string.Empty : device.StableId));
                return false;
            }

            FloatingMonitorOption monitor = selectionWindow.SelectedMonitor;
            preference.Left = double.NaN;
            preference.Top = double.NaN;
            preference.MonitorDeviceName = monitor.DeviceName;
            preference.MonitorIdentity = monitor.MonitorIdentity;
            preference.MonitorWorkWidth = monitor.WorkWidth;
            preference.MonitorWorkHeight = monitor.WorkHeight;
            preference.MonitorCenterXRatio = 0.5;
            preference.MonitorCenterYRatio = 0.5;
            _diagnosticLogger.LogMessage(
                "用户选择悬浮窗显示屏幕 | StableId=" + (device == null ? string.Empty : device.StableId) +
                " | Monitor=" + monitor.DeviceName +
                " | Resolution=" + monitor.ResolutionWidth + "x" + monitor.ResolutionHeight +
                " | WorkArea=" + monitor.WorkLeft + "," + monitor.WorkTop + "," +
                monitor.WorkWidth + "x" + monitor.WorkHeight);
            return true;
        }

        private static bool IsValidPosition(double left, double top)
        {
            if (!HasFinitePosition(left, top)) return false;
            return left >= SystemParameters.VirtualScreenLeft - 120 &&
                   left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 &&
                   top >= SystemParameters.VirtualScreenTop - 30 &&
                   top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 30;
        }

        private static bool HasFinitePosition(double left, double top)
        {
            return !double.IsNaN(left) && !double.IsNaN(top) &&
                   !double.IsInfinity(left) && !double.IsInfinity(top);
        }

        private void PrepareDevicePlacement(
            FloatingBatteryWindow window,
            DevicePreference preference,
            int fallbackIndex)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            bool hasMonitorPlacement = FloatingWindowPlacement.HasSavedPlacement(
                preference.MonitorDeviceName,
                preference.MonitorWorkWidth,
                preference.MonitorWorkHeight,
                preference.MonitorCenterXRatio,
                preference.MonitorCenterYRatio);
            if (!hasMonitorPlacement && IsValidPosition(preference.Left, preference.Top))
            {
                window.Left = preference.Left;
                window.Top = preference.Top;
            }
            else
            {
                // Temporary invisible coordinates for HWND creation, not a
                // fallback destination for a missing saved monitor.
                window.Left = SystemParameters.WorkArea.Right - window.Width - 24;
                window.Top = SystemParameters.WorkArea.Top + 24 + (fallbackIndex * 78);
            }

            PrepareFloatingPlacement(window, hasMonitorPlacement,
                delegate { return IsDeviceTargetMonitorAvailable(preference); },
                delegate
                {
                    return FloatingWindowPlacement.TryRestore(window, preference.MonitorDeviceName,
                        preference.MonitorWorkWidth, preference.MonitorWorkHeight,
                        preference.MonitorCenterXRatio, preference.MonitorCenterYRatio, preference.MonitorIdentity);
                },
                window.SetPositionPersistenceEnabled,
                delegate
                {
                    if (!hasMonitorPlacement) CaptureDevicePlacement(window, preference);
                    else if (string.IsNullOrWhiteSpace(preference.MonitorIdentity))
                    {
                        FloatingWindowPlacementSnapshot snapshot = FloatingWindowPlacement.Capture(window);
                        if (snapshot != null && string.Equals(snapshot.MonitorDeviceName, preference.MonitorDeviceName,
                            StringComparison.OrdinalIgnoreCase)) preference.MonitorIdentity = snapshot.MonitorIdentity;
                    }
                });
        }

        private void PrepareFloatingPlacement(Window window, bool hasMonitorPlacement, Func<bool> targetAvailable,
            Func<bool> restore, Action<bool> enablePersistence, Action rememberPlacement)
        {
            enablePersistence(false);
            double requestedOpacity = window.Opacity;
            window.Opacity = 0;
            bool closed = false;
            window.Closed += delegate { closed = true; };
            FloatingMonitorGuard.Attach(window,
                delegate { return !_isClosing && !_displayTopologySettling && targetAvailable(); },
                delegate { enablePersistence(false); }, SuspendFloatingForDisplayChange);
            window.SourceInitialized += delegate
            {
                if (hasMonitorPlacement) restore();
            };
            window.Loaded += delegate
            {
                window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                {
                    if (closed || _isClosing) return;
                    // The display can disappear between enumeration, HWND
                    // creation and DPI/layout completion. Validate at every stage.
                    if (_displayTopologySettling || !targetAvailable() ||
                        (hasMonitorPlacement && !restore()) || !CanCaptureFloatingPosition())
                    {
                        enablePersistence(false);
                        Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(SuspendFloatingForDisplayChange));
                        return;
                    }
                    enablePersistence(true);
                    // Existing ratios are never recaptured after an automatic
                    // restore. Only a new/legacy identity needs to be recorded.
                    rememberPlacement();
                    _settingsStore.Save(_settings);
                    window.Opacity = requestedOpacity;
                }));
            };
        }

        private void PrepareMergedPlacement(MergedFloatingWindow window)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            bool hasMonitorPlacement = FloatingWindowPlacement.HasSavedPlacement(
                _settings.MergedMonitorDeviceName,
                _settings.MergedMonitorWorkWidth,
                _settings.MergedMonitorWorkHeight,
                _settings.MergedMonitorCenterXRatio,
                _settings.MergedMonitorCenterYRatio);
            if (!hasMonitorPlacement && IsValidPosition(_settings.MergedLeft, _settings.MergedTop))
            {
                window.Left = _settings.MergedLeft;
                window.Top = _settings.MergedTop;
            }
            else
            {
                window.Left = SystemParameters.WorkArea.Left + 24;
                window.Top = SystemParameters.WorkArea.Top + 24;
            }

            PrepareFloatingPlacement(window, hasMonitorPlacement, IsMergedTargetMonitorAvailable,
                delegate
                {
                    return FloatingWindowPlacement.TryRestore(window, _settings.MergedMonitorDeviceName,
                        _settings.MergedMonitorWorkWidth, _settings.MergedMonitorWorkHeight,
                        _settings.MergedMonitorCenterXRatio, _settings.MergedMonitorCenterYRatio, _settings.MergedMonitorIdentity);
                },
                window.SetPositionPersistenceEnabled,
                delegate
                {
                    if (!hasMonitorPlacement) CaptureMergedPlacement(window);
                    else if (string.IsNullOrWhiteSpace(_settings.MergedMonitorIdentity))
                    {
                        FloatingWindowPlacementSnapshot snapshot = FloatingWindowPlacement.Capture(window);
                        if (snapshot != null && string.Equals(snapshot.MonitorDeviceName, _settings.MergedMonitorDeviceName,
                            StringComparison.OrdinalIgnoreCase)) _settings.MergedMonitorIdentity = snapshot.MonitorIdentity;
                    }
                });
        }

        private static void CaptureDevicePlacement(FloatingBatteryWindow window, DevicePreference preference)
        {
            if (window == null || preference == null || !window.CanPersistPosition) return;
            FloatingWindowPlacementSnapshot placement = FloatingWindowPlacement.Capture(window);
            if (placement == null) return;
            preference.Left = window.Left;
            preference.Top = window.Top;
            preference.MonitorDeviceName = placement.MonitorDeviceName;
            preference.MonitorIdentity = placement.MonitorIdentity;
            preference.MonitorWorkWidth = placement.MonitorWorkWidth;
            preference.MonitorWorkHeight = placement.MonitorWorkHeight;
            preference.MonitorCenterXRatio = placement.CenterXRatio;
            preference.MonitorCenterYRatio = placement.CenterYRatio;
        }

        private void CaptureMergedPlacement(MergedFloatingWindow window)
        {
            if (window == null || !window.CanPersistPosition) return;
            FloatingWindowPlacementSnapshot placement = FloatingWindowPlacement.Capture(window);
            if (placement == null) return;
            _settings.MergedLeft = window.Left;
            _settings.MergedTop = window.Top;
            _settings.MergedMonitorDeviceName = placement.MonitorDeviceName;
            _settings.MergedMonitorIdentity = placement.MonitorIdentity;
            _settings.MergedMonitorWorkWidth = placement.MonitorWorkWidth;
            _settings.MergedMonitorWorkHeight = placement.MonitorWorkHeight;
            _settings.MergedMonitorCenterXRatio = placement.CenterXRatio;
            _settings.MergedMonitorCenterYRatio = placement.CenterYRatio;
        }

        private void CloseFloatingWindow(string stableId)
        {
            FloatingBatteryWindow window;
            if (!_floatingWindows.TryGetValue(stableId, out window)) return;
            _floatingWindows.Remove(stableId);
            window.Close();
        }

        private void ShowStatusMessage(string message)
        {
            if (_statusText != null) _statusText.Text = "✓ " + message;
        }

        private void InitializeTrayIcon()
        {
            System.Drawing.Icon trayIcon = null;
            try { trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(RuntimeCompat.LauncherPath); }
            catch { }
            if (trayIcon == null) trayIcon = System.Drawing.SystemIcons.Application;

            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = trayIcon,
                Text = "WavePulse 无线设备电量助手",
                Visible = true
            };
            _trayIcon.MouseClick += delegate(object sender, System.Windows.Forms.MouseEventArgs e)
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left) ShowMainWindowFromTray();
                else if (e.Button == System.Windows.Forms.MouseButtons.Right) ShowTrayMenu();
            };
        }

        private void ShowTrayMenu()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke((Action)ShowTrayMenu);
                return;
            }
            if (_trayMenuWindow != null)
            {
                _trayMenuWindow.Dismiss();
                _trayMenuWindow = null;
            }
            TrayMenuWindow menu = new TrayMenuWindow(
                ShowMainWindowFromTray,
                delegate
                {
                    ShowMainWindowFromTray();
                    _ = RefreshDevicesAsync(true);
                },
                RequestExit);
            menu.Closed += delegate { if (ReferenceEquals(_trayMenuWindow, menu)) _trayMenuWindow = null; };
            _trayMenuWindow = menu;
            menu.Show();
            menu.Activate();
        }

        private void ShowMainWindowFromTray()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke((Action)ShowMainWindowFromTray);
                return;
            }
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void HideDevice(BluetoothDeviceBattery device, DevicePreference preference)
        {
            if (device == null || preference == null || preference.IsHidden) return;
            preference.IsHidden = true;
            preference.IsFloating = false;
            preference.IncludeInMergedFloating = false;
            preference.IsExpanded = false;
            StopDeviceTimer(preference.StableId);
            CloseFloatingWindow(preference.StableId);
            _settingsStore.Save(_settings);
            UpdateMergedFloatingWindow();
            RenderDevices();
            _diagnosticLogger.LogMessage("设备已隐藏 | StableId=" + preference.StableId + " | Name=" + device.Name);
            AppToast.Show(this, "设备已隐藏", "可通过右下角的“查看隐藏设备”恢复显示");
        }

        private void RestoreHiddenDevice(DevicePreference preference)
        {
            if (preference == null || !preference.IsHidden) return;
            preference.IsHidden = false;
            preference.IsFloating = false;
            preference.IncludeInMergedFloating = false;
            _settingsStore.Save(_settings);
            BluetoothDeviceBattery device = _devices.FirstOrDefault(delegate(BluetoothDeviceBattery item)
            {
                return string.Equals(item.StableId, preference.StableId, StringComparison.OrdinalIgnoreCase);
            });
            if (device != null) ConfigureDeviceTimer(device.StableId, preference);
            RenderDevices();
            UpdateMergedFloatingWindow();
            _diagnosticLogger.LogMessage("设备已取消隐藏 | StableId=" + preference.StableId);
            AppToast.Show(this, "已取消隐藏", "设备已恢复到相应的无线设备列表");
        }

        private bool DeleteDeviceRecord(DevicePreference preference)
        {
            if (preference == null) return false;
            string displayName = string.IsNullOrWhiteSpace(preference.CustomName)
                ? (string.IsNullOrWhiteSpace(preference.LastKnownName) ? "未命名设备" : preference.LastKnownName)
                : preference.CustomName;
            Window dialogOwner = _hiddenDevicesWindow != null && _hiddenDevicesWindow.IsVisible
                ? (Window)_hiddenDevicesWindow
                : this;
            ConfirmActionWindow confirm = new ConfirmActionWindow(
                dialogOwner,
                "删除设备记录",
                "删除“" + displayName + "”的本地记录？",
                "名称、类型、自定义图标、刷新设置及悬浮窗设置都会被删除。设备当前会从列表消失；如果它仍在电脑上，下一次点击“刷新列表”会重新识别为新设备。",
                "删除记录");
            if (confirm.ShowDialog() != true || !confirm.Confirmed) return false;

            StopDeviceTimer(preference.StableId);
            CloseFloatingWindow(preference.StableId);
            _settingsStore.DeleteImportedIcon(preference.CustomIconPath);
            _settings.Devices.Remove(preference);
            _devices = _devices.Where(delegate(BluetoothDeviceBattery device)
            {
                return !string.Equals(device.StableId, preference.StableId, StringComparison.OrdinalIgnoreCase);
            }).ToList();
            _settingsStore.Save(_settings);
            UpdateMergedFloatingWindow();
            RenderDevices();
            _diagnosticLogger.LogMessage("设备本地记录已删除 | StableId=" + preference.StableId + " | Name=" + displayName);
            AppToast.Show(this, "设备记录已删除", "点击“刷新列表”可重新识别仍然存在的设备");
            return true;
        }

        private void StopDeviceTimer(string stableId)
        {
            DispatcherTimer timer;
            if (!_deviceRefreshTimers.TryGetValue(stableId, out timer)) return;
            timer.Stop();
            _deviceRefreshTimers.Remove(stableId);
        }

        private void ShowHiddenDevicesWindow()
        {
            if (_hiddenDevicesWindow != null)
            {
                if (!_hiddenDevicesWindow.IsVisible) _hiddenDevicesWindow.Show();
                _hiddenDevicesWindow.Activate();
                return;
            }
            HiddenDevicesWindow window = new HiddenDevicesWindow(
                this,
                _settings,
                RestoreHiddenDevice,
                DeleteDeviceRecord);
            _hiddenDevicesWindow = window;
            window.Closed += delegate
            {
                _hiddenDevicesWindow = null;
                WindowVisualHelper.BringToFront(this);
            };
            window.Show();
            window.Activate();
        }

        private void ShowSettingsWindow()
        {
            if (_settingsWindow != null)
            {
                if (!_settingsWindow.IsVisible) _settingsWindow.Show();
                _settingsWindow.Activate();
                return;
            }
            _diagnosticLogger.LogMessage("用户打开应用设置窗口。");
            try
            {
                SettingsWindow settingsWindow = new SettingsWindow(
                    this,
                    _settings,
                    _settingsStore,
                    ExportDiagnosticLog,
                    OnMergeFloatingChanged,
                    OnMergeOptionsChanged);
                _settingsWindow = settingsWindow;
                settingsWindow.Closed += delegate
                {
                    _settingsWindow = null;
                    if (!_isScanning) RenderDevices();
                    _diagnosticLogger.LogMessage("设置窗口已关闭。");
                };
                _diagnosticLogger.LogMessage("设置窗口已构造，准备显示。");
                settingsWindow.Show();
                settingsWindow.Activate();
            }
            catch (Exception ex)
            {
                _diagnosticLogger.LogError("打开设置窗口", ex);
                AppToast.ShowError(this, "设置窗口错误", ex.Message);
            }
        }

        private void ShowReceiverSupportWindow()
        {
            _diagnosticLogger.LogMessage("用户查看 2.4G 适配设备列表。");
            ReceiverSupportWindow window = new ReceiverSupportWindow(this);
            window.ShowDialog();
        }

        private void ExportDiagnosticLog()
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "导出 WavePulse 诊断日志",
                Filter = "文本日志|*.txt",
                DefaultExt = ".txt",
                AddExtension = true,
                FileName = "WavePulse-无线设备电量诊断日志-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt"
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                _diagnosticLogger.Export(dialog.FileName, _devices, _settings, _reader.LastDiagnosticDetails);
                AppToast.Show(this, "导出完成", dialog.FileName);
            }
            catch (Exception ex)
            {
                _diagnosticLogger.LogError("导出诊断日志", ex);
                AppToast.ShowError(this, "导出失败", ex.Message);
            }
        }

        internal void RequestExit()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(RequestExit);
                return;
            }
            if (_allowExit) return;
            _allowExit = true;
            Close();
            if (Application.Current != null) Application.Current.Shutdown();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (!_allowExit)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                _diagnosticLogger.LogMessage("主窗口关闭按钮被点击，窗口已隐藏，程序继续在通知区域运行。");
                return;
            }
            _isClosing = true;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanging -= OnDisplaySettingsChanged;
            _displayMonitorTimer.Stop();
            foreach (DispatcherTimer timer in _deviceRefreshTimers.Values) timer.Stop();
            _deviceRefreshTimers.Clear();
            if (_scanCancellation != null) _scanCancellation.Cancel();
            foreach (FloatingBatteryWindow window in _floatingWindows.Values.ToList())
            {
                window.Close();
            }
            _floatingWindows.Clear();
            if (_mergedFloatingWindow != null)
            {
                _mergedFloatingWindow.Close();
                _mergedFloatingWindow = null;
            }
            _settingsStore.Save(_settings);
            if (_trayMenuWindow != null)
            {
                _trayMenuWindow.Dismiss();
                _trayMenuWindow = null;
            }
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                if (_trayIcon.Icon != null && _trayIcon.Icon != System.Drawing.SystemIcons.Application)
                {
                    _trayIcon.Icon.Dispose();
                }
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
    }
}
