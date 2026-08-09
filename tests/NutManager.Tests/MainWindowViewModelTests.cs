using NutManager.App.ViewModels;
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
}
