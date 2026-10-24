using System.Runtime.InteropServices;
using HaloVolt.Infrastructure;
using Microsoft.Win32.SafeHandles;

namespace HaloVolt.Providers.Hid;

internal sealed record Nordic52Target(string Path, string DisplayName, ushort VendorId, ushort ProductId);

/// <summary>
/// Compx / Nordic 52840 wireless mice (DAREU A950 Air / Wing, ATK, VXE, ...).
/// The battery collection is usage page 0xFF02 / usage 0x02. One 17-byte interrupt
/// transaction returns the percentage at byte 6.
///
/// Reference protocol description: Fan4Metal/mouse_tray (Nordic52 driver).
/// This is a clean-room implementation of the wire format.
/// </summary>
internal static class Nordic52Battery
{
    private const ushort VendorId = 0x260D;
    private const ushort UsagePage = 0xFF02;
    private const ushort Usage = 0x0002;
    private const int ReportLength = 17;
    private const int ReadTimeoutMs = 1100;
    private const uint ErrorIoPending = 997;

    public static bool TryCreateTarget(HidPhysicalGroup group, out Nordic52Target target)
    {
        target = null!;
        if (group.VendorId != VendorId)
        {
            return false;
        }

        var name = group.ProductId switch
        {
            0x1175 => "DAREU 无线鼠标",
            0x1193 => "DAREU 无线鼠标（有线）",
            0x1154 => "DAREU 无线鼠标 Wing",
            0x1196 => "DAREU 无线鼠标 Wing（有线）",
            _ => null
        };

        if (name is null)
        {
            return false;
        }

        var path = group.FindInterface(UsagePage, Usage);
        if (path is null)
        {
            return false;
        }

        target = new Nordic52Target(path, name, group.VendorId, group.ProductId);
        return true;
    }

    public static int? ReadPercentage(Nordic52Target target)
    {
        SafeFileHandle? handle = null;
        try
        {
            handle = CreateFile(
                target.Path,
                GenericRead | GenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                FileFlagOverlapped,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                AppLog.Debug($"Nordic52 无法打开 HID 通道 {target.DisplayName}: {Marshal.GetLastWin32Error()}");
                handle.Dispose();
                return null;
            }

            var request = new byte[ReportLength];
            request[0] = 0x08;
            request[1] = 0x04;
            request[16] = 0x49;
            AppLog.Debug($"Nordic52 TX {target.DisplayName}: {BitConverter.ToString(request)}");

            if (!WriteWithTimeout(handle, request, 500))
            {
                return null;
            }

            var response = ReadWithTimeout(handle, ReportLength, ReadTimeoutMs);
            if (response is null)
            {
                AppLog.Debug($"Nordic52 无响应：{target.DisplayName}");
                return null;
            }

            AppLog.Debug($"Nordic52 RX {target.DisplayName}: {BitConverter.ToString(response)}");
            if (response.Length < 8 || response[0] != 0x08 || response[1] != 0x04)
            {
                return null;
            }

            var percent = response[6];
            if (percent > 100)
            {
                return null;
            }

            return percent;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Nordic52 读取失败 {target.DisplayName}: {ex.Message}");
            return null;
        }
        finally
        {
            handle?.Dispose();
        }
    }

    private static bool WriteWithTimeout(SafeFileHandle handle, byte[] data, int timeoutMs)
    {
        using var completed = new ManualResetEvent(false);
        var buffer = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var overlapped = new NativeOverlapped
            {
                EventHandle = completed.SafeWaitHandle.DangerousGetHandle()
            };

            var ok = WriteFile(handle, data, data.Length, out var written, ref overlapped);
            if (!ok)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ErrorIoPending)
                {
                    AppLog.Debug("Nordic52 WriteFile failed: " + error);
                    return false;
                }

                if (!completed.WaitOne(timeoutMs) || !GetOverlappedResult(handle, ref overlapped, out written, false))
                {
                    CancelIoEx(handle, ref overlapped);
                    return false;
                }
            }

            return written == data.Length;
        }
        finally
        {
            buffer.Free();
        }
    }

    private static byte[]? ReadWithTimeout(SafeFileHandle handle, int length, int timeoutMs)
    {
        using var completed = new ManualResetEvent(false);
        var buffer = new byte[Math.Max(64, length)];
        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var overlapped = new NativeOverlapped
            {
                EventHandle = completed.SafeWaitHandle.DangerousGetHandle()
            };

            var ok = ReadFile(handle, buffer, buffer.Length, out var read, ref overlapped);
            if (!ok)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ErrorIoPending)
                {
                    AppLog.Debug("Nordic52 ReadFile failed: " + error);
                    return null;
                }

                if (!completed.WaitOne(timeoutMs))
                {
                    CancelIoEx(handle, ref overlapped);
                    return null;
                }

                if (!GetOverlappedResult(handle, ref overlapped, out read, false))
                {
                    return null;
                }
            }

            if (read <= 0)
            {
                return null;
            }

            var result = new byte[read];
            Buffer.BlockCopy(buffer, 0, result, 0, read);
            return result;
        }
        finally
        {
            pinned.Free();
        }
    }

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool WriteFile(
        SafeFileHandle file,
        byte[] buffer,
        int numberOfBytesToWrite,
        out int numberOfBytesWritten,
        ref NativeOverlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool ReadFile(
        SafeFileHandle file,
        [Out] byte[] buffer,
        int numberOfBytesToRead,
        out int numberOfBytesRead,
        ref NativeOverlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool GetOverlappedResult(
        SafeFileHandle file,
        ref NativeOverlapped overlapped,
        out int numberOfBytesTransferred,
        [MarshalAs(UnmanagedType.U1)] bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CancelIoEx(SafeFileHandle file, ref NativeOverlapped overlapped);
}
