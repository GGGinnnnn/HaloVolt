using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using HaloVolt.Models;
using HaloVolt.Services;
using HaloVolt.ViewModels;
using HaloVolt.Views;

namespace HaloVolt;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Dictionary<PageKey, UserControl> _pages = [];
    private bool _navigating;

    public MainWindow(MainViewModel viewModel, PageKey? initialPage = null)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.NavigationRequested = NavigateTo;
        NavList.SelectedIndex = 0;
        SourceInitialized += (_, _) => WindowEffects.Apply(this, ThemeManager.IsDark);
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            PageHost.Content = null;
            _pages.Clear();
            DataContext = null;
        };

        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(MinWidth, workArea.Width - 28));
        Height = Math.Min(Height, Math.Max(MinHeight, workArea.Height - 28));
        if (initialPage is not null and not PageKey.Overview)
        {
            NavigateTo(initialPage.Value);
        }
    }

    public bool ForceClose { get; set; }

    public void NavigateTo(PageKey key)
    {
        var index = key switch
        {
            PageKey.Overview => 0,
            PageKey.Devices => 1,
            PageKey.Alerts => 2,
            PageKey.Tray => 3,
            PageKey.General => 4,
            PageKey.Diagnostics => 5,
            PageKey.About => 6,
            _ => 0
        };

        if (NavList.SelectedIndex == index)
        {
            ShowPage(key);
        }
        else
        {
            NavList.SelectedIndex = index;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (App.CurrentApp.HandleWindowClosing(this))
        {
            return;
        }

        e.Cancel = true;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        if (WindowState == WindowState.Minimized)
        {
            App.CurrentApp.OnWindowMinimized(this);
        }
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_navigating || NavList.SelectedItem is not ListBoxItem { Tag: string tag })
        {
            return;
        }

        if (Enum.TryParse<PageKey>(tag, out var key))
        {
            ShowPage(key);
        }
    }

    private void ShowPage(PageKey key)
    {
        _navigating = true;
        try
        {
            if (!_pages.TryGetValue(key, out var page))
            {
                PageHost.Content = null;
                _pages.Clear();
                page = key switch
                {
                    PageKey.Overview => new OverviewPage(),
                    PageKey.Devices => new DevicesPage(),
                    PageKey.Alerts => new AlertsPage(),
                    PageKey.Tray => new TrayPage(),
                    PageKey.General => new GeneralPage(),
                    PageKey.Diagnostics => new DiagnosticsPage(),
                    PageKey.About => new AboutPage(),
                    _ => new OverviewPage()
                };
                page.DataContext = _viewModel;
                _pages[key] = page;
            }

            PageHost.Content = page;
            if (!_viewModel.Settings.ReduceMotion)
            {
                page.BeginAnimation(OpacityProperty, null);
                page.Opacity = 0;
                page.BeginAnimation(
                    OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    });
            }
            else
            {
                page.Opacity = 1;
            }
        }
        finally
        {
            _navigating = false;
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
