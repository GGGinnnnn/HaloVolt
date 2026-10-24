using System.Drawing;
using System.Windows.Forms;
using HaloVolt.Infrastructure;
using HaloVolt.Models;

namespace HaloVolt.Services;

public sealed class TrayService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly NotifyIcon _mainIcon;
    private readonly NotifyIcon _batteryIcon;
    private Icon? _batteryDrawing;
    private string? _lastFingerprint;
    private bool _disposed;

    public TrayService(AppSettings settings)
    {
        _settings = settings;

        _mainIcon = new NotifyIcon
        {
            Icon = TrayIconRenderer.CreateAppIcon(),
            Text = "HaloVolt 环电",
            Visible = true
        };
        _mainIcon.MouseUp += OnMouseUp;

        _batteryIcon = new NotifyIcon
        {
            Icon = TrayIconRenderer.CreateBatteryIcon(new TrayRingSnapshot(null, null, Color.FromArgb(76, 194, 255), Color.FromArgb(255, 184, 77), null, false)),
            Text = "HaloVolt · 正在读取电量",
            Visible = settings.TrayBatteryIconEnabled
        };
        _batteryIcon.MouseUp += OnMouseUp;
    }

    public event Action? OpenSettings;
    public event Action<Point>? MenuRequested;

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            OpenSettings?.Invoke();
        }
        else if (e.Button == MouseButtons.Right)
        {
            MenuRequested?.Invoke(Cursor.Position);
        }
    }

    public void Update(string tooltip, TrayRingSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        var safeTip = string.IsNullOrWhiteSpace(tooltip) ? "HaloVolt 环电" : tooltip;
        if (safeTip.Length > 120)
        {
            safeTip = safeTip[..120];
        }

        _mainIcon.Text = safeTip;
        _batteryIcon.Text = safeTip;
        _batteryIcon.Visible = _settings.TrayBatteryIconEnabled;

        var fingerprint = string.Join(
            "|",
            snapshot.OuterPercent?.ToString() ?? "-",
            snapshot.InnerPercent?.ToString() ?? "-",
            snapshot.OuterColor.ToArgb(),
            snapshot.InnerColor.ToArgb(),
            snapshot.SystemPercent?.ToString() ?? "-",
            snapshot.ShowCenterDot,
            _settings.TrayBatteryIconEnabled);
        if (fingerprint == _lastFingerprint)
        {
            return;
        }

        _lastFingerprint = fingerprint;
        var newIcon = TrayIconRenderer.CreateBatteryIcon(snapshot);
        _batteryIcon.Icon = newIcon;
        _batteryDrawing?.Dispose();
        _batteryDrawing = newIcon;
    }

    public void ShowBalloon(string title, string message)
    {
        try
        {
            var target = _settings.TrayBatteryIconEnabled ? _batteryIcon : _mainIcon;
            target.BalloonTipTitle = title;
            target.BalloonTipText = message;
            target.BalloonTipIcon = ToolTipIcon.Info;
            target.ShowBalloonTip(6000);
        }
        catch (Exception ex)
        {
            AppLog.Warn("托盘气泡显示失败：" + ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mainIcon.Visible = false;
        _batteryIcon.Visible = false;
        _mainIcon.MouseUp -= OnMouseUp;
        _batteryIcon.MouseUp -= OnMouseUp;
        _mainIcon.Dispose();
        _batteryIcon.Dispose();
        _batteryDrawing?.Dispose();
    }
}
