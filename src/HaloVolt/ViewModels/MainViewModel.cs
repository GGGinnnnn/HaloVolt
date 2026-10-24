using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HaloVolt.Infrastructure;
using HaloVolt.Models;
using HaloVolt.Services;

namespace HaloVolt.ViewModels;

public sealed record ChoiceOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly DeviceMonitor _monitor;
    private readonly NotificationService _notifications;
    private readonly DiagnosticsService _diagnostics;
    private readonly NotifyCollectionChangedEventHandler _historyHandler;
    private readonly Dictionary<string, DeviceViewModel> _deviceMap = new(StringComparer.OrdinalIgnoreCase);
    private DeviceViewModel? _systemDevice;
    private bool _disposed;
    private DiagnosticsSnapshot? _diagnosticsSnapshot;
    private bool _diagnosticsLoading;
    private int _hiddenUnsupportedCount;

    private DeviceViewModel? _mouseDevice;
    private DeviceViewModel? _keyboardDevice;
    private string _lastSyncText = "等待首次同步";
    private string _trayTooltip = "HaloVolt · 正在启动";

    public MainViewModel(
        AppSettings settings,
        SettingsService settingsService,
        DeviceMonitor monitor,
        NotificationService notifications,
        DiagnosticsService diagnostics)
    {
        _settings = settings;
        _settingsService = settingsService;
        _monitor = monitor;
        _notifications = notifications;
        _diagnostics = diagnostics;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ForceRefreshCommand = new AsyncRelayCommand(ForceRefreshAsync);
        TestNotificationCommand = new RelayCommand(() => _notifications.ShowTest());
        OpenDataFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.AppDataDirectory));
        OpenLogCommand = new RelayCommand(() => OpenFile(AppPaths.LogFile));
        CopyDiagnosticReportCommand = new RelayCommand(CopyDiagnosticReport);
        Pause30Command = new RelayCommand(() => PauseFor(TimeSpan.FromMinutes(30)));
        Pause60Command = new RelayCommand(() => PauseFor(TimeSpan.FromMinutes(60)));
        ResumeMonitoringCommand = new RelayCommand(ResumeMonitoring);
        SetMouseColorCommand = new RelayCommand<string>(SetMouseColor);
        SetKeyboardColorCommand = new RelayCommand<string>(SetKeyboardColor);
        NavigateCommand = new RelayCommand<PageKey>(key => NavigationRequested?.Invoke(key));
        RefreshDiagnosticsCommand = new AsyncRelayCommand(RefreshDiagnosticsAsync);
        ExportDiagnosticsCommand = new RelayCommand(ExportDiagnostics);
        ClearAlertMemoryCommand = new RelayCommand(() =>
        {
            _settings.AlertMemories.Clear();
            _settingsService.ScheduleSave();
        });
        ResetSettingsCommand = new RelayCommand(() => ResetSettingsRequested?.Invoke());
        ExitApplicationCommand = new RelayCommand(() => ExitRequested?.Invoke());

        _settings.PropertyChanged += OnSettingsChanged;
        _monitor.DevicesChanged += OnDevicesChanged;
        _monitor.ReadingUpdated += OnReadingUpdated;
        _historyHandler = (_, _) => OnPropertiesChanged(nameof(HasNotificationHistory), nameof(NotificationCountText));
        _notifications.History.CollectionChanged += _historyHandler;

        RefreshFromMonitor();
    }

    public AppSettings Settings => _settings;
    public ObservableCollection<DeviceViewModel> Devices { get; } = [];
    public DeviceViewModel? SystemDevice => _systemDevice;
    public ObservableCollection<NotificationRecord> NotificationHistory => _notifications.History;

    public ICommand RefreshCommand { get; }
    public ICommand ForceRefreshCommand { get; }
    public ICommand TestNotificationCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand CopyDiagnosticReportCommand { get; }
    public ICommand Pause30Command { get; }
    public ICommand Pause60Command { get; }
    public ICommand ResumeMonitoringCommand { get; }
    public ICommand SetMouseColorCommand { get; }
    public ICommand SetKeyboardColorCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand RefreshDiagnosticsCommand { get; }
    public ICommand ExportDiagnosticsCommand { get; }
    public ICommand ClearAlertMemoryCommand { get; }
    public ICommand ResetSettingsCommand { get; }
    public ICommand ExitApplicationCommand { get; }

    public Action<PageKey>? NavigationRequested { get; set; }

    public Func<Task>? RefreshRequested { get; set; }
    public Func<Task>? ForceRefreshRequested { get; set; }
    public Action? ResetSettingsRequested { get; set; }
    public Action? ExitRequested { get; set; }
    public Action? ThemeChanged { get; set; }

    public event EventHandler? TrayNeedsUpdate;

    public IReadOnlyList<ChoiceOption<ThemeMode>> ThemeOptions { get; } =
    [
        new(ThemeMode.System, "跟随系统"),
        new(ThemeMode.Light, "浅色"),
        new(ThemeMode.Dark, "深色")
    ];

    public IReadOnlyList<ChoiceOption<StartupMode>> StartupModeOptions { get; } =
    [
        new(StartupMode.Silent, "静默启动（仅托盘）"),
        new(StartupMode.OpenSettings, "启动后打开设置")
    ];

    public IReadOnlyList<ChoiceOption<CloseAction>> CloseActionOptions { get; } =
    [
        new(CloseAction.MinimizeToTray, "最小化到托盘"),
        new(CloseAction.Exit, "退出程序")
    ];

    public IReadOnlyList<ChoiceOption<RingContent>> RingContentOptions { get; } =
    [
        new(RingContent.Mouse, "外圈显示鼠标电量"),
        new(RingContent.Keyboard, "外圈显示键盘电量")
    ];

    public IReadOnlyList<ChoiceOption<int>> RefreshIntervalOptions { get; } =
    [
        new(5, "每 5 分钟"),
        new(10, "每 10 分钟"),
        new(15, "每 15 分钟"),
        new(30, "每 30 分钟"),
        new(60, "每 60 分钟"),
        new(120, "每 120 分钟")
    ];

    public IReadOnlyList<string> ColorPalette { get; } =
        ["#4CC2FF", "#5B8DEF", "#7C6CFF", "#FFB84D", "#FF8C69", "#6CCB5F", "#E66FA4", "#DADADA"];

    public DeviceViewModel? MouseDevice
    {
        get => _mouseDevice;
        private set
        {
            if (SetProperty(ref _mouseDevice, value))
            {
                OnPropertiesChanged(nameof(MousePercentText), nameof(MouseName), nameof(MouseSubtitle), nameof(HasMouse));
            }
        }
    }

    public DeviceViewModel? KeyboardDevice
    {
        get => _keyboardDevice;
        private set
        {
            if (SetProperty(ref _keyboardDevice, value))
            {
                OnPropertiesChanged(nameof(KeyboardPercentText), nameof(KeyboardName), nameof(KeyboardSubtitle), nameof(HasKeyboard));
            }
        }
    }

    public bool HasMouse => MouseDevice is not null;
    public bool HasKeyboard => KeyboardDevice is not null;
    public bool HasSystemBattery => _settings.ShowSystemBattery && SystemDevice is not null;
    public bool HasAnyDevice => Devices.Count > 0;
    public int HiddenUnsupportedCount => _hiddenUnsupportedCount;
    public bool HasHiddenUnsupported => _settings.HideUnsupportedDevices && HiddenUnsupportedCount > 0;
    public bool ShowUnsupportedHint => !HasAnyDevice && HasHiddenUnsupported;
    public bool ShowEmptyState => !HasAnyDevice && !HasHiddenUnsupported;
    public bool HasLowDevices => Devices.Any(d => d.IsLow && d.HasBattery);    public int MonitoredDeviceCount => Devices.Count(d => d.Monitored && d.SupportsBattery);
    public int LowDeviceCount => Devices.Count(d => d.IsLow && d.HasBattery);
    public bool IsDemoMode => _settings.DemoMode;
    public bool IsMonitoringPaused => _settings.IsMonitoringPaused;
    public bool HasNotificationHistory => NotificationHistory.Count > 0;
    public string NotificationCountText => NotificationHistory.Count == 0 ? "暂无通知记录" : $"最近 {NotificationHistory.Count} 条";
    public string TrayStatusText => _settings.TrayBatteryIconEnabled ? "圆环图标已显示" : "圆环图标已关闭";
    public string NotificationStatusText => _settings.NotificationsEnabled ? "提醒已开启" : "提醒已关闭";
    public string RefreshStatusText => _settings.IsMonitoringPaused ? "监控已暂停" : $"每 {_settings.SafeRefreshMinutes} 分钟检查";
    public string PreviewNextThreshold => $"{Math.Max(0, _settings.LowThreshold - _settings.RepeatStep)}%";

    public string MouseName => MouseDevice?.Name ?? "未选择鼠标";
    public string KeyboardName => KeyboardDevice?.Name ?? "未选择键盘";
    public string MousePercentText => MouseDevice?.BatteryText ?? "—";
    public string KeyboardPercentText => KeyboardDevice?.BatteryText ?? "—";
    public string MouseSubtitle => MouseDevice is null ? "连接无线鼠标后自动出现" : $"{MouseDevice.TransportText} · {MouseDevice.FreshnessText}";
    public string KeyboardSubtitle => KeyboardDevice is null ? "连接无线键盘后自动出现" : $"{KeyboardDevice.TransportText} · {KeyboardDevice.FreshnessText}";
    public string SystemPercentText => SystemDevice?.BatteryText ?? "—";
    public string SystemSubtitle => SystemDevice is null ? "未检测到内置电池" : $"{SystemDevice.DetailText} · {SystemDevice.FreshnessText}";
    public bool IsSystemCharging => SystemDevice?.IsCharging == true;

    public int RingOuterValue => (int)Math.Round(OuterRoleDevice?.RingValue ?? 0);
    public int RingInnerValue => (int)Math.Round(InnerRoleDevice?.RingValue ?? 0);
    public double RingOuterFraction => Math.Clamp((OuterRoleDevice?.RingValue ?? 0) / 100.0, 0, 1);
    public double RingInnerFraction => Math.Clamp((InnerRoleDevice?.RingValue ?? 0) / 100.0, 0, 1);
    public string OuterPercentText => OuterRoleDevice?.BatteryText ?? "—";
    public string InnerPercentText => InnerRoleDevice?.BatteryText ?? "—";
    public string OuterLabel => _settings.OuterRingContent == RingContent.Mouse ? "鼠标" : "键盘";
    public string InnerLabel => _settings.OuterRingContent == RingContent.Mouse ? "键盘" : "鼠标";
    public string OuterDeviceName => OuterRoleDevice?.Name ?? $"未检测到{OuterLabel}";
    public string InnerDeviceName => InnerRoleDevice?.Name ?? $"未检测到{InnerLabel}";
    public bool OuterIsLow => OuterRoleDevice?.BatteryPercent is { } p && p <= _settings.LowThreshold;
    public bool InnerIsLow => InnerRoleDevice?.BatteryPercent is { } p && p <= _settings.LowThreshold;

    public DeviceViewModel? OuterRoleDevice => _settings.OuterRingContent == RingContent.Mouse ? MouseDevice : KeyboardDevice;
    public DeviceViewModel? InnerRoleDevice => _settings.OuterRingContent == RingContent.Mouse ? KeyboardDevice : MouseDevice;

    public Brush MouseAccentBrush => LowOrBrandColor(MouseDevice, _settings.MouseColor);
    public Brush KeyboardAccentBrush => LowOrBrandColor(KeyboardDevice, _settings.KeyboardColor);
    public Brush OuterRingBrush => _settings.OuterRingContent == RingContent.Mouse ? MouseAccentBrush : KeyboardAccentBrush;
    public Brush InnerRingBrush => _settings.OuterRingContent == RingContent.Mouse ? KeyboardAccentBrush : MouseAccentBrush;
    public Color MouseAccentColor => UiBrushes.ColorFromHex(_settings.MouseColor, Color.FromRgb(0x4C, 0xC2, 0xFF));
    public Color KeyboardAccentColor => UiBrushes.ColorFromHex(_settings.KeyboardColor, Color.FromRgb(0xFF, 0xB8, 0x4D));

    public string LastSyncText
    {
        get => _lastSyncText;
        private set => SetProperty(ref _lastSyncText, value);
    }

    public string TrayTooltip
    {
        get => _trayTooltip;
        private set => SetProperty(ref _trayTooltip, value);
    }

    public string DeviceCountText
    {
        get
        {
            var monitored = Devices.Count(d => d.Monitored && d.SupportsBattery);
            var connected = Devices.Count(d => d.IsOnline);
            return monitored == 0 ? $"{connected} 个设备在线" : $"{monitored} 个监控中 · {connected} 个在线";
        }
    }

    public string LowDeviceCountText
    {
        get
        {
            var low = Devices.Count(d => d.IsLow && d.HasBattery);
            return low == 0 ? "没有低电量设备" : $"{low} 个设备电量偏低";
        }
    }

    public string MonitoringPill => _settings.DemoMode
        ? "演示模式"
        : _settings.IsMonitoringPaused
            ? "监控已暂停"
            : "安全读取中";

    public string VersionText => "版本 " + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    public string AboutDeviceCount => $"{Devices.Count} 个外设";

    public string DiagnosticSummary
    {
        get
        {
            var lines = new List<string>
            {
                $"HaloVolt {VersionText}",
                $"OS: {Environment.OSVersion}",
                $"监控状态: {_settings.MonitoringStatusText}",
                $"读取策略: {( _settings.ActiveReadWhenInUse ? "仅活动时主动读取" : "仅使用缓存")}",
                $"刷新间隔: {_settings.SafeRefreshMinutes} 分钟",
                $"低电量阈值: {_settings.LowThreshold}% / 每 {_settings.RepeatStep}%",
                "设备:"
            };
            lines.AddRange(Devices.Select(d =>
                $"  - {d.Name} [{d.KindText}] {d.BatteryText} online={d.IsOnline} source={d.SourceText}"));
            return string.Join(Environment.NewLine, lines);
        }
    }

    public DiagnosticsSnapshot? Diagnostics
    {
        get => _diagnosticsSnapshot;
        private set
        {
            if (SetProperty(ref _diagnosticsSnapshot, value))
            {
                OnPropertiesChanged(nameof(HasDiagnostics), nameof(DiagnosticsSummaryText), nameof(DiagnosticReport));
            }
        }
    }

    public bool HasDiagnostics => Diagnostics is not null;
    public bool IsDiagnosticsLoading
    {
        get => _diagnosticsLoading;
        private set
        {
            if (SetProperty(ref _diagnosticsLoading, value))
            {
                OnPropertyChanged(nameof(DiagnosticsStatus));
            }
        }
    }

    public string DiagnosticsStatus => IsDiagnosticsLoading ? "正在收集诊断信息…" : Diagnostics is null ? "尚未收集" : $"采集于 {Diagnostics.CollectedAt:HH:mm:ss}";
    public string DiagnosticsSummaryText => Diagnostics?.Summary ?? "点击“重新收集”查看原始 HID 通道、设备解析与日志。";
    public string DiagnosticReport => Diagnostics?.ToReport() ?? DiagnosticSummary;

    public async Task RefreshAsync()
    {
        if (RefreshRequested is not null)
        {
            await RefreshRequested();
        }
        else
        {
            await _monitor.RefreshAsync(force: false, discover: true);
        }

        RefreshFromMonitor();
    }

    public async Task ForceRefreshAsync()
    {
        if (ForceRefreshRequested is not null)
        {
            await ForceRefreshRequested();
        }
        else
        {
            await _monitor.RefreshAsync(force: true, discover: true);
        }

        RefreshFromMonitor();
    }

    public void RefreshFromMonitor()
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(RefreshFromMonitor);
            return;
        }

        var states = _monitor.States.ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasEk75 = states.Any(s => s.SupportsBattery
            && string.Equals(s.ProviderId, "dareu-ek75", StringComparison.OrdinalIgnoreCase));
        _hiddenUnsupportedCount = states.Count(s => !s.IsSystemBattery
            && !s.SupportsBattery
            && !(hasEk75 && s.Name.Contains("USB 2.4G Receiver", StringComparison.OrdinalIgnoreCase)));

        foreach (var state in states)
        {
            if (state.IsSystemBattery)
            {
                continue;
            }

            if (!state.SupportsBattery && _settings.HideUnsupportedDevices)
            {
                continue;
            }

            seen.Add(state.Id);
            var pref = _settings.DevicePreferences.FirstOrDefault(p => p.Id.Equals(state.Id, StringComparison.OrdinalIgnoreCase))
                       ?? _settings.GetOrCreatePreference(state.Id);
            if (_deviceMap.TryGetValue(state.Id, out var existing))
            {
                existing.RefreshProperties();
            }
            else
            {
                var vm = new DeviceViewModel(state, pref, _settings, OnDevicePreferenceChanged);
                vm.Changed += OnDeviceViewModelChanged;
                _deviceMap[state.Id] = vm;
            }
        }

        foreach (var id in _deviceMap.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            if (_deviceMap.Remove(id, out var vm))
            {
                vm.Changed -= OnDeviceViewModelChanged;
                vm.Dispose();
            }
        }

        var sorted = _deviceMap.Values
            .OrderBy(d => d.Kind switch
            {
                DeviceKind.Mouse => 0,
                DeviceKind.Keyboard => 1,
                DeviceKind.KeyboardMouse => 2,
                _ => 3
            })
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Keep the same instances so selections and scroll position survive refreshes.
        for (var i = 0; i < sorted.Count; i++)
        {
            var item = sorted[i];
            var existingIndex = Devices.IndexOf(item);
            if (existingIndex < 0)
            {
                Devices.Insert(i, item);
            }
            else if (existingIndex != i)
            {
                Devices.Move(existingIndex, i);
            }
        }

        while (Devices.Count > sorted.Count)
        {
            Devices.RemoveAt(Devices.Count - 1);
        }

        var systemState = states.FirstOrDefault(s => s.IsSystemBattery);
        if (systemState is null)
        {
            if (_systemDevice is not null)
            {
                _systemDevice.Changed -= OnDeviceViewModelChanged;
                _systemDevice.Dispose();
                _systemDevice = null;
            }
        }
        else
        {
            var pref = _settings.DevicePreferences.FirstOrDefault(p => p.Id.Equals(systemState.Id, StringComparison.OrdinalIgnoreCase))
                       ?? _settings.GetOrCreatePreference(systemState.Id);
            if (_systemDevice is null || _systemDevice.State != systemState)
            {
                if (_systemDevice is not null)
                {
                    _systemDevice.Changed -= OnDeviceViewModelChanged;
                    _systemDevice.Dispose();
                }

                _systemDevice = new DeviceViewModel(systemState, pref, _settings, OnDevicePreferenceChanged);
                _systemDevice.Changed += OnDeviceViewModelChanged;
            }
            else
            {
                _systemDevice.RefreshProperties();
            }
        }

        UpdateDerived();
    }

    public void UpdateClock()
    {
        var last = _monitor.LastRefresh;
        LastSyncText = last is null
            ? "等待首次同步"
            : (DateTimeOffset.Now - last.Value).TotalSeconds < 45
                ? "刚刚同步"
                : $"{(int)(DateTimeOffset.Now - last.Value).TotalMinutes} 分钟前同步";
        TrayTooltip = BuildTrayTooltip();
        OnPropertiesChanged(nameof(LastSyncText));
    }

    public void TraySettingsChanged() => TrayNeedsUpdate?.Invoke(this, EventArgs.Empty);

    public async Task RefreshDiagnosticsAsync()
    {
        if (IsDiagnosticsLoading)
        {
            return;
        }

        IsDiagnosticsLoading = true;
        try
        {
            await _monitor.RefreshDevicesAsync(activityAvailable: false);
            RefreshFromMonitor();
            Diagnostics = await _diagnostics.CollectAsync(_settings, _monitor.States);
        }
        catch (Exception ex)
        {
            AppLog.Error("收集诊断信息失败", ex);
        }
        finally
        {
            IsDiagnosticsLoading = false;
        }
    }

    private void ExportDiagnostics()
    {
        try
        {
            AppPaths.EnsureAppDataDirectory();
            var path = Path.Combine(AppPaths.AppDataDirectory, $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, DiagnosticReport, System.Text.Encoding.UTF8);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("导出诊断报告失败：" + ex.Message, "HaloVolt", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateDerived()
    {
        var mouseCandidates = Devices
            .Where(d => d.Monitored && d.SupportsBattery && d.EffectiveRole == DeviceRole.Mouse)
            .ToList();
        var keyboardCandidates = Devices
            .Where(d => d.Monitored && d.SupportsBattery && d.EffectiveRole == DeviceRole.Keyboard)
            .ToList();

        MouseDevice = ResolveRingDevice(_settings.MouseRingDeviceId, mouseCandidates);
        KeyboardDevice = ResolveRingDevice(_settings.KeyboardRingDeviceId, keyboardCandidates);

        UpdateClock();
        OnPropertiesChanged(
            nameof(RingOuterValue), nameof(RingInnerValue),
            nameof(RingOuterFraction), nameof(RingInnerFraction),
            nameof(OuterPercentText), nameof(InnerPercentText),
            nameof(OuterLabel), nameof(InnerLabel), nameof(OuterDeviceName), nameof(InnerDeviceName),
            nameof(OuterIsLow), nameof(InnerIsLow), nameof(OuterRingBrush), nameof(InnerRingBrush),
            nameof(MouseAccentBrush), nameof(KeyboardAccentBrush), nameof(MouseAccentColor), nameof(KeyboardAccentColor),
            nameof(MouseName), nameof(KeyboardName), nameof(MousePercentText), nameof(KeyboardPercentText),
            nameof(MouseSubtitle), nameof(KeyboardSubtitle), nameof(SystemPercentText), nameof(SystemSubtitle),
            nameof(IsSystemCharging), nameof(HasSystemBattery), nameof(HasMouse), nameof(HasKeyboard),
            nameof(DeviceCountText), nameof(LowDeviceCountText), nameof(IsDemoMode), nameof(MonitoringPill),
            nameof(IsMonitoringPaused), nameof(AboutDeviceCount), nameof(DiagnosticSummary),
            nameof(HasAnyDevice), nameof(HasLowDevices), nameof(MonitoredDeviceCount), nameof(LowDeviceCount),
            nameof(TrayStatusText), nameof(NotificationStatusText), nameof(RefreshStatusText), nameof(PreviewNextThreshold),
            nameof(HiddenUnsupportedCount), nameof(HasHiddenUnsupported), nameof(ShowUnsupportedHint), nameof(ShowEmptyState));

        TrayNeedsUpdate?.Invoke(this, EventArgs.Empty);
    }

    private static DeviceViewModel? ResolveRingDevice(string? preferredId, List<DeviceViewModel> candidates)
    {
        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            var preferred = candidates.FirstOrDefault(d => d.Id.Equals(preferredId, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return candidates.FirstOrDefault();
    }

    private Brush LowOrBrandColor(DeviceViewModel? device, string hex)
    {
        if (device?.BatteryPercent is { } value && value <= 10)
        {
            return (Brush)Application.Current.Resources["DangerBrush"];
        }

        return UiBrushes.FromHex(hex, Color.FromRgb(0x4C, 0xC2, 0xFF));
    }

    private string BuildTrayTooltip()
    {
        var bits = new List<string>();
        if (MouseDevice?.BatteryPercent is { } mouse) bits.Add($"鼠标 {mouse}%");
        if (KeyboardDevice?.BatteryPercent is { } keyboard) bits.Add($"键盘 {keyboard}%");
        if (SystemDevice?.BatteryPercent is { } system) bits.Add($"电池 {system}%");
        if (bits.Count == 0) bits.Add("等待设备电量");
        var sync = _monitor.LastRefresh is { } last
            ? (DateTimeOffset.Now - last).TotalMinutes < 1
                ? "刚刚"
                : $"{(int)(DateTimeOffset.Now - last).TotalMinutes} 分钟前"
            : "等待同步";
        return "HaloVolt · " + string.Join(" · ", bits) + " · " + sync;
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(RefreshFromMonitor);
            return;
        }

        RefreshFromMonitor();
    }

    private void OnReadingUpdated(object? sender, DeviceState state)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => OnReadingUpdated(sender, state));
            return;
        }

        UpdateDerived();
    }

    private void OnDeviceViewModelChanged(object? sender, EventArgs e) => UpdateDerived();

    private void OnDevicePreferenceChanged()
    {
        _settingsService.ScheduleSave();
        UpdateDerived();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.Theme))
        {
            ThemeChanged?.Invoke();
        }

        if (e.PropertyName is nameof(AppSettings.HideUnsupportedDevices))
        {
            RefreshFromMonitor();
        }

        _settingsService.ScheduleSave();
        UpdateDerived();
    }

    private void PauseFor(TimeSpan duration)
    {
        _settings.MonitoringPaused = false;
        _settings.PauseUntil = DateTimeOffset.Now + duration;
        OnPropertiesChanged(nameof(IsMonitoringPaused), nameof(MonitoringPill));
        TraySettingsChanged();
    }

    private void ResumeMonitoring()
    {
        _settings.MonitoringPaused = false;
        _settings.PauseUntil = null;
        OnPropertiesChanged(nameof(IsMonitoringPaused), nameof(MonitoringPill));
        TraySettingsChanged();
    }

    private void SetMouseColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            _settings.MouseColor = hex;
            UpdateDerived();
        }
    }

    private void SetKeyboardColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            _settings.KeyboardColor = hex;
            UpdateDerived();
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法打开文件夹：" + ex.Message, "HaloVolt", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                MessageBox.Show("日志文件尚未生成。", "HaloVolt", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法打开日志：" + ex.Message, "HaloVolt", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public static void OpenWebDriverSearch(DeviceViewModel? device)
    {
        if (device is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"该功能需要联网。\n\n将在默认浏览器中搜索「{device.Name} 官方驱动 / 网页驱动」。搜索词会由浏览器发送给搜索引擎。\n\nHaloVolt 不上传设备信息，也不会读取网页内容；网页驱动里的电量无法自动回传到 HaloVolt。\n\n是否继续？",
            "联网查找官方驱动",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var query = Uri.EscapeDataString($"{device.Name} 官方驱动 网页驱动 下载");
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.bing.com/search?q=" + query,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("无法打开浏览器：" + ex.Message, "HaloVolt", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyDiagnosticReport()
    {
        try
        {
            Clipboard.SetText(DiagnosticReport);
        }
        catch
        {
            // Clipboard can be locked by another app; silently keep the button harmless.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.PropertyChanged -= OnSettingsChanged;
        _monitor.DevicesChanged -= OnDevicesChanged;
        _monitor.ReadingUpdated -= OnReadingUpdated;

        foreach (var device in _deviceMap.Values)
        {
            device.Changed -= OnDeviceViewModelChanged;
            device.Dispose();
        }

        _deviceMap.Clear();
        if (_systemDevice is not null)
        {
            _systemDevice.Changed -= OnDeviceViewModelChanged;
            _systemDevice.Dispose();
        }

        _notifications.History.CollectionChanged -= _historyHandler;
    }
}
