using HaloVolt.Infrastructure;
using static HaloVolt.Providers.Hid.HidNative;

namespace HaloVolt.Providers.Hid;

internal sealed class HidBatteryTarget
{
    public required string Path { get; init; }
    public required byte ReportId { get; init; }
    public required ushort LinkCollection { get; init; }
    public required ushort BitField { get; init; }
    public required ushort BitSize { get; init; }
    public required int LogicalMin { get; init; }
    public required int LogicalMax { get; init; }
    public required ushort FeatureReportLength { get; init; }

    public int ToPercentage(uint raw)
    {
        if (LogicalMax <= LogicalMin)
        {
            return raw <= 100 ? (int)raw : (int)Math.Min(100, raw);
        }

        var normalized = (raw - (double)LogicalMin) / (LogicalMax - LogicalMin) * 100.0;
        return (int)Math.Round(Math.Clamp(normalized, 0, 100));
    }
}

internal static class HidBatteryProbe
{
    public static HidBatteryTarget? Probe(string path)
    {
        try
        {
            using var handle = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                return null;
            }

            if (!HidD_GetPreparsedData(handle, out var preparsed) || preparsed == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var status = HidP_GetCaps(preparsed, out var caps);
                if (status != HIDP_STATUS_SUCCESS || caps.FeatureReportByteLength == 0 || caps.NumberFeatureValueCaps == 0)
                {
                    return null;
                }

                var count = caps.NumberFeatureValueCaps;
                var valueCaps = new HIDP_VALUE_CAPS[Math.Max(1, (int)count)];
                var length = count;
                status = HidP_GetValueCaps(HIDP_REPORT_TYPE.Feature, valueCaps, ref length, preparsed);
                if (status != HIDP_STATUS_SUCCESS)
                {
                    return null;
                }

                for (var i = 0; i < length; i++)
                {
                    var cap = valueCaps[i];
                    if (cap.UsagePage != 0x0006)
                    {
                        continue;
                    }

                    var isBattery = cap.IsRange
                        ? cap.Union is { Length: >= 2 } && cap.Union[0] <= 0x20 && cap.Union[1] >= 0x20
                        : cap.Union is { Length: >= 1 } && cap.Union[0] == 0x20;

                    if (!isBattery)
                    {
                        continue;
                    }

                    return new HidBatteryTarget
                    {
                        Path = path,
                        ReportId = cap.ReportID,
                        LinkCollection = cap.LinkCollection,
                        BitField = cap.BitField,
                        BitSize = cap.BitSize,
                        LogicalMin = cap.LogicalMin,
                        LogicalMax = cap.LogicalMax,
                        FeatureReportLength = caps.FeatureReportByteLength
                    };
                }
            }
            finally
            {
                HidD_FreePreparsedData(preparsed);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("探测 HID 电量特性失败：" + ex.Message);
        }

        return null;
    }

    public static int? ReadPercentage(HidBatteryTarget target)
    {
        try
        {
            using var handle = CreateFile(target.Path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                return null;
            }

            var length = Math.Max((int)target.FeatureReportLength, 2);
            var buffer = new byte[length];
            buffer[0] = target.ReportId;
            if (!HidD_GetFeature(handle, buffer, length))
            {
                return null;
            }

            if (HidD_GetPreparsedData(handle, out var preparsed) && preparsed != IntPtr.Zero)
            {
                try
                {
                    var status = HidP_GetUsageValue(
                        HIDP_REPORT_TYPE.Feature,
                        0x0006,
                        target.LinkCollection,
                        0x0020,
                        out var value,
                        preparsed,
                        buffer,
                        (uint)length);

                    if (status == HIDP_STATUS_SUCCESS)
                    {
                        return target.ToPercentage(value);
                    }
                }
                finally
                {
                    HidD_FreePreparsedData(preparsed);
                }
            }

            // Some drivers dislike HidP_GetUsageValue for vendor feature reports. Parse the
            // documented battery-strength value directly from its bit field as a fallback.
            var dataStart = target.ReportId == 0 ? 0 : 1;
            var bitOffset = target.BitField;
            ulong raw = 0;
            for (var bit = 0; bit < target.BitSize; bit++)
            {
                var absoluteBit = bitOffset + bit;
                var byteIndex = dataStart + absoluteBit / 8;
                if (byteIndex >= buffer.Length)
                {
                    return null;
                }

                if ((buffer[byteIndex] & (1 << (absoluteBit % 8))) != 0)
                {
                    raw |= 1UL << bit;
                }
            }

            return target.ToPercentage((uint)raw);
        }
        catch (Exception ex)
        {
            AppLog.Warn("读取 HID 电量失败：" + ex.Message);
            return null;
        }
    }
}
