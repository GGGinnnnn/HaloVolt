using HaloVolt.Infrastructure;

namespace HaloVolt.Models;

public sealed record BatteryReading(
    int Percentage,
    bool? IsCharging,
    DateTimeOffset Timestamp,
    BatterySourceKind Source,
    bool IsCached = false,
    int? VoltageMv = null,
    string? Note = null)
{
    public int ClampedPercentage => Math.Clamp(Percentage, 0, 100);
}

public sealed class DiscoveredDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public DeviceKind Kind { get; init; }
    public TransportKind Transport { get; init; }
    public required string ProviderId { get; init; }
    public required string ProviderName { get; init; }
    public bool SupportsBattery { get; init; }
    public string? Detail { get; init; }
    public bool IsSystem { get; init; }
    public object? Tag { get; init; }
}

public sealed class DeviceState : ObservableObject
{
    private string _name = string.Empty;
    private DeviceKind _kind;
    private TransportKind _transport;
    private bool _supportsBattery;
    private string? _detail;
    private bool _isOnline = true;
    private int? _batteryPercent;
    private bool? _isCharging;
    private DateTimeOffset? _lastUpdated;
    private BatterySourceKind _source;
    private bool _isCached;
    private string? _error;
    private bool _isDemo;

    public required string Id { get; init; }
    public required string ProviderId { get; init; }
    public required string ProviderName { get; init; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public DeviceKind Kind
    {
        get => _kind;
        set
        {
            if (SetProperty(ref _kind, value))
            {
                OnPropertiesChanged(nameof(KindText), nameof(KindGlyph), nameof(IsSystemBattery));
            }
        }
    }

    public TransportKind Transport
    {
        get => _transport;
        set
        {
            if (SetProperty(ref _transport, value))
            {
                OnPropertyChanged(nameof(TransportText));
            }
        }
    }

    public bool SupportsBattery
    {
        get => _supportsBattery;
        set
        {
            if (SetProperty(ref _supportsBattery, value))
            {
                OnPropertiesChanged(nameof(BatteryText), nameof(HasBattery));
            }
        }
    }

    public string? Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (SetProperty(ref _isOnline, value))
            {
                OnPropertiesChanged(nameof(OnlineText), nameof(IsDisconnected));
            }
        }
    }

    public int? BatteryPercent
    {
        get => _batteryPercent;
        set
        {
            if (SetProperty(ref _batteryPercent, value))
            {
                OnPropertiesChanged(nameof(BatteryText), nameof(HasBattery), nameof(RingValue));
            }
        }
    }

    public bool? IsCharging
    {
        get => _isCharging;
        set
        {
            if (SetProperty(ref _isCharging, value))
            {
                OnPropertiesChanged(nameof(ChargingText), nameof(IsChargingNow));
            }
        }
    }

    public DateTimeOffset? LastUpdated
    {
        get => _lastUpdated;
        set
        {
            if (SetProperty(ref _lastUpdated, value))
            {
                OnPropertiesChanged(nameof(LastUpdatedText), nameof(FreshnessText));
            }
        }
    }

    public BatterySourceKind Source
    {
        get => _source;
        set
        {
            if (SetProperty(ref _source, value))
            {
                OnPropertyChanged(nameof(SourceText));
            }
        }
    }

    public bool IsCached
    {
        get => _isCached;
        set
        {
            if (SetProperty(ref _isCached, value))
            {
                OnPropertyChanged(nameof(SourceText));
            }
        }
    }

    public string? Error
    {
        get => _error;
        set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertiesChanged(nameof(HasError), nameof(ErrorText));
            }
        }
    }

    public bool IsDemo
    {
        get => _isDemo;
        set
        {
            if (SetProperty(ref _isDemo, value))
            {
                OnPropertyChanged(nameof(SourceText));
            }
        }
    }

    public bool IsSystemBattery => Kind == DeviceKind.SystemBattery;
    public bool HasBattery => BatteryPercent is not null;
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool IsDisconnected => !IsOnline;
    public bool IsChargingNow => IsCharging == true;
    public double RingValue => BatteryPercent ?? 0;

    public string BatteryText => SupportsBattery
        ? BatteryPercent is { } value ? $"{value}%" : "尚未读取"
        : "不支持读取";

    public string ChargingText => IsCharging switch
    {
        true => "正在充电",
        false => "使用电池",
        _ => "状态未知"
    };

    public string OnlineText => IsOnline ? "在线" : "未连接";

    public string KindText => Kind switch
    {
        DeviceKind.Mouse => "鼠标",
        DeviceKind.Keyboard => "键盘",
        DeviceKind.KeyboardMouse => "键鼠",
        DeviceKind.SystemBattery => "笔记本电池",
        _ => "设备"
    };

    public string TransportText => Transport switch
    {
        TransportKind.UsbReceiver => "2.4G 接收器",
        TransportKind.Bluetooth => "蓝牙",
        TransportKind.Usb => "USB",
        TransportKind.BuiltIn => "内置电池",
        TransportKind.Demo => "演示数据",
        _ => "未知连接"
    };

    public string SourceText
    {
        get
        {
            if (IsDemo) return "演示数据";
            if (IsCached) return "本地缓存";
            return Source switch
            {
                BatterySourceKind.WindowsCache => "Windows 缓存",
                BatterySourceKind.LogitechHidpp => "Logitech HID++",
                BatterySourceKind.HidFeature => "HID 电量特性",
                BatterySourceKind.DareuNordic => "DAREU Nordic 2.4G",
                BatterySourceKind.DareuDriver => "DAREU 官方驱动",
                BatterySourceKind.DareuEk75 => "DAREU EK75 HID",
                BatterySourceKind.System => "系统电源",
                BatterySourceKind.Demo => "演示数据",
                _ => "尚未读取"
            };
        }
    }

    public string LastUpdatedText => LastUpdated is { } time
        ? time.ToLocalTime().ToString("HH:mm:ss")
        : "—";

    public string FreshnessText
    {
        get
        {
            if (LastUpdated is not { } time) return "等待首次读取";
            var span = DateTimeOffset.Now - time;
            if (span.TotalSeconds < 45) return "刚刚更新";
            if (span.TotalMinutes < 60) return $"{Math.Max(1, (int)span.TotalMinutes)} 分钟前";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} 小时前";
            return $"{Math.Max(1, (int)span.TotalDays)} 天前";
        }
    }

    public string ErrorText => string.IsNullOrWhiteSpace(Error) ? string.Empty : Error;

    public string KindGlyph => Kind switch
    {
        DeviceKind.Mouse => "\uE962",
        DeviceKind.Keyboard => "\uE765",
        DeviceKind.KeyboardMouse => "\uE7F4",
        DeviceKind.SystemBattery => "\uE83F",
        _ => "\uE7F4"
    };

    public void Apply(DiscoveredDevice discovered, bool isDemoSession)
    {
        Name = discovered.Name;
        Kind = discovered.Kind;
        Transport = discovered.Transport;
        SupportsBattery = discovered.SupportsBattery;
        Detail = discovered.Detail;
        IsDemo = discovered.Transport == TransportKind.Demo;
    }

    public void Apply(BatteryReading reading)
    {
        BatteryPercent = reading.ClampedPercentage;
        IsCharging = reading.IsCharging;
        LastUpdated = reading.Timestamp;
        Source = reading.Source;
        IsCached = reading.IsCached;
        Error = null;
        IsOnline = true;
    }
}

public sealed record ReadContext(
    bool UserActive,
    TimeSpan IdleTime,
    bool Force,
    bool AllowActiveRead,
    DateTimeOffset Now)
{
    public bool CanActivelyQuery => Force || (AllowActiveRead && UserActive && IdleTime <= TimeSpan.FromSeconds(75));
}
