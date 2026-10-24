using HaloVolt.Models;

namespace HaloVolt.Providers;

public sealed record ProviderContext(AppSettings Settings, bool IsDemoSession, bool AllowActiveProbe);

public interface IBatteryProvider
{
    string Id { get; }
    string DisplayName { get; }
    Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(ProviderContext context, CancellationToken cancellationToken);
    Task<BatteryReading?> ReadAsync(DiscoveredDevice device, ReadContext context, CancellationToken cancellationToken);
}
