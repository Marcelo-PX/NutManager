using NutManager.App.ViewModels;
using Xunit;

namespace NutManager.Tests;

/// <summary>Stable structural guards for the first T37 layout and navigation polish pass.</summary>
public sealed class T37PresentationTests
{
    [Fact]
    public void EverySidebarDestinationUsesTheSharedClippingSafeNavigationIcon()
    {
        var window = Read("src", "NutManager.App", "MainWindow.axaml");
        var icon = Read("src", "NutManager.App", "Presentation", "Controls", "NutNavigationIcon.axaml");
        var viewModel = new MainWindowViewModel();

        Assert.Equal(5, viewModel.NavigationItems.Count);
        Assert.Equal(2, window.Split("<controls:NutNavigationIcon Kind=\"{Binding Page}\" />", StringSplitOptions.None).Length - 1);
        foreach (var flag in new[] { "IsOverview", "IsDevices", "IsAdministration", "IsDiagnostics", "IsSettings" })
        {
            Assert.Contains($"IsVisible=\"{{Binding {flag}, ElementName=Root}}\"", icon, StringComparison.Ordinal);
        }

        var motion = Read("src", "NutManager.App", "Presentation", "Controls", "NutIconMotion.cs");
        var styles = Read("src", "NutManager.App", "Presentation", "Themes", "NutShellStyles.axaml");
        Assert.DoesNotContain("visual.Offset =", motion, StringComparison.Ordinal);
        Assert.DoesNotContain("translateY(-1px)", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedServerListAndMonitoringShareTheWideRowWithNameBesideHost()
    {
        var view = Read("src", "NutManager.App", "Views", "SettingsPageView.axaml");
        var behavior = Read("src", "NutManager.App", "Views", "SettingsPageView.axaml.cs");

        Assert.Contains("ColumnDefinitions=\"330,16,*\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProfileListPanel\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProfileEditorHeader\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProfileIdentityPanel\" Grid.Row=\"2\" Grid.Column=\"2\"", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProfileMonitoringPanel\"", view, StringComparison.Ordinal);
        var monitoring = view[view.IndexOf("x:Name=\"ProfileMonitoringPanel\"", StringComparison.Ordinal)..];
        Assert.True(monitoring.IndexOf("NameLabel", StringComparison.Ordinal) < monitoring.IndexOf("MonitoringHostLabel", StringComparison.Ordinal));
        Assert.Contains("Position(ProfileEditorHeader, compact ? 0 : 2, compact ? 2 : 0)", behavior, StringComparison.Ordinal);
        Assert.Contains("Position(ProfileListPanel, 0, compact ? 0 : 2)", behavior, StringComparison.Ordinal);
        Assert.Contains("Position(ProfileIdentityPanel, compact ? 0 : 2, compact ? 4 : 2)", behavior, StringComparison.Ordinal);
        Assert.Contains("Position(ProfileEditorPanel, 0, compact ? 6 : 4)", behavior, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteConfigurationContextSharesTheWideNavigationRowAndStacksWhenNarrow()
    {
        var view = Read("src", "NutManager.App", "Views", "NutConfigurationAdministrationView.axaml");
        var behavior = Read("src", "NutManager.App", "Views", "NutConfigurationAdministrationView.axaml.cs");

        var filesRegion = view[view.IndexOf("x:Name=\"ConfigurationFilesRegion\"", StringComparison.Ordinal)..];
        filesRegion = filesRegion[..filesRegion.IndexOf('>')];
        Assert.Contains("Grid.Column=\"0\"", filesRegion, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RemoteConfigurationCard\"", view, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"2\"", view[view.IndexOf("x:Name=\"RemoteConfigurationCard\"", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsRemoteManagementProfile}\"", view, StringComparison.Ordinal);
        Assert.Contains("var wide = Bounds.Width >= 980", behavior, StringComparison.Ordinal);
        Assert.Contains("var sideBySide = wide && filesVisible", behavior, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(RemoteConfigurationCard, sideBySide ? 2 : 0)", behavior, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(RemoteConfigurationCard, filesVisible && !sideBySide ? 2 : 0)", behavior, StringComparison.Ordinal);
        Assert.Contains("Classes=\"nut-card\"", view[view.IndexOf("x:Name=\"RemoteConfigurationCard\"", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Stretch\"", view[view.IndexOf("x:Name=\"RemoteConfigurationCard\"", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.DoesNotContain("RemoteConfigurationCard.MaxWidth", behavior, StringComparison.Ordinal);

        // Only the position changed; the existing visibility predicate remains the sole predicate.
        Assert.Equal(1, view.Split("IsVisible=\"{Binding IsRemoteManagementProfile}\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void AdministrationMenusUseEqualRoundedProductSurfaces()
    {
        var view = Read("src", "NutManager.App", "Views", "AdministrationPageView.axaml");
        var styles = Read("src", "NutManager.App", "Presentation", "Themes", "NutShellStyles.axaml");

        Assert.Contains("<Border Classes=\"nut-card\" Padding=\"8\">", view, StringComparison.Ordinal);
        Assert.Contains("<UniformGrid Columns=\"4\" Rows=\"1\" />", view, StringComparison.Ordinal);
        Assert.Contains("Style Selector=\"Border.nut-file-strip-frame\"", styles, StringComparison.Ordinal);
        Assert.Contains("Property=\"CornerRadius\" Value=\"20\"", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void SavingAProfilePreservesTheSettingsScrollOffset()
    {
        var view = Read("src", "NutManager.App", "Views", "SettingsPageView.axaml");
        var behavior = Read("src", "NutManager.App", "Views", "SettingsPageView.axaml.cs");

        Assert.Contains("x:Name=\"SettingsScrollViewer\"", view, StringComparison.Ordinal);
        Assert.Contains("Click=\"SaveProfileButton_OnClick\"", view, StringComparison.Ordinal);
        Assert.Contains("var offset = SettingsScrollViewer.Offset", behavior, StringComparison.Ordinal);
        Assert.Contains("SettingsScrollViewer.Offset = offset", behavior, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Background", behavior, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistedManagedFileScopeIsForwardedToTheRunningAdministrationContext()
    {
        var app = Read("src", "NutManager.App", "App.axaml.cs");

        Assert.Contains("settingsPage.ProfilePersisted += profile =>", app, StringComparison.Ordinal);
        Assert.Contains("profile.Id == runtimeProfile.Profile.Id", app, StringComparison.Ordinal);
        Assert.Contains(
            "administration.UpdateManagedConfigurationFiles(profile.Management.ManagedFiles)",
            app,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SmbDirectoryHasOneEditableSourceAndAdministrationOnlyValidatesIt()
    {
        var settings = Read("src", "NutManager.App", "Views", "SettingsPageView.axaml");
        var remote = Read("src", "NutManager.App", "Views", "RemoteAccessAdministrationView.axaml");

        Assert.Contains("ProfileDraft.SmbSharePath, Mode=TwoWay", settings, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"{Binding RemoteManagement.IsSmbDirectoryFixed}\"", remote, StringComparison.Ordinal);
        Assert.Contains("Administration.Remote.SmbDirectory.Fixed", remote, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding RemoteManagement.IsSshSftp}\"", remote, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoteValidateDirectoryButton_OnClick", remote, StringComparison.Ordinal);

        var probe = remote.IndexOf("RemoteProbeWriteButton_OnClick", StringComparison.Ordinal);
        var directory = remote.IndexOf("Administration.Remote.Directory]", StringComparison.Ordinal);
        Assert.True(probe >= 0 && directory >= 0 && probe < directory);
        Assert.Contains("IsWriteCapabilityUnverified", remote, StringComparison.Ordinal);
        Assert.Contains("IsWriteCapabilitySupported", remote, StringComparison.Ordinal);
        Assert.Contains("IsWriteCapabilityRejected", remote, StringComparison.Ordinal);
        Assert.Contains("Classes=\"nut-danger\"", remote, StringComparison.Ordinal);
        Assert.Contains("Administration.Remote.SafeWrite.Verify", remote, StringComparison.Ordinal);
        Assert.Contains("Classes=\"nut-success-outline nut-status-locked\"", remote, StringComparison.Ordinal);
        Assert.Contains("Administration.Remote.SafeWrite.Verified", remote, StringComparison.Ordinal);
        Assert.Equal(2, remote.Split("Width=\"176\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, remote.Split("FontWeight=\"Bold\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, remote.Split("HorizontalContentAlignment=\"Center\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("IsEnabled=\"False\"", remote, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding !RemoteManagement.IsWriteCapabilitySupported}\"", remote, StringComparison.Ordinal);

        var directoryCard = remote.IndexOf("<!-- SFTP keeps its directory browser", StringComparison.Ordinal);
        Assert.True(directoryCard >= 0);
        Assert.Contains("IsVisible=\"{Binding RemoteManagement.ShowsDirectoryBrowser}\"",
            remote[directoryCard..], StringComparison.Ordinal);
    }

    [Fact]
    public void StartupRestoresSavedSmbContextAndReadsActualRemoteServiceStateOnce()
    {
        var app = Read("src", "NutManager.App", "App.axaml.cs");

        Assert.Contains("TryConnectAndValidateConfiguredSmbAsync", app, StringComparison.Ordinal);
        Assert.Contains("await remoteWindowsService.RefreshAsync()", app, StringComparison.Ordinal);
        Assert.DoesNotContain("new PeriodicTimer", app, StringComparison.Ordinal);
    }

    [Fact]
    public void BasicAndAdvancedConfigurationOptionsUseProductCards()
    {
        var ups = Read("src", "NutManager.App", "Views", "UpsConfigurationEditorView.axaml");
        var general = Read("src", "NutManager.App", "Views", "NutGeneralConfigurationEditorView.axaml");
        var server = Read("src", "NutManager.App", "Views", "UpsdConfigurationEditorView.axaml");
        var monitoring = Read("src", "NutManager.App", "Views", "UpsmonConfigurationEditorView.axaml");

        Assert.Contains("Classes=\"nut-card\" IsVisible=\"{Binding IsBasicSelected}\"", ups, StringComparison.Ordinal);
        Assert.Contains("Classes=\"nut-card\" IsVisible=\"{Binding ShowAdvanced}\"", ups, StringComparison.Ordinal);
        Assert.Contains("Classes=\"nut-card\" IsVisible=\"{Binding HasGlobalFields}\"", ups, StringComparison.Ordinal);
        Assert.Contains("Classes=\"nut-card\" IsVisible=\"{Binding !ShowAdvanced}\"", general, StringComparison.Ordinal);
        Assert.Contains("Classes=\"nut-card\" IsVisible=\"{Binding ShowAdvanced}\"", general, StringComparison.Ordinal);
        Assert.Contains("<Border Classes=\"nut-card\">", server, StringComparison.Ordinal);
        Assert.Contains("<Border Classes=\"nut-card\">", monitoring, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding BasicFields}\"", monitoring, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding AdvancedFields}\"", monitoring, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadedRemoteFileShowsAuthorizationWarningAndCapabilityRefreshKeepsItsSnapshot()
    {
        var view = Read("src", "NutManager.App", "Views", "NutConfigurationAdministrationView.axaml");
        var viewModel = Read("src", "NutManager.App", "ViewModels", "AdministrationPageViewModel.cs");

        Assert.Contains("RequiresRemoteWriteAuthorization", view, StringComparison.Ordinal);
        Assert.Contains("Administration.Configuration.WriteAuthorizationRequired", view, StringComparison.Ordinal);
        Assert.Contains("preservesLoadedFile", viewModel, StringComparison.Ordinal);
        Assert.Contains("BuildEditorsAsync(snapshot!, CancellationToken.None)", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadSelectedFileAsync(CancellationToken.None", viewModel[
            viewModel.IndexOf("private async void OnRemoteConfigurationContextChanged", StringComparison.Ordinal)..],
            StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsAreOneResponsiveDocumentRatherThanThreeTabs()
    {
        var view = Read("src", "NutManager.App", "Views", "DiagnosticsPageView.axaml");
        var behavior = Read("src", "NutManager.App", "Views", "DiagnosticsPageView.axaml.cs");

        Assert.DoesNotContain("ShowOverviewTabCommand", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowConnectivityTabCommand", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowEnvironmentTabCommand", view, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DiagnosticsSummaryGrid\"", view, StringComparison.Ordinal);
        Assert.Contains("width >= 1120 ? 4 : width >= 650 ? 2 : 1", behavior, StringComparison.Ordinal);
        Assert.Contains("Diagnostics.Group.Overview", view, StringComparison.Ordinal);
        Assert.Contains("Diagnostics.Group.Connection", view, StringComparison.Ordinal);
        Assert.Contains("Diagnostics.Group.Environment", view, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsLeavesRoomBetweenVersionAndRuntime()
    {
        var view = Read("src", "NutManager.App", "Views", "DiagnosticsPageView.axaml");

        var version = view.IndexOf("ApplicationVersion", StringComparison.Ordinal);
        var runtime = view.IndexOf("Diagnostics.Runtime", version, StringComparison.Ordinal);
        Assert.True(version >= 0 && runtime > version);
        Assert.Contains("Width=\"220\" Margin=\"0,0,24,10\"", view[(version - 220)..runtime], StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionShellNoLongerOffersMockModeAndCardCopyUsesTheReadableToken()
    {
        var app = Read("src", "NutManager.App", "App.axaml.cs");
        var settings = Read("src", "NutManager.App", "Views", "SettingsPageView.axaml");
        var shell = Read("src", "NutManager.App", "MainWindow.axaml");
        var typography = Read("src", "NutManager.App", "Presentation", "Themes", "NutTypography.axaml");

        Assert.DoesNotContain("new MockNutClient", app, StringComparison.Ordinal);
        Assert.DoesNotContain("MockModeLabel", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("SimulationText", shell, StringComparison.Ordinal);
        Assert.Contains("Border.nut-card TextBlock.nut-metadata", typography, StringComparison.Ordinal);
        Assert.Contains("NutCardSmallTextBrush", typography, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) => Repository.Read(Path.Combine(segments));
}
