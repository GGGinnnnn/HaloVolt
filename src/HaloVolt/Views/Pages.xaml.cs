using System.Windows;
using System.Windows.Controls;
using HaloVolt.ViewModels;

namespace HaloVolt.Views;

public partial class OverviewPage : UserControl
{
    public OverviewPage() => InitializeComponent();
}

public partial class DevicesPage : UserControl
{
    public DevicesPage() => InitializeComponent();
}

public partial class AlertsPage : UserControl
{
    public AlertsPage() => InitializeComponent();
}

public partial class TrayPage : UserControl
{
    public TrayPage() => InitializeComponent();
}

public partial class GeneralPage : UserControl
{
    public GeneralPage() => InitializeComponent();
}

public partial class DiagnosticsPage : UserControl
{
    public DiagnosticsPage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.HasDiagnostics && !vm.IsDiagnosticsLoading)
        {
            await vm.RefreshDiagnosticsAsync();
        }
    }
}

public partial class AboutPage : UserControl
{
    public AboutPage() => InitializeComponent();
}
