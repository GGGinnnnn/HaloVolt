using System.Threading;

namespace HaloVolt.Services;

/// <summary>
/// HKCU Run plus a named mutex. The second instance pings the first one to show settings
/// and then exits, so users never end up with two tray icons.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\HaloVolt.SingleInstance.7F0D";
    private const string ShowEventName = @"Local\HaloVolt.ShowSettings.7F0D";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private Thread? _listener;
    private volatile bool _running;

    public bool IsFirstInstance { get; private set; }

    public bool TryAcquire(Action showSettings)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;

        if (!createdNew)
        {
            SignalFirstInstance();
            return false;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _running = true;
        _listener = new Thread(() =>
        {
            while (_running)
            {
                try
                {
                    if (_showEvent.WaitOne(TimeSpan.FromMilliseconds(400)))
                    {
                        showSettings();
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    // A broken signal must not kill the tray app.
                }
            }
        })
        {
            IsBackground = true,
            Name = "HaloVolt single-instance listener",
            Priority = ThreadPriority.BelowNormal
        };
        _listener.Start();
        return true;
    }

    private static void SignalFirstInstance()
    {
        for (var attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                using var evt = EventWaitHandle.OpenExisting(ShowEventName);
                evt.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(80);
            }
            catch
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        try
        {
            _showEvent?.Set();
        }
        catch
        {
            // ignored
        }

        if (_listener is { IsAlive: true })
        {
            _listener.Join(500);
        }

        _showEvent?.Dispose();
        _showEvent = null;

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // ignored
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }
}
