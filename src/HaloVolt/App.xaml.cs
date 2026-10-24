using System.Windows;
using System.Windows.Threading;
using HaloVolt.Infrastructure;
using HaloVolt.Models;
using HaloVolt.Providers;
using HaloVolt.Providers.Hid;
using HaloVolt.Services;
using HaloVolt.ViewModels;
using HaloVolt.Views;
using Microsoft.Win32;

namespace HaloVolt;

public partial class App : System.Windows.Application
{
    private SettingsService _settingsService = null!;
    private StartupService _startupService = null!;
    private SingleInstanceService _singleInstance = null!;
    private DeviceMonitor _monitor = null!;
    private NotificationService _notifications = null!;
    private TrayService _tray = null!;
    private TrayFlyoutWindow? _trayMenu;
    private MainWindow? _window;
    private DispatcherTimer? _timer;
    private DispatcherTimer? _deviceChangeTimer;
    private DeviceChangeWatcher? _deviceWatcher;
    private bool _exiting;
    private bool _cleanedUp;
    private bool _tickRunning;
    private DateTimeOffset _lastAutoDiscovery = DateTimeOffset.Now;
    private DateTimeOffset _lastRefreshRequest = DateTimeOffset.MinValue;

    public static App CurrentApp => (App)Current;

    public AppSettings Settings => _settingsService.Settings;
    public MainViewModel ViewModel { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsService = new SettingsService();
        _settingsService.Settings.Normalize();
        AppLog.Initialize(true, _settingsService.Settings.DebugMode);

        ThemeManager.Initialize(Settings.Theme);
        System.Windows.Forms.Application.EnableVisualStyles();

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquire(() => Dispatcher.BeginInvoke(() => OpenSettings())))
        {
            _cleanedUp = true;
            _singleInstance.Dispose();
            Shutdown(0);
            return;
        }

        HookGlobalExceptions();

