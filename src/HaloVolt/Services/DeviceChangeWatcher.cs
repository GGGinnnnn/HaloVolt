using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace HaloVolt.Services;

/// <summary>
/// A tiny message-only window for WM_DEVICECHANGE. SystemEvents.DeviceAdded is not
/// available on modern .NET desktop, and polling USB every second would be wasteful.
/// </summary>
public sealed class DeviceChangeWatcher : IDisposable
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;
    private const int DbtDevTypeDeviceInterface = 5;
    private const int DeviceNotifyAllInterfaceClasses = 0x00000004;

    private readonly HwndSource _source;
    private IntPtr _notificationHandle;
    private bool _disposed;

    public DeviceChangeWatcher()
    {
        var parameters = new HwndSourceParameters("HaloVolt.DeviceWatcher")
        {
            Width = 0,
            Height = 0,
            PositionX = -10000,
            PositionY = -10000,
            WindowStyle = unchecked((int)0x80000000)
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        var filter = new DEV_BROADCAST_DEVICEINTERFACE
        {
            Size = Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE>(),
            DeviceType = DbtDevTypeDeviceInterface
        };

        try
        {
            _notificationHandle = RegisterDeviceNotification(
                _source.Handle,
                ref filter,
                DeviceNotifyAllInterfaceClasses);
        }
        catch
        {
            _notificationHandle = IntPtr.Zero;
        }
    }

    public event EventHandler? Changed;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDeviceChange)
        {
            var reason = wParam.ToInt32();
            if (reason is DbtDeviceArrival or DbtDeviceRemoveComplete)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_notificationHandle != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_notificationHandle);
            _notificationHandle = IntPtr.Zero;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1)]
        public string Name;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, ref DEV_BROADCAST_DEVICEINTERFACE notificationFilter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);
}
