using Avalonia.Controls;
using Avalonia.Interactivity;
using NutManager.App.ViewModels;
using NutManager.Core.Services;

namespace NutManager.App.Views;

public partial class RemoteAccessAdministrationView : UserControl
{
    public RemoteAccessAdministrationView() => InitializeComponent();

    private async void RemoteConnectPasswordButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { UsesSshPassword: true } remote, CanConnectRemote: true })
        {
            var secret = RemotePasswordBox.Text ?? string.Empty;
            try { await remote.ConnectWithPasswordAsync(secret.AsMemory(), RemoteRememberCredentialCheckBox.IsChecked == true); }
            finally { RemotePasswordBox.Text = string.Empty; }
        }
    }

    private async void RemoteConnectPrivateKeyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { UsesSshPrivateKey: true } remote, CanConnectRemote: true })
        {
            var secret = RemotePassphraseBox.Text ?? string.Empty;
            try { await remote.ConnectWithPrivateKeyAsync(remote.ConfiguredSshPrivateKeyPath ?? string.Empty, secret.AsMemory(), RemoteRememberPassphraseCheckBox.IsChecked == true); }
            finally { RemotePassphraseBox.Text = string.Empty; }
        }
    }

    private async void RemoteConnectSmbButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { UsesSmbExplicitCredentials: true } remote, CanConnectRemote: true })
        {
            var secret = SmbPasswordBox.Text ?? string.Empty;
            try { await remote.ConnectWithPasswordAsync(secret.AsMemory(), SmbRememberCredentialCheckBox.IsChecked == true); }
            finally { SmbPasswordBox.Text = string.Empty; }
        }
    }

    private async void RemoteConnectCurrentIdentityButton_OnClick(object? sender, RoutedEventArgs e) { if (DataContext is AdministrationPageViewModel { RemoteManagement: { IsSmb: true } remote, CanConnectRemote: true }) await remote.ConnectWithCurrentWindowsIdentityAsync(); }
    private async void RemoteConnectStoredCredentialButton_OnClick(object? sender, RoutedEventArgs e) { if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remote }) await remote.ConnectWithStoredCredentialAsync(); }
    private async void RemoteForgetStoredCredentialButton_OnClick(object? sender, RoutedEventArgs e) { if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remote }) await remote.ForgetStoredCredentialAsync(); }
    private async void RemoteDisconnectButton_OnClick(object? sender, RoutedEventArgs e) { if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remote, CanDisconnectRemote: true }) await remote.DisconnectAsync(); }
    private async void RemoteTrustHostKeyButton_OnClick(object? sender, RoutedEventArgs e) { if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remote, CanTrustRemoteHostKey: true }) await remote.TrustPresentedHostKeyAsync(); }
    private async void RemoteBrowseButton_OnClick(object? sender, RoutedEventArgs e) { if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remote, CanBrowseRemoteDirectory: true }) await remote.BrowseDirectoryAsync(remote.CurrentDirectory); }
    private async void RemoteBrowseParentButton_OnClick(object? sender, RoutedEventArgs e) { if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remote, CanBrowseRemoteDirectory: true }) await remote.BrowseParentAsync(); }
    private async void RemoteValidateDirectoryButton_OnClick(object? sender, RoutedEventArgs e) { if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remote, CanValidateRemoteDirectory: true }) await remote.ValidateCurrentDirectoryAsync(); }
    private async void RemoteUseDirectoryButton_OnClick(object? sender, RoutedEventArgs e) { if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remote, CanUseRemoteDirectory: true }) await remote.UseCurrentDirectoryAsync(); }
    private async void RemoteProbeWriteButton_OnClick(object? sender, RoutedEventArgs e) { if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remote, CanProbeRemoteWriteCapability: true }) await remote.ProbeWriteCapabilityAsync(); }

    private async void RemoteDirectoryList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is AdministrationPageViewModel { RemoteManagement: { } remote, CanBrowseRemoteDirectory: true } &&
            e.AddedItems.OfType<RemoteNutDirectoryEntry>().FirstOrDefault() is { } directory)
        {
            await remote.BrowseChildAsync(directory);
        }
    }
}
