using NutManager.App.Services;
using NutManager.App.ViewModels;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Infrastructure.Persistence;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// The configuration file switcher: one segmented strip of square tiles above the editor.
///
/// It used to be a collapsible column, and most of this file used to defend the fold — that
/// collapsing was presentation and nothing else. The fold is gone, along with its toggle, its
/// persisted preference and the width threshold that folded it regardless, so what is left to
/// defend is what the strip still owes: one tile per managed file, each announcing itself, joined
/// into a single control rather than scattered into five.
/// </summary>
public sealed class ConfigurationFileRailTests
{
    private static string RailStyles() =>
        Repository.Read(Path.Combine("src", "NutManager.App", "Presentation", "Themes", "NutShellStyles.axaml"));

    private static string RailView() =>
        Repository.Read(Path.Combine("src", "NutManager.App", "Views", "NutConfigurationAdministrationView.axaml"));

    // ==================== Tiles ====================

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
            // The tile shows the category under its icon, not the file name, so the accessible
            // name is what carries both.
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
    }

    // ==================== Presentation ====================

    [Fact]
    public void TheRailReusesTheShellsSelectionLanguageRatherThanInventingItsOwn()
    {
        var styles = RailStyles();

        // Same accent and sheen the shell's own selection uses, so the section tabs, the file chips
        // and the sidebar read as one idea rather than three components that happen to be nearby.
        Assert.Contains("Button.nut-file-tile.selected", styles, StringComparison.Ordinal);
        Assert.Contains("ListBox.nut-section-tabs ListBoxItem:selected", styles, StringComparison.Ordinal);
        Assert.Contains("NutSelectedSheenBrush", styles, StringComparison.Ordinal);
        Assert.Contains("NutAccentBrush", styles, StringComparison.Ordinal);
        // Selection is never colour alone: the label also goes semibold.
        var selected = styles[styles.IndexOf("Button.nut-file-tile.selected\"", StringComparison.Ordinal)..];
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
    public void EveryTileIsReachableAndNamed()
    {
        var view = RailView();

        // Tiles are buttons, so they take keyboard focus, and each announces its file. The label
        // under the icon is not enough on its own: it is the category, not the file name.
        Assert.Contains("AutomationProperties.Name=\"{Binding AccessibleName}\"", view, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding AccessibleName}\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTilesAreOneJoinedControlRatherThanFiveSeparateButtons()
    {
        var view = RailView();
        var styles = RailStyles();

        // One frame around a horizontal run of tiles. A WrapPanel would break the joined edge the
        // moment it wrapped, so the strip is a StackPanel inside a clipping frame.
        Assert.Contains("Classes=\"nut-file-strip-frame\"", view, StringComparison.Ordinal);
        Assert.Contains("<ItemsPanelTemplate><StackPanel Orientation=\"Horizontal\" /></ItemsPanelTemplate>", view, StringComparison.Ordinal);

        var tile = styles[styles.IndexOf("Button.nut-file-tile\"", StringComparison.Ordinal)..];
        tile = tile[..tile.IndexOf("</Style>", StringComparison.Ordinal)];
        // Butted together: no gap between neighbours, no rounding of their own, and a hairline on
        // one side only so adjacent tiles share a single divider rather than drawing two.
        Assert.Contains("<Setter Property=\"Margin\" Value=\"0\" />", tile, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"0\" />", tile, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"BorderThickness\" Value=\"0,0,1,0\" />", tile, StringComparison.Ordinal);

        // The frame clips, and the strip overhangs by the one pixel the last divider occupies, so
        // the run ends on the frame's own edge rather than on a stray hairline.
        var frame = styles[styles.IndexOf("Border.nut-file-strip-frame\"", StringComparison.Ordinal)..];
        frame = frame[..frame.IndexOf("</Style>", StringComparison.Ordinal)];
        Assert.Contains("<Setter Property=\"ClipToBounds\" Value=\"True\" />", frame, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Margin\" Value=\"0,0,-1,0\" />", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsLeftOfTheFoldThatWasRemoved()
    {
        var view = RailView();
        var styles = RailStyles();
        var viewModel = Repository.Read(Path.Combine("src", "NutManager.App", "ViewModels", "AdministrationPageViewModel.cs"));

        // The toggle, the preference and the width threshold went together. Any one of them left
        // behind is dead state that still has to be reasoned about when reading the page.
        foreach (var gone in new[]
        {
            "ToggleConfigurationRail", "IsConfigurationRailExpanded", "IsConfigurationRailOpen",
            "ConfigurationRailToggleText", "SetConfigurationLayoutWidth", "ConfigurationFileTileSize"
        })
        {
            Assert.DoesNotContain(gone, view, StringComparison.Ordinal);
            Assert.DoesNotContain(gone, styles, StringComparison.Ordinal);
            Assert.DoesNotContain(gone, viewModel, StringComparison.Ordinal);
        }

        // And it is no longer written to disk either, so a settings file stops carrying a
        // preference for a control that does not exist.
        Assert.DoesNotContain(
            "ConfigurationRailPreference",
            Repository.Read(Path.Combine("src", "NutManager.Core", "Models", "ApplicationSettings.cs")),
            StringComparison.Ordinal);
    }
}
