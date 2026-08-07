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
            var endpoint = new NutEndpoint("mock.nut.local");
            var overview = new OverviewPageViewModel(
                mockClient,
                endpoint,
                "mockups",
                mockClient.ConnectionState,
                mockClient.DataFreshness);
            var devices = new DevicesPageViewModel(mockClient, endpoint);
            var viewModel = new MainWindowViewModel(themePreferenceStore.Load(), overview, devices);

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
            _ = devices.InitializeAsync();
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
