using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using NutManager.App.Services;
using NutManager.App.ViewModels;
using NutManager.Core.Models;
using NutManager.Infrastructure.Mock;

namespace NutManager.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var themePreferenceStore = new ThemePreferenceStore();
            var mockClient = new MockNutClient(MockScenario.Online, DateTimeOffset.UtcNow);
            var overview = new OverviewPageViewModel(
                mockClient,
                new NutEndpoint("mock.nut.local"),
                "mockups",
                mockClient.ConnectionState,
                mockClient.DataFreshness);
            var viewModel = new MainWindowViewModel(themePreferenceStore.Load(), overview);

            ApplyTheme(viewModel.SelectedTheme);
            viewModel.ThemeChanged += preference =>
            {
                ApplyTheme(preference);
                themePreferenceStore.Save(preference);
            };

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            _ = overview.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyTheme(ThemePreference preference)
    {
        RequestedThemeVariant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
