using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using HaloVolt.Infrastructure;
using HaloVolt.Models;

namespace HaloVolt.Providers;

internal sealed class BleTarget
{
    public required string DeviceInformationId { get; init; }
    public required ulong Address { get; init; }
    public required string Name { get; init; }
    public required DeviceKind Kind { get; init; }
    public string? Manufacturer { get; init; }
}

/// <summary>
/// Reads the standard Bluetooth LE Battery Service (0x180F / 0x2A19).
///
/// Safety is the priority: cached GATT values are used first and are safe to read while
/// a peripheral sleeps. Only a currently connected device, with recent user input, may
/// be read with BluetoothCacheMode.Uncached.
/// </summary>
internal sealed class BleBatteryProvider : IBatteryProvider
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);
    private static readonly Guid BatteryServiceUuid = new("0000180f-0000-1000-8000-00805f9b34fb");
    private static readonly Guid BatteryLevelUuid = new("00002a19-0000-1000-8000-00805f9b34fb");

    private readonly AppSettings _settings;

    public BleBatteryProvider(AppSettings settings)
    {
        _settings = settings;
    }

    public string Id => "ble";
    public string DisplayName => "蓝牙低功耗电池服务";

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(ProviderContext context, CancellationToken cancellationToken)
    {
        if (!_settings.EnableBleProvider)
        {
            return [];
        }

        var results = new List<DiscoveredDevice>();
        IReadOnlyList<DeviceInformation> devices;
        try
        {
            var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            devices = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken)
                .WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn("枚举蓝牙 LE 设备失败：" + ex.Message);
            return results;
        }

        foreach (var info in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BluetoothLEDevice? device = null;
            try
            {
                device = await BluetoothLEDevice.FromIdAsync(info.Id).AsTask(cancellationToken)
                    .WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
                if (device is null)
                {
                    continue;
                }

                var rawAppearance = 0;
                try
                {
                    rawAppearance = device.Appearance.RawValue;
                }
                catch
                {
                    // Appearance is optional.
                }

                var name = string.IsNullOrWhiteSpace(info.Name) ? device.Name : info.Name;
                var kind = Classify(name, rawAppearance);
                if (kind == DeviceKind.Unknown)
                {
                    continue;
                }

                var address = device.BluetoothAddress;
                var id = address != 0
                    ? $"ble:{address:X12}"
                    : "ble:" + info.Id.GetHashCode().ToString("x8");

                var connected = device.ConnectionStatus == BluetoothConnectionStatus.Connected;
                var hasBatteryService = await HasBatteryServiceAsync(device, cancellationToken).ConfigureAwait(false);
                AppLog.Debug($"BLE {name}: appearance=0x{rawAppearance:X4} connected={connected} batteryService={hasBatteryService} kind={kind}");

                results.Add(new DiscoveredDevice
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(name) ? "蓝牙无线设备" : name,
                    Kind = kind,
                    Transport = TransportKind.Bluetooth,
                    ProviderId = Id,
                    ProviderName = "Bluetooth LE Battery Service",
                    SupportsBattery = hasBatteryService || kind is DeviceKind.Mouse or DeviceKind.Keyboard or DeviceKind.KeyboardMouse,
                    Detail = connected ? "蓝牙已连接" : "蓝牙已配对 · 可读取 Windows 缓存",
                    Tag = new BleTarget
                    {
                        DeviceInformationId = info.Id,
                        Address = address,
                        Name = string.IsNullOrWhiteSpace(name) ? "蓝牙无线设备" : name,
                        Kind = kind,
                        Manufacturer = info.Properties.TryGetValue("System.Devices.Manufacturer", out var m) ? m?.ToString() : null
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Warn($"枚举蓝牙设备 {info.Name} 失败：{ex.Message}");
            }
            finally
            {
                device?.Dispose();
            }
        }

        return results;
    }

    public async Task<BatteryReading?> ReadAsync(DiscoveredDevice discovered, ReadContext context, CancellationToken cancellationToken)
    {
        if (discovered.Tag is not BleTarget target)
        {
            return null;
        }

        BluetoothLEDevice? device = null;
        try
        {
            device = await BluetoothLEDevice.FromIdAsync(target.DeviceInformationId).AsTask(cancellationToken)
                .WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
            if (device is null)
            {
                return null;
            }

            var connected = device.ConnectionStatus == BluetoothConnectionStatus.Connected;
            var reading = await ReadBatteryAsync(device, connected, context, cancellationToken).ConfigureAwait(false);
            return reading;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"读取蓝牙电量失败 {target.Name}：{ex.Message}");
            return null;
        }
        finally
        {
            device?.Dispose();
        }
    }

    private async Task<BatteryReading?> ReadBatteryAsync(
        BluetoothLEDevice device,
        bool connected,
        ReadContext context,
        CancellationToken cancellationToken)
    {
        GattDeviceServicesResult services;
        try
        {
            services = await device.GetGattServicesAsync(BluetoothCacheMode.Cached).AsTask(cancellationToken)
                .WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            services = null!;
        }

        if (services is null || services.Status != GattCommunicationStatus.Success)
        {
            if (!(connected && context.CanActivelyQuery))
            {
                return null;
            }

            try
            {
                services = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken)
                    .WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        var service = services.Services.FirstOrDefault(s => s.Uuid == BatteryServiceUuid);
        if (service is null)
        {
            return null;
        }

        GattCharacteristicsResult characteristics;
        try
        {
            characteristics = await service.GetCharacteristicsAsync(BluetoothCacheMode.Cached).AsTask(cancellationToken)
                .WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        var characteristic = characteristics.Characteristics.FirstOrDefault(c => c.Uuid == BatteryLevelUuid);
        if (characteristic is null)
        {
            return null;
        }

        var value = await ReadCharacteristicAsync(characteristic, BluetoothCacheMode.Cached, cancellationToken).ConfigureAwait(false);
        if (value is not null)
        {
            return new BatteryReading(value.Value, null, DateTimeOffset.Now, BatterySourceKind.WindowsCache, IsCached: true);
        }

        // Never force a read on a disconnected/sleeping peripheral. Only a connected
        // device with very recent input is a safe candidate for an uncached read.
        if (!(connected && context.CanActivelyQuery && _settings.ActiveReadWhenInUse))
        {
            AppLog.Info($"保留蓝牙缓存值（设备可能休眠）：{device.Name}");
            return null;
        }

        value = await ReadCharacteristicAsync(characteristic, BluetoothCacheMode.Uncached, cancellationToken).ConfigureAwait(false);
        return value is null
            ? null
            : new BatteryReading(value.Value, null, DateTimeOffset.Now, BatterySourceKind.WindowsCache, IsCached: false);
    }

    private static async Task<byte?> ReadCharacteristicAsync(
        GattCharacteristic characteristic,
        BluetoothCacheMode cacheMode,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await characteristic.ReadValueAsync(cacheMode).AsTask(cancellationToken)
                .WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
            if (result.Status != GattCommunicationStatus.Success || result.Value is null || result.Value.Length == 0)
            {
                return null;
            }

            var reader = DataReader.FromBuffer(result.Value);
            var value = reader.ReadByte();
            return value <= 100 ? value : (byte)Math.Min(100, (int)value);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> HasBatteryServiceAsync(BluetoothLEDevice device, CancellationToken cancellationToken)
    {
        try
        {
            var services = await device.GetGattServicesAsync(BluetoothCacheMode.Cached).AsTask(cancellationToken)
                .WaitAsync(OperationTimeout, cancellationToken).ConfigureAwait(false);
            return services.Status == GattCommunicationStatus.Success
                   && services.Services.Any(s => s.Uuid == BatteryServiceUuid);
        }
        catch
        {
            return false;
        }
    }

    private static DeviceKind Classify(string name, int appearance)
    {
        if (appearance is 0x03C1 or 0x03C0)
        {
            return DeviceKind.Keyboard;
        }

        if (appearance is 0x03C2)
        {
            return DeviceKind.Mouse;
        }

        var lower = name.ToLowerInvariant();
        var keyboard = lower.Contains("keyboard") || lower.Contains("键盘") || lower.Contains("keychron")
                       || lower.Contains("mx keys") || lower.Contains("k380") || lower.Contains("k580");
        var mouse = lower.Contains("mouse") || lower.Contains("mice") || lower.Contains("鼠标")
                    || lower.Contains("mx master") || lower.Contains("mx anywhere") || lower.Contains("m720");

        return (keyboard, mouse) switch
        {
            (true, true) => DeviceKind.KeyboardMouse,
            (true, false) => DeviceKind.Keyboard,
            (false, true) => DeviceKind.Mouse,
            _ => DeviceKind.Unknown
        };
    }
}
