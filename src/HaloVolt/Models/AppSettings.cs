using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using HaloVolt.Infrastructure;

namespace HaloVolt.Models;

public sealed class AppSettings : ObservableObject
{
    private int _schemaVersion = 1;
    private bool _hasCompletedFirstRun;
    private bool _notificationsEnabled = true;
    private int _lowThreshold = 20;
    private int _repeatStep = 5;
    private int _criticalThreshold = 5;
    private bool _criticalEnabled = true;
    private int _repeatCooldownMinutes = 30;
    private bool _soundEnabled = true;
    private bool _notifyOnDisconnect;
    private bool _notifySystemBattery;
    private int _systemLowThreshold = 20;
    private bool _trayBatteryIconEnabled = true;
    private bool _trayCenterDot = true;
    private RingContent _outerRingContent = RingContent.Mouse;
    private string _mouseColor = "#4CC2FF";
    private string _keyboardColor = "#FFB84D";
    private bool _autoStart;
    private StartupMode _startupMode = StartupMode.Silent;
    private CloseAction _closeAction = CloseAction.MinimizeToTray;
    private bool _minimizeToTray = true;
    private bool _showTrayHintOnMinimize;
    private ThemeMode _theme = ThemeMode.System;
    private int _safeRefreshMinutes = 15;
    private bool _enablePolling = true;
    private bool _monitoringPaused;
    private DateTimeOffset? _pauseUntil;
    private bool _demoMode;
    private bool _activeReadWhenInUse = true;
    private bool _enableBleProvider = true;
    private bool _enableLogitechProvider = true;
    private bool _enableHidBatteryProvider = true;
    private bool _enableDareuDriverBridge = true;
    private bool _showSystemBattery = true;
    private bool _hideUnsupportedDevices = true;
    private string? _mouseRingDeviceId;
    private string? _keyboardRingDeviceId;
    private bool _reduceMotion;
    private bool _debugMode;

    public int SchemaVersion
    {
        get => _schemaVersion;
        set => SetProperty(ref _schemaVersion, value);
    }

    public bool HasCompletedFirstRun
    {
        get => _hasCompletedFirstRun;
        set => SetProperty(ref _hasCompletedFirstRun, value);
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set => SetProperty(ref _notificationsEnabled, value);
    }

    public int LowThreshold
    {
        get => _lowThreshold;
        set
        {
            if (SetProperty(ref _lowThreshold, Math.Clamp(value, 1, 80)))
            {
                OnPropertyChanged(nameof(LowThresholdSummary));
            }
        }
    }

    public int RepeatStep
    {
        get => _repeatStep;
        set
        {
            if (SetProperty(ref _repeatStep, Math.Clamp(value, 1, 30)))
            {
                OnPropertyChanged(nameof(LowThresholdSummary));
            }
        }
    }

    public int CriticalThreshold
    {
        get => _criticalThreshold;
        set
        {
            if (SetProperty(ref _criticalThreshold, Math.Clamp(value, 1, 50)))
            {
                OnPropertyChanged(nameof(LowThresholdSummary));
            }
        }
    }

    public bool CriticalEnabled
    {
        get => _criticalEnabled;
        set => SetProperty(ref _criticalEnabled, value);
    }

    public int RepeatCooldownMinutes
    {
        get => _repeatCooldownMinutes;
        set => SetProperty(ref _repeatCooldownMinutes, Math.Clamp(value, 5, 240));
    }

    public bool SoundEnabled
    {
        get => _soundEnabled;
        set => SetProperty(ref _soundEnabled, value);
    }

    public bool NotifyOnDisconnect
    {
        get => _notifyOnDisconnect;
        set => SetProperty(ref _notifyOnDisconnect, value);
    }

    public bool NotifySystemBattery
    {
        get => _notifySystemBattery;
        set => SetProperty(ref _notifySystemBattery, value);
    }

    public int SystemLowThreshold
    {
        get => _systemLowThreshold;
        set => SetProperty(ref _systemLowThreshold, Math.Clamp(value, 5, 50));
    }

