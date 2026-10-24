using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using HaloVolt.Infrastructure;
using HaloVolt.Models;

namespace HaloVolt.ViewModels;

public sealed record RoleOption(DeviceRole Value, string Label)
{
    public override string ToString() => Label;
}

public sealed class DeviceViewModel : ObservableObject, IDisposable
{
    private static readonly RoleOption[] Options =
    [
        new(DeviceRole.Auto, "自动识别"),
        new(DeviceRole.Mouse, "作为鼠标"),
        new(DeviceRole.Keyboard, "作为键盘"),
        new(DeviceRole.Ignore, "忽略此设备")
    ];

    private readonly DeviceState _state;
    private readonly DevicePreference _preference;
    private readonly AppSettings _settings;
    private readonly Action _preferenceChanged;

    public DeviceViewModel(DeviceState state, DevicePreference preference, AppSettings settings, Action preferenceChanged)
    {
        _state = state;
        _preference = preference;
        _settings = settings;
        _preferenceChanged = preferenceChanged;
        _state.PropertyChanged += OnStateChanged;
        _preference.PropertyChanged += OnPreferenceChanged;
        SetMouseRingCommand = new RelayCommand(() => SetRingSelection(true, false));
        SetKeyboardRingCommand = new RelayCommand(() => SetRingSelection(false, true));
        ClearRingCommand = new RelayCommand(() => SetRingSelection(false, false));
    }

    public ICommand SetMouseRingCommand { get; }
    public ICommand SetKeyboardRingCommand { get; }
    public ICommand ClearRingCommand { get; }

    public DeviceState State => _state;
    public string Id => _state.Id;
    public string Name => string.IsNullOrWhiteSpace(_preference.CustomName) ? _state.Name : _preference.CustomName!;
    public string RawName => _state.Name;
    public DeviceKind Kind => _state.Kind;
    public bool SupportsBattery => _state.SupportsBattery;
    public bool IsOnline => _state.IsOnline;
    public bool IsSystem => _state.IsSystemBattery;
    public bool IsCharging => _state.IsCharging == true;
    public int? BatteryPercent => _state.BatteryPercent;
    public double RingValue => _state.BatteryPercent ?? 0;
    public string BatteryText => _state.BatteryText;
    public string KindText => _state.KindText;
    public string TransportText => _state.TransportText;
    public string DetailText => string.IsNullOrWhiteSpace(_state.Detail) ? _state.TransportText : _state.Detail!;
    public string LastUpdatedText => _state.LastUpdatedText;
    public string FreshnessText => _state.FreshnessText;
    public string SourceText => _state.SourceText;
    public string ProviderName => _state.ProviderName;
    public string ChargingText => _state.ChargingText;
    public bool HasError => _state.HasError;
    public string ErrorText => _state.ErrorText;
    public string KindGlyph => _state.KindGlyph;
    public bool HasBattery => _state.HasBattery;
    public bool IsLow => _state.BatteryPercent is { } value && value <= _settings.LowThreshold;
    public bool IsCritical => _state.BatteryPercent is { } value && value <= _settings.CriticalThreshold;
    public bool CanBeRing => !IsSystem && SupportsBattery && Monitored && EffectiveRole is DeviceRole.Mouse or DeviceRole.Keyboard;
    public bool CanSetMouseRing => !IsSystem && SupportsBattery && (Kind == DeviceKind.KeyboardMouse || EffectiveRole == DeviceRole.Mouse);
    public bool CanSetKeyboardRing => !IsSystem && SupportsBattery && (Kind == DeviceKind.KeyboardMouse || EffectiveRole == DeviceRole.Keyboard);
    public bool IsSelectedRingMouse => _settings.MouseRingDeviceId == Id;
    public bool IsSelectedRingKeyboard => _settings.KeyboardRingDeviceId == Id;
    public bool IsRingSelected => IsSelectedRingMouse || IsSelectedRingKeyboard;
    public bool CanCustomize => !IsSystem;
    public bool NeedsRoleChoice => Kind == DeviceKind.KeyboardMouse && Role == DeviceRole.Auto;
    public string RoleHint => NeedsRoleChoice ? "接收器同时提供键盘/鼠标集合，请按实际设备选择用途" : string.Empty;
    public IReadOnlyList<RoleOption> RoleOptions => Options;

