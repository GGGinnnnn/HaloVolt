using Microsoft.Win32;
using HaloVolt.Infrastructure;

namespace HaloVolt.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HaloVolt";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                var exe = AppPaths.ExecutablePath;
                key.SetValue(ValueName, $"\"{exe}\" --autostart", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("设置开机自启失败", ex);
        }
    }

    public void RefreshIfEnabled()
    {
        if (IsEnabled())
        {
            SetEnabled(true);
        }
    }
}