    public bool TrayBatteryIconEnabled
    {
        get => _trayBatteryIconEnabled;
        set => SetProperty(ref _trayBatteryIconEnabled, value);
    }

    public bool TrayCenterDot
    {
        get => _trayCenterDot;
        set => SetProperty(ref _trayCenterDot, value);
    }

    public RingContent OuterRingContent
    {
        get => _outerRingContent;
        set => SetProperty(ref _outerRingContent, value);
    }

    public string MouseColor
    {
        get => _mouseColor;
        set => SetProperty(ref _mouseColor, value);
    }

    public string KeyboardColor
    {
        get => _keyboardColor;
        set => SetProperty(ref _keyboardColor, value);
    }

    public bool AutoStart
    {
        get => _autoStart;
        set => SetProperty(ref _autoStart, value);
    }

    public StartupMode StartupMode
    {
        get => _startupMode;
        set => SetProperty(ref _startupMode, value);
    }

    public CloseAction CloseAction
    {
        get => _closeAction;
        set => SetProperty(ref _closeAction, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    public bool ShowTrayHintOnMinimize
    {
        get => _showTrayHintOnMinimize;
        set => SetProperty(ref _showTrayHintOnMinimize, value);
    }

    public ThemeMode Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value);
    }

    public int SafeRefreshMinutes
    {
        get => _safeRefreshMinutes;
        set => SetProperty(ref _safeRefreshMinutes, Math.Clamp(value, 5, 120));
    }

    public bool EnablePolling
    {
        get => _enablePolling;
        set => SetProperty(ref _enablePolling, value);
    }

    public bool MonitoringPaused
    {
        get => _monitoringPaused;
        set
        {
            if (SetProperty(ref _monitoringPaused, value))
            {
                OnPropertyChanged(nameof(IsMonitoringPaused));
                OnPropertyChanged(nameof(MonitoringStatusText));
            }
        }
    }

    public DateTimeOffset? PauseUntil
    {
        get => _pauseUntil;
        set
        {
            if (SetProperty(ref _pauseUntil, value))
            {
                OnPropertyChanged(nameof(IsMonitoringPaused));
                OnPropertyChanged(nameof(MonitoringStatusText));
            }
        }
    }

    [JsonIgnore]
    public bool IsMonitoringPaused => MonitoringPaused || (PauseUntil is { } until && until > DateTimeOffset.Now);

    [JsonIgnore]
    public string MonitoringStatusText => IsMonitoringPaused
        ? PauseUntil is { } until ? $"已暂停至 {until:HH:mm}" : "监控已暂停"
        : "省电守护已开启";

    public bool DemoMode
    {
        get => _demoMode;
        set => SetProperty(ref _demoMode, value);
    }

    public bool ActiveReadWhenInUse
    {
        get => _activeReadWhenInUse;
        set => SetProperty(ref _activeReadWhenInUse, value);
    }

    public bool EnableBleProvider
    {
        get => _enableBleProvider;
        set => SetProperty(ref _enableBleProvider, value);
    }

    public bool EnableLogitechProvider
    {
        get => _enableLogitechProvider;
        set => SetProperty(ref _enableLogitechProvider, value);
    }

    public bool EnableHidBatteryProvider
    {
        get => _enableHidBatteryProvider;
        set => SetProperty(ref _enableHidBatteryProvider, value);
    }

    public bool EnableDareuDriverBridge
    {
        get => _enableDareuDriverBridge;
        set => SetProperty(ref _enableDareuDriverBridge, value);
    }

    public bool ShowSystemBattery
    {
        get => _showSystemBattery;
        set => SetProperty(ref _showSystemBattery, value);
    }

    public bool HideUnsupportedDevices
    {
        get => _hideUnsupportedDevices;
        set => SetProperty(ref _hideUnsupportedDevices, value);
    }

    public string? MouseRingDeviceId
    {
        get => _mouseRingDeviceId;
        set => SetProperty(ref _mouseRingDeviceId, value);
    }

