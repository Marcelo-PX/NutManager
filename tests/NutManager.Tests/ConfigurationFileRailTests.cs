using NutManager.App.Services;
using NutManager.App.ViewModels;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Infrastructure.Persistence;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// The collapsible file rail. The property worth defending is that folding it is presentation and
/// nothing else: the selected file, its draft and its editor must come through a collapse
/// untouched, because the rail exists to give the form more room, not to change what is being
/// edited.
/// </summary>
public sealed class ConfigurationFileRailTests
{
    private static string RailStyles() =>
        Repository.Read(Path.Combine("src", "NutManager.App", "Presentation", "Themes", "NutShellStyles.axaml"));

    private static string RailView() =>
        Repository.Read(Path.Combine("src", "NutManager.App", "Views", "NutConfigurationAdministrationView.axaml"));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nutmanager-rail-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    // ==================== State ====================

    [Fact]
    public void TheRailStartsExpanded()
    {
        var viewModel = new AdministrationPageViewModel();

        Assert.True(viewModel.IsConfigurationRailExpanded);
        Assert.Equal(228, viewModel.ConfigurationRailWidth);
    }

    [Fact]
    public void TogglingSwitchesBetweenTheTwoWidths()
    {
        var viewModel = new AdministrationPageViewModel();

        viewModel.ToggleConfigurationRailCommand.Execute(null);

        Assert.False(viewModel.IsConfigurationRailExpanded);
        Assert.Equal(64, viewModel.ConfigurationRailWidth);

        viewModel.ToggleConfigurationRailCommand.Execute(null);

        Assert.True(viewModel.IsConfigurationRailExpanded);
        Assert.Equal(228, viewModel.ConfigurationRailWidth);
    }

