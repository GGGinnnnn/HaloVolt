using System.Security.Principal;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using HaloVolt.Infrastructure;
using HaloVolt.Models;
using HaloVolt.Providers.Hid;

namespace HaloVolt.Services;

public sealed record HidDiagnosticGroup(
    string Name,
    string VidPid,
    string Container,
    string Usages,
    string Support,
    string Detail,
    bool Supported);

public sealed record BleDiagnosticDevice(
    string Name,
    string Address,
    string Connection,
    string Appearance,
    string BatteryService,
    bool HasBattery);

public sealed record DeviceDiagnosticRow(
    string Name,
    string Kind,
    string Provider,
    string Support,
    string Battery,
    string Source,
    string Online,
    string Detail,
    string Error);

public sealed class DiagnosticsSnapshot
{
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.Now;
    public string Environment { get; init; } = string.Empty;
    public string LogTail { get; init; } = string.Empty;
    public IReadOnlyList<HidDiagnosticGroup> HidGroups { get; init; } = [];
    public IReadOnlyList<BleDiagnosticDevice> BleDevices { get; init; } = [];
    public IReadOnlyList<DeviceDiagnosticRow> Devices { get; init; } = [];
    public string Summary { get; init; } = string.Empty;

    public string ToReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("HaloVolt diagnostic report");
        sb.AppendLine("Generated: " + CollectedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        sb.AppendLine();
        sb.AppendLine("== 环境 ==");
        sb.AppendLine(Environment);
        sb.AppendLine();
        sb.AppendLine("== 设备解析 ==");
        foreach (var d in Devices)
        {
            sb.AppendLine($"{d.Name} | {d.Kind} | {d.Provider} | support={d.Support} | battery={d.Battery} | source={d.Source} | online={d.Online}");
            if (!string.IsNullOrWhiteSpace(d.Detail)) sb.AppendLine("  detail: " + d.Detail);
            if (!string.IsNullOrWhiteSpace(d.Error)) sb.AppendLine("  error: " + d.Error);
        }

        sb.AppendLine();
        sb.AppendLine("== 原始 HID 通道 ==");
        foreach (var g in HidGroups)
        {
            sb.AppendLine($"{g.Name} | {g.VidPid} | container={g.Container} | support={g.Support}");
            sb.AppendLine("  usages: " + g.Usages);
            if (!string.IsNullOrWhiteSpace(g.Detail)) sb.AppendLine("  detail: " + g.Detail);
        }

        sb.AppendLine();
        sb.AppendLine("== 已配对 BLE 设备 ==");
        if (BleDevices.Count == 0) sb.AppendLine("(none)");
        foreach (var b in BleDevices)
        {
            sb.AppendLine($"{b.Name} | {b.Address} | {b.Connection} | appearance={b.Appearance} | batteryService={b.BatteryService}");
        }

        sb.AppendLine();
        sb.AppendLine("== 最近日志 ==");
        sb.AppendLine(LogTail);
        return sb.ToString();
    }
}

public sealed class DiagnosticsService
{
    public async Task<DiagnosticsSnapshot> CollectAsync(
        AppSettings settings,
        IReadOnlyCollection<DeviceState> states,
        CancellationToken cancellationToken = default)
    {
        var groups = await CollectHidAsync(settings, cancellationToken).ConfigureAwait(false);
        var ble = await CollectBleAsync(cancellationToken).ConfigureAwait(false);
        var devices = states
            .OrderBy(s => s.IsSystemBattery ? 1 : 0)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(state => new DeviceDiagnosticRow(
                state.Name,
                state.KindText,
                state.ProviderName,
                state.SupportsBattery ? "是" : "否",
                state.BatteryPercent is { } p ? p + "%" : "—",
                state.SourceText,
                state.IsOnline ? "在线" : "离线",
                state.Detail ?? string.Empty,
                state.Error ?? string.Empty))
            .ToList();

        var supported = groups.Count(g => g.Supported);
        var summary = $"发现 {groups.Count} 个 HID 物理设备，其中 {supported} 个具备可识别的电量通道；已配对 BLE {ble.Count} 个；当前解析外设 {devices.Count} 个。";

        return new DiagnosticsSnapshot
        {
            Environment = BuildEnvironment(settings),
            LogTail = ReadLogTail(),
            HidGroups = groups,
            BleDevices = ble,
            Devices = devices,
            Summary = summary
        };
    }

