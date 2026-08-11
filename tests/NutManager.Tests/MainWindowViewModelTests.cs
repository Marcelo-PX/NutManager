using NutManager.App.ViewModels;
using NutManager.Core.Models;
using Xunit;

namespace NutManager.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void StartsOnOverviewPage()
    {
        var viewModel = new MainWindowViewModel();

        Assert.Equal(AppPage.Overview, viewModel.SelectedPage);
        Assert.IsType<OverviewPageViewModel>(viewModel.CurrentPage);
        Assert.True(viewModel.NavigationItems.Single(item => item.Page == AppPage.Overview).IsSelected);
    }

    [Fact]
    public void NavigateCommandChangesTheSelectedPage()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.NavigateCommand.Execute(AppPage.Diagnostics);

        Assert.Equal(AppPage.Diagnostics, viewModel.SelectedPage);
        Assert.IsType<DiagnosticsPageViewModel>(viewModel.CurrentPage);
        Assert.True(viewModel.NavigationItems.Single(item => item.Page == AppPage.Diagnostics).IsSelected);
        Assert.False(viewModel.NavigationItems.Single(item => item.Page == AppPage.Overview).IsSelected);
    }

    [Fact]
    public void AdministrationIsIncludedInNavigationAndOpensItsPageViewModel()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.NavigateCommand.Execute(AppPage.Administration);

        Assert.Equal(AppPage.Administration, viewModel.SelectedPage);
        Assert.IsType<AdministrationPageViewModel>(viewModel.CurrentPage);
        Assert.True(viewModel.NavigationItems.Single(item => item.Page == AppPage.Administration).IsSelected);
        Assert.Equal(
            [AppPage.Overview, AppPage.Devices, AppPage.Administration, AppPage.Diagnostics, AppPage.Settings],
            viewModel.NavigationItems.Select(item => item.Page));
    }

    [Fact]
    public void NavigationTogglePersistsCollapsedPreferenceOutsideCompactLayout()
    {
        var viewModel = CreateShell(sidebarPreference: SidebarPreference.Expanded);

        viewModel.ToggleNavigationCommand.Execute(null);

        Assert.Equal(SidebarPreference.Collapsed, viewModel.SidebarPreference);
        Assert.Equal(SidebarDisplayState.Collapsed, viewModel.SidebarDisplay);
    }

    [Fact]
    public void CompactOverlayDoesNotReplaceTheSavedSidebarPreference()
    {
        var viewModel = CreateShell(sidebarPreference: SidebarPreference.Expanded);
        viewModel.UpdateLayoutWidth(859);

        viewModel.ToggleNavigationCommand.Execute(null);

        Assert.Equal(SidebarPreference.Expanded, viewModel.SidebarPreference);
        Assert.True(viewModel.IsOverlayOpen);
        Assert.Equal(SidebarDisplayState.Overlay, viewModel.SidebarDisplay);
    }

    [Fact]
    public void HeaderThemeToggleMakesSystemPreferenceExplicitUsingEffectiveTheme()
    {
        var viewModel = new MainWindowViewModel(ThemePreference.System);

        viewModel.ToggleThemeCommand.Execute(true);

        Assert.Equal(ThemePreference.Light, viewModel.SelectedTheme);
    }

    private static MainWindowViewModel CreateShell(SidebarPreference sidebarPreference) => new(
        ThemePreference.System,
        new OverviewPageViewModel(),
        new DevicesPageViewModel(),
        sidebarPreference: sidebarPreference);
}
