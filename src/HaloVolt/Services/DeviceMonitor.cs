using HaloVolt.Infrastructure;
using HaloVolt.Models;
using HaloVolt.Providers;

namespace HaloVolt.Services;

public sealed class DeviceMonitor : IDisposable
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly ActivityMonitor _activity;
    private readonly NotificationService _notifications;
    private readonly List<IBatteryProvider> _providers;
    private readonly SystemBatteryReader _systemReader = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, DeviceState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _offlineNotified = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDiscovery = DateTimeOffset.MinValue;
    private volatile bool _disposed;

    public DeviceMonitor(
        AppSettings settings,
        SettingsService settingsService,
        NotificationService notifications,
        params IBatteryProvider[] providers)
    {
        _settings = settings;
        _settingsService = settingsService;
        _notifications = notifications;
        _activity = new ActivityMonitor();
        _providers = providers.ToList();
    }

    public IReadOnlyCollection<DeviceState> States => _states.Values;
    public DateTimeOffset? LastRefresh => _lastRefresh == DateTimeOffset.MinValue ? null : _lastRefresh;
    public bool IsRefreshing { get; private set; }
    public string StatusText { get; private set; } = "准备就绪";

    public event EventHandler? DevicesChanged;
    public event EventHandler<DeviceState>? ReadingUpdated;
    public event EventHandler<(DeviceState Device, AlertDecision Decision)>? NotificationRaised;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Passive enumeration is safe, but HID++ probing is only allowed when the user is
        // currently active. This avoids waking a sleeping mouse during login.
        await RefreshDevicesAsync(activityAvailable: _activity.IsUserActive, cancellationToken);
        await RefreshReadingsAsync(force: false, discover: false, cancellationToken);
    }

    public async Task RefreshAsync(bool force, bool discover, CancellationToken cancellationToken = default)
    {
        if (_settings.IsMonitoringPaused && !force)
        {
            StatusText = "监控已暂停";
            return;
        }

        if (discover)
        {
            await RefreshDevicesAsync(activityAvailable: _activity.IsUserActive || force, cancellationToken);
        }

        await RefreshReadingsAsync(force, discover: false, cancellationToken);
    }

    public async Task RefreshDevicesAsync(bool activityAvailable, CancellationToken cancellationToken = default)
    {
        if (_disposed || !await _gate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            IsRefreshing = true;
            StatusText = "正在扫描设备…";
            DevicesChanged?.Invoke(this, EventArgs.Empty);

            var context = new ProviderContext(_settings, _settings.DemoMode, activityAvailable);
            var discovered = new List<DiscoveredDevice>();
            var succeeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AppLog.Debug($"设备发现开始：active={activityAvailable} demo={_settings.DemoMode}");

            if (!_settings.DemoMode)
            {
                foreach (var id in _states
                             .Where(kv => kv.Value.ProviderId.Equals("demo", StringComparison.OrdinalIgnoreCase))
                             .Select(kv => kv.Key)
                             .ToList())
                {
                    _states.Remove(id);
                    _descriptorCache.Remove(id);
                }
            }

            foreach (var provider in _providers)
            {
                try
                {
                    var devices = await provider.DiscoverAsync(context, cancellationToken);
                    discovered.AddRange(devices);
                    succeeded.Add(provider.Id);
                    AppLog.Debug($"Provider {provider.Id} 发现 {devices.Count} 个：{string.Join(", ", devices.Select(d => d.Name + (d.SupportsBattery ? "(支持电量)" : "(无电量通道)")))}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"设备发现失败（{provider.DisplayName}）：{ex.Message}");
                }
            }

            // Merge without destroying last-known values for temporarily missing devices.
            foreach (var item in discovered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _descriptorCache[item.Id] = item;
                if (_states.TryGetValue(item.Id, out var state))
                {
                    state.Apply(item, _settings.DemoMode);
                    state.IsOnline = true;
                }
                else
                {
                    state = new DeviceState
                    {
                        Id = item.Id,
                        ProviderId = item.ProviderId,
                        ProviderName = item.ProviderName
                    };
                    state.Apply(item, _settings.DemoMode);
                    _states[item.Id] = state;
                }

                TryApplyCachedReading(state);
                EnsurePreference(state, isNew: !_settings.DevicePreferences.Any(p => p.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)));
            }

            foreach (var state in _states.Values)
            {
                if (!succeeded.Contains(state.ProviderId))
                {
                    continue;
                }

                if (discovered.All(d => !d.Id.Equals(state.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    if (state.IsOnline && !state.IsSystemBattery)
                    {
                        state.IsOnline = false;
                        MaybeNotifyDisconnect(state);
                    }
                }
            }

            UpdateSystemBattery();
            _lastDiscovery = DateTimeOffset.Now;
            StatusText = "扫描完成";
        }
        finally
        {
            IsRefreshing = false;
            _gate.Release();
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task RefreshReadingsAsync(bool force, bool discover, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            IsRefreshing = true;
            var idle = _activity.IdleTime;
            var active = _activity.IsUserActive;
            if (!active && !force)
            {
                StatusText = "用户空闲，跳过主动读取";
            }

            UpdateSystemBattery();

            var context = new ReadContext(active, idle, force, _settings.ActiveReadWhenInUse && !_settings.IsMonitoringPaused, DateTimeOffset.Now);
            var anyReading = false;

            foreach (var state in _states.Values.ToList())
            {
                if (_disposed)
                {
                    break;
                }

                if (state.IsSystemBattery || !state.SupportsBattery || !state.IsOnline || !IsMonitored(state))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var provider = _providers.FirstOrDefault(p => p.Id.Equals(state.ProviderId, StringComparison.OrdinalIgnoreCase));
                if (provider is null)
                {
                    continue;
                }

                if (!force && !active && state.Source == BatterySourceKind.WindowsCache)
                {
                    // Cached BLE values are still safe to read while the user is away.
                }

                try
                {
                    var discovered = _states[state.Id];
                    var descriptor = BuildDescriptorFromState(discovered);
                    if (descriptor is null)
                    {
                        continue;
                    }

                    var reading = await provider.ReadAsync(descriptor, context, cancellationToken);
                    if (reading is not null)
                    {
                        AppLog.Debug($"读取成功 {state.Name}: {reading.Percentage}% source={reading.Source} cached={reading.IsCached}");
                        ApplyReading(state, reading);
                        anyReading = true;
                    }
                    else
                    {
                        AppLog.Debug($"读取无结果 {state.Name}: provider={state.ProviderId} active={active} force={force}");
                        if (!_settings.UseCachedFallback && active && !state.IsSystemBattery)
                        {
                            state.BatteryPercent = null;
                            state.IsCharging = null;
                            state.LastUpdated = null;
                            state.Source = BatterySourceKind.None;
                            state.IsCached = false;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    state.Error = ex.Message;
                    AppLog.Warn($"读取 {state.Name} 电量失败：{ex.Message}");
                }
            }

            _lastRefresh = DateTimeOffset.Now;
            StatusText = anyReading ? "电量已更新" : active ? "等待设备响应" : "用户空闲，已保留上次电量";
        }
        finally
        {
            IsRefreshing = false;
            _gate.Release();
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void UpdateSystemBattery()
    {
        var battery = _systemReader.TryRead();
        if (battery is null)
        {
            if (_states.TryGetValue("system:battery", out var old))
            {
                _states.Remove("system:battery");
                DevicesChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (_states.TryGetValue(battery.Id, out var existing))
        {
            existing.BatteryPercent = battery.BatteryPercent;
            existing.IsCharging = battery.IsCharging;
            existing.Detail = battery.Detail;
            existing.LastUpdated = battery.LastUpdated;
            existing.SupportsBattery = true;
            existing.IsOnline = true;
            existing.Source = BatterySourceKind.System;
        }
        else
        {
            _states[battery.Id] = battery;
            EnsurePreference(battery, isNew: true);
        }

        EvaluateAlerts(battery, isSystem: true);
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkAllStale()
    {
        foreach (var state in _states.Values)
        {
            if (!state.IsSystemBattery)
            {
                state.IsOnline = false;
            }
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyReading(DeviceState state, BatteryReading reading)
    {
        var wasLow = state.BatteryPercent is { } old && old <= _settings.LowThreshold;
        state.Apply(reading);
        if (!state.IsSystemBattery && reading.Source != BatterySourceKind.Demo)
        {
            _settings.BatteryCache[state.Id] = new CachedBattery
            {
                Percentage = reading.ClampedPercentage,
                IsCharging = reading.IsCharging,
                Timestamp = reading.Timestamp,
                Source = reading.Source,
                Name = state.Name
            };
            _settingsService.ScheduleSave();
        }

        ReadingUpdated?.Invoke(this, state);
        EvaluateAlerts(state, isSystem: false);

        if (reading.Percentage <= _settings.LowThreshold && !wasLow)
        {
            AppLog.Info($"{state.Name} 进入低电量区间：{reading.Percentage}%");
        }
    }

    private void TryApplyCachedReading(DeviceState state)
    {
        if (!_settings.UseCachedFallback)
        {
            return;
        }

        if (state.IsSystemBattery || !state.SupportsBattery || state.BatteryPercent is not null)
        {
            return;
        }

        if (!_settings.BatteryCache.TryGetValue(state.Id, out var cached))
        {
            return;
        }

        state.BatteryPercent = cached.Percentage;
        state.IsCharging = cached.IsCharging;
        state.Source = cached.Source;
        state.LastUpdated = cached.Timestamp;
        state.IsCached = true;
    }

    private void EvaluateAlerts(DeviceState state, bool isSystem)
    {
        if (!_settings.NotificationsEnabled || _settings.IsMonitoringPaused)
        {
            return;
        }

        if (isSystem && !_settings.NotifySystemBattery)
        {
            return;
        }

        if (!isSystem && (!state.SupportsBattery || !IsMonitored(state)))
        {
            return;
        }

        var threshold = isSystem ? _settings.SystemLowThreshold : _settings.LowThreshold;
        var critical = isSystem
            ? Math.Min(5, threshold)
            : Math.Clamp(_settings.CriticalThreshold, 1, threshold);

        var key = state.Id;
        _settings.AlertMemories.TryGetValue(key, out var memory);
        var decision = LowBatteryPolicy.Evaluate(state, threshold, critical, _settings, memory, DateTimeOffset.Now);

        if (decision.Memory.LastSeenLevel != memory?.LastSeenLevel
            || decision.Memory.LastNotifiedLevel != memory?.LastNotifiedLevel
            || decision.Memory.CriticalNotified != memory?.CriticalNotified
            || decision.Memory.LastNotifiedAt != memory?.LastNotifiedAt)
        {
            _settings.AlertMemories[key] = decision.Memory;
            _settingsService.ScheduleSave();
        }

        if (decision.ShouldNotify)
        {
            NotificationRaised?.Invoke(this, (state, decision));
        }
    }

    private void MaybeNotifyDisconnect(DeviceState state)
    {
        if (!_settings.NotifyOnDisconnect || !_settings.NotificationsEnabled || _settings.IsMonitoringPaused)
        {
            return;
        }

        if (!state.HasBattery || !_offlineNotified.Add(state.Id))
        {
            return;
        }

        _notifications.ShowRaw(
            "设备连接已断开",
            $"{state.Name} 已离开连接范围或进入休眠。已保留最后一次读取的 {state.BatteryPercent}% 电量。",
            silent: !_settings.SoundEnabled);
    }

    public void ResetDisconnectNotification(string deviceId) => _offlineNotified.Remove(deviceId);

    private bool IsMonitored(DeviceState state)
    {
        var pref = _settings.DevicePreferences.FirstOrDefault(p => p.Id.Equals(state.Id, StringComparison.OrdinalIgnoreCase));
        if (pref is null)
        {
            return state.SupportsBattery && !state.IsSystemBattery;
        }

        if (pref.Role == DeviceRole.Ignore)
        {
            return false;
        }

        return pref.Monitored;
    }

    private void EnsurePreference(DeviceState state, bool isNew)
    {
        if (state.IsSystemBattery)
        {
            return;
        }

        var pref = _settings.DevicePreferences.FirstOrDefault(p => p.Id.Equals(state.Id, StringComparison.OrdinalIgnoreCase));
        if (pref is not null)
        {
            if (!pref.UserConfigured && state.SupportsBattery && !pref.Monitored)
            {
                pref.Monitored = true;
                _settingsService.ScheduleSave();
            }

            return;
        }

        pref = new DevicePreference
        {
            Id = state.Id,
            Monitored = state.SupportsBattery,
            Role = DeviceRole.Auto
        };
        _settings.DevicePreferences.Add(pref);
        _settingsService.ScheduleSave();
    }

    private DiscoveredDevice? BuildDescriptorFromState(DeviceState state)
    {
        // The descriptor tag is kept in a side table by the providers' original discovery
        // result. Rebuild from the live provider cache via a lightweight lookup.
        return _descriptorCache.TryGetValue(state.Id, out var descriptor)
            ? descriptor
            : null;
    }

    private readonly Dictionary<string, DiscoveredDevice> _descriptorCache = new(StringComparer.OrdinalIgnoreCase);

    public void RememberDescriptors(IEnumerable<DiscoveredDevice> descriptors)
    {
        _descriptorCache.Clear();
        foreach (var descriptor in descriptors)
        {
            _descriptorCache[descriptor.Id] = descriptor;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _gate.Dispose();
    }
}
