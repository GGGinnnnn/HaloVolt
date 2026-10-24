using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using HaloVolt.Infrastructure;

namespace HaloVolt.Providers.Hid;

internal sealed class HidInterfaceInfo
{
    public required string Path { get; init; }
    public required string DeviceInstanceId { get; init; }
    public required Guid ContainerId { get; init; }
    public required ushort UsagePage { get; init; }
    public required ushort UsageId { get; init; }
    public required string Name { get; init; }
    public ushort VendorId { get; init; }
    public ushort ProductId { get; init; }
}

internal sealed class HidPhysicalGroup
{
    public required string Id { get; init; }
    public required Guid ContainerId { get; init; }
    public required string Name { get; init; }
    public required string SampleInstanceId { get; init; }
    public required IReadOnlyList<HidInterfaceInfo> Interfaces { get; init; }
    public ushort VendorId { get; init; }
    public ushort ProductId { get; init; }
    public bool HasMouse { get; init; }
    public bool HasKeyboard { get; init; }
    public bool IsLogitech => VendorId == 0x046D;
    public bool IsReceiverName => Name.Contains("receiver", StringComparison.OrdinalIgnoreCase)
                                  || Name.Contains("接收器", StringComparison.OrdinalIgnoreCase)
                                  || Name.Contains("unifying", StringComparison.OrdinalIgnoreCase)
                                  || Name.Contains("bolt", StringComparison.OrdinalIgnoreCase);

    public string? VendorPath => Interfaces
        .FirstOrDefault(i => i.UsagePage >= 0xFF00)?.Path;

    public string? FindInterface(ushort usagePage, ushort usageId)
        => Interfaces.FirstOrDefault(i => i.UsagePage == usagePage && i.UsageId == usageId)?.Path;

    public IEnumerable<string> CandidatePaths => Interfaces
        .OrderByDescending(i => i.UsagePage >= 0xFF00)
        .Select(i => i.Path);
}