    [Fact]
    public void TheToggleIsDescribedByWhatItWillDo()
    {
        var viewModel = new AdministrationPageViewModel();
        var whenExpanded = viewModel.ConfigurationRailToggleText;

        viewModel.ToggleConfigurationRailCommand.Execute(null);

        Assert.NotEqual(whenExpanded, viewModel.ConfigurationRailToggleText);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ConfigurationRailToggleText));
    }

    [Fact]
    public void CollapsingTellsWhoeverIsPersistingTheChoice()
    {
        var written = new List<SidebarPreference>();
        var viewModel = new AdministrationPageViewModel(
            null, null, persistConfigurationRailPreference: written.Add);

        viewModel.ToggleConfigurationRailCommand.Execute(null);
        viewModel.ToggleConfigurationRailCommand.Execute(null);

        Assert.Equal([SidebarPreference.Collapsed, SidebarPreference.Expanded], written);
    }

    [Theory]
    [InlineData(SidebarPreference.Expanded, true)]
    [InlineData(SidebarPreference.Collapsed, false)]
    public void ThePersistedChoiceIsHonouredOnOpen(SidebarPreference preference, bool expanded)
    {
        var viewModel = new AdministrationPageViewModel(null, null, configurationRailPreference: preference);

        Assert.Equal(expanded, viewModel.IsConfigurationRailExpanded);
    }

    // ==================== Persistence ====================

    [Fact]
    public async Task TheRailChoiceSurvivesARoundTripThroughSettings()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonApplicationSettingsStore(directory.Path);

        await store.SaveAsync(
            new ApplicationSettings(configurationRailPreference: SidebarPreference.Collapsed),
            CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(SidebarPreference.Collapsed, loaded.ConfigurationRailPreference);
    }

    [Fact]
    public async Task SettingsWrittenBeforeThisPreferenceExistedOpenExpanded()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "settings.json"),
            """
            {"schemaVersion":3,"pollingIntervalSeconds":5,"connectionTimeoutSeconds":5,
             "theme":"System","mockMode":false,"language":"PtBr","sidebarPreference":"Collapsed"}
            """);

        var loaded = await new JsonApplicationSettingsStore(directory.Path).LoadAsync(CancellationToken.None);

        // The shell sidebar keeps its own collapsed choice; the rail, which did not exist then,
        // opens expanded rather than inheriting an unrelated preference.
        Assert.Equal(SidebarPreference.Collapsed, loaded.SidebarPreference);
        Assert.Equal(SidebarPreference.Expanded, loaded.ConfigurationRailPreference);
    }

    // ==================== Folding changes nothing else ====================

    [Fact]
    public void FoldingTheRailLeavesTheFileListExactlyAsItWas()
    {
        var viewModel = new AdministrationPageViewModel();
        var before = viewModel.ConfigurationFiles.ToArray();
        var selected = viewModel.SelectedFile;

        viewModel.ToggleConfigurationRailCommand.Execute(null);
        viewModel.ToggleConfigurationRailCommand.Execute(null);

        Assert.Equal(before, viewModel.ConfigurationFiles);
        Assert.Same(selected, viewModel.SelectedFile);
        Assert.False(viewModel.HasDraftChanges);
    }

    [Fact]
    public void FoldingTheRailNeverTouchesTheEditorOrAPendingDraft()
    {
        var viewModel = new AdministrationPageViewModel();

        viewModel.ToggleConfigurationRailCommand.Execute(null);

        // Collapsing is a width change. Nothing about the editing surface may react to it.
        Assert.Null(viewModel.UpsConfigurationEditor);
        Assert.Null(viewModel.NutGeneralConfigurationEditor);
        Assert.False(viewModel.HasPreview);
        Assert.False(viewModel.HasDraftChanges);
    }

    // ==================== Rows ====================

    [Fact]
    public void EveryRowCarriesItsOwnIconFlagSoNoTwoFilesLookAlike()
    {
        var viewModel = new AdministrationPageViewModel();

        foreach (var file in viewModel.ConfigurationFiles)
        {
            var flags = new[] { file.IsNutConf, file.IsUpsConf, file.IsUpsdConf, file.IsUpsdUsers, file.IsUpsmonConf };
            Assert.Single(flags, flag => flag);
        }
    }

    [Fact]
    public void ARowIsAnnouncedByItsPurposeAndItsRealFileName()
    {
        var viewModel = new AdministrationPageViewModel();

        foreach (var file in viewModel.ConfigurationFiles)
        {
            // Collapsed, the row is only an icon, so the accessible name is the whole answer.
            Assert.Contains(file.FileName, file.AccessibleName, StringComparison.Ordinal);
            Assert.Contains(file.Category, file.AccessibleName, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRailListsOnlyTheFilesTheProfileManages()
    {
        var profile = new ManagedNutServerProfile(
            Guid.NewGuid(),
            "Subset",
            new NutMonitoringProfile("monitor.example"),
            new NutManagementProfile(
                NutManagementMode.Local,
                managedFiles: ManagedNutConfigurationFiles.Create(
                    [NutConfigurationFileKind.NutConf, NutConfigurationFileKind.UpsmonConf])),
            ManagedNutServerAccessMode.Manage);
        var context = ManagedNutServerRuntimeContext.FromProfiles(
            new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]),
            new ApplicationSettings());

        var viewModel = new AdministrationPageViewModel(null, null, profileContext: context);

        Assert.Equal(
            ["nut.conf", "upsmon.conf"],
            viewModel.ConfigurationFiles.Select(file => file.FileName));
    }

    [Fact]
    public void AProfileThatManagesNothingLeavesAnEmptyRailRatherThanInventingAFile()
    {
        var profile = new ManagedNutServerProfile(
            Guid.NewGuid(),
            "None",
            new NutMonitoringProfile("monitor.example"),
            new NutManagementProfile(NutManagementMode.Local, managedFiles: ManagedNutConfigurationFiles.Create([])),
            ManagedNutServerAccessMode.Manage);
        var context = ManagedNutServerRuntimeContext.FromProfiles(
            new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]),
            new ApplicationSettings());

        var viewModel = new AdministrationPageViewModel(null, null, profileContext: context);

        Assert.Empty(viewModel.ConfigurationFiles);
        Assert.True(viewModel.IsConfigurationFileListEmpty);
        // The rail still folds; an empty list is not a broken screen.
        viewModel.ToggleConfigurationRailCommand.Execute(null);
        Assert.False(viewModel.IsConfigurationRailExpanded);
    }

    // ==================== Presentation ====================

    [Fact]
    public void TheRailAnimatesItsWidthWithinTheShellsOwnMotionBudget()
    {
        var styles = RailStyles();

        var rail = styles[styles.IndexOf("Border.nut-file-rail\"", StringComparison.Ordinal)..];
        rail = rail[..rail.IndexOf("</Style>", StringComparison.Ordinal)];

        Assert.Contains("DoubleTransition Property=\"Width\"", rail, StringComparison.Ordinal);
        Assert.Contains("CubicEaseOut", rail, StringComparison.Ordinal);
        // 0.22s matches NutMotionShell; anything longer starts to feel like the panel is stuck.
        Assert.Contains("0:0:0.22", rail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRailReusesTheShellsSelectionLanguageRatherThanInventingItsOwn()
    {
        var styles = RailStyles();

        // Same accent bar and sheen the navigation item uses, so the two rails read as one idea.
        Assert.Contains("Button.nut-file-rail-item.selected", styles, StringComparison.Ordinal);
        Assert.Contains("NutSelectedSheenBrush", styles, StringComparison.Ordinal);
        Assert.Contains("NutAccentBrush", styles, StringComparison.Ordinal);
        // Selection is never colour alone: the label also goes semibold.
        var selected = styles[styles.IndexOf("Button.nut-file-rail-item.selected\"", StringComparison.Ordinal)..];
        Assert.Contains("FontWeight", selected[..selected.IndexOf("</Style>", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Fact]
    public void NothingInTheRailLoopsForever()
    {
        // The connection light stays the only continuous animation in the application.
        Assert.DoesNotContain("IterationCount=\"Infinite\"", RailStyles(), StringComparison.Ordinal);
        Assert.DoesNotContain("IterationCount=\"Infinite\"", RailView(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheToggleAndEveryRowAreReachableAndNamed()
    {
        var view = RailView();

        Assert.Contains("Command=\"{Binding ToggleConfigurationRailCommand}\"", view, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding ConfigurationRailToggleText}\"", view, StringComparison.Ordinal);
        // Rows are buttons, so they take keyboard focus, and each announces its file.
        Assert.Contains("AutomationProperties.Name=\"{Binding AccessibleName}\"", view, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding AccessibleName}\"", view, StringComparison.Ordinal);
    }
}
