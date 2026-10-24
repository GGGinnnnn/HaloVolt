using HaloVolt.Models;

namespace HaloVolt.Providers;

internal sealed class DemoBatteryProvider : IBatteryProvider
{
    private sealed record DemoTarget(string Id, int Percent);

    public string Id => "demo";
    public string DisplayName => "演示数据";

    public Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(ProviderContext context, CancellationToken cancellationToken)
    {
        if (!context.Settings.DemoMode)
        {
            return Task.FromResult<IReadOnlyList<DiscoveredDevice>>([]);
        }

        IReadOnlyList<DiscoveredDevice> devices =
        [
            new DiscoveredDevice
            {
                Id = "demo:mouse",
                Name = "MX Master 3S",
                Kind = DeviceKind.Mouse,
                Transport = TransportKind.Demo,
                ProviderId = Id,
                ProviderName = "演示数据",
                SupportsBattery = true,
                Detail = "蓝牙 · 演示模式",
                Tag = new DemoTarget("demo:mouse", 64)
            },
            new DiscoveredDevice
            {
                Id = "demo:keyboard",
                Name = "MX Keys S",
                Kind = DeviceKind.Keyboard,
                Transport = TransportKind.Demo,
                ProviderId = Id,
                ProviderName = "演示数据",
                SupportsBattery = true,
                Detail = "蓝牙 · 演示模式",
                Tag = new DemoTarget("demo:keyboard", 78)
            }
        ];

        return Task.FromResult(devices);
    }

    public Task<BatteryReading?> ReadAsync(DiscoveredDevice device, ReadContext context, CancellationToken cancellationToken)
    {
        if (device.Tag is not DemoTarget target)
        {
            return Task.FromResult<BatteryReading?>(null);
        }

        return Task.FromResult<BatteryReading?>(new BatteryReading(
            target.Percent,
            false,
            DateTimeOffset.Now,
            BatterySourceKind.Demo,
            IsCached: false,
            Note: "演示模式"));
    }
}
