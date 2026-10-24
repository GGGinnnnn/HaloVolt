using System.Collections.Concurrent;
using Microsoft.Win32.SafeHandles;
using HaloVolt.Infrastructure;
using static HaloVolt.Providers.Hid.HidNative;

namespace HaloVolt.Providers.Hid;

internal sealed class HidppConnection : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly ConcurrentQueue<byte[]> _reports = new();
    private readonly AutoResetEvent _reportReady = new(false);
    private readonly Thread _readThread;
    private readonly object _queryLock = new();
    private volatile bool _running = true;

    private HidppConnection(SafeFileHandle handle)
    {
        _handle = handle;
        _readThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "HaloVolt HID++ reader",
            Priority = ThreadPriority.BelowNormal
        };
        _readThread.Start();
    }

    public static HidppConnection? Open(string path)
    {
        try
        {
            var handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                return null;
            }

            return new HidppConnection(handle);
        }
        catch (Exception ex)
        {
            AppLog.Warn("打开 Logitech HID++ 通道失败：" + ex.Message);
            return null;
        }
    }

    public byte[]? Query(byte[] request, Func<byte[], bool> predicate, int timeoutMs = 220)
    {
        if (!_running)
        {
            return null;
        }

        lock (_queryLock)
        {
            while (_reports.TryDequeue(out _))
            {
                // Drop stale responses/notifications before a new transaction.
            }

            if (!HidNative.WriteFile(_handle, request, request.Length, out _, IntPtr.Zero))
            {
                return null;
            }

            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline && _running)
            {
                var remaining = (int)Math.Max(1, deadline - Environment.TickCount64);
                if (!_reportReady.WaitOne(remaining))
                {
                    break;
                }

                while (_reports.TryDequeue(out var report))
                {
                    if (report.Length >= 7 && predicate(report))
                    {
                        return report;
                    }
                }
            }

            return null;
        }
    }

    public byte[]? Query2(byte deviceIndex, byte featureIndex, byte functionAndSwId, byte p0, byte p1, byte p2, int timeoutMs = 220)
    {
        var request = new byte[] { 0x10, deviceIndex, featureIndex, functionAndSwId, p0, p1, p2 };
        return Query(
            request,
            report => report[0] == 0x10
                      && report[1] == deviceIndex
                      && report[2] == featureIndex
                      && (report[3] & 0x0F) == (functionAndSwId & 0x0F),
            timeoutMs);
    }

    private void ReadLoop()
    {
        var buffer = new byte[64];
        try
        {
            while (_running)
            {
                if (!HidNative.ReadFile(_handle, buffer, buffer.Length, out var read, IntPtr.Zero))
                {
                    break;
                }

                if (read <= 0)
                {
                    continue;
                }

                var report = new byte[read];
                Buffer.BlockCopy(buffer, 0, report, 0, read);
                _reports.Enqueue(report);
                _reportReady.Set();
            }
        }
        catch
        {
            // Closing the handle is the normal shutdown path.
        }
        finally
        {
            _running = false;
            _reportReady.Set();
        }
    }

    public void Dispose()
    {
        _running = false;
        try
        {
            CancelIoEx(_handle, IntPtr.Zero);
        }
        catch
        {
            // ignored
        }

        _handle.Dispose();
        _reportReady.Set();
        if (_readThread.IsAlive)
        {
            _readThread.Join(300);
        }

        _reportReady.Dispose();
    }
}