    private static async Task<List<HidDiagnosticGroup>> CollectHidAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var result = new List<HidDiagnosticGroup>();
        try
        {
            var catalog = new HidDeviceCatalog();
            var groups = await catalog.GetAsync(force: true, cancellationToken).ConfigureAwait(false);
            foreach (var group in groups)
            {
                var usages = string.Join("  ", group.Interfaces
                    .Select(i => $"{i.UsagePage:X4}:{i.UsageId:X4}")
                    .Distinct());
                var detail = string.Join(Environment.NewLine, group.Interfaces.Select(i => $"  {i.UsagePage:X4}:{i.UsageId:X4}  {i.Path}"));
                var support = "未发现标准电量通道";
                var supported = false;

                if (Nordic52Battery.TryCreateTarget(group, out var nordic))
                {
                    support = "支持 · DAREU Nordic 2.4G";
                    supported = true;
                    detail = $"  battery path: {nordic.Path}" + Environment.NewLine + detail;
                }
                else if (group.IsLogitech && group.VendorPath is not null)
                {
                    support = settings.EnableLogitechProvider ? "尝试 Logitech HID++" : "Logitech provider 已关闭";
                    supported = settings.EnableLogitechProvider;
                    detail = "  vendor path: " + group.VendorPath + Environment.NewLine + detail;
                }
                else if ((group.HasMouse || group.HasKeyboard) && settings.EnableHidBatteryProvider)
                {
                    HidBatteryTarget? target = null;
                    foreach (var path in group.CandidatePaths)
                    {
                        target = HidBatteryProbe.Probe(path);
                        if (target is not null) break;
                    }

                    if (target is not null)
                    {
                        support = $"支持 · HID Battery Strength（report {target.ReportId}）";
                        supported = true;
                        detail = "  feature path: " + target.Path + Environment.NewLine + detail;
                    }
                }

                result.Add(new HidDiagnosticGroup(
                    group.Name,
                    $"{group.VendorId:X4}:{group.ProductId:X4}",
                    group.ContainerId == Guid.Empty ? "—" : group.ContainerId.ToString("D"),
                    usages,
                    support,
                    detail,
                    supported));
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("诊断：HID 枚举失败 " + ex.Message);
            result.Add(new HidDiagnosticGroup("HID 枚举失败", "—", "—", "—", "错误", ex.ToString(), false));
        }

        return result;
    }

    private static async Task<List<BleDiagnosticDevice>> CollectBleAsync(CancellationToken cancellationToken)
    {
        var result = new List<BleDiagnosticDevice>();
        try
        {
            var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var infos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
            foreach (var info in infos)
            {
                BluetoothLEDevice? device = null;
                try
                {
                    device = await BluetoothLEDevice.FromIdAsync(info.Id).AsTask(cancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                    if (device is null) continue;

                    var hasBattery = false;
                    var serviceState = "不可用";
                    try
                    {
                        var services = await device.GetGattServicesAsync(BluetoothCacheMode.Cached).AsTask(cancellationToken)
                            .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                        if (services.Status == GattCommunicationStatus.Success)
                        {
                            hasBattery = services.Services.Any(s => s.Uuid == new Guid("0000180f-0000-1000-8000-00805f9b34fb"));
                            serviceState = hasBattery ? "有 0x180F Battery Service（缓存）" : "无 Battery Service（缓存）";
                        }
                        else
                        {
                            serviceState = services.Status.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        serviceState = "读取 GATT 失败: " + ex.Message;
                    }

                    var appearance = 0;
                    try { appearance = device.Appearance.RawValue; } catch { }
                    result.Add(new BleDiagnosticDevice(
                        string.IsNullOrWhiteSpace(info.Name) ? device.Name : info.Name,
                        $"0x{device.BluetoothAddress:X12}",
                        device.ConnectionStatus.ToString(),
                        $"0x{appearance:X4}",
                        serviceState,
                        hasBattery));
                }
                catch (Exception ex)
                {
                    result.Add(new BleDiagnosticDevice(info.Name ?? "未知 BLE 设备", "—", "错误", "—", ex.Message, false));
                }
                finally
                {
                    device?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("诊断：BLE 枚举失败 " + ex.Message);
        }

        return result;
    }

    private static string BuildEnvironment(AppSettings settings)
    {
        var isAdmin = false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { }

        var dareuDriverRunning = false;
        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("DareuDriver");
            dareuDriverRunning = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
        }
        catch { }

        return string.Join(Environment.NewLine,
            $"OS: {Environment.OSVersion} ({Environment.OSVersion.Version.Build})",
            $"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} / {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}",
            $"Executable: {AppPaths.ExecutablePath}",
            $"Data: {AppPaths.AppDataDirectory}",
            $"Admin: {(isAdmin ? "yes" : "no")}",
            $"DebugMode: {(settings.DebugMode ? "on" : "off")}",
            $"Providers: BLE={(settings.EnableBleProvider ? "on" : "off")} Logitech={(settings.EnableLogitechProvider ? "on" : "off")} HID={(settings.EnableHidBatteryProvider ? "on" : "off")} EK75={(settings.EnableDareuDriverBridge ? (dareuDriverRunning ? "on/driver-running" : "on/direct-hid") : "off")}",
            $"ActiveRead: {(settings.ActiveReadWhenInUse ? "on" : "off")} / Demo: {(settings.DemoMode ? "on" : "off")}");
    }

    private static string ReadLogTail()
    {
        try
        {
            if (!File.Exists(AppPaths.LogFile)) return "(日志尚未生成)";
            var lines = File.ReadLines(AppPaths.LogFile).TakeLast(120);
            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            return "读取日志失败: " + ex.Message;
        }
    }
}
