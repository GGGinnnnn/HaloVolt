using System.Text;

namespace HaloVolt.Infrastructure;

public static class AppPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HaloVolt");

    public static string SettingsFile => Path.Combine(AppDataDirectory, "settings.json");
    public static string LogFile => Path.Combine(AppDataDirectory, "halovolt.log");
    public static string CrashFile => Path.Combine(AppDataDirectory, "last-crash.log");
    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "HaloVolt.exe");

    public static string EnsureAppDataDirectory()
    {
        Directory.CreateDirectory(AppDataDirectory);
        return AppDataDirectory;
    }
}

public static class AppLog
{
    private static readonly object Sync = new();
    private static bool _enabled;
    private static volatile bool _verbose;

    public static bool VerboseEnabled
    {
        get => _verbose;
        set => _verbose = value;
    }

    public static void Initialize(bool enabled, bool verbose = false)
    {
        _enabled = enabled;
        _verbose = verbose;
        if (!enabled)
        {
            return;
        }

        try
        {
            AppPaths.EnsureAppDataDirectory();
            Write("INFO", "HaloVolt started. Version=" + typeof(AppLog).Assembly.GetName().Version);
        }
        catch
        {
            // Logging must never take down the app.
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Debug(string message)
    {
        if (_verbose)
        {
            Write("DEBUG", message);
        }
    }

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null)
        => Write("ERROR", exception is null ? message : message + Environment.NewLine + exception);

    public static void Crash(Exception exception)
    {
        try
        {
            AppPaths.EnsureAppDataDirectory();
            File.WriteAllText(AppPaths.CrashFile, exception.ToString(), Encoding.UTF8);
        }
        catch
        {
            // ignored
        }

        Write("FATAL", exception.ToString());
    }

    private static void Write(string level, string message)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            AppPaths.EnsureAppDataDirectory();
            lock (Sync)
            {
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > 1024 * 1024)
                {
                    var backup = AppPaths.LogFile + ".old";
                    File.Delete(backup);
                    File.Move(AppPaths.LogFile, backup);
                }

                File.AppendAllText(
                    AppPaths.LogFile,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // ignored
        }
    }
}
