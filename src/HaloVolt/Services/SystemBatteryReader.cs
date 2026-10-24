using System.Runtime.InteropServices;
using HaloVolt.Models;

namespace HaloVolt.Services;

public sealed class SystemBatteryReader
{
    public DeviceState? TryRead()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            return null;
        }

        // 128 = no system battery; 255 = unknown.
        if (status.BatteryFlag == 128 || status.BatteryLifePercent == 255)
        {
            return null;
        }

        var charging = (status.BatteryFlag & 8) != 0
            ? true
            : status.ACLineStatus == 1 && status.BatteryLifePercent >= 100
                ? false
                : status.ACLineStatus == 1
                    ? true
                    : false;

        var state = new DeviceState
        {
            Id = "system:battery",
            ProviderId = "system",
            ProviderName = "Windows 系统电源",
            Name = "笔记本电池",
            Kind = DeviceKind.SystemBattery,
            Transport = TransportKind.BuiltIn,
            SupportsBattery = true,
            Detail = status.ACLineStatus == 1 ? "已接通电源" : "使用电池供电",
            IsOnline = true,
            BatteryPercent = status.BatteryLifePercent,
            IsCharging = charging,
            LastUpdated = DateTimeOffset.Now,
            Source = BatterySourceKind.System
        };

        return state;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
}
