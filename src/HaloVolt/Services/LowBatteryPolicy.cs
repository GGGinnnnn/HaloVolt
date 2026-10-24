using HaloVolt.Models;

namespace HaloVolt.Services;

public sealed record AlertDecision(
    AlertSeverity Severity,
    string Title,
    string Body,
    string Tag,
    AlertMemory Memory)
{
    public bool ShouldNotify => Severity != AlertSeverity.None;
}

/// <summary>
/// Pure, testable low-battery policy. It implements threshold entry, repeat-by-step
/// alerts, a cooldown, and a separate critical alert. State is persisted with settings
/// so restarting the tray app does not produce a notification storm.
/// </summary>
public static class LowBatteryPolicy
{
    public static AlertDecision Evaluate(
        DeviceState device,
        int threshold,
        int criticalThreshold,
        AppSettings settings,
        AlertMemory? previous,
        DateTimeOffset now)
    {
        var memory = Clone(previous);
        var level = device.BatteryPercent;
        var charging = device.IsCharging == true;
        var kind = device.Kind == DeviceKind.SystemBattery ? "笔记本" : device.Kind == DeviceKind.Keyboard ? "键盘" : device.Kind == DeviceKind.Mouse ? "鼠标" : "设备";
        var tag = "battery-" + StableHash(device.Id);

        if (level is null || !device.SupportsBattery)
        {
            return NoAlert(memory, tag);
        }

        memory.LastSeenLevel = level;

        if (charging)
        {
            if (criticalThreshold >= 0 && level > criticalThreshold + 2)
            {
                memory.CriticalNotified = false;
            }

            return NoAlert(memory, tag);
        }

        if (level > threshold)
        {
            memory.LastNotifiedLevel = null;
            memory.LastNotifiedAt = null;
            memory.CriticalNotified = false;
            return NoAlert(memory, tag);
        }

        if (settings.CriticalEnabled && level <= criticalThreshold && !memory.CriticalNotified)
        {
            memory.CriticalNotified = true;
            memory.LastNotifiedLevel = level;
            memory.LastNotifiedAt = now;
            return new AlertDecision(
                AlertSeverity.Critical,
                $"{kind}电量告急",
                $"{device.Name} 仅剩 {level}%，请立即充电，以免影响使用。",
                tag,
                memory);
        }

        var firstBelowThreshold = memory.LastNotifiedLevel is null;
        var droppedByStep = memory.LastNotifiedLevel is { } last && level <= last - Math.Max(1, settings.RepeatStep);
        var cooldownPassed = memory.LastNotifiedAt is null ||
                             now - memory.LastNotifiedAt.Value >= TimeSpan.FromMinutes(Math.Max(5, settings.RepeatCooldownMinutes));

        if (firstBelowThreshold || (droppedByStep && cooldownPassed))
        {
            memory.LastNotifiedLevel = level;
            memory.LastNotifiedAt = now;
            var next = Math.Max(0, level.Value - Math.Max(1, settings.RepeatStep));
            return new AlertDecision(
                AlertSeverity.Low,
                $"{kind}电量偏低",
                $"{device.Name} 剩余 {level}%，已低于 {threshold}% 提醒线。建议尽快充电。{(next > 0 ? $"下次将在约 {next}% 时再次提醒。" : string.Empty)}",
                tag,
                memory);
        }

        return NoAlert(memory, tag);
    }

    public static AlertMemory ResetForCharging(AlertMemory? previous)
    {
        var memory = Clone(previous);
        memory.LastNotifiedLevel = null;
        memory.LastNotifiedAt = null;
        memory.CriticalNotified = false;
        return memory;
    }

    private static AlertDecision NoAlert(AlertMemory memory, string tag)
        => new(AlertSeverity.None, string.Empty, string.Empty, tag, memory);

    private static AlertMemory Clone(AlertMemory? source)
        => source is null
            ? new AlertMemory()
            : new AlertMemory
            {
                LastNotifiedLevel = source.LastNotifiedLevel,
                LastNotifiedAt = source.LastNotifiedAt,
                CriticalNotified = source.CriticalNotified,
                LastSeenLevel = source.LastSeenLevel
            };

    private static string StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in value)
            {
                hash = (hash ^ c) * 16777619u;
            }

            return hash.ToString("x8");
        }
    }
}
