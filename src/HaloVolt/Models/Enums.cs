namespace HaloVolt.Models;

public enum DeviceKind
{
    Unknown,
    Mouse,
    Keyboard,
    KeyboardMouse,
    SystemBattery
}

public enum DeviceRole
{
    Auto,
    Mouse,
    Keyboard,
    Ignore
}

public enum TransportKind
{
    Unknown,
    UsbReceiver,
    Bluetooth,
    Usb,
    BuiltIn,
    Demo
}

public enum BatterySourceKind
{
    None,
    WindowsCache,
    LogitechHidpp,
    HidFeature,
    DareuNordic,
    DareuDriver,
    DareuEk75,
    System,
    Demo
}

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public enum StartupMode
{
    Silent,
    OpenSettings
}

public enum CloseAction
{
    MinimizeToTray,
    Exit
}

public enum RingContent
{
    Mouse,
    Keyboard
}

public enum AlertSeverity
{
    None,
    Low,
    Critical
}

public enum PageKey
{
    Overview,
    Devices,
    Alerts,
    Tray,
    General,
    Diagnostics,
    About
}
