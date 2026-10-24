using HaloVolt.Models;
using HaloVolt.Services;

namespace HaloVolt.Tests;

public class LowBatteryPolicyTests
{
    private static DeviceState Device(int percent, bool? charging = false, DeviceKind kind = DeviceKind.Mouse)
        => new()
        {
            Id = "device:test",
            ProviderId = "test",
            ProviderName = "Test",
            Name = "MX Master 3S",
            Kind = kind,
            SupportsBattery = true,
            BatteryPercent = percent,
            IsCharging = charging,
            IsOnline = true
        };

    private static AppSettings Settings() => new()
    {
        LowThreshold = 20,
        RepeatStep = 5,
        CriticalThreshold = 5,
        CriticalEnabled = true,
        RepeatCooldownMinutes = 30,
        NotificationsEnabled = true
    };

    [Fact]
    public void FirstLevelBelowThreshold_Notifies()
    {
        var now = DateTimeOffset.Now;
        var decision = LowBatteryPolicy.Evaluate(Device(19), 20, 5, Settings(), null, now);

        Assert.True(decision.ShouldNotify);
        Assert.Equal(AlertSeverity.Low, decision.Severity);
        Assert.Equal(19, decision.Memory.LastNotifiedLevel);
    }

    [Fact]
    public void SameLevel_DoesNotNotifyAgain()
    {
        var now = DateTimeOffset.Now;
        var first = LowBatteryPolicy.Evaluate(Device(19), 20, 5, Settings(), null, now);
        var second = LowBatteryPolicy.Evaluate(Device(19), 20, 5, Settings(), first.Memory, now.AddMinutes(2));

        Assert.False(second.ShouldNotify);
    }

    [Fact]
    public void StepDrop_AfterCooldown_Notifies()
    {
        var now = DateTimeOffset.Now;
        var first = LowBatteryPolicy.Evaluate(Device(19), 20, 5, Settings(), null, now);
        var second = LowBatteryPolicy.Evaluate(Device(14), 20, 5, Settings(), first.Memory, now.AddMinutes(31));

        Assert.True(second.ShouldNotify);
        Assert.Equal(14, second.Memory.LastNotifiedLevel);
    }

    [Fact]
    public void StepDrop_BeforeCooldown_IsSuppressed()
    {
        var now = DateTimeOffset.Now;
        var first = LowBatteryPolicy.Evaluate(Device(19), 20, 5, Settings(), null, now);
        var second = LowBatteryPolicy.Evaluate(Device(14), 20, 5, Settings(), first.Memory, now.AddMinutes(10));

        Assert.False(second.ShouldNotify);
    }

    [Fact]
    public void CriticalLevel_UsesCriticalAlertOnce()
    {
        var now = DateTimeOffset.Now;
        var first = LowBatteryPolicy.Evaluate(Device(4), 20, 5, Settings(), null, now);
        Assert.Equal(AlertSeverity.Critical, first.Severity);

        var second = LowBatteryPolicy.Evaluate(Device(4), 20, 5, Settings(), first.Memory, now.AddMinutes(5));
        Assert.False(second.ShouldNotify);
    }

    [Fact]
    public void Charging_SuppressesAlerts()
    {
        var decision = LowBatteryPolicy.Evaluate(Device(10, charging: true), 20, 5, Settings(), null, DateTimeOffset.Now);
        Assert.False(decision.ShouldNotify);
    }

    [Fact]
    public void RisingAboveThreshold_ResetsMemory()
    {
        var now = DateTimeOffset.Now;
        var first = LowBatteryPolicy.Evaluate(Device(18), 20, 5, Settings(), null, now);
        var charged = LowBatteryPolicy.Evaluate(Device(24), 20, 5, Settings(), first.Memory, now.AddMinutes(5));
        var lowAgain = LowBatteryPolicy.Evaluate(Device(18), 20, 5, Settings(), charged.Memory, now.AddMinutes(10));

        Assert.True(lowAgain.ShouldNotify);
    }

    [Fact]
    public void KeyboardAlert_UsesKeyboardWording()
    {
        var decision = LowBatteryPolicy.Evaluate(Device(12, kind: DeviceKind.Keyboard), 20, 5, Settings(), null, DateTimeOffset.Now);
        Assert.Contains("键盘", decision.Title);
    }
}
