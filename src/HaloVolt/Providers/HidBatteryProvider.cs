using HaloVolt.Infrastructure;
using HaloVolt.Models;
using HaloVolt.Providers.Hid;

namespace HaloVolt.Providers;

internal sealed class HidPeripheralTag
{
    public required HidPhysicalGroup Group { get; init; }
    public HidBatteryTarget? BatteryTarget { get; init; }
    public Nordic52Target? NordicTarget { get; init; }
}

/// <summary>
/// Enumerates concrete HID keyboards/mice. It reads battery through the standard HID
/// Generic Device Controls / Battery Strength feature usage when the device exposes it.
/// A feature-report read is device I/O, so it only runs while the user is active.
/// </summary>
internal sealed class HidBatteryProvider : IBatteryProvider
{
    private readonly HidDeviceCatalog _catalog;

    public HidBatteryProvider(HidDeviceCatalog catalog)
    {
        _catalog = catalog;
    }

    public string Id => "hid";
    public string DisplayName => "HID 设备";

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(ProviderContext context, CancellationToken cancellationToken)
    {
        var groups = await _catalog.GetAsync(force: false, cancellationToken).ConfigureAwait(false);
        var results = new List<DiscoveredDevice>();

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Logitech devices are represented by the HID++ provider, which can separate
            // multiple peripherals attached to one receiver.
            if (group.IsLogitech && context.Settings.EnableLogitechProvider)
            {
                continue;
            }

            if (!group.HasMouse && !group.HasKeyboard)
            {
                continue;
            }

            Nordic52Battery.TryCreateTarget(group, out var nordicTarget);
            HidBatteryTarget? target = null;
            if (nordicTarget is null && context.Settings.EnableHidBatteryProvider)
            {
                foreach (var path in group.CandidatePaths)
                {
                    target = HidBatteryProbe.Probe(path);
                    if (target is not null)
                    {
                        break;
                    }
                }
            }

            var knownDareuMouse = nordicTarget is { VendorId: 0x260D, ProductId: 0x1175 or 0x1193 or 0x1154 or 0x1196 };
            var kind = knownDareuMouse
                ? DeviceKind.Mouse
                : nordicTarget is not null
                    ? group.HasMouse && group.HasKeyboard
                        ? DeviceKind.KeyboardMouse
                        : group.HasMouse ? DeviceKind.Mouse : DeviceKind.Keyboard
                    : group.HasMouse && group.HasKeyboard
                        ? DeviceKind.KeyboardMouse
                        : group.HasMouse
                            ? DeviceKind.Mouse
                            : DeviceKind.Keyboard;

            var isWiredDareu = nordicTarget is { VendorId: 0x260D, ProductId: 0x1193 or 0x1196 };
            var transport = nordicTarget is not null
                ? (isWiredDareu ? TransportKind.Usb : TransportKind.UsbReceiver)
                : group.IsReceiverName ? TransportKind.UsbReceiver : TransportKind.Usb;
            var detail = nordicTarget is not null
                ? $"{group.VendorId:X4}:{group.ProductId:X4} · Compx / Nordic 52840 · 17 字节电量通道"
                : $"{group.VendorId:X4}:{group.ProductId:X4} · {group.Interfaces.Count} 个 HID 集合";

            AppLog.Debug($"HID 组 {group.Name} {group.VendorId:X4}:{group.ProductId:X4} 集合=[{string.Join(", ", group.Interfaces.Select(i => $"{i.UsagePage:X4}:{i.UsageId:X4}"))}] Nordic={nordicTarget is not null} StandardBattery={target is not null}");

            results.Add(new DiscoveredDevice
            {
                Id = group.Id,
                Name = nordicTarget?.DisplayName ?? group.Name,
                Kind = kind,
                Transport = transport,
                ProviderId = Id,
                ProviderName = nordicTarget is not null
                    ? "Compx / Nordic 2.4G"
                    : target is null ? "HID 设备（暂无标准电量接口）" : "HID Battery Strength",
                SupportsBattery = nordicTarget is not null || target is not null,
                Detail = detail,
                Tag = new HidPeripheralTag { Group = group, BatteryTarget = target, NordicTarget = nordicTarget }
            });
        }

        return results;
    }

    public Task<BatteryReading?> ReadAsync(DiscoveredDevice device, ReadContext context, CancellationToken cancellationToken)
    {
        if (device.Tag is not HidPeripheralTag { } tag)
        {
            return Task.FromResult<BatteryReading?>(null);
        }

        if (tag.NordicTarget is { } nordicTarget)
        {
            if (!context.CanActivelyQuery)
            {
                AppLog.Debug($"跳过 Nordic52 读取（设备可能休眠）：{device.Name}");
                return Task.FromResult<BatteryReading?>(null);
            }

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var percent = Nordic52Battery.ReadPercentage(nordicTarget);
                return percent is null
                    ? null
                    : new BatteryReading(
                        percent.Value,
                        false,
                        DateTimeOffset.Now,
                        BatterySourceKind.DareuNordic,
                        IsCached: false,
                        Note: "Nordic 52 2.4G");
            }, cancellationToken);
        }

        if (tag.BatteryTarget is not { } target)
        {
            return Task.FromResult<BatteryReading?>(null);
        }

        if (!context.CanActivelyQuery)
        {
            AppLog.Info($"跳过 HID 读取（设备可能休眠）：{device.Name}");
            return Task.FromResult<BatteryReading?>(null);
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var percent = HidBatteryProbe.ReadPercentage(target);
            return percent is null
                ? null
                : new BatteryReading(
                    percent.Value,
                    null,
                    DateTimeOffset.Now,
                    BatterySourceKind.HidFeature,
                    IsCached: false,
                    Note: "标准 HID 电量特性");
        }, cancellationToken);
    }
}
