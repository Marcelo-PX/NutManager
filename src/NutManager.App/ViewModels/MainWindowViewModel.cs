using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NutManager.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<AppPage, PageViewModel> _pages;

    public MainWindowViewModel(ThemePreference themePreference = ThemePreference.System)
        : this(themePreference, new OverviewPageViewModel())
    {
    }

    public MainWindowViewModel(ThemePreference themePreference, OverviewPageViewModel overviewPage)
        : this(themePreference, overviewPage, new DevicesPageViewModel())
    {
    }

    public MainWindowViewModel(
        ThemePreference themePreference,
        OverviewPageViewModel overviewPage,
        DevicesPageViewModel devicesPage)
    {
        ArgumentNullException.ThrowIfNull(overviewPage);
        ArgumentNullException.ThrowIfNull(devicesPage);

        _pages = new Dictionary<AppPage, PageViewModel>
        {
            [AppPage.Overview] = overviewPage,
            [AppPage.Devices] = devicesPage,
            [AppPage.Diagnostics] = new DiagnosticsPageViewModel(),
            [AppPage.Settings] = new SettingsPageViewModel()
        };

        NavigationItems = new List<NavigationItemViewModel>
        {
            CreateNavigationItem(AppPage.Overview, "Visão geral", "⌂"),
            CreateNavigationItem(AppPage.Devices, "Dispositivos", "▣"),
            CreateNavigationItem(AppPage.Diagnostics, "Diagnóstico", "ⓘ"),
            CreateNavigationItem(AppPage.Settings, "Configurações", "⚙")
        };

        ThemeOptions = new List<ThemeOption>
        {
            new(ThemePreference.System, "Seguir o sistema"),
            new(ThemePreference.Light, "Claro"),
            new(ThemePreference.Dark, "Escuro")
        };

        _selectedThemeOption = ThemeOptions.Single(option => option.Preference == themePreference);
        _selectedPage = AppPage.Overview;
        _currentPage = _pages[AppPage.Overview];
        UpdateNavigationSelection();
    }

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    [ObservableProperty]
    private AppPage _selectedPage;

    [ObservableProperty]
    private PageViewModel _currentPage;

    [ObservableProperty]
    private ThemeOption? _selectedThemeOption;

    public ThemePreference SelectedTheme => SelectedThemeOption?.Preference ?? ThemePreference.System;

    public event Action<ThemePreference>? ThemeChanged;

    [RelayCommand]
    private void Navigate(AppPage page)
    {
        SelectedPage = page;
        CurrentPage = _pages[page];
        UpdateNavigationSelection();
    }

    partial void OnSelectedThemeOptionChanged(ThemeOption? value)
    {
        if (value is not null)
        {
            OnPropertyChanged(nameof(SelectedTheme));
            ThemeChanged?.Invoke(value.Preference);
        }
    }

    private NavigationItemViewModel CreateNavigationItem(AppPage page, string title, string symbol) =>
        new(page, title, symbol, new RelayCommand(() => Navigate(page)));

    private void UpdateNavigationSelection()
    {
        foreach (var item in NavigationItems)
        {
            item.IsSelected = item.Page == SelectedPage;
        }
    }
}
