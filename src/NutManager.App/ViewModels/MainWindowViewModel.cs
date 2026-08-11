using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NutManager.App.Localization;
using NutManager.Core.Models;

namespace NutManager.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<AppPage, PageViewModel> _pages;
    private readonly OverviewPageViewModel _overviewPage;
    private readonly bool _isMockModeConfigured;
    private readonly string? _activeEndpoint;
    private bool _isOverlayOpen;

    public MainWindowViewModel(ThemePreference themePreference = ThemePreference.System)
        : this(themePreference, new OverviewPageViewModel(), new DevicesPageViewModel(), new SettingsPageViewModel())
    {
    }

    public MainWindowViewModel(ThemePreference themePreference, OverviewPageViewModel overviewPage)
        : this(themePreference, overviewPage, new DevicesPageViewModel(), new SettingsPageViewModel())
    {
    }

    public MainWindowViewModel(
        ThemePreference themePreference,
        OverviewPageViewModel overviewPage,
        DevicesPageViewModel devicesPage,
        SettingsPageViewModel? settingsPage = null,
        DiagnosticsPageViewModel? diagnosticsPage = null,
        AdministrationPageViewModel? administrationPage = null,
        UiLanguagePreference language = UiLanguagePreference.PtBr,
        SidebarPreference sidebarPreference = SidebarPreference.Expanded,
        bool mockMode = false,
        string? activeEndpoint = null)
    {
        ArgumentNullException.ThrowIfNull(overviewPage);
        ArgumentNullException.ThrowIfNull(devicesPage);

        _overviewPage = overviewPage;
        _isMockModeConfigured = mockMode;
        _activeEndpoint = string.IsNullOrWhiteSpace(activeEndpoint) ? null : activeEndpoint;
        _language = language;
        _sidebarPreference = sidebarPreference;
        Localizer = new NutManagerLocalizer(language);
        _pages = new Dictionary<AppPage, PageViewModel>
        {
            [AppPage.Overview] = overviewPage,
            [AppPage.Devices] = devicesPage,
            [AppPage.Administration] = administrationPage ?? new AdministrationPageViewModel(),
            [AppPage.Diagnostics] = diagnosticsPage ?? new DiagnosticsPageViewModel(),
            [AppPage.Settings] = settingsPage ?? new SettingsPageViewModel()
        };

        NavigationItems = new List<NavigationItemViewModel>
        {
            CreateNavigationItem(AppPage.Overview, "Nav.Overview", "⌂"),
            CreateNavigationItem(AppPage.Devices, "Nav.Devices", "▣"),
            CreateNavigationItem(AppPage.Administration, "Nav.Administration", "⚙"),
            CreateNavigationItem(AppPage.Diagnostics, "Nav.Diagnostics", "ⓘ"),
            CreateNavigationItem(AppPage.Settings, "Nav.Settings", "⚙")
        };
        ThemeOptions =
        [
            new ThemeOption(ThemePreference.System, Localizer.Get("Theme.System")),
            new ThemeOption(ThemePreference.Light, Localizer.Get("Theme.Light")),
            new ThemeOption(ThemePreference.Dark, Localizer.Get("Theme.Dark"))
        ];
        _selectedThemeOption = ThemeOptions.Single(option => option.Preference == themePreference);
        _selectedPage = AppPage.Overview;
        _currentPage = _pages[AppPage.Overview];
        _overviewPage.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(OverviewPageViewModel.ConnectionState) or nameof(OverviewPageViewModel.DataFreshness) or nameof(OverviewPageViewModel.Snapshot))
            {
                OnPropertyChanged(nameof(ConnectionPresentation));
                OnPropertyChanged(nameof(ConnectionStatusText));
                OnPropertyChanged(nameof(ConnectionTooltip));
                OnPropertyChanged(nameof(IsConnectionHealthy));
                OnPropertyChanged(nameof(IsConnectionPending));
                OnPropertyChanged(nameof(IsConnectionCritical));
                OnPropertyChanged(nameof(IsConnectionUnavailable));
                OnPropertyChanged(nameof(IsMockMode));
            }
        };
        UpdateNavigationSelection();
    }

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }
    public IReadOnlyList<ThemeOption> ThemeOptions { get; }
    public NutManagerLocalizer Localizer { get; private set; }

    [ObservableProperty] private AppPage _selectedPage;
    [ObservableProperty] private PageViewModel _currentPage;
    [ObservableProperty] private ThemeOption? _selectedThemeOption;
    [ObservableProperty] private UiLanguagePreference _language;
    [ObservableProperty] private SidebarPreference _sidebarPreference;
    [ObservableProperty] private ShellLayoutState _shellLayout = ShellLayoutState.Wide;

    public ThemePreference SelectedTheme => SelectedThemeOption?.Preference ?? ThemePreference.System;
    public SidebarDisplayState SidebarDisplay => ShellPresentationMapper.SidebarFor(ShellLayout, SidebarPreference);
    public ReviewDrawerDisplayState ReviewDrawerDisplay => ReviewDrawerDisplayState.Hidden;
    public bool IsSidebarExpanded => SidebarDisplay == SidebarDisplayState.Expanded;
    public bool IsSidebarCollapsed => SidebarDisplay == SidebarDisplayState.Collapsed;
    public bool IsSidebarOverlay => SidebarDisplay == SidebarDisplayState.Overlay;
    public bool IsOverlayOpen => IsSidebarOverlay && _isOverlayOpen;
    public double SidebarWidth => IsSidebarExpanded ? 212 : IsSidebarCollapsed ? 68 : 0;
    public bool IsMockMode => _isMockModeConfigured || _overviewPage.IsSimulated;
    public string? ActiveEndpoint => _activeEndpoint;
    public bool HasActiveEndpoint => _activeEndpoint is not null;
    public ConnectionPresentationState ConnectionPresentation => ShellPresentationMapper.ConnectionFor(_overviewPage.ConnectionState, _overviewPage.DataFreshness, true);
    public string ConnectionStatusText => ConnectionPresentation switch
    {
        ConnectionPresentationState.Healthy => Localizer.Get("Status.Connected"),
        ConnectionPresentationState.Pending => _overviewPage.ConnectionState == ConnectionState.Reconnecting ? Localizer.Get("Status.Reconnecting") : Localizer.Get("Status.Connecting"),
        ConnectionPresentationState.Warning => Localizer.Get("Status.Stale"),
        ConnectionPresentationState.Critical => _overviewPage.ConnectionState == ConnectionState.ConnectionFailed ? Localizer.Get("Status.ConnectionFailed") : Localizer.Get("Status.Disconnected"),
        _ => Localizer.Get("Status.Unavailable")
    };
    public string ConnectionTooltip => ConnectionStatusText;
    public string ApplicationName => Localizer.Get("App.Name");
    public bool IsConnectionHealthy => ConnectionPresentation == ConnectionPresentationState.Healthy;
    public bool IsConnectionPending => ConnectionPresentation is ConnectionPresentationState.Pending or ConnectionPresentationState.Warning;
    public bool IsConnectionCritical => ConnectionPresentation == ConnectionPresentationState.Critical;
    public bool IsConnectionUnavailable => ConnectionPresentation == ConnectionPresentationState.Unavailable;
    public string ThemeToggleSymbol => SelectedTheme == ThemePreference.Dark ? "☀" : "☾";
    public string ThemeToggleName => Localizer.Get("Shell.ToggleTheme");
    public string NavigationToggleName => Localizer.Get("Shell.ToggleNavigation");
    public string SimulationText => Localizer.Get("Shell.SimulationActive");

    public event Action<ThemePreference>? ThemeChanged;
    public event Action<SidebarPreference>? SidebarPreferenceChanged;

    public void SetTheme(ThemePreference preference)
    {
        var option = ThemeOptions.Single(option => option.Preference == preference);
        if (!Equals(SelectedThemeOption, option)) SelectedThemeOption = option;
    }

    public void UpdateLayoutWidth(double width) => ShellLayout = ShellPresentationMapper.LayoutFor(width);

    [RelayCommand]
    private void Navigate(AppPage page)
    {
        SelectedPage = page;
        CurrentPage = _pages[page];
        UpdateNavigationSelection();
        if (IsSidebarOverlay)
        {
            _isOverlayOpen = false;
            OnPropertyChanged(nameof(IsOverlayOpen));
        }
    }

    [RelayCommand]
    private void ToggleNavigation()
    {
        if (IsSidebarOverlay)
        {
            _isOverlayOpen = !_isOverlayOpen;
            OnPropertyChanged(nameof(IsOverlayOpen));
            return;
        }

        SidebarPreference = SidebarPreference == SidebarPreference.Expanded ? SidebarPreference.Collapsed : SidebarPreference.Expanded;
    }

    [RelayCommand]
    private void ToggleTheme(bool effectiveDark) => SetTheme(SelectedTheme switch
    {
        ThemePreference.Light => ThemePreference.Dark,
        ThemePreference.Dark => ThemePreference.Light,
        _ => effectiveDark ? ThemePreference.Light : ThemePreference.Dark
    });

    partial void OnSelectedThemeOptionChanged(ThemeOption? value)
    {
        if (value is not null)
        {
            OnPropertyChanged(nameof(SelectedTheme));
            OnPropertyChanged(nameof(ThemeToggleSymbol));
            ThemeChanged?.Invoke(value.Preference);
        }
    }

    partial void OnSidebarPreferenceChanged(SidebarPreference value)
    {
        NotifyShellProperties();
        SidebarPreferenceChanged?.Invoke(value);
    }

    partial void OnShellLayoutChanged(ShellLayoutState value)
    {
        _isOverlayOpen = false;
        NotifyShellProperties();
    }

    private NavigationItemViewModel CreateNavigationItem(AppPage page, string resourceKey, string symbol) =>
        new(page, Localizer.Get(resourceKey), symbol, new RelayCommand(() => Navigate(page)));

    private void UpdateNavigationSelection()
    {
        foreach (var item in NavigationItems) item.IsSelected = item.Page == SelectedPage;
    }

    private void NotifyShellProperties()
    {
        OnPropertyChanged(nameof(SidebarDisplay));
        OnPropertyChanged(nameof(ReviewDrawerDisplay));
        OnPropertyChanged(nameof(IsSidebarExpanded));
        OnPropertyChanged(nameof(IsSidebarCollapsed));
        OnPropertyChanged(nameof(IsSidebarOverlay));
        OnPropertyChanged(nameof(IsOverlayOpen));
        OnPropertyChanged(nameof(SidebarWidth));
    }
}
