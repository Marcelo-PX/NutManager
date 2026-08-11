using Avalonia.Controls;
using Avalonia.Interactivity;
using NutManager.App.ViewModels;
using NutManager.Core.Administration;

namespace NutManager.App.Views;

public partial class DevicesDriversAdministrationView : UserControl
{
    public DevicesDriversAdministrationView() => InitializeComponent();
    private void UpsdrvctlHelpButton_OnClick(object? sender, RoutedEventArgs eventArgs) => Prepare(NutDriverDiagnosticKind.UpsdrvctlHelp);
    private void UpsdrvctlListButton_OnClick(object? sender, RoutedEventArgs eventArgs) => Prepare(NutDriverDiagnosticKind.UpsdrvctlList);
    private void UpsdrvctlStatusButton_OnClick(object? sender, RoutedEventArgs eventArgs) => Prepare(NutDriverDiagnosticKind.UpsdrvctlStatus);
    private void UpsdrvctlDryRunButton_OnClick(object? sender, RoutedEventArgs eventArgs) => Prepare(NutDriverDiagnosticKind.UpsdrvctlDryRunStart);
    private void DriverHelpButton_OnClick(object? sender, RoutedEventArgs eventArgs) => Prepare(NutDriverDiagnosticKind.DriverHelp);
    private void DriverVersionButton_OnClick(object? sender, RoutedEventArgs eventArgs) => Prepare(NutDriverDiagnosticKind.DriverVersion);
    private void DriverVariablesButton_OnClick(object? sender, RoutedEventArgs eventArgs) => Prepare(NutDriverDiagnosticKind.DriverVariableList);
    private void DriverDataDumpButton_OnClick(object? sender, RoutedEventArgs eventArgs) => Prepare(NutDriverDiagnosticKind.DriverDataDump);
    private void Prepare(NutDriverDiagnosticKind kind) => (DataContext as AdministrationPageViewModel)?.PrepareDriverDiagnostic(kind);
}
