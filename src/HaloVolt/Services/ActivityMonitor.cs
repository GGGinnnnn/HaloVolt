using System.Runtime.InteropServices;
using HaloVolt.Infrastructure;

namespace HaloVolt.Services;

public sealed class ActivityMonitor
{
    private readonly TimeSpan _activeWindow;

    public ActivityMonitor(TimeSpan? activeWindow = null)
    {
        _activeWindow = activeWindow ?? TimeSpan.FromSeconds(75);
    }

    public TimeSpan IdleTime
    {
        get
        {
            try
            {
                var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
                if (!GetLastInputInfo(ref info))
                {
                    return TimeSpan.Zero;
                }

                var current = unchecked((uint)Environment.TickCount);
                var elapsed = unchecked(current - info.dwTime);
                return TimeSpan.FromMilliseconds(elapsed);
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }
    }

    public bool IsUserActive => IdleTime <= _activeWindow;

    public bool IsSystemIdleFor(TimeSpan span) => IdleTime >= span;

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
