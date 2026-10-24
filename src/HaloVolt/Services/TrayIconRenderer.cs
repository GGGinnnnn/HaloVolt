using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace HaloVolt.Services;

public sealed record TrayRingSnapshot(
    int? OuterPercent,
    int? InnerPercent,
    Color OuterColor,
    Color InnerColor,
    int? SystemPercent,
    bool ShowCenterDot);

public static class TrayIconRenderer
{
    public static Icon CreateBatteryIcon(TrayRingSnapshot snapshot, int size = 32)
    {
        using var bitmap = CreateBatteryBitmap(snapshot, size);
        var handle = bitmap.GetHicon();
        try
        {
            using var fromHandle = Icon.FromHandle(handle);
            return (Icon)fromHandle.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static Bitmap CreateBatteryBitmap(TrayRingSnapshot snapshot, int size = 32)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        var track = Color.FromArgb(88, 140, 150, 165);
        using var trackPen = new Pen(track, Math.Max(2f, size / 10f));
        var outerRect = new RectangleF(size * 0.16f, size * 0.16f, size * 0.68f, size * 0.68f);
        var innerRect = new RectangleF(size * 0.29f, size * 0.29f, size * 0.42f, size * 0.42f);
        graphics.DrawEllipse(trackPen, outerRect);
        graphics.DrawEllipse(trackPen, innerRect);

        DrawArc(graphics, outerRect, snapshot.OuterPercent, snapshot.OuterColor, Math.Max(2.2f, size / 10f));
        DrawArc(graphics, innerRect, snapshot.InnerPercent, snapshot.InnerColor, Math.Max(2.0f, size / 11f));

        if (snapshot.ShowCenterDot && snapshot.SystemPercent is { } systemPercent)
        {
            var dot = systemPercent <= 10
                ? Color.FromArgb(255, 240, 92, 92)
                : systemPercent <= 25
                    ? Color.FromArgb(255, 255, 184, 77)
                    : Color.FromArgb(255, 124, 224, 124);
            using var dotBrush = new SolidBrush(dot);
            var radius = size * 0.055f;
            graphics.FillEllipse(dotBrush, size / 2f - radius, size / 2f - radius, radius * 2, radius * 2);
        }

        return bitmap;
    }

    public static Icon CreateAppIcon()
    {
        var uri = new Uri("pack://application:,,,/Assets/app.ico");
        var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
        if (stream is not null)
        {
            using (stream)
            {
                return new Icon(stream, new Size(32, 32));
            }
        }

        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(16, 28, 48));
        using var pen = new Pen(Color.FromArgb(76, 194, 255), 3);
        graphics.DrawArc(pen, 4, 4, 24, 24, -90, 280);
        var handle = bitmap.GetHicon();
        try
        {
            using var fromHandle = Icon.FromHandle(handle);
            return (Icon)fromHandle.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void DrawArc(Graphics graphics, RectangleF rect, int? percent, Color color, float width)
    {
        using var pen = new Pen(color, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        if (percent is null)
        {
            using var unknownPen = new Pen(Color.FromArgb(150, color), Math.Max(1.5f, width * 0.65f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                DashStyle = DashStyle.Dot
            };
            graphics.DrawArc(unknownPen, rect, -90, 360);
            return;
        }

        var sweep = Math.Clamp(percent.Value, 0, 100) * 3.6f;
        if (sweep < 2)
        {
            sweep = 2;
        }

        graphics.DrawArc(pen, rect, -90, sweep);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
