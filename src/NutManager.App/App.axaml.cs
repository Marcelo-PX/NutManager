using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using NutManager.App.Services;
using NutManager.App.ViewModels;

namespace NutManager.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var themePreferenceStore = new ThemePreferenceStore();
            var viewModel = new MainWindowViewModel(themePreferenceStore.Load());

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