internal sealed class HidDeviceCatalog
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<HidPhysicalGroup> _cache = [];
    private DateTimeOffset _cachedAt;
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(2);

    public async Task<IReadOnlyList<HidPhysicalGroup>> GetAsync(bool force, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!force && _cache.Count > 0 && DateTimeOffset.Now - _cachedAt < _ttl)
            {
                return _cache;
            }

            _cache = await EnumerateAsync(cancellationToken).ConfigureAwait(false);
            _cachedAt = DateTimeOffset.Now;
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<IReadOnlyList<HidPhysicalGroup>> EnumerateAsync(CancellationToken cancellationToken)
    {
        var interfaces = new List<HidInterfaceInfo>();
        var queries = new (ushort Page, ushort Usage)[]
        {
            (0x01, 0x02), // mouse
            (0x01, 0x06), // keyboard
            (0xFF00, 0x0001), // vendor-defined (Logitech and others)
            (0xFF02, 0x0002) // Compx / Nordic 52840 battery collection
        };

        foreach (var query in queries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var selector = HidDevice.GetDeviceSelector(query.Page, query.Usage);
                var found = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken).ConfigureAwait(false);
                foreach (var info in found)
                {
                    var path = info.Id;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    info.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var instanceObj);
                    var instance = instanceObj as string ?? string.Empty;
                    info.Properties.TryGetValue("System.Devices.ContainerId", out var containerObj);
                    var container = containerObj is Guid guid ? guid : Guid.Empty;
                    var (vid, pid) = ParseVidPid(instance);

                    interfaces.Add(new HidInterfaceInfo
                    {
                        Path = path,
                        DeviceInstanceId = instance,
                        ContainerId = container,
                        UsagePage = query.Page,
                        UsageId = query.Usage,
                        Name = info.Name ?? string.Empty,
                        VendorId = vid,
                        ProductId = pid
                    });
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"枚举 HID 集合 {query.Page:X4}:{query.Usage:X4} 失败：{ex.Message}");
            }
        }

        var groups = new List<HidPhysicalGroup>();
        foreach (var groupInterfaces in interfaces
                     .GroupBy(i => i.ContainerId != Guid.Empty
                         ? i.ContainerId.ToString("D")
                         : NormalizeInstance(i.DeviceInstanceId)))
        {
            var list = groupInterfaces.ToList();
            var first = list[0];
            var container = first.ContainerId;
            var name = ChooseName(list);
            var sample = first.DeviceInstanceId;
            var hasMouse = list.Any(i => i.UsagePage == 0x01 && i.UsageId == 0x02);
            var hasKeyboard = list.Any(i => i.UsagePage == 0x01 && i.UsageId == 0x06);

            if (IsInternal(sample, name))
            {
                continue;
            }

            var id = container != Guid.Empty
                ? "hid:container:" + container.ToString("D")
                : "hid:instance:" + NormalizeInstance(sample);

            groups.Add(new HidPhysicalGroup
            {
                Id = id,
                ContainerId = container,
                Name = name,
                SampleInstanceId = sample,
                Interfaces = list,
                VendorId = first.VendorId,
                ProductId = first.ProductId,
                HasMouse = hasMouse,
                HasKeyboard = hasKeyboard
            });
        }

        return groups
            .OrderByDescending(g => g.HasMouse || g.HasKeyboard)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string NormalizeInstance(string instance)
    {
        // Collapse column/interface suffix so separate HID collections of the same
        // physical keyboard or receiver become one row.
        var parts = instance.Split('\\');
        if (parts.Length >= 2)
        {
            return parts[0] + "\\" + parts[1];
        }

        return instance;
    }

    private static string ChooseName(List<HidInterfaceInfo> list)
    {
        var host = Environment.MachineName;
        var candidates = list
            .Select(i => i.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n) && !string.Equals(n, host, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return candidates
                   .OrderByDescending(n => n.Contains("receiver", StringComparison.OrdinalIgnoreCase)
                                           || n.Contains("接收器", StringComparison.OrdinalIgnoreCase)
                                           || n.Contains("keyboard", StringComparison.OrdinalIgnoreCase)
                                           || n.Contains("mouse", StringComparison.OrdinalIgnoreCase))
                   .FirstOrDefault()
               ?? candidates.FirstOrDefault()
               ?? "HID 输入设备";
    }

    private static bool IsInternal(string instance, string name)
    {
        if (instance.StartsWith(@"HID\SYNA", StringComparison.OrdinalIgnoreCase)
            || instance.StartsWith(@"HID\ELAN", StringComparison.OrdinalIgnoreCase)
            || instance.StartsWith(@"HID\MSFT", StringComparison.OrdinalIgnoreCase)
            || instance.StartsWith(@"HID\GVInput", StringComparison.OrdinalIgnoreCase)
            || instance.StartsWith(@"HID\VID_06CB", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var lowered = name.ToLowerInvariant();
        return lowered.Contains("touchpad")
               || lowered.Contains("触摸板")
               || lowered.Contains("precision touch")
               || lowered.Contains("i2c hid")
               || lowered.Contains("synaptics")
               || lowered.Contains("elan");
    }

    private static (ushort Vid, ushort Pid) ParseVidPid(string instance)
    {
        try
        {
            var vid = 0;
            var pid = 0;
            var parts = instance.Split('\\');
            if (parts.Length < 2)
            {
                return (0, 0);
            }

            foreach (var token in parts[1].Split('&'))
            {
                if (token.StartsWith("VID_", StringComparison.OrdinalIgnoreCase)
                    && ushort.TryParse(token.AsSpan(4), System.Globalization.NumberStyles.HexNumber, null, out var v))
                {
                    vid = v;
                }
                else if (token.StartsWith("PID_", StringComparison.OrdinalIgnoreCase)
                         && ushort.TryParse(token.AsSpan(4), System.Globalization.NumberStyles.HexNumber, null, out var p))
                {
                    pid = p;
                }
            }

            return ((ushort)vid, (ushort)pid);
        }
        catch
        {
            return (0, 0);
        }
    }
}
