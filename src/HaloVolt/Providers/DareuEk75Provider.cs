using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using HaloVolt.Infrastructure;
using HaloVolt.Models;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;

namespace HaloVolt.Providers;

internal sealed record Ek75Target(string Model, string? HidPath);

/// <summary>
/// DAREU EK75 support. The keyboard's public HID interface has no standard
/// battery service, but its 65-byte feature report implements the DAREU
/// "Power Management / Battery Status" command. When the official driver is
/// running we read its displayed value; when it is closed we query the HID
/// feature channel directly. Both paths are passive with respect to the
/// keyboard's normal input reports.
/// </summary>
internal sealed class DareuEk75Provider : IBatteryProvider
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly object HidGate = new();

    public string Id => "dareu-ek75";
    public string DisplayName => "DAREU EK75";

    public async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(ProviderContext context, CancellationToken cancellationToken)
    {
        if (!context.Settings.EnableDareuDriverBridge)
        {
            return [];
        }

        var hidPath = await Task.Run(FindEk75HidPath, cancellationToken).ConfigureAwait(false);
        var driver = await TryProbeDriverAsync(cancellationToken).ConfigureAwait(false);
        if (hidPath is null && driver is null)
        {
            return [];
        }

        var model = driver?.Target.Model ?? "EK75";
        var mode = driver?.Mode ?? "2.4G无线";
        AppLog.Debug($"DAREU EK75: driver={(driver is null ? "no" : driver.Percent + "%")} hid={(hidPath is null ? "no" : "yes")} mode={mode}");
        return
        [
            new DiscoveredDevice
            {
                Id = "dareu-ek75",
                Name = "DAREU " + model,
                Kind = DeviceKind.Keyboard,
                Transport = TransportKind.UsbReceiver,
                ProviderId = Id,
                ProviderName = DisplayName,
                SupportsBattery = true,
                Detail = driver is not null
                    ? $"官方驱动读取 · 关闭驱动后自动直读 · {mode}"
                    : $"直接 HID 查询 · {mode} · 无需官方驱动常驻",
                Tag = new Ek75Target(model, hidPath)
            }
        ];
    }

    public async Task<BatteryReading?> ReadAsync(DiscoveredDevice device, ReadContext context, CancellationToken cancellationToken)
    {
        if (device.Tag is not Ek75Target target)
        {
            return null;
        }

        var driver = await TryProbeDriverAsync(cancellationToken).ConfigureAwait(false);
        if (driver is not null && string.Equals(driver.Target.Model, target.Model, StringComparison.OrdinalIgnoreCase))
        {
            AppLog.Debug($"DAREU EK75 官方驱动读取 {target.Model}: {driver.Percent}%");
            return Reading(driver.Percent, BatterySourceKind.DareuDriver, "DAREU Driver");
        }

        // The 65-byte feature channel is shared with the running vendor driver.
        // Reading it concurrently produces inconsistent values, so direct HID is
        // used only after the driver process is gone.
        if (IsDriverRunning())
        {
            return null;
        }

        var path = target.HidPath ?? await Task.Run(FindEk75HidPath, cancellationToken).ConfigureAwait(false);
        if (path is null)
        {
            return null;
        }

        var percent = await Task.Run(() => ReadHid(path), cancellationToken).ConfigureAwait(false);
        if (percent is null)
        {
            return null;
        }

        AppLog.Debug($"DAREU EK75 HID 读取: {percent}%");
        return Reading(percent.Value, BatterySourceKind.DareuEk75, "DAREU EK75 HID");
    }

    private static BatteryReading Reading(int percent, BatterySourceKind source, string note) =>
        new(percent, null, DateTimeOffset.Now, source, IsCached: false, Note: note);

    private static bool IsDriverRunning()
    {
        var processes = Process.GetProcessesByName("DareuDriver");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static async Task<DriverInfo?> TryProbeDriverAsync(CancellationToken cancellationToken)
    {
        if (!IsDriverRunning())
        {
            return null;
        }

        try
        {
            return await Task.Run(ProbeDriver, cancellationToken)
                .WaitAsync(ProbeTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Debug("DAREU EK75 驱动探测失败：" + ex.Message);
            return null;
        }
    }

    private static DriverInfo? ProbeDriver()
    {
        var processes = Process.GetProcessesByName("DareuDriver");
        try
        {
            foreach (var process in processes)
            {
                var condition = new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id);
                var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, condition);
                foreach (AutomationElement window in windows)
                {
                    if (!window.Current.Name.Contains("DAREU", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var modelElement = ById(window, "tbMode");
                    var batteryElement = ById(window, "pgValue");
                    if (modelElement is null || batteryElement is null)
                    {
                        continue;
                    }

                    var model = modelElement.Current.Name?.Trim();
                    if (string.IsNullOrWhiteSpace(model) || !model.StartsWith("EK", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (batteryElement.TryGetCurrentPattern(RangeValuePattern.Pattern, out var patternObj)
                        && patternObj is RangeValuePattern range)
                    {
                        var value = range.Current.Value;
                        if (value is >= 0 and <= 100)
                        {
                            var mode = ById(window, "tbProductMode")?.Current.Name?.Trim() ?? "DAREU Driver";
                            return new DriverInfo(new Ek75Target(model, null), (int)Math.Round(value), mode);
                        }
                    }
                }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }

        return null;
    }

    private static AutomationElement? ById(AutomationElement root, string automationId)
    {
        try
        {
            var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);
            return root.FindFirst(TreeScope.Descendants, condition);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> FindEk75HidPath()
    {
        try
        {
            var selector = HidDevice.GetDeviceSelector(0x0C, 0x01);
            var devices = await DeviceInformation.FindAllAsync(selector);
            foreach (var device in devices)
            {
                if (!device.Id.Contains("VID_260D&PID_0042", StringComparison.OrdinalIgnoreCase)
                    || !device.Id.Contains("MI_03", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (HasFeatureReport(device.Id, 65))
                {
                    return device.Id;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug("DAREU EK75 HID 枚举失败：" + ex.Message);
        }

        return null;
    }

    private static bool HasFeatureReport(string path, int expectedLength)
    {
        try
        {
            using var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out var preparsed) || preparsed == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return HidP_GetCaps(preparsed, out var caps) >= 0 && caps.FeatureReportByteLength == expectedLength;
            }
            finally
            {
                HidD_FreePreparsedData(preparsed);
            }
        }
        catch
        {
            return false;
        }
    }

    private static int? ReadHid(string path)
    {
        lock (HidGate)
        {
            try
            {
                using var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    return null;
                }

                var request = new byte[65];
                request[1] = 0x10; // HID command header
                request[2] = 0x04;
                request[3] = 0x07; // Power Management
                request[4] = 0x80; // GET
                if (!HidD_SetFeature(handle, request, request.Length))
                {
                    return null;
                }

                for (var attempt = 0; attempt < 20; attempt++)
                {
                    Thread.Sleep(20);
                    var response = new byte[65];
                    if (!HidD_GetFeature(handle, response, response.Length))
                    {
                        continue;
                    }

                    // Status nibble 2 means "command complete".
                    if ((response[1] & 0x0F) != 2)
                    {
                        continue;
                    }

                    var current = response[8];
                    var max = response[9];
                    var percent = max is > 0 and <= 100
                        ? (int)Math.Round(current * 100.0 / max)
                        : current;
                    if (percent is >= 0 and <= 100)
                    {
                        return percent;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Debug("DAREU EK75 HID 读取失败：" + ex.Message);
            }

            return null;
        }
    }

    private sealed record DriverInfo(Ek75Target Target, int Percent, string Mode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sec, uint create, uint flags, IntPtr template);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr data);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] buffer, int length);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetFeature(SafeFileHandle handle, [Out] byte[] buffer, int length);

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }
}
