using HaloVolt.Infrastructure;
using HaloVolt.Models;
using HaloVolt.Providers.Hid;

namespace HaloVolt.Providers;

internal sealed class HidppTarget
{
    public required string GroupId { get; init; }
    public required string ReceiverPath { get; init; }
    public required byte DeviceIndex { get; init; }
    public required bool IsReceiver { get; init; }
    public required string Name { get; init; }
    public required DeviceKind Kind { get; init; }
    public byte UnifiedBatteryFeature { get; init; }
    public byte PercentageBatteryFeature { get; init; }
    public byte VoltageBatteryFeature { get; init; }
    public bool SupportsBattery => UnifiedBatteryFeature != 0 || PercentageBatteryFeature != 0 || VoltageBatteryFeature != 0;
}

/// <summary>
/// Logitech HID++ 2.0 reader for direct connections and Unifying/Bolt-style receivers.
/// It deliberately caches discovery results and only performs device I/O while the user
/// is active, so a sleeping peripheral is not polled around the clock.
/// </summary>
internal sealed class LogitechHidppProvider : IBatteryProvider
{
    private const byte SwId = 0x0A;
    private readonly HidDeviceCatalog _catalog;

    public LogitechHidppProvider(HidDeviceCatalog catalog)
    {
        _catalog = catalog;
    }

    public string Id => "logitech";
    public string DisplayName => "Logitech HID++ 2.0";

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(ProviderContext context, CancellationToken cancellationToken)
    {
        if (!context.Settings.EnableLogitechProvider)
        {
            return [];
        }

        var groups = await _catalog.GetAsync(force: false, cancellationToken).ConfigureAwait(false);
        var logitechGroups = groups.Where(g => g.IsLogitech && g.VendorPath is not null).ToList();
        if (logitechGroups.Count == 0)
        {
            return [];
        }

        if (!context.AllowActiveProbe || !context.Settings.ActiveReadWhenInUse)
        {
            return logitechGroups.Select(CreatePlaceholder).ToList();
        }

        var results = new List<DiscoveredDevice>();
        foreach (var group in logitechGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                AppLog.Debug($"Logitech HID++ 开始探测 {group.Name} ({group.VendorId:X4}:{group.ProductId:X4}) receiver={group.IsReceiverName || (group.HasKeyboard && group.HasMouse)}");
                var found = await Task.Run(() => DiscoverGroup(group, cancellationToken), cancellationToken).ConfigureAwait(false);
                AppLog.Debug($"Logitech HID++ {group.Name} 发现 {found.Count} 个逻辑设备");
                results.AddRange(found.Count > 0 ? found : [CreatePlaceholder(group)]);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Logitech HID++ 发现失败：{ex.Message}");
                results.Add(CreatePlaceholder(group));
            }
        }

