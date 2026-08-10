using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NutManager.App.ViewModels;
using NutManager.Core.Administration;

namespace NutManager.App.Views;

public partial class AdministrationPageView : UserControl
{
    public AdministrationPageView()
    {
        InitializeComponent();
    }

    private async void SelectDirectoryButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not AdministrationPageViewModel viewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Selecionar instalação local do NUT",
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await viewModel.InspectInstallationDirectoryAsync(path);
        }
    }

    private async void ConfigurationFileList_OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (DataContext is AdministrationPageViewModel viewModel && eventArgs.AddedItems.OfType<NutConfigurationFileItemViewModel>().FirstOrDefault() is { } file)
        {
            await viewModel.SelectFileAsync(file);
        }
    }

    private void StartServiceButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareServiceAction(NutAdministrativeAction.StartService);
    private void StopServiceButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareServiceAction(NutAdministrativeAction.StopService);
    private void RestartServiceButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareServiceAction(NutAdministrativeAction.RestartService);
    private void RepairPermissionsButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PreparePermissionRepair();
    private void UpsdrvctlHelpButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareDriverDiagnostic(NutDriverDiagnosticKind.UpsdrvctlHelp);
    private void UpsdrvctlListButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareDriverDiagnostic(NutDriverDiagnosticKind.UpsdrvctlList);
    private void UpsdrvctlStatusButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareDriverDiagnostic(NutDriverDiagnosticKind.UpsdrvctlStatus);
    private void UpsdrvctlDryRunButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareDriverDiagnostic(NutDriverDiagnosticKind.UpsdrvctlDryRunStart);
    private void DriverHelpButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareDriverDiagnostic(NutDriverDiagnosticKind.DriverHelp);
    private void DriverVersionButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareDriverDiagnostic(NutDriverDiagnosticKind.DriverVersion);
    private void DriverVariablesButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareDriverDiagnostic(NutDriverDiagnosticKind.DriverVariableList);
    private void DriverDataDumpButton_OnClick(object? sender, RoutedEventArgs eventArgs) => (DataContext as AdministrationPageViewModel)?.PrepareDriverDiagnostic(NutDriverDiagnosticKind.DriverDataDump);
}
