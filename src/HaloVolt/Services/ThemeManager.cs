using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using HaloVolt.Models;
using Microsoft.Win32;

namespace HaloVolt.Services;

public static class ThemeManager
{
    private static ResourceDictionary? _themeDictionary;
    private static bool _initialized;

    public static bool IsDark { get; private set; }

    public static event EventHandler? ThemeChanged;

    public static void Initialize(ThemeMode mode)
    {
        if (!_initialized)
        {
            _initialized = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        Apply(mode);
    }

    public static void Apply(ThemeMode mode)
    {
        if (System.Windows.Application.Current is not { } app)
        {
            return;
        }

        var dark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => IsSystemDark()
        };

        IsDark = dark;
        var next = new ResourceDictionary
        {
            Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative)
        };

        if (_themeDictionary is null)
        {
            _themeDictionary = next;
            app.Resources.MergedDictionaries.Insert(0, _themeDictionary);
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("Themes/Styles.xaml", UriKind.Relative)
            });
        }
        else
        {
            var index = app.Resources.MergedDictionaries.IndexOf(_themeDictionary);
            if (index < 0)
            {
                app.Resources.MergedDictionaries.Insert(0, next);
            }
            else
            {
                app.Resources.MergedDictionaries[index] = next;
            }

            _themeDictionary = next;
        }

        foreach (Window window in app.Windows)
        {
            WindowEffects.Apply(window, dark);
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color))
        {
            return;
        }

        var settings = (System.Windows.Application.Current as App)?.ViewModel?.Settings;
        if (settings is null || settings.Theme != ThemeMode.System)
        {
            return;
        }

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Apply(ThemeMode.System));
    }
}

public static class WindowEffects
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwcpRound = 2;
    private const int DwmsbtMainWindow = 2;

    public static void Apply(Window window, bool dark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                window.SourceInitialized += (_, _) => Apply(window, dark);
                return;
            }

            var useDark = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
            var corner = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));

            if (Environment.OSVersion.Version.Build >= 22000)
            {
                // A solid WPF surface is intentionally kept opaque for reliable rendering
                // across RDP, virtual machines and mixed-DPI multi-monitor setups. Mica can
                // make a custom WindowChrome window appear transparent on some drivers.
                var cornerPreference = DwmwcpRound;
                DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
            }
        }
        catch
        {
            // Visual polish must never prevent the window from opening.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
}
