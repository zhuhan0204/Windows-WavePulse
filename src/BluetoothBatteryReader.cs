using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace BluetoothBatteryMonitor
{
    internal sealed class BluetoothBatteryReader
    {
        private const string ConnectedProperty = "System.Devices.Aep.IsConnected";
        private const string AddressProperty = "System.Devices.Aep.DeviceAddress";
        private const string BatteryProperty = "System.Devices.BatteryLife";
        private const string PnpBatteryProperty = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
        private const string ContainerProperty = "System.Devices.Aep.ContainerId";
        private const string ProtocolProperty = "System.Devices.Aep.ProtocolId";
        private const string SignalProperty = "System.Devices.Aep.SignalStrength";
        private const string PresentProperty = "System.Devices.Aep.IsPresent";
        private const string PairedProperty = "System.Devices.Aep.IsPaired";
        private const string ManufacturerProperty = "System.Devices.Manufacturer";
        private const string ModelProperty = "System.Devices.ModelName";
        private const string InstanceProperty = "System.Devices.DeviceInstanceId";
        private const string InterfaceClassProperty = "System.Devices.InterfaceClassGuid";

        private static readonly string[] RequestedProperties =
        {
            ConnectedProperty,
            AddressProperty,
            BatteryProperty,
            ContainerProperty,
            ProtocolProperty,
            SignalProperty,
            PresentProperty,
            PairedProperty,
            ManufacturerProperty,
            ModelProperty,
            InstanceProperty,
            InterfaceClassProperty,
        };

        public string LastDiagnosticDetails { get; private set; } = string.Empty;
        public string LastBluetoothError { get; private set; } = string.Empty;
        public string LastReceiverError { get; private set; } = string.Empty;

        public async Task<IList<BluetoothDeviceBattery>> ScanAsync(CancellationToken cancellationToken)
        {
            StringBuilder diagnostics = new StringBuilder();
            diagnostics.AppendLine("扫描时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            Dictionary<string, BluetoothDeviceBattery> devices =
                new Dictionary<string, BluetoothDeviceBattery>(StringComparer.OrdinalIgnoreCase);
            LastBluetoothError = string.Empty;
            LastReceiverError = string.Empty;

            try
            {
                await EnsureBluetoothAvailableAsync(diagnostics, cancellationToken);

                await AddDevicesAsync(
                    BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                    false,
                    devices,
                    diagnostics,
                    cancellationToken);

                await AddDevicesAsync(
                    BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                    true,
                    devices,
                    diagnostics,
                    cancellationToken);

                // 经典蓝牙耳机通常把 HFP 电量放在 Hands-Free AG 子设备，
                // 而不是主蓝牙 Association Endpoint。按物理蓝牙地址合并回来。
                await AddPnpBatteryLevelsAsync(devices, diagnostics, cancellationToken);

                List<Task> batteryTasks = new List<Task>();
                foreach (BluetoothDeviceBattery device in devices.Values.ToList())
                {
                    if (device.IsConnected && device.IsLowEnergy && !device.BatteryPercent.HasValue)
                    {
                        batteryTasks.Add(ReadGattBatterySafelyAsync(device, diagnostics, cancellationToken));
                    }
                }
                if (batteryTasks.Count > 0) await Task.WhenAll(batteryTasks);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastBluetoothError = ex.Message;
                diagnostics.AppendLine("蓝牙扫描不可用：" + ex.Message);
            }

            try
            {
                LogitechHidppScanResult receiverResult = await LogitechHidppReader.ScanAsync(cancellationToken);
                diagnostics.AppendLine(receiverResult.Diagnostics);
                foreach (BluetoothDeviceBattery receiverDevice in receiverResult.Devices)
                {
                    devices[receiverDevice.StableId] = receiverDevice;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastReceiverError = ex.Message;
                diagnostics.AppendLine("2.4G/HID++ 扫描不可用：" + ex.Message);
            }

            try
            {
                GenericReceiverScanResult mchoseResult = await MchoseTopSpeedKeyboardReader.ScanAsync(cancellationToken);
                diagnostics.AppendLine(mchoseResult.Diagnostics);
                foreach (BluetoothDeviceBattery receiverDevice in mchoseResult.Devices)
                {
                    devices[receiverDevice.StableId] = receiverDevice;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastReceiverError = string.IsNullOrWhiteSpace(LastReceiverError)
                    ? ex.Message
                    : LastReceiverError + " | " + ex.Message;
                diagnostics.AppendLine("2.4G/迈从 TopSpeed 扫描不可用：" + ex.Message);
            }

            try
            {
                GenericReceiverScanResult eweadnResult = await EweadnS9MouseReader.ScanAsync(cancellationToken);
                diagnostics.AppendLine(eweadnResult.Diagnostics);
                foreach (BluetoothDeviceBattery receiverDevice in eweadnResult.Devices)
                {
                    devices[receiverDevice.StableId] = receiverDevice;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastReceiverError = string.IsNullOrWhiteSpace(LastReceiverError)
                    ? ex.Message
                    : LastReceiverError + " | " + ex.Message;
                diagnostics.AppendLine("2.4G/前行者 S9 Pro 扫描不可用：" + ex.Message);
            }

            try
            {
                GenericReceiverScanResult genericResult = await GenericReceiverBatteryReader.ScanAsync(cancellationToken);
                diagnostics.AppendLine(genericResult.Diagnostics);
                foreach (BluetoothDeviceBattery receiverDevice in genericResult.Devices)
                {
                    devices[receiverDevice.StableId] = receiverDevice;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastReceiverError = string.IsNullOrWhiteSpace(LastReceiverError)
                    ? ex.Message
                    : LastReceiverError + " | " + ex.Message;
                diagnostics.AppendLine("2.4G/HID Feature 扫描不可用：" + ex.Message);
            }

            List<BluetoothDeviceBattery> result = devices.Values.ToList();
            diagnostics.AppendLine("最终合并设备：");
            foreach (BluetoothDeviceBattery device in result)
            {
                device.LastRefreshedAt = DateTime.Now;
                diagnostics.AppendLine(
                    "  StableId=" + device.StableId +
                    " | Name=" + device.Name +
                    " | Address=" + device.Address +
                    " | Transport=" + device.Transport +
                    " | Connected=" + device.IsConnected +
                    " | Charging=" + device.IsCharging +
                    " | BLE=" + device.IsLowEnergy +
                    " | Battery=" + (device.BatteryPercent.HasValue ? device.BatteryPercent.Value.ToString() : "null") +
                    " | LeftBattery=" + (device.LeftBatteryPercent.HasValue ? device.LeftBatteryPercent.Value.ToString() : "null") +
                    " | RightBattery=" + (device.RightBatteryPercent.HasValue ? device.RightBatteryPercent.Value.ToString() : "null") +
                    " | CaseBattery=" + (device.CaseBatteryPercent.HasValue ? device.CaseBatteryPercent.Value.ToString() : "null") +
                    " | Source=" + device.BatterySource +
                    " | Error=" + device.Error +
                    " | DeviceId=" + device.DeviceId);
            }
            LastDiagnosticDetails = diagnostics.ToString();

            result.Sort(delegate(BluetoothDeviceBattery left, BluetoothDeviceBattery right)
            {
                int connectionCompare = right.IsConnected.CompareTo(left.IsConnected);
                if (connectionCompare != 0) return connectionCompare;
                int batteryCompare = Nullable.Compare(right.BatteryPercent, left.BatteryPercent);
                if (batteryCompare != 0) return batteryCompare;
                return string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return result;
        }

        private static async Task EnsureBluetoothAvailableAsync(StringBuilder diagnostics, CancellationToken cancellationToken)
        {
            try
            {
                BluetoothAdapter adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(cancellationToken);
                if (adapter == null)
                {
                    throw new InvalidOperationException("未检测到可用的蓝牙适配器。");
                }

                try
                {
                    Radio radio = await adapter.GetRadioAsync().AsTask(cancellationToken);
                    if (radio != null)
                    {
                        diagnostics.AppendLine("蓝牙无线电状态：" + radio.State);
                        if (radio.State == RadioState.Off || radio.State == RadioState.Disabled)
                        {
                            throw new InvalidOperationException("蓝牙已关闭，请先在 Windows 中开启蓝牙。");
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    diagnostics.AppendLine("无法直接读取蓝牙无线电开关状态，继续通过设备接口扫描。");
                }
            }
            catch (COMException ex)
            {
                uint code = unchecked((uint)ex.HResult);
                if (code == 0x80004004 || code == 0x800710DF || code == 0x80070490)
                {
                    throw new InvalidOperationException("Windows 中的蓝牙当前不可用或已关闭。", ex);
                }
                throw;
            }
        }

        private static async Task AddPnpBatteryLevelsAsync(
            IDictionary<string, BluetoothDeviceBattery> devices,
            StringBuilder diagnostics,
            CancellationToken cancellationToken)
        {
            string[] properties = { PnpBatteryProperty };
            DeviceInformationCollection found = await DeviceInformation.FindAllAsync(
                string.Empty,
                properties,
                DeviceInformationKind.Device).AsTask(cancellationToken);

            foreach (DeviceInformation info in found)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object rawBattery;
                if (!info.Properties.TryGetValue(PnpBatteryProperty, out rawBattery) || rawBattery == null)
                {
                    continue;
                }

                int battery;
                try { battery = Convert.ToInt32(rawBattery); }
                catch { continue; }
                if (battery < 0 || battery > 100) continue;

                string address = ExtractAddress(info.Id);
                diagnostics.AppendLine(
                    "HFP/PnP 电量节点 | Name=" + info.Name +
                    " | Battery=" + battery +
                    " | Address=" + address +
                    " | Id=" + info.Id);
                BluetoothDeviceBattery device;
                if (!TryFindAssociatedDevice(devices, info.Name, address, out device))
                {
                    continue;
                }

                EarbudComponent component = DetectEarbudComponent(info.Name, info.Id);
                if (component == EarbudComponent.Left)
                {
                    device.LeftBatteryPercent = battery;
                }
                else if (component == EarbudComponent.Right)
                {
                    device.RightBatteryPercent = battery;
                }
                else if (component == EarbudComponent.Case)
                {
                    device.CaseBatteryPercent = battery;
                }
                else if (!device.BatteryPercent.HasValue)
                {
                    device.BatteryPercent = battery;
                    device.BatterySource = "Windows HFP/PnP";
                    device.Error = string.Empty;
                }

                if (device.Kind == DeviceKind.Unknown &&
                    ContainsAny((info.Name ?? string.Empty).ToLowerInvariant(), "hands-free", "headset", "earphone", "earbud", "buds", "耳机", "左耳", "右耳"))
                {
                    device.Kind = DeviceKind.Headphones;
                    device.DetectedKind = DeviceKind.Headphones;
                }
            }
        }

        private enum EarbudComponent
        {
            None,
            Left,
            Right,
            Case
        }

        private static EarbudComponent DetectEarbudComponent(string name, string id)
        {
            string value = ((name ?? string.Empty) + " " + (id ?? string.Empty)).ToLowerInvariant();
            if (ContainsAny(value, "charging case", "charge case", "battery case", "充电盒", "耳机盒")) return EarbudComponent.Case;
            if (Regex.IsMatch(value, @"(^|[\s_\-\(\[])left([\s_\-\)\]]|$)") ||
                Regex.IsMatch(value, @"(^|[\s_\-\(\[])l([\s_\-\)\]]|$)") || ContainsAny(value, "左耳"))
            {
                return EarbudComponent.Left;
            }
            if (Regex.IsMatch(value, @"(^|[\s_\-\(\[])right([\s_\-\)\]]|$)") ||
                Regex.IsMatch(value, @"(^|[\s_\-\(\[])r([\s_\-\)\]]|$)") || ContainsAny(value, "右耳"))
            {
                return EarbudComponent.Right;
            }
            return EarbudComponent.None;
        }

        private static bool TryFindAssociatedDevice(
            IDictionary<string, BluetoothDeviceBattery> devices,
            string nodeName,
            string address,
            out BluetoothDeviceBattery device)
        {
            if (!string.IsNullOrWhiteSpace(address) && devices.TryGetValue("address:" + address, out device))
            {
                return true;
            }

            string normalizedNode = NormalizeAccessoryName(nodeName);
            if (normalizedNode.Length >= 4)
            {
                foreach (BluetoothDeviceBattery candidate in devices.Values)
                {
                    string normalizedDevice = NormalizeAccessoryName(candidate.DetectedName);
                    if (normalizedDevice.Length >= 4 &&
                        (normalizedDevice.Contains(normalizedNode) || normalizedNode.Contains(normalizedDevice)))
                    {
                        device = candidate;
                        return true;
                    }
                }
            }
            device = null;
            return false;
        }

        private static string NormalizeAccessoryName(string name)
        {
            string value = (name ?? string.Empty).ToLowerInvariant();
            value = Regex.Replace(value, "hands[- ]?free|headset|headphone|earphone|earbud|charging case|charge case|battery case|左耳|右耳|充电盒|耳机盒", string.Empty);
            value = Regex.Replace(value, @"(^|[\s_\-\(\[])(?:left|right|l|r)([\s_\-\)\]]|$)", " ");
            return Regex.Replace(value, @"[^a-z0-9\u4e00-\u9fff]+", string.Empty);
        }

        private static async Task AddDevicesAsync(
            string selector,
            bool isLowEnergy,
            IDictionary<string, BluetoothDeviceBattery> devices,
            StringBuilder diagnostics,
            CancellationToken cancellationToken)
        {
            DeviceInformationCollection found = await DeviceInformation.FindAllAsync(selector, RequestedProperties).AsTask(cancellationToken);
            foreach (DeviceInformation info in found)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string detectedName = string.IsNullOrWhiteSpace(info.Name) ? "未命名蓝牙设备" : info.Name.Trim();
                diagnostics.AppendLine(
                    (isLowEnergy ? "BLE AEP" : "经典蓝牙 AEP") +
                    " | Name=" + detectedName +
                    " | Connected=" + GetBoolean(info, ConnectedProperty) +
                    " | Address=" + GetAddress(info) +
                    " | BatteryProperty=" + (GetBattery(info).HasValue ? GetBattery(info).Value.ToString() : "null") +
                    " | Properties=" + FormatDeviceProperties(info) +
                    " | Id=" + info.Id);
                string stableId = GetStableId(info);
                BluetoothDeviceBattery existing;
                if (!devices.TryGetValue(stableId, out existing))
                {
                    existing = new BluetoothDeviceBattery
                    {
                        StableId = stableId,
                        DeviceId = info.Id,
                        Name = detectedName,
                        DetectedName = detectedName,
                        Address = GetAddress(info),
                        IsLowEnergy = isLowEnergy,
                        IsConnected = GetBoolean(info, ConnectedProperty),
                        IsPaired = true,
                        BatteryPercent = GetBattery(info),
                        BatterySource = GetBattery(info).HasValue ? "Windows 设备属性" : string.Empty
                    };
                    existing.Kind = GuessKind(existing.Name);
                    existing.DetectedKind = existing.Kind;
                    devices.Add(stableId, existing);
                }
                else
                {
                    bool connected = GetBoolean(info, ConnectedProperty);
                    int? battery = GetBattery(info);
                    existing.IsConnected = existing.IsConnected || connected;
                    existing.IsPaired = true;
                    if (isLowEnergy)
                    {
                        existing.IsLowEnergy = true;
                        // GATT 电量只能通过 BLE Association Endpoint 打开；
                        // 双模设备即使经典蓝牙端点报告“已连接”，也要保留 BLE Id。
                        existing.DeviceId = info.Id;
                    }
                    if (battery.HasValue)
                    {
                        existing.BatteryPercent = battery;
                        existing.BatterySource = "Windows 设备属性";
                    }
                }
            }
        }

        private static async Task ReadGattBatterySafelyAsync(
            BluetoothDeviceBattery device,
            StringBuilder diagnostics,
            CancellationToken cancellationToken)
        {
            BluetoothLEDevice bluetoothDevice = null;
            AppendDiagnostic(diagnostics,
                "BLE GATT 电量尝试开始 | StableId=" + device.StableId +
                " | Name=" + device.Name + " | DeviceId=" + device.DeviceId);
            try
            {
                bluetoothDevice = await BluetoothLEDevice.FromIdAsync(device.DeviceId).AsTask(cancellationToken);
                if (bluetoothDevice == null)
                {
                    device.Error = "无法打开 BLE 设备";
                    AppendDiagnostic(diagnostics, "BLE GATT 打开失败 | StableId=" + device.StableId + " | Result=null");
                    return;
                }

                device.IsConnected = bluetoothDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;
                AppendDiagnostic(diagnostics,
                    "BLE GATT 设备状态 | StableId=" + device.StableId +
                    " | ConnectionStatus=" + bluetoothDevice.ConnectionStatus +
                    " | BluetoothAddress=0x" + bluetoothDevice.BluetoothAddress.ToString("X12"));
                GattDeviceServicesResult servicesResult = await bluetoothDevice
                    .GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken);

                if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
                {
                    device.Error = "设备未公开标准 Battery Service";
                    AppendDiagnostic(diagnostics,
                        "BLE GATT Battery Service | StableId=" + device.StableId +
                        " | Status=" + servicesResult.Status + " | ServiceCount=" + servicesResult.Services.Count);
                    await LogGattInventoryAsync(bluetoothDevice, device, diagnostics, cancellationToken);
                    return;
                }
                AppendDiagnostic(diagnostics,
                    "BLE GATT Battery Service | StableId=" + device.StableId +
                    " | Status=" + servicesResult.Status + " | ServiceCount=" + servicesResult.Services.Count);

                GattDeviceService service = servicesResult.Services[0];
                try
                {
                    GattCharacteristicsResult characteristicsResult = await service
                        .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached)
                        .AsTask(cancellationToken);

                    if (characteristicsResult.Status != GattCommunicationStatus.Success || characteristicsResult.Characteristics.Count == 0)
                    {
                        device.Error = "Battery Service 未提供电量特征";
                        AppendDiagnostic(diagnostics,
                            "BLE GATT Battery Level 特征 | StableId=" + device.StableId +
                            " | Status=" + characteristicsResult.Status +
                            " | CharacteristicCount=" + characteristicsResult.Characteristics.Count);
                        await LogGattInventoryAsync(bluetoothDevice, device, diagnostics, cancellationToken);
                        return;
                    }
                    AppendDiagnostic(diagnostics,
                        "BLE GATT Battery Level 特征 | StableId=" + device.StableId +
                        " | Status=" + characteristicsResult.Status +
                        " | CharacteristicCount=" + characteristicsResult.Characteristics.Count);

                    GattReadResult readResult = await characteristicsResult.Characteristics[0]
                        .ReadValueAsync(BluetoothCacheMode.Uncached)
                        .AsTask(cancellationToken);

                    if (readResult.Status == GattCommunicationStatus.Success && readResult.Value != null && readResult.Value.Length > 0)
                    {
                        using (DataReader reader = DataReader.FromBuffer(readResult.Value))
                        {
                            byte[] raw = new byte[(int)readResult.Value.Length];
                            reader.ReadBytes(raw);
                            int value = raw[0];
                            device.BatteryPercent = Math.Max(0, Math.Min(100, value));
                            device.BatterySource = "BLE Battery Service";
                            device.Error = string.Empty;
                            AppendDiagnostic(diagnostics,
                                "BLE GATT Battery Level 读取 | StableId=" + device.StableId +
                                " | Status=" + readResult.Status +
                                " | Raw=" + ToHex(raw) +
                                " | Battery=" + device.BatteryPercent.Value + "%");
                        }
                    }
                    else
                    {
                        device.Error = "读取电量时设备没有响应";
                        AppendDiagnostic(diagnostics,
                            "BLE GATT Battery Level 读取失败 | StableId=" + device.StableId +
                            " | Status=" + readResult.Status +
                            " | Length=" + (readResult.Value == null ? 0 : readResult.Value.Length));
                    }
                }
                finally
                {
                    service.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                // 一个休眠设备不应让整批设备列表读取失败。
                device.Error = "读取电量超时，请先唤醒设备";
                AppendDiagnostic(diagnostics, "BLE GATT 读取超时 | StableId=" + device.StableId);
            }
            catch (UnauthorizedAccessException)
            {
                device.Error = "Windows 拒绝了蓝牙访问，请检查隐私设置";
                AppendDiagnostic(diagnostics, "BLE GATT 访问被拒绝 | StableId=" + device.StableId);
            }
            catch (Exception ex)
            {
                device.Error = FriendlyError(ex);
                AppendDiagnostic(diagnostics,
                    "BLE GATT 读取异常 | StableId=" + device.StableId +
                    " | HResult=0x" + unchecked((uint)ex.HResult).ToString("X8") +
                    " | Type=" + ex.GetType().FullName + " | Message=" + ex.Message);
            }
            finally
            {
                if (bluetoothDevice != null)
                {
                    bluetoothDevice.Dispose();
                }
            }
        }

        private static async Task LogGattInventoryAsync(
            BluetoothLEDevice bluetoothDevice,
            BluetoothDeviceBattery device,
            StringBuilder diagnostics,
            CancellationToken cancellationToken)
        {
            try
            {
                GattDeviceServicesResult allServices = await bluetoothDevice
                    .GetGattServicesAsync(BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken);
                AppendDiagnostic(diagnostics,
                    "BLE GATT 深度清单 | StableId=" + device.StableId +
                    " | Status=" + allServices.Status +
                    " | ServiceCount=" + allServices.Services.Count);

                foreach (GattDeviceService candidateService in allServices.Services)
                {
                    try
                    {
                        GattCharacteristicsResult allCharacteristics = await candidateService
                            .GetCharacteristicsAsync(BluetoothCacheMode.Uncached)
                            .AsTask(cancellationToken);
                        AppendDiagnostic(diagnostics,
                            "  GATT Service | Uuid=" + candidateService.Uuid +
                            " | CharacteristicStatus=" + allCharacteristics.Status +
                            " | CharacteristicCount=" + allCharacteristics.Characteristics.Count);

                        foreach (GattCharacteristic characteristic in allCharacteristics.Characteristics)
                        {
                            AppendDiagnostic(diagnostics,
                                "    GATT Characteristic | Uuid=" + characteristic.Uuid +
                                " | Properties=" + characteristic.CharacteristicProperties);
                            if ((characteristic.CharacteristicProperties & GattCharacteristicProperties.Read) == 0)
                            {
                                continue;
                            }

                            try
                            {
                                GattReadResult candidateRead = await characteristic
                                    .ReadValueAsync(BluetoothCacheMode.Uncached)
                                    .AsTask(cancellationToken);
                                byte[] raw = Array.Empty<byte>();
                                if (candidateRead.Value != null && candidateRead.Value.Length > 0)
                                {
                                    using (DataReader reader = DataReader.FromBuffer(candidateRead.Value))
                                    {
                                        raw = new byte[(int)candidateRead.Value.Length];
                                        reader.ReadBytes(raw);
                                    }
                                }
                                AppendDiagnostic(diagnostics,
                                    "      GATT Read | Status=" + candidateRead.Status +
                                    " | Length=" + raw.Length +
                                    " | Raw=" + ToHex(raw));
                            }
                            catch (Exception readException)
                            {
                                AppendDiagnostic(diagnostics,
                                    "      GATT Read 异常 | HResult=0x" +
                                    unchecked((uint)readException.HResult).ToString("X8") +
                                    " | Type=" + readException.GetType().FullName +
                                    " | Message=" + readException.Message);
                            }
                        }
                    }
                    catch (Exception serviceException)
                    {
                        AppendDiagnostic(diagnostics,
                            "  GATT Service 枚举异常 | Uuid=" + candidateService.Uuid +
                            " | HResult=0x" + unchecked((uint)serviceException.HResult).ToString("X8") +
                            " | Message=" + serviceException.Message);
                    }
                    finally
                    {
                        candidateService.Dispose();
                    }
                }
            }
            catch (Exception inventoryException)
            {
                AppendDiagnostic(diagnostics,
                    "BLE GATT 深度清单异常 | StableId=" + device.StableId +
                    " | HResult=0x" + unchecked((uint)inventoryException.HResult).ToString("X8") +
                    " | Type=" + inventoryException.GetType().FullName +
                    " | Message=" + inventoryException.Message);
            }
        }

        private static void AppendDiagnostic(StringBuilder diagnostics, string line)
        {
            if (diagnostics == null) return;
            lock (diagnostics) diagnostics.AppendLine(line);
        }

        private static string FormatDeviceProperties(DeviceInformation info)
        {
            if (info == null || info.Properties == null || info.Properties.Count == 0) return "{}";
            return "{" + string.Join(", ", info.Properties
                .OrderBy(delegate(KeyValuePair<string, object> property) { return property.Key; }, StringComparer.OrdinalIgnoreCase)
                .Select(delegate(KeyValuePair<string, object> property)
                {
                    return property.Key + "=" + FormatPropertyValue(property.Value);
                })) + "}";
        }

        private static string FormatPropertyValue(object value)
        {
            if (value == null) return "null";
            byte[] bytes = value as byte[];
            if (bytes != null) return ToHex(bytes);
            string text = value as string;
            if (text != null) return text.Replace("\r", " ").Replace("\n", " ");
            IEnumerable values = value as IEnumerable;
            if (values != null)
            {
                List<string> parts = new List<string>();
                foreach (object item in values) parts.Add(item == null ? "null" : item.ToString());
                return "[" + string.Join(";", parts) + "]";
            }
            return value.ToString();
        }

        private static string ToHex(byte[] bytes)
        {
            return bytes == null ? "<null>" : string.Join(" ", bytes.Select(delegate(byte value) { return value.ToString("X2"); }));
        }

        private static string FriendlyError(Exception ex)
        {
            uint code = unchecked((uint)ex.HResult);
            if (code == 0x80070490) return "设备当前不可用";
            if (code == 0x800710DF) return "蓝牙设备尚未连接";
            if (code == 0x80070005) return "没有蓝牙访问权限";
            return "设备未上报可读取的电量";
        }

        private static bool GetBoolean(DeviceInformation info, string key)
        {
            return GetBoolean(info, key, false);
        }

        private static bool GetBoolean(DeviceInformation info, string key, bool fallback)
        {
            object value;
            if (!info.Properties.TryGetValue(key, out value) || value == null)
            {
                return fallback;
            }
            try { return Convert.ToBoolean(value); }
            catch { return fallback; }
        }

        private static int? GetBattery(DeviceInformation info)
        {
            object value;
            if (!info.Properties.TryGetValue(BatteryProperty, out value) || value == null)
            {
                return null;
            }
            try
            {
                int percent = Convert.ToInt32(value);
                if (percent >= 0 && percent <= 100) return percent;
            }
            catch
            {
            }
            return null;
        }

        private static string GetStableId(DeviceInformation info)
        {
            string address = GetAddress(info);
            if (!string.IsNullOrWhiteSpace(address))
            {
                return "address:" + address;
            }
            return "id:" + info.Id.ToLowerInvariant();
        }

        private static string GetAddress(DeviceInformation info)
        {
            object value;
            if (info.Properties.TryGetValue(AddressProperty, out value) && value != null)
            {
                string address = value.ToString().Trim().ToUpperInvariant().Replace('-', ':');
                if (!string.IsNullOrWhiteSpace(address)) return address;
            }
            return ExtractAddress(info.Id);
        }

        private static string ExtractAddress(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            Match match = Regex.Match(id, "(?:Dev_|BluetoothDevice_)([0-9A-Fa-f]{12})", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                match = Regex.Match(id, "&([0-9A-Fa-f]{12})(?:_C|$)", RegexOptions.IgnoreCase);
            }
            if (!match.Success)
            {
                MatchCollection matches = Regex.Matches(id, "([0-9A-Fa-f]{12})");
                if (matches.Count > 0) match = matches[matches.Count - 1];
            }
            if (!match.Success) return string.Empty;
            string raw = match.Groups[1].Value.ToUpperInvariant();
            return string.Join(":", Enumerable.Range(0, 6).Select(delegate(int index) { return raw.Substring(index * 2, 2); }));
        }

        private static DeviceKind GuessKind(string name)
        {
            string value = (name ?? string.Empty).ToLowerInvariant();
            if (ContainsAny(value, "mouse", "鼠标", "mx master", "m650", "m720", "m750", "magic mouse")) return DeviceKind.Mouse;
            if (ContainsAny(value, "keyboard", "键盘", "keys", "kbd", "magic keyboard")) return DeviceKind.Keyboard;
            if (ContainsAny(value, "headphone", "headset", "earbud", "耳机", "airpods", "buds", "freebuds", "wh-", "wf-")) return DeviceKind.Headphones;
            if (ContainsAny(value, "gamepad", "controller", "手柄", "xbox", "dualshock", "dualsense")) return DeviceKind.Gamepad;
            if (ContainsAny(value, "pen", "stylus", "触控笔", "pencil")) return DeviceKind.Pen;
            if (ContainsAny(value, "speaker", "音箱", "音响", "soundbar")) return DeviceKind.Speaker;
            if (ContainsAny(value, "phone", "手机", "iphone", "pixel")) return DeviceKind.Phone;
            return DeviceKind.Unknown;
        }

        private static bool ContainsAny(string source, params string[] values)
        {
            foreach (string value in values)
            {
                if (source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
