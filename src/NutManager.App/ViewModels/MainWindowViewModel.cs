using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using NutManager.App.Localization;
using NutManager.Core.Models;

namespace NutManager.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<AppPage, PageViewModel> _pages;
    private readonly OverviewPageViewModel _overviewPage;
    private readonly bool _isMockModeConfigured;
    private readonly string? _activeEndpoint;
    private readonly string? _activeProfileName;
    private readonly NutManagementMode? _managementMode;
    private readonly ManagedNutServerAccessMode? _accessMode;
    private readonly string? _preferredUpsName;
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
        string? activeEndpoint = null,
        string? activeProfileName = null,
        NutManagementMode? managementMode = null,
        ManagedNutServerAccessMode? accessMode = null,
        string? preferredUpsName = null)
    {
        ArgumentNullException.ThrowIfNull(overviewPage);
        ArgumentNullException.ThrowIfNull(devicesPage);

        _overviewPage = overviewPage;
        _isMockModeConfigured = mockMode;
        _activeEndpoint = string.IsNullOrWhiteSpace(activeEndpoint) ? null : activeEndpoint;
        _activeProfileName = string.IsNullOrWhiteSpace(activeProfileName) ? null : activeProfileName;
        _managementMode = managementMode;
        _accessMode = accessMode;
        _preferredUpsName = string.IsNullOrWhiteSpace(preferredUpsName) ? null : preferredUpsName;
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
            CreateNavigationItem(AppPage.Overview, "Nav.Overview"),
            CreateNavigationItem(AppPage.Devices, "Nav.Devices"),
            CreateNavigationItem(AppPage.Administration, "Nav.Administration"),
            CreateNavigationItem(AppPage.Diagnostics, "Nav.Diagnostics"),
            CreateNavigationItem(AppPage.Settings, "Nav.Settings")
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
                OnPropertyChanged(nameof(ConnectionSummaryText));
                OnPropertyChanged(nameof(ActiveUpsName));
                OnPropertyChanged(nameof(ConnectionDetailText));
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
    [ObservableProperty] private bool _isEffectiveDark;

    public ThemePreference SelectedTheme => SelectedThemeOption?.Preference ?? ThemePreference.System;
    public SidebarDisplayState SidebarDisplay => ShellPresentationMapper.SidebarFor(ShellLayout, SidebarPreference);
    public ReviewDrawerDisplayState ReviewDrawerDisplay => ReviewDrawerDisplayState.Hidden;
    public bool IsSidebarExpanded => SidebarDisplay == SidebarDisplayState.Expanded;
    public bool IsSidebarCollapsed => SidebarDisplay == SidebarDisplayState.Collapsed;
    public bool IsSidebarOverlay => SidebarDisplay == SidebarDisplayState.Overlay;
    public bool IsWideLayout => ShellLayout == ShellLayoutState.Wide;
    public bool IsCompactLayout => ShellLayout == ShellLayoutState.Compact;
    public bool IsOverlayOpen => IsSidebarOverlay && _isOverlayOpen;
    public bool IsBackgroundInteractionEnabled => !IsOverlayOpen;
    public bool IsNavigationToggleVisible => ShellLayout != ShellLayoutState.Medium;
    public double SidebarWidth => IsSidebarExpanded ? 220 : IsSidebarCollapsed ? 72 : 0;
    public Thickness ContentPadding => ShellLayout switch
    {
        ShellLayoutState.Wide => new Thickness(28),
        ShellLayoutState.Medium => new Thickness(20),
        _ => new Thickness(14)
    };
    public bool IsMockMode => _isMockModeConfigured || _overviewPage.IsSimulated;
    public string? ActiveEndpoint => _activeEndpoint;
    public bool HasActiveEndpoint => _activeEndpoint is not null;
    public string? ActiveUpsName => _overviewPage.Snapshot?.Identity.Name ?? _preferredUpsName;
    public string ConnectionDetailText => _activeEndpoint is null
        ? Localizer.Get("Shell.NoActiveProfile")
        : ActiveUpsName is null ? _activeEndpoint : $"{ActiveUpsName}@{_activeEndpoint}";
    public bool HasActiveProfile => _activeProfileName is not null;
    public string ActiveProfileName => _activeProfileName ?? Localizer.Get("Shell.NoActiveProfile");
    public string ActiveProfileLabel => Localizer.Get("Shell.ActiveProfile");
    public string ActiveProfileStatus => Localizer.Get("Shell.ProfileActive");
    public string ActiveProfileModeText => _managementMode switch
    {
        NutManagementMode.Local => Localizer.Get("Management.Local"),
        NutManagementMode.Remote => Localizer.Get("Management.Remote"),
        _ => Localizer.Get("Status.Unavailable")
    } + " · " + (_accessMode switch
    {
        ManagedNutServerAccessMode.ReadOnly => Localizer.Get("Access.ReadOnly"),
        ManagedNutServerAccessMode.Manage => Localizer.Get("Access.Manage"),
        _ => Localizer.Get("Status.Unavailable")
    });
    public string ApplicationVersionText => $"v{typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";
    public string AdministrationConfirmationText => Localizer.Get("Shell.AdministrationConfirmation");
    public ConnectionPresentationState ConnectionPresentation => ShellPresentationMapper.ConnectionFor(
        _overviewPage.ConnectionState,
        _overviewPage.DataFreshness,
        _activeEndpoint is not null || _overviewPage.Snapshot is not null);
    public string ConnectionStatusText => ConnectionPresentation switch
    {
        ConnectionPresentationState.Healthy => Localizer.Get("Status.Connected"),
        ConnectionPresentationState.Pending => _overviewPage.ConnectionState == ConnectionState.Reconnecting ? Localizer.Get("Status.Reconnecting") : Localizer.Get("Status.Connecting"),
        ConnectionPresentationState.Warning => Localizer.Get("Status.Stale"),
        ConnectionPresentationState.Critical => _overviewPage.ConnectionState == ConnectionState.ConnectionFailed ? Localizer.Get("Status.ConnectionFailed") : Localizer.Get("Status.Disconnected"),
        _ => Localizer.Get("Status.Unavailable")
    };
    public string ConnectionTooltip => ConnectionSummaryText;
    public string ConnectionSummaryText => $"{ConnectionStatusText} · {ConnectionDetailText}";
    public string ApplicationName => Localizer.Get("App.Name");
    public bool IsConnectionHealthy => ConnectionPresentation == ConnectionPresentationState.Healthy;
    public bool IsConnectionPending => ConnectionPresentation is ConnectionPresentationState.Pending or ConnectionPresentationState.Warning;
    public bool IsConnectionCritical => ConnectionPresentation == ConnectionPresentationState.Critical;
    public bool IsConnectionUnavailable => ConnectionPresentation == ConnectionPresentationState.Unavailable;
    public bool ShowLightThemeAction => SelectedTheme == ThemePreference.Dark || (SelectedTheme == ThemePreference.System && IsEffectiveDark);
    public bool ShowDarkThemeAction => !ShowLightThemeAction;
    public string ThemeToggleName => Localizer.Get("Shell.ToggleTheme");
    public string NavigationToggleName => IsSidebarExpanded || IsOverlayOpen
        ? Localizer.Get("Shell.CollapseNavigation")
        : Localizer.Get("Shell.ExpandNavigation");
    public string SimulationText => Localizer.Get("Shell.SimulationActive");
    public string ReviewDrawerTitle => Localizer.Get("Shell.ReviewChanges");
    public bool IsReviewDrawerVisible => ReviewDrawerDisplay != ReviewDrawerDisplayState.Hidden;

    public event Action<ThemePreference>? ThemeChanged;
    public event Action<SidebarPreference>? SidebarPreferenceChanged;

    public void SetTheme(ThemePreference preference)
    {
        var option = ThemeOptions.Single(option => option.Preference == preference);
        if (!Equals(SelectedThemeOption, option)) SelectedThemeOption = option;
    }

    public void UpdateLayoutWidth(double width) => ShellLayout = ShellPresentationMapper.LayoutFor(width);

    public void UpdateEffectiveTheme(bool isDark) => IsEffectiveDark = isDark;

    [RelayCommand]
    private void Navigate(AppPage page)
    {
        SelectedPage = page;
        CurrentPage = _pages[page];
        UpdateNavigationSelection();
        if (IsSidebarOverlay)
        {
            CloseNavigationOverlay();
        }
    }

    [RelayCommand]
    private void ToggleNavigation()
    {
        if (ShellLayout == ShellLayoutState.Medium) return;

        if (IsSidebarOverlay)
        {
            _isOverlayOpen = !_isOverlayOpen;
            OnPropertyChanged(nameof(IsOverlayOpen));
            OnPropertyChanged(nameof(IsBackgroundInteractionEnabled));
            OnPropertyChanged(nameof(NavigationToggleName));
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
            OnPropertyChanged(nameof(ShowLightThemeAction));
            OnPropertyChanged(nameof(ShowDarkThemeAction));
            ThemeChanged?.Invoke(value.Preference);
        }
    }

    partial void OnIsEffectiveDarkChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLightThemeAction));
        OnPropertyChanged(nameof(ShowDarkThemeAction));
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

    private NavigationItemViewModel CreateNavigationItem(AppPage page, string resourceKey) =>
        new(page, Localizer.Get(resourceKey), new RelayCommand(() => Navigate(page)));

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
        OnPropertyChanged(nameof(IsWideLayout));
        OnPropertyChanged(nameof(IsCompactLayout));
        OnPropertyChanged(nameof(IsOverlayOpen));
        OnPropertyChanged(nameof(IsBackgroundInteractionEnabled));
        OnPropertyChanged(nameof(IsNavigationToggleVisible));
        OnPropertyChanged(nameof(SidebarWidth));
        OnPropertyChanged(nameof(ContentPadding));
        OnPropertyChanged(nameof(NavigationToggleName));
        OnPropertyChanged(nameof(IsReviewDrawerVisible));
    }

    public void CloseNavigationOverlay()
    {
        if (!_isOverlayOpen) return;
        _isOverlayOpen = false;
        OnPropertyChanged(nameof(IsOverlayOpen));
        OnPropertyChanged(nameof(IsBackgroundInteractionEnabled));
        OnPropertyChanged(nameof(NavigationToggleName));
    }
}