        return results;
    }

    public Task<BatteryReading?> ReadAsync(DiscoveredDevice discovered, ReadContext context, CancellationToken cancellationToken)
    {
        if (discovered.Tag is not HidppTarget target)
        {
            return Task.FromResult<BatteryReading?>(null);
        }

        if (!target.SupportsBattery)
        {
            return Task.FromResult<BatteryReading?>(null);
        }

        if (!context.CanActivelyQuery)
        {
            AppLog.Info($"跳过 Logitech 读取（设备可能休眠）：{target.Name}");
            return Task.FromResult<BatteryReading?>(null);
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = HidppConnection.Open(target.ReceiverPath);
            if (connection is null)
            {
                return null;
            }

            var reading = ReadBattery(connection, target);
            return reading;
        }, cancellationToken);
    }

    private static List<DiscoveredDevice> DiscoverGroup(HidPhysicalGroup group, CancellationToken cancellationToken)
    {
        var results = new List<DiscoveredDevice>();
        var receiver = group.IsReceiverName || (group.HasKeyboard && group.HasMouse);
        using var connection = HidppConnection.Open(group.VendorPath!);
        if (connection is null)
        {
            return results;
        }

        var indexes = new List<byte>();
        if (receiver)
        {
            var countResponse = connection.Query(
                [0x10, 0xFF, 0x81, 0x02, 0x00, 0x00, 0x00],
                report => report[0] == 0x10 && report[2] == 0x81 && report[3] == 0x02,
                450);
            var count = countResponse is { Length: > 5 } ? Math.Min((byte)6, countResponse[5]) : (byte)0;
            for (byte i = 1; i <= (count == 0 ? (byte)6 : count); i++)
            {
                indexes.Add(i);
            }
        }
        else
        {
            indexes.Add(0xFF);
        }

        foreach (var index in indexes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Ping(connection, index))
            {
                continue;
            }

            var name = ReadDeviceName(connection, index) ?? $"罗技无线设备 {index}";
            var deviceType = ReadDeviceType(connection, index);
            var kind = deviceType switch
            {
                0 => DeviceKind.Keyboard,
                3 => DeviceKind.Mouse,
                _ => group.HasMouse && !group.HasKeyboard ? DeviceKind.Mouse : group.HasKeyboard && !group.HasMouse ? DeviceKind.Keyboard : DeviceKind.Unknown
            };

            var unified = GetFeatureIndex(connection, index, 0x1004);
            var percentage = GetFeatureIndex(connection, index, 0x1000);
            var voltage = GetFeatureIndex(connection, index, 0x1001);

            var id = receiver
                ? $"logi:{group.Id}:{index}"
                : $"logi:{group.Id}";

            results.Add(new DiscoveredDevice
            {
                Id = id,
                Name = name,
                Kind = kind == DeviceKind.Unknown ? DeviceKind.Mouse : kind,
                Transport = receiver ? TransportKind.UsbReceiver : TransportKind.Usb,
                ProviderId = "logitech",
                ProviderName = "Logitech HID++ 2.0",
                SupportsBattery = unified != 0 || percentage != 0 || voltage != 0,
                Detail = receiver ? $"接收器通道 {index} · {group.Name}" : group.Name,
                Tag = new HidppTarget
                {
                    GroupId = group.Id,
                    ReceiverPath = group.VendorPath!,
                    DeviceIndex = index,
                    IsReceiver = receiver,
                    Name = name,
                    Kind = kind,
                    UnifiedBatteryFeature = unified,
                    PercentageBatteryFeature = percentage,
                    VoltageBatteryFeature = voltage
                }
            });
        }

        return results;
    }

    private static DiscoveredDevice CreatePlaceholder(HidPhysicalGroup group)
    {
        var receiver = group.IsReceiverName || (group.HasKeyboard && group.HasMouse);
        return new DiscoveredDevice
        {
            Id = "logi:" + group.Id,
            Name = receiver ? "罗技无线接收器" : group.Name,
            Kind = group.HasMouse ? DeviceKind.Mouse : DeviceKind.Keyboard,
            Transport = receiver ? TransportKind.UsbReceiver : TransportKind.Usb,
            ProviderId = "logitech",
            ProviderName = "Logitech HID++ 2.0",
            SupportsBattery = false,
            Detail = "设备休眠或尚未完成初始化 · 使用设备后自动重试",
            Tag = new HidppTarget
            {
                GroupId = group.Id,
                ReceiverPath = group.VendorPath!,
                DeviceIndex = 0xFF,
                IsReceiver = receiver,
                Name = group.Name,
                Kind = group.HasMouse ? DeviceKind.Mouse : DeviceKind.Keyboard
            }
        };
    }

    private static bool Ping(HidppConnection connection, byte index)
    {
        var response = connection.Query2(index, 0x00, 0x1A, 0x00, 0x00, 0x55, 160);
        return response is { Length: >= 7 } && response[2] == 0x00;
    }

    private static byte GetFeatureIndex(HidppConnection connection, byte index, ushort featureId)
    {
        var response = connection.Query2(index, 0x00, 0x0A, (byte)(featureId >> 8), (byte)featureId, 0);
        return response is { Length: >= 5 } && response[2] == 0x00 ? response[4] : (byte)0;
    }

    private static string? ReadDeviceName(HidppConnection connection, byte index)
    {
        var feature = GetFeatureIndex(connection, index, 0x0005);
        if (feature == 0)
        {
            return null;
        }

        var countResponse = connection.Query2(index, feature, 0x0A, 0x00, 0x00, 0x00);
        if (countResponse is not { Length: >= 5 })
        {
            return null;
        }

        var length = countResponse[4];
        if (length == 0 || length > 64)
        {
            return null;
        }

        var bytes = new List<byte>(length);
        for (var offset = 0; offset < length; offset += 3)
        {
            var part = connection.Query2(index, feature, 0x1A, (byte)offset, 0x00, 0x00);
            if (part is not { Length: >= 7 })
            {
                break;
            }

            bytes.Add(part[4]);
            bytes.Add(part[5]);
            bytes.Add(part[6]);
        }

        var text = System.Text.Encoding.UTF8.GetString(bytes.Take(length).ToArray()).TrimEnd('\0', ' ');
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static byte? ReadDeviceType(HidppConnection connection, byte index)
    {
        var feature = GetFeatureIndex(connection, index, 0x0005);
        if (feature == 0)
        {
            return null;
        }

        var response = connection.Query2(index, feature, 0x2A, 0x00, 0x00, 0x00);
        return response is { Length: >= 5 } ? response[4] : null;
    }

    private static BatteryReading? ReadBattery(HidppConnection connection, HidppTarget target)
    {
        var index = target.DeviceIndex;
        if (target.UnifiedBatteryFeature is var unified && unified != 0)
        {
            var response = connection.Query2(index, unified, 0x1A, 0x00, 0x00, 0x00);
            if (response is { Length: >= 7 })
            {
                var percent = response[4];
                if (percent <= 100)
                {
                    var charging = response[6] switch
                    {
                        0 => false,
                        1 or 2 or 4 => true,
                        3 => false,
                        _ => (bool?)null
                    };
                    return new BatteryReading(percent, charging, DateTimeOffset.Now, BatterySourceKind.LogitechHidpp);
                }
            }
        }

        if (target.PercentageBatteryFeature is var percentage && percentage != 0)
        {
            var response = connection.Query2(index, percentage, 0x0A, 0x00, 0x00, 0x00);
            if (response is { Length: >= 7 })
            {
                var percent = response[4];
                if (percent <= 100)
                {
                    var charging = response[6] switch
                    {
                        0 => false,
                        1 or 2 or 4 => true,
                        3 => false,
                        _ => (bool?)null
                    };
                    return new BatteryReading(percent, charging, DateTimeOffset.Now, BatterySourceKind.LogitechHidpp);
                }
            }
        }

        if (target.VoltageBatteryFeature is var voltage && voltage != 0)
        {
            var response = connection.Query2(index, voltage, 0x0A, 0x00, 0x00, 0x00);
            if (response is { Length: >= 7 })
            {
                var millivolts = (response[4] << 8) | response[5];
                var percent = VoltageToPercent(millivolts);
                var flags = response[6];
                var charging = (flags & 0x80) != 0
                    ? (flags & 0x07) switch { 0 => true, 1 => false, 2 => false, _ => (bool?)null }
                    : false;
                return new BatteryReading(percent, charging, DateTimeOffset.Now, BatterySourceKind.LogitechHidpp, VoltageMv: millivolts);
            }
        }

        return null;
    }

    private static readonly int[] VoltageCurve =
    [
        4186, 4156, 4143, 4133, 4122, 4113, 4103, 4094, 4086, 4075,
        4067, 4059, 4051, 4043, 4035, 4027, 4019, 4011, 4003, 3997,
        3989, 3983, 3976, 3969, 3961, 3955, 3949, 3942, 3935, 3929,
        3922, 3916, 3909, 3902, 3896, 3890, 3883, 3877, 3870, 3865,
        3859, 3853, 3848, 3842, 3837, 3833, 3828, 3824, 3819, 3815,
        3811, 3808, 3804, 3800, 3797, 3793, 3790, 3787, 3784, 3781,
        3778, 3775, 3772, 3770, 3767, 3764, 3762, 3759, 3757, 3754,
        3751, 3748, 3744, 3741, 3737, 3734, 3730, 3726, 3724, 3720,
        3717, 3714, 3710, 3706, 3702, 3697, 3693, 3688, 3683, 3677,
        3671, 3666, 3662, 3658, 3654, 3646, 3633, 3612, 3579, 3537
    ];

    private static int VoltageToPercent(int millivolts)
    {
        for (var i = 0; i < VoltageCurve.Length; i++)
        {
            if (millivolts > VoltageCurve[i])
            {
                return VoltageCurve.Length - i;
            }
        }

        return 0;
    }
}
