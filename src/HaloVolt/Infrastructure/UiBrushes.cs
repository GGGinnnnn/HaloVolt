using System.Globalization;
using System.Windows.Media;

namespace HaloVolt.Infrastructure;

public static class UiBrushes
{
    public static SolidColorBrush FromHex(string? hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
            // fall through
        }

        var fallbackBrush = new SolidColorBrush(fallback);
        fallbackBrush.Freeze();
        return fallbackBrush;
    }

    public static Color ColorFromHex(string? hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color color)
            {
                return color;
            }
        }
        catch
        {
            // ignored
        }

        return fallback;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static string PercentText(int? value) => value is null ? "—" : $"{value.Value.ToString(CultureInfo.InvariantCulture)}%";
}
