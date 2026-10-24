using System.Collections.ObjectModel;
using System.Security;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using HaloVolt.Infrastructure;
using HaloVolt.Models;

namespace HaloVolt.Services;

public sealed class NotificationRecord
{
    public DateTimeOffset Time { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Glyph { get; init; } = "\uE7E7";
}

public sealed class NotificationService : IDisposable
{
    public const string AppUserModelId = "HaloVolt.Desktop";
    public const string ProtocolName = "halovolt";

    private readonly AppSettings _settings;
    private bool _initialized;

    public NotificationService(AppSettings settings)
    {
        _settings = settings;
    }

    public ObservableCollection<NotificationRecord> History { get; } = [];

    public event EventHandler<(string Title, string Message)>? FallbackRequested;

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            RegisterIdentity();
        }
        catch (Exception ex)
        {
            AppLog.Warn("初始化 Windows 通知身份失败：" + ex.Message);
        }
    }

    public bool ShowAlert(DeviceState device, AlertDecision decision, bool isTest = false)
    {
        if (!decision.ShouldNotify && !isTest)
        {
            return false;
        }

        var title = decision.Title;
        var body = decision.Body;
        var silent = !_settings.SoundEnabled;
        var xml = BuildToastXml(title, body, silent);

        try
        {
            Initialize();
            var toast = new ToastNotification(xml)
            {
                Tag = decision.Tag,
                Group = "halovolt"
            };

            if (!isTest)
            {
                try
                {
                    ToastNotificationManager.History.Remove(decision.Tag, "halovolt", AppUserModelId);
                }
                catch
                {
                    // History may be disabled by policy.
                }
            }

            ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(toast);
            AddHistory(title, body, decision.Severity, isTest);
            AppLog.Info($"已发送通知：{title} - {body}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Windows 通知发送失败，回退到托盘气泡：" + ex.Message);
            FallbackRequested?.Invoke(this, (title, body));
            AddHistory(title, body, decision.Severity, isTest);
            return false;
        }
    }

    public void ShowRaw(string title, string body, bool silent = false)
    {
        try
        {
            Initialize();
            var xml = BuildToastXml(title, body, silent);
            var toast = new ToastNotification(xml)
            {
                Tag = "halovolt-info",
                Group = "halovolt"
            };
            ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(toast);
            AddHistory(title, body, AlertSeverity.None, false);
        }
        catch (Exception ex)
        {
            AppLog.Warn("信息通知发送失败：" + ex.Message);
            FallbackRequested?.Invoke(this, (title, body));
        }
    }

    public void ShowTest()
    {
        var xml = BuildToastXml(
            "HaloVolt 通知测试",
            "通知工作正常。低电量提醒会通过 Windows 通知中心送达。",
            silent: !_settings.SoundEnabled);
        try
        {
            Initialize();
            ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(new ToastNotification(xml)
            {
                Tag = "halovolt-test",
                Group = "halovolt"
            });
            AddHistory("HaloVolt 通知测试", "通知工作正常。低电量提醒会通过 Windows 通知中心送达。", AlertSeverity.None, true);
        }
        catch (Exception ex)
        {
            AppLog.Warn("测试通知发送失败：" + ex.Message);
            FallbackRequested?.Invoke(this, ("HaloVolt 通知测试", "Windows 通知不可用，请检查系统通知设置。"));
        }
    }

    private void AddHistory(string title, string body, AlertSeverity severity, bool isTest)
    {
        var record = new NotificationRecord
        {
            Time = DateTimeOffset.Now,
            Title = title,
            Body = body,
            Severity = isTest ? "测试" : severity == AlertSeverity.Critical ? "紧急" : severity == AlertSeverity.Low ? "低电量" : "信息",
            Glyph = severity == AlertSeverity.Critical ? "\uE7BA" : severity == AlertSeverity.Low ? "\uE7E7" : "\uE946"
        };

        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() =>
            {
                History.Insert(0, record);
                TrimHistory();
            });
        }
        else
        {
            History.Insert(0, record);
            TrimHistory();
        }
    }

    private void TrimHistory()
    {
        while (History.Count > 20)
        {
            History.RemoveAt(History.Count - 1);
        }
    }

    private static XmlDocument BuildToastXml(string title, string body, bool silent)
    {
        var safeTitle = SecurityElement.Escape(title) ?? string.Empty;
        var safeBody = SecurityElement.Escape(body) ?? string.Empty;
        var audio = silent ? "<audio silent=\"true\" />" : "<audio src=\"ms-winsoundevent:Notification.Default\" />";
        var xml = $"""
                   <toast activationType="protocol" launch="halovolt://open">
                     <visual>
                       <binding template="ToastGeneric">
                         <text>{safeTitle}</text>
                         <text>{safeBody}</text>
                         <text placement="attribution">HaloVolt 环电</text>
                       </binding>
                     </visual>
                     <actions>
                       <action content="打开设置" activationType="protocol" arguments="halovolt://open" />
                     </actions>
                     {audio}
                   </toast>
                   """;
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document;
    }

    private static void RegisterIdentity()
    {
        var exe = AppPaths.ExecutablePath;
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppUserModelId, writable: true))
        {
            key.SetValue("DisplayName", "HaloVolt 环电");
            key.SetValue("IconUri", exe);
            key.SetValue("ShowInSettings", 1, RegistryValueKind.DWord);
        }

        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\" + AppUserModelId, writable: true))
        {
            key.SetValue("Enabled", 1, RegistryValueKind.DWord);
            key.SetValue("ShowInActionCenter", 1, RegistryValueKind.DWord);
        }

        using (var protocol = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProtocolName, writable: true))
        {
            protocol.SetValue(string.Empty, "HaloVolt Protocol");
            protocol.SetValue("URL Protocol", string.Empty);
        }

        using (var command = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProtocolName}\shell\open\command", writable: true))
        {
            command.SetValue(string.Empty, $"\"{exe}\" --open");
        }
    }

    public void Dispose()
    {
        // Notification history intentionally survives app exit so a low-battery alert
        // stays available in Windows Notification Center.
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
}
