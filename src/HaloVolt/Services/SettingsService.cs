using System.Text.Json;
using System.Text.Json.Serialization;
using HaloVolt.Infrastructure;
using HaloVolt.Models;

namespace HaloVolt.Services;

public sealed class SettingsService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _sync = new();
    private readonly System.Threading.Timer _saveTimer;
    private bool _attached;
    private bool _disposed;

    public SettingsService()
    {
        Settings = LoadOrCreate();
        Attach();
        _saveTimer = new System.Threading.Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public AppSettings Settings { get; }

    public event EventHandler? Saved;

    public void ScheduleSave()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            _saveTimer.Change(650, Timeout.Infinite);
        }
    }

    public void SaveNow()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            AppPaths.EnsureAppDataDirectory();
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            var temp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, AppPaths.SettingsFile, overwrite: true);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Error("保存设置失败", ex);
        }
    }

    public void Reset()
    {
        var replacement = new AppSettings { HasCompletedFirstRun = true };
        Settings.DevicePreferences.Clear();
        Settings.AlertMemories.Clear();

        foreach (var property in typeof(AppSettings).GetProperties())
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (property.Name is nameof(AppSettings.DevicePreferences) or nameof(AppSettings.AlertMemories))
            {
                continue;
            }

            property.SetValue(Settings, property.GetValue(replacement));
        }

        Settings.HasCompletedFirstRun = true;
        SaveNow();
    }

    private AppSettings LoadOrCreate()
    {
        try
        {
            AppPaths.EnsureAppDataDirectory();
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("读取设置失败，将使用默认设置", ex);
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    File.Move(AppPaths.SettingsFile, AppPaths.SettingsFile + ".corrupt", overwrite: true);
                }
            }
            catch
            {
                // ignored
            }
        }

        return new AppSettings();
    }

    private void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        Settings.PropertyChanged += (_, _) => ScheduleSave();
        Settings.DevicePreferences.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (DevicePreference pref in e.NewItems)
                {
                    pref.PropertyChanged += (_, _) => ScheduleSave();
                }
            }

            ScheduleSave();
        };

        foreach (var pref in Settings.DevicePreferences)
        {
            pref.PropertyChanged += (_, _) => ScheduleSave();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SaveNow();
        _disposed = true;
        _saveTimer.Dispose();
    }
}
