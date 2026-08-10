using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NutManager.App.ViewModels;
using NutManager.Core.Administration;
using NutManager.Core.Services;

namespace NutManager.App.Views;

public partial class AdministrationPageView : UserControl
{
    public AdministrationPageView()
    {
        InitializeComponent();
    }

    private async void SelectDirectoryButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not AdministrationPageViewModel viewModel ||
            !viewModel.IsLocalManagementProfile ||
            !viewModel.CanChangeInstallation ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
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

    private async void RemoteConnectPasswordButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { IsSshSftp: true } remoteManagement, CanConnectRemote: true })
        {
            var password = RemotePasswordBox.Text ?? string.Empty;
            try
            {
                await remoteManagement.ConnectWithPasswordAsync(password.AsMemory());
            }
            finally
            {
                RemotePasswordBox.Text = string.Empty;
            }
        }
    }

    private async void RemoteConnectCurrentIdentityButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { IsSmb: true } remoteManagement, CanConnectRemote: true })
        {
            await remoteManagement.ConnectWithCurrentWindowsIdentityAsync();
        }
    }

    private async void RemoteConnectSmbButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { UsesSmbExplicitCredentials: true } remoteManagement, CanConnectRemote: true })
        {
            var password = SmbPasswordBox.Text ?? string.Empty;
            try
            {
                await remoteManagement.ConnectWithPasswordAsync(password.AsMemory());
            }
            finally
            {
                SmbPasswordBox.Text = string.Empty;
            }
        }
    }

    private async void RemoteConnectPrivateKeyButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is not AdministrationPageViewModel { RemoteManagement: { IsSshSftp: true } remoteManagement, CanConnectRemote: true } || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecionar chave privada SSH",
            AllowMultiple = false
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            var passphrase = RemotePasswordBox.Text ?? string.Empty;
            try
            {
                await remoteManagement.ConnectWithPrivateKeyAsync(path, passphrase.AsMemory());
            }
            finally
            {
                RemotePasswordBox.Text = string.Empty;
            }
        }
    }

    private async void RemoteDisconnectButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remoteManagement, CanDisconnectRemote: true })
        {
            await remoteManagement.DisconnectAsync();
        }
    }

    private async void RemoteTrustHostKeyButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remoteManagement, CanTrustRemoteHostKey: true })
        {
            await remoteManagement.TrustPresentedHostKeyAsync();
        }
    }

    private async void RemoteBrowseButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remoteManagement, CanBrowseRemoteDirectory: true })
        {
            await remoteManagement.BrowseDirectoryAsync(remoteManagement.CurrentDirectory);
        }
    }

    private async void RemoteBrowseParentButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remoteManagement, CanBrowseRemoteDirectory: true })
        {
            await remoteManagement.BrowseParentAsync();
        }
    }

    private async void RemoteValidateDirectoryButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remoteManagement, CanValidateRemoteDirectory: true })
        {
            await remoteManagement.ValidateCurrentDirectoryAsync();
        }
    }

    private async void RemoteDirectoryList_OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remoteManagement, CanBrowseRemoteDirectory: true } && eventArgs.AddedItems.OfType<RemoteNutDirectoryEntry>().FirstOrDefault() is { } directory)
        {
            await remoteManagement.BrowseChildAsync(directory);
        }
    }

    private async void RemoteUseDirectoryButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remoteManagement, CanUseRemoteDirectory: true })
        {
            await remoteManagement.UseCurrentDirectoryAsync();
        }
    }

    private async void RemoteProbeWriteButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remoteManagement, CanProbeRemoteWriteCapability: true })
        {
            await remoteManagement.ProbeWriteCapabilityAsync();
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
