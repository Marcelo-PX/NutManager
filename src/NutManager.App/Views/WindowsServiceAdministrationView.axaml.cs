using Avalonia.Controls;
using Avalonia.Interactivity;
using NutManager.App.ViewModels;
using NutManager.Core.Administration;

namespace NutManager.App.Views;

public partial class WindowsServiceAdministrationView : UserControl
{
    public WindowsServiceAdministrationView() => InitializeComponent();
    private void StartServiceButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareServiceAction(NutAdministrativeAction.StartService);
    private void StopServiceButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareServiceAction(NutAdministrativeAction.StopService);
    private void RestartServiceButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareServiceAction(NutAdministrativeAction.RestartService);
    private void RepairPermissionsButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PreparePermissionRepair();
}