        var args = e.Args;
        if (args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase)))
        {
            Settings.DemoMode = true;
        }

        if (args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase)))
        {
            Settings.StartupMode = StartupMode.Silent;
        }

        if (args.Any(a => a.Equals("--debug", StringComparison.OrdinalIgnoreCase)))
        {
            Settings.DebugMode = true;
            AppLog.VerboseEnabled = true;
        }

        AppLog.Debug($"Command line: {string.Join(' ', args)}");
        AppLog.Debug($"OS: {Environment.OSVersion}; 64-bit process: {Environment.Is64BitProcess}; path: {AppPaths.ExecutablePath}");

        _startupService = new StartupService();
        if (Settings.AutoStart)
        {
            _startupService.RefreshIfEnabled();
            if (!_startupService.IsEnabled())
            {
                _startupService.SetEnabled(true);
            }
        }
        else if (_startupService.IsEnabled())
        {
            _startupService.SetEnabled(false);
        }

        _notifications = new NotificationService(Settings);
        _notifications.Initialize();

        var catalog = new HidDeviceCatalog();
        _monitor = new DeviceMonitor(
            Settings,
            _settingsService,
            _notifications,
            new HidBatteryProvider(catalog),
            new DareuEk75Provider(),
            new LogitechHidppProvider(catalog),
            new BleBatteryProvider(Settings),
            new DemoBatteryProvider());

        ViewModel = new MainViewModel(Settings, _settingsService, _monitor, _notifications, new DiagnosticsService())
        {
            RefreshRequested = () => RefreshSafelyAsync(force: false, discover: true),
            ForceRefreshRequested = () => ForceRefreshAsync(),
            ResetSettingsRequested = ResetAllSettings,
            ExitRequested = BeginExit
        };

        _tray = new TrayService(Settings);
        _tray.OpenSettings += () => OpenSettings();
        _tray.MenuRequested += ShowTrayMenu;
        _notifications.FallbackRequested += (_, message) => _tray.ShowBalloon(message.Title, message.Message);
        _monitor.NotificationRaised += OnNotificationRaised;
        _monitor.DevicesChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            ViewModel.RefreshFromMonitor();
            UpdateTray();
        });
        ViewModel.TrayNeedsUpdate += (_, _) => UpdateTray();
        Settings.PropertyChanged += OnSettingsChanged;
        ThemeManager.ThemeChanged += (_, _) =>
        {
            ViewModel.RefreshFromMonitor();
        };
        SubscribeSystemEvents();

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) => await OnTimerTickAsync();
        _timer.Start();

        _ = InitializeAsync(args);
    }

    private async Task InitializeAsync(string[] args)
    {
        try
        {
            await _monitor.InitializeAsync();
            ViewModel.RefreshFromMonitor();
            UpdateTray();

            var forceOpen = args.Any(a => a.Equals("--open", StringComparison.OrdinalIgnoreCase)
                                          || a.Equals("--settings", StringComparison.OrdinalIgnoreCase)
                                          || a.Equals("--show", StringComparison.OrdinalIgnoreCase));
            var firstRun = !Settings.HasCompletedFirstRun;
            var pageArg = args.FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
            PageKey? startPage = null;
            if (pageArg is not null && Enum.TryParse<PageKey>(pageArg["--page=".Length..], ignoreCase: true, out var parsedPage))
            {
                startPage = parsedPage;
                forceOpen = true;
            }

            if (forceOpen || firstRun || Settings.StartupMode == StartupMode.OpenSettings)
            {
                OpenSettings(startPage);
            }

            if (args.Any(a => a.Equals("--tray-menu", StringComparison.OrdinalIgnoreCase)))
            {
                ShowTrayMenu(System.Windows.Forms.Cursor.Position);
            }

            if (firstRun)
            {
                Settings.HasCompletedFirstRun = true;
                _settingsService.ScheduleSave();
            }

            if (_window is null)
            {
                TrimMemorySoon();
            }
        }
        catch (Exception ex)
        {
            AppLog.Crash(ex);
            if (Settings.StartupMode == StartupMode.OpenSettings || args.Any(a => a.Equals("--open", StringComparison.OrdinalIgnoreCase)))
            {
                OpenSettings();
            }
        }
    }

    public void OpenSettings(PageKey? initialPage = null)
    {
        if (_exiting)
        {
            return;
        }

        _trayMenu?.Close();

        if (_window is { IsLoaded: true })
        {
            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Show();
            _window.Activate();
            _window.Focus();
            if (initialPage is not null)
            {
                _window.NavigateTo(initialPage.Value);
            }

            return;
        }

        _window = new MainWindow(ViewModel, initialPage);
        var window = _window;
        _window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        };
        _window.Show();
        _window.Activate();

        if (!Settings.IsMonitoringPaused)
        {
            _ = RefreshSafelyAsync(force: false, discover: false);
        }

        var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            WindowEffects.Apply(_window, ThemeManager.IsDark);
        }
    }

    private void ShowTrayMenu(System.Drawing.Point screenPoint)
    {
        if (_exiting)
        {
            return;
        }

        if (_trayMenu is { IsVisible: true })
        {
            _trayMenu.Close();
            _trayMenu = null;
            return;
        }

        var menu = new TrayFlyoutWindow(ViewModel);
        _trayMenu = menu;
        menu.OpenRequested += () => OpenSettings();
        menu.ExitRequested += BeginExit;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_trayMenu, menu))
            {
                _trayMenu = null;
            }
        };
        menu.ShowAt(screenPoint);

        if (!Settings.IsMonitoringPaused)
        {
            _ = RefreshSafelyAsync(force: false, discover: false);
        }
    }

    public bool HandleWindowClosing(MainWindow window)
    {
        if (window.ForceClose || _exiting)
        {
            return true;
        }

        if (Settings.CloseAction == CloseAction.Exit)
        {
            Dispatcher.BeginInvoke(BeginExit, DispatcherPriority.Background);
            return true;
        }

        window.ForceClose = true;
        window.Hide();
        Dispatcher.BeginInvoke(() =>
        {
            if (window.IsVisible)
            {
                window.Hide();
            }

            window.Close();
        }, DispatcherPriority.Background);

        if (Settings.ShowTrayHintOnMinimize)
        {
            _tray.ShowBalloon("HaloVolt 仍在运行", "电量监控已缩到托盘。左键单击托盘图标可重新打开设置。");
        }

        TrimMemorySoon();
        return false;
    }

    public void OnWindowMinimized(MainWindow window)
    {
        if (_exiting || !Settings.MinimizeToTray || window.WindowState != WindowState.Minimized)
        {
            return;
        }

        window.ForceClose = true;
        window.Hide();
        Dispatcher.BeginInvoke(() => window.Close(), DispatcherPriority.Background);
        if (Settings.ShowTrayHintOnMinimize)
        {
            _tray.ShowBalloon("HaloVolt 仍在运行", "电量监控已缩到托盘。左键单击托盘图标可重新打开设置。");
        }

        TrimMemorySoon();
    }

    private void TrimMemorySoon()
    {
        Dispatcher.BeginInvoke(() =>
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            try
            {
                EmptyWorkingSet(GetCurrentProcess());
            }
            catch
            {
                // Trimming is an optimization, not a requirement.
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    [System.Runtime.InteropServices.DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr process);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private async Task OnTimerTickAsync()
    {
        if (_exiting || _tickRunning)
        {
            return;
        }

        _tickRunning = true;
        try
        {
            _monitor.UpdateSystemBattery();
            ViewModel.UpdateClock();
            UpdateTray();

            if (!Settings.MonitoringPaused && Settings.PauseUntil is { } pauseUntil && pauseUntil <= DateTimeOffset.Now)
            {
                Settings.PauseUntil = null;
            }

            if (Settings.IsMonitoringPaused)
            {
                return;
            }

            if (!IsUserActive())
            {
                return;
            }

            var now = DateTimeOffset.Now;
            var interval = TimeSpan.FromMinutes(Settings.SafeRefreshMinutes);
            if (Settings.EnablePolling && ViewModel.RefreshCommand.CanExecute(null) && now - _lastRefreshRequest >= interval)
            {
                _lastRefreshRequest = now;
                await _monitor.RefreshAsync(force: false, discover: false);
                ViewModel.RefreshFromMonitor();
            }

            if (now - _lastAutoDiscovery >= TimeSpan.FromMinutes(15))
            {
                _lastAutoDiscovery = now;
                await _monitor.RefreshDevicesAsync(activityAvailable: true);
                ViewModel.RefreshFromMonitor();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("后台刷新失败", ex);
        }
        finally
        {
            _tickRunning = false;
        }
    }

    private async Task RefreshSafelyAsync(bool force, bool discover)
    {
        try
        {
            _lastRefreshRequest = DateTimeOffset.Now;
            await _monitor.RefreshAsync(force, discover);
            ViewModel.RefreshFromMonitor();
            UpdateTray();
        }
        catch (Exception ex)
        {
            AppLog.Error("手动刷新失败", ex);
        }
    }

    private async Task ForceRefreshAsync()
    {
        var result = MessageBox.Show(
            "强制读取会向设备发送一次查询，可能让已休眠的鼠标或键盘短暂唤醒。\n\n仅在普通刷新读不到电量时使用。是否继续？",
            "强制读取电量",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await RefreshSafelyAsync(force: true, discover: true);
    }

    private static bool IsUserActive()
    {
        var monitor = new ActivityMonitor();
        return monitor.IsUserActive;
    }

    private void UpdateTray()
    {
        if (_tray is null || ViewModel is null)
        {
            return;
        }

        var mouseColor = UiBrushes.ColorFromHex(Settings.MouseColor, System.Windows.Media.Color.FromRgb(0x4C, 0xC2, 0xFF));
        var keyboardColor = UiBrushes.ColorFromHex(Settings.KeyboardColor, System.Windows.Media.Color.FromRgb(0xFF, 0xB8, 0x4D));
        var outerDevice = ViewModel.OuterRoleDevice;
        var innerDevice = ViewModel.InnerRoleDevice;
        var outerColor = Settings.OuterRingContent == RingContent.Mouse ? mouseColor : keyboardColor;
        var innerColor = Settings.OuterRingContent == RingContent.Mouse ? keyboardColor : mouseColor;

        if (outerDevice?.BatteryPercent is { } outerPercent && outerPercent <= 10)
        {
            outerColor = System.Windows.Media.Color.FromRgb(0xE8, 0x5D, 0x5D);
        }

        if (innerDevice?.BatteryPercent is { } innerPercent && innerPercent <= 10)
        {
            innerColor = System.Windows.Media.Color.FromRgb(0xE8, 0x5D, 0x5D);
        }

        var systemPercent = Settings.ShowSystemBattery && Settings.TrayCenterDot
            ? ViewModel.SystemDevice?.BatteryPercent
            : null;

        _tray.Update(
            ViewModel.TrayTooltip,
            new TrayRingSnapshot(
                outerDevice?.BatteryPercent,
                innerDevice?.BatteryPercent,
                ToDrawingColor(outerColor),
                ToDrawingColor(innerColor),
                systemPercent,
                Settings.TrayCenterDot));
    }

    private static System.Drawing.Color ToDrawingColor(System.Windows.Media.Color color)
        => System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);

    private void OnNotificationRaised(object? sender, (DeviceState Device, AlertDecision Decision) e)
    {
        _notifications.ShowAlert(e.Device, e.Decision);
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.AutoStart):
                _startupService.SetEnabled(Settings.AutoStart);
                break;
            case nameof(AppSettings.Theme):
                ThemeManager.Apply(Settings.Theme);
                break;
            case nameof(AppSettings.DebugMode):
                AppLog.VerboseEnabled = Settings.DebugMode;
                AppLog.Debug("调试模式已启用");
                break;
            case nameof(AppSettings.TrayBatteryIconEnabled):
            case nameof(AppSettings.TrayCenterDot):
            case nameof(AppSettings.ShowSystemBattery):
            case nameof(AppSettings.OuterRingContent):
            case nameof(AppSettings.MouseColor):
            case nameof(AppSettings.KeyboardColor):
                UpdateTray();
                break;
            case nameof(AppSettings.MonitoringPaused):
            case nameof(AppSettings.PauseUntil):
                break;
            case nameof(AppSettings.DemoMode):
                _ = RefreshSafelyAsync(force: false, discover: true);
                break;
            case nameof(AppSettings.EnableBleProvider):
            case nameof(AppSettings.EnableLogitechProvider):
            case nameof(AppSettings.EnableHidBatteryProvider):
            case nameof(AppSettings.EnableDareuDriverBridge):
                _ = RefreshSafelyAsync(force: false, discover: true);
                break;
            case nameof(AppSettings.ActiveReadWhenInUse):
                _ = RefreshSafelyAsync(force: false, discover: false);
                break;
        }
    }

    private void SubscribeSystemEvents()
    {
        _deviceWatcher = new DeviceChangeWatcher();
        _deviceWatcher.Changed += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _deviceChangeTimer?.Stop();
            _deviceChangeTimer?.Start();
        });
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += (_, _) => _exiting = true;

        _deviceChangeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _deviceChangeTimer.Tick += async (_, _) =>
        {
            _deviceChangeTimer.Stop();
            if (!Settings.IsMonitoringPaused)
            {
                await _monitor.RefreshDevicesAsync(activityAvailable: IsUserActive());
                ViewModel.RefreshFromMonitor();
                UpdateTray();
            }
        };
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                _monitor.UpdateSystemBattery();
                await _monitor.RefreshAsync(force: false, discover: true);
                ViewModel.RefreshFromMonitor();
                UpdateTray();
            });
        }
        else if (e.Mode == PowerModes.StatusChange)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _monitor.UpdateSystemBattery();
                UpdateTray();
            });
        }
    }

    private void HookGlobalExceptions()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            AppLog.Crash(e.Exception);
            e.Handled = true;
            MessageBox.Show("发生了一个错误，HaloVolt 会继续在后台运行。\n\n" + e.Exception.Message,
                "HaloVolt", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                AppLog.Crash(exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("未观察到的任务异常", e.Exception);
            e.SetObserved();
        };
    }

    private void ResetAllSettings()
    {
        var result = MessageBox.Show("将恢复所有设置（设备选择与通知记录会保留）。是否继续？",
            "恢复默认设置", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _settingsService.Reset();
        ThemeManager.Apply(Settings.Theme);
        UpdateTray();
        ViewModel.RefreshFromMonitor();
    }

    public void BeginExit()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _timer?.Stop();
        _deviceChangeTimer?.Stop();
        _trayMenu?.Close();
        _trayMenu = null;
        if (_window is not null)
        {
            _window.ForceClose = true;
            _window.Close();
            _window = null;
        }

        Dispatcher.BeginInvoke(CleanUpAndShutdown, DispatcherPriority.Background);
    }

    private void CleanUpAndShutdown()
    {
        CleanUpResources();
        Shutdown(0);
    }

    private void CleanUpResources()
    {
        if (_cleanedUp)
        {
            return;
        }

        _cleanedUp = true;
        try
        {
            Settings.PropertyChanged -= OnSettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _deviceWatcher?.Dispose();
            if (_monitor is not null)
            {
                _monitor.NotificationRaised -= OnNotificationRaised;
                _monitor.Dispose();
            }

            _notifications?.Dispose();
            _tray?.Dispose();
            ViewModel?.Dispose();
            _settingsService?.SaveNow();
            _settingsService?.Dispose();
            _singleInstance?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error("退出清理失败", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CleanUpResources();
        base.OnExit(e);
    }
}
