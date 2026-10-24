using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using HaloVolt.ViewModels;

namespace HaloVolt.Views;

public partial class TrayFlyoutWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _closing;

    public TrayFlyoutWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Left = -10000;
        Top = -10000;
        Deactivated += (_, _) => SafeClose();
    }

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public void ShowAt(System.Drawing.Point screenPoint)
    {
        Show();
        UpdateLayout();

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (!GetWindowRect(handle, out var rect))
        {
            return;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var screen = System.Windows.Forms.Screen.FromPoint(screenPoint);
        var area = screen.WorkingArea;
        var x = Math.Clamp(screenPoint.X - width + 8, area.Left + 8, area.Right - width - 8);
        var y = Math.Clamp(screenPoint.Y - height - 8, area.Top + 8, area.Bottom - height - 8);

        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpShowWindow);
        Activate();
        SetForegroundWindow(handle);
        Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SafeClose();
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenRequested?.Invoke();
        SafeClose();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.RefreshCommand.CanExecute(null))
        {
            _viewModel.RefreshCommand.Execute(null);
        }

        SafeClose();
    }

    private void Pause30_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Pause30Command.CanExecute(null))
        {
            _viewModel.Pause30Command.Execute(null);
        }

        SafeClose();
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ResumeMonitoringCommand.CanExecute(null))
        {
            _viewModel.ResumeMonitoringCommand.Execute(null);
        }

        SafeClose();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        ExitRequested?.Invoke();
        SafeClose();
    }

    private void SafeClose()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
            // The window can already be closing when Deactivated is raised.
        }
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