    public string? KeyboardRingDeviceId
    {
        get => _keyboardRingDeviceId;
        set => SetProperty(ref _keyboardRingDeviceId, value);
    }

    public bool ReduceMotion
    {
        get => _reduceMotion;
        set => SetProperty(ref _reduceMotion, value);
    }

    public bool DebugMode
    {
        get => _debugMode;
        set => SetProperty(ref _debugMode, value);
    }

    public ObservableCollection<DevicePreference> DevicePreferences { get; set; } = new();

    public Dictionary<string, CachedBattery> BatteryCache { get; set; } = [];

    public Dictionary<string, AlertMemory> AlertMemories { get; set; } = [];

    [JsonIgnore]
    public string LowThresholdSummary =>
        $"低于 {LowThreshold}% 时提醒；之后每再下降 {RepeatStep}% 提醒一次；{CriticalThreshold}% 时紧急提醒";

    public DevicePreference GetOrCreatePreference(string deviceId)
    {
        var pref = DevicePreferences.FirstOrDefault(p => string.Equals(p.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        if (pref is not null)
        {
            return pref;
        }

        pref = new DevicePreference { Id = deviceId };
        DevicePreferences.Add(pref);
        OnPropertyChanged(nameof(DevicePreferences));
        return pref;
    }

    public void Normalize()
    {
        LowThreshold = LowThreshold;
        RepeatStep = RepeatStep;
        CriticalThreshold = CriticalThreshold;
        RepeatCooldownMinutes = RepeatCooldownMinutes;
        SafeRefreshMinutes = SafeRefreshMinutes;
        EnablePolling = EnablePolling;
        SystemLowThreshold = SystemLowThreshold;
        if (!Enum.IsDefined(Theme)) Theme = ThemeMode.System;
        if (!Enum.IsDefined(StartupMode)) StartupMode = StartupMode.Silent;
        if (!Enum.IsDefined(CloseAction)) CloseAction = CloseAction.MinimizeToTray;
        if (!Enum.IsDefined(OuterRingContent)) OuterRingContent = RingContent.Mouse;
        if (string.IsNullOrWhiteSpace(MouseColor)) MouseColor = "#4CC2FF";
        if (string.IsNullOrWhiteSpace(KeyboardColor)) KeyboardColor = "#FFB84D";
        DevicePreferences ??= new ObservableCollection<DevicePreference>();
        BatteryCache ??= new Dictionary<string, CachedBattery>();
        AlertMemories ??= new Dictionary<string, AlertMemory>();
        if (PauseUntil is { } until && until <= DateTimeOffset.Now)
        {
            MonitoringPaused = false;
            PauseUntil = null;
        }
    }
}

public sealed class DevicePreference : ObservableObject
{
    private bool _monitored = true;
    private DeviceRole _role = DeviceRole.Auto;
    private string? _customName;
    private bool _userConfigured;

    public string Id { get; set; } = string.Empty;

    public bool Monitored
    {
        get => _monitored;
        set => SetProperty(ref _monitored, value);
    }

    public DeviceRole Role
    {
        get => _role;
        set => SetProperty(ref _role, value);
    }

    public string? CustomName
    {
        get => _customName;
        set => SetProperty(ref _customName, value);
    }

    /// <summary>
    /// True once the user explicitly changed the role/monitor switch. This lets a
    /// formerly unsupported device become monitored automatically after an app
    /// update, without overriding a deliberate opt-out.
    /// </summary>
    public bool UserConfigured
    {
        get => _userConfigured;
        set => SetProperty(ref _userConfigured, value);
    }
}

public sealed class AlertMemory
{
    public int? LastNotifiedLevel { get; set; }
    public DateTimeOffset? LastNotifiedAt { get; set; }
    public bool CriticalNotified { get; set; }
    public int? LastSeenLevel { get; set; }
}

public sealed class CachedBattery
{
    public int Percentage { get; set; }
    public bool? IsCharging { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public BatterySourceKind Source { get; set; }
    public string Name { get; set; } = string.Empty;
}