    public bool Monitored
    {
        get => _preference.Monitored;
        set
        {
            if (value && !SupportsBattery)
            {
                return;
            }

            if (_preference.Monitored == value)
            {
                return;
            }

            _preference.Monitored = value;
            _preference.UserConfigured = true;
            if (!value)
            {
                ClearRingSelection();
            }

            RaiseView();
            _preferenceChanged();
        }
    }

    public DeviceRole Role
    {
        get => _preference.Role;
        set
        {
            if (_preference.Role == value)
            {
                return;
            }

            _preference.Role = value;
            _preference.UserConfigured = true;
            if (value == DeviceRole.Ignore)
            {
                _preference.Monitored = false;
                ClearRingSelection();
            }
            else if (value is DeviceRole.Mouse or DeviceRole.Keyboard)
            {
                _preference.Monitored = SupportsBattery;
            }

            RaiseView();
            _preferenceChanged();
        }
    }

    public DeviceRole EffectiveRole => _preference.Role != DeviceRole.Auto
        ? _preference.Role
        : _state.Kind switch
        {
            DeviceKind.Mouse => DeviceRole.Mouse,
            DeviceKind.Keyboard => DeviceRole.Keyboard,
            DeviceKind.KeyboardMouse => DeviceRole.Mouse,
            _ => DeviceRole.Ignore
        };

    public string RingSelectionText
    {
        get
        {
            if (IsSelectedRingMouse && IsSelectedRingKeyboard) return "圆环：鼠标 + 键盘";
            if (IsSelectedRingMouse) return "圆环：鼠标";
            if (IsSelectedRingKeyboard) return "圆环：键盘";
            return string.Empty;
        }
    }

    public string MonitoredText => !SupportsBattery ? "暂无可用电量通道" : Monitored ? "正在监控" : "未监控";

    public Brush AccentBrush
    {
        get
        {
            if (IsCritical) return (Brush)System.Windows.Application.Current.Resources["DangerBrush"];
            return (Brush)System.Windows.Application.Current.Resources["AccentBrush"];
        }
    }

    public Brush DeviceAccentBrush
    {
        get
        {
            if (IsCritical) return (Brush)System.Windows.Application.Current.Resources["DangerBrush"];
            var hex = EffectiveRole == DeviceRole.Keyboard ? _settings.KeyboardColor : _settings.MouseColor;
            return UiBrushes.FromHex(hex, System.Windows.Media.Color.FromRgb(0x4C, 0xC2, 0xFF));
        }
    }

    public string SupportBadgeText => SupportsBattery ? "支持电量" : "无电量接口";
    public string RingMouseButtonText => IsSelectedRingMouse ? "✓ 鼠标圆环" : "设为鼠标圆环";
    public string RingKeyboardButtonText => IsSelectedRingKeyboard ? "✓ 键盘圆环" : "设为键盘圆环";
    public Brush BatteryForeground => SupportsBattery
        ? DeviceAccentBrush
        : (Brush)System.Windows.Application.Current.Resources["TextTertiaryBrush"];

    public string BatteryDescription => SupportsBattery
        ? BatteryPercent is { } value ? $"{value}% · {ChargingText}" : "等待读取"
        : "此设备未暴露标准电量接口";

    public event EventHandler? Changed;

    public void SetRingSelection(bool mouse, bool keyboard)
    {
        if (mouse)
        {
            _settings.MouseRingDeviceId = Id;
        }
        else if (_settings.MouseRingDeviceId == Id)
        {
            _settings.MouseRingDeviceId = null;
        }

        if (keyboard)
        {
            _settings.KeyboardRingDeviceId = Id;
        }
        else if (_settings.KeyboardRingDeviceId == Id)
        {
            _settings.KeyboardRingDeviceId = null;
        }

        RaiseView();
        _preferenceChanged();
    }

    public void ClearRingSelection()
    {
        if (_settings.MouseRingDeviceId == Id) _settings.MouseRingDeviceId = null;
        if (_settings.KeyboardRingDeviceId == Id) _settings.KeyboardRingDeviceId = null;
        RaiseView();
        _preferenceChanged();
    }

    public void RefreshProperties() => RaiseView();

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => RaiseView();

    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs e) => RaiseView();

    private void RaiseView()
    {
        OnPropertyChanged(string.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _state.PropertyChanged -= OnStateChanged;
        _preference.PropertyChanged -= OnPreferenceChanged;
    }
}
