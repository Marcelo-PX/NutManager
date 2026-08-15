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
    private readonly SettingsPageViewModel _settingsPage;
    private readonly bool _isMockModeConfigured;
    private readonly string? _activeEndpoint;
    private readonly string? _activeProfileName;
    private readonly NutManagementMode? _managementMode;
    private readonly ManagedNutServerAccessMode? _accessMode;
    private readonly string? _preferredUpsName;
    private bool _isOverlayOpen;
    private SemanticConfigurationReviewViewModel? _semanticReview;

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
        _settingsPage = settingsPage ?? new SettingsPageViewModel();
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
            [AppPage.Settings] = _settingsPage
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
        PublishDashboardContext();
    }

    /// <summary>
    /// Hands the Overview dashboard the management context the shell already owns, plus shortcuts
    /// that only navigate to existing surfaces. No new state, capability or command is created.
    /// </summary>
    private void PublishDashboardContext()
    {
        var rows = new List<OverviewInfoRowViewModel>
        {
            new(Localizer.Get("Overview.Profile"), ActiveProfileName)
        };
        if (_managementMode is { } mode)
            rows.Add(new(Localizer.Get("Administration.Context.Management"),
                Localizer.Get(mode == NutManagementMode.Local ? "Management.Local" : "Management.Remote")));
        if (_accessMode is { } access)
            rows.Add(new(Localizer.Get("Administration.Context.Access"),
                Localizer.Get(access == ManagedNutServerAccessMode.Manage ? "Access.Manage" : "Access.ReadOnly")));
        rows.Add(new(
            Localizer.Get("Overview.MockState"),
            Localizer.Get(IsMockMode ? "Common.Enabled" : "Common.Disabled"),
            IsMockMode));

        var administrationPage = _pages[AppPage.Administration] as AdministrationPageViewModel;
        var shortcuts = new List<OverviewShortcutViewModel>
        {
            CreateShortcut(AdministrationSection.NutConfiguration, OverviewShortcutGlyph.Configuration,
                "Administration.Section.Configuration", administrationPage),
            CreateShortcut(AdministrationSection.WindowsService, OverviewShortcutGlyph.Service,
                "Administration.Section.WindowsService", administrationPage),
            CreateShortcut(AdministrationSection.DevicesAndDrivers, OverviewShortcutGlyph.Devices,
                "Administration.Section.DevicesDrivers", administrationPage),
            new(Localizer.Get("Nav.Diagnostics"), Localizer.Get("Diagnostics.Description"),
                OverviewShortcutGlyph.Diagnostics, new RelayCommand(() => Navigate(AppPage.Diagnostics)))
        };

        _overviewPage.SetDashboardContext(rows, shortcuts);
    }

    private OverviewShortcutViewModel CreateShortcut(
        AdministrationSection section,
        OverviewShortcutGlyph glyph,
        string resourcePrefix,
        AdministrationPageViewModel? administrationPage) =>
        new(Localizer.Get(resourcePrefix),
            Localizer.Get($"{resourcePrefix}.Description"),
            glyph,
            new RelayCommand(() =>
            {
                if (administrationPage?.AdministrationSections.FirstOrDefault(item => item.Section == section) is { } target)
                    administrationPage.SelectedAdministrationSection = target;
                Navigate(AppPage.Administration);
            }));

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }
    public IReadOnlyList<ThemeOption> ThemeOptions { get; }
    public IReadOnlyList<ManagedProfileCardViewModel> ManagedProfileCards => _settingsPage.ManagedProfileCards;
    public NutManagerLocalizer Localizer { get; private set; }

    [ObservableProperty] private AppPage _selectedPage;
    [ObservableProperty] private PageViewModel _currentPage;
    [ObservableProperty] private ThemeOption? _selectedThemeOption;
    [ObservableProperty] private UiLanguagePreference _language;
    [ObservableProperty] private SidebarPreference _sidebarPreference;

    /// <summary>
    /// Whether the window paints its acrylic backdrop or a solid one. The window binds both the
    /// acrylic pane and the opaque panel behind the shell to this, so switching it swaps which of
    /// the two is drawn rather than dimming the effect towards invisibility.
    /// </summary>
    [ObservableProperty] private bool _isBackgroundTransparent = true;
    [ObservableProperty] private ShellLayoutState _shellLayout = ShellLayoutState.Wide;
    [ObservableProperty] private bool _isEffectiveDark;

    public ThemePreference SelectedTheme => SelectedThemeOption?.Preference ?? ThemePreference.System;
    public SidebarDisplayState SidebarDisplay => ShellPresentationMapper.SidebarFor(ShellLayout, SidebarPreference);
    public ReviewDrawerDisplayState ReviewDrawerDisplay => ShellPresentationMapper.ReviewFor(ShellLayout, _semanticReview?.HasChanges == true, true);
    public bool IsSidebarExpanded => SidebarDisplay == SidebarDisplayState.Expanded;
    public bool IsSidebarCollapsed => SidebarDisplay == SidebarDisplayState.Collapsed;
    public bool IsSidebarOverlay => SidebarDisplay == SidebarDisplayState.Overlay;
    public bool IsWideLayout => ShellLayout == ShellLayoutState.Wide;
    public bool IsCompactLayout => ShellLayout == ShellLayoutState.Compact;
    public bool IsFooterAuthorshipVisible => !IsCompactLayout;
    public bool IsOverlayOpen => IsSidebarOverlay && _isOverlayOpen;
    public double NavigationOverlayOpacity => IsOverlayOpen ? 1d : 0d;
    public Thickness NavigationOverlayMargin => IsOverlayOpen ? new Thickness(0) : new Thickness(-24, 0, 24, 0);
    public bool IsBackgroundInteractionEnabled => !IsOverlayOpen && !IsReviewDrawerOverlay;
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
    public string FooterAuthorshipText => Localizer.Get("Shell.Authorship");
    public string OpenProfilesText => Localizer.Get("Shell.OpenProfiles");
    public string SavedProfilesText => Localizer.Get("Shell.SavedProfiles");
    public string ManageProfilesText => Localizer.Get("Shell.ManageProfiles");
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
    public string ReviewDrawerCloseText => Localizer.Get("Shell.CloseReview");
    public string? ReviewDrawerPendingText => _semanticReview?.PendingText;
    public string? ReviewDrawerPendingCount => _semanticReview?.ChangeCount.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public object? ReviewDrawerContent => _semanticReview;
    public bool IsReviewDrawerInline => ReviewDrawerDisplay == ReviewDrawerDisplayState.Expanded;
    public bool IsReviewDrawerOverlay => ReviewDrawerDisplay == ReviewDrawerDisplayState.Overlay;
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

    public void SetSemanticReview(SemanticConfigurationReviewViewModel? review)
    {
        _semanticReview = review;
        OnPropertyChanged(nameof(ReviewDrawerDisplay));
        OnPropertyChanged(nameof(IsReviewDrawerVisible));
        OnPropertyChanged(nameof(ReviewDrawerPendingText));
        OnPropertyChanged(nameof(ReviewDrawerPendingCount));
        OnPropertyChanged(nameof(ReviewDrawerContent));
        OnPropertyChanged(nameof(IsReviewDrawerInline));
        OnPropertyChanged(nameof(IsReviewDrawerOverlay));
        OnPropertyChanged(nameof(IsBackgroundInteractionEnabled));
    }

    [RelayCommand]
    private void CloseReviewDrawer() => SetSemanticReview(null);

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
    private void OpenManagedProfile(ManagedProfileCardViewModel? profile)
    {
        if (profile is not null)
        {
            // Selection remains owned by Settings. Its setter preserves a dirty draft by opening
            // the existing decision flow instead of silently replacing it.
            _settingsPage.SelectedProfileCard = profile;
        }

        Navigate(AppPage.Settings);
    }

    [RelayCommand]
    private void ToggleNavigation()
    {
        if (ShellLayout == ShellLayoutState.Medium) return;

        if (IsSidebarOverlay)
        {
            _isOverlayOpen = !_isOverlayOpen;
            OnPropertyChanged(nameof(IsOverlayOpen));
            OnPropertyChanged(nameof(NavigationOverlayOpacity));
            OnPropertyChanged(nameof(NavigationOverlayMargin));
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
        OnPropertyChanged(nameof(IsFooterAuthorshipVisible));
        OnPropertyChanged(nameof(IsOverlayOpen));
        OnPropertyChanged(nameof(NavigationOverlayOpacity));
        OnPropertyChanged(nameof(NavigationOverlayMargin));
        OnPropertyChanged(nameof(IsBackgroundInteractionEnabled));
        OnPropertyChanged(nameof(IsNavigationToggleVisible));
        OnPropertyChanged(nameof(SidebarWidth));
        OnPropertyChanged(nameof(ContentPadding));
        OnPropertyChanged(nameof(NavigationToggleName));
        OnPropertyChanged(nameof(IsReviewDrawerVisible));
        OnPropertyChanged(nameof(IsReviewDrawerInline));
        OnPropertyChanged(nameof(IsReviewDrawerOverlay));
    }

    public void CloseNavigationOverlay()
    {
        if (!_isOverlayOpen) return;
        _isOverlayOpen = false;
        OnPropertyChanged(nameof(IsOverlayOpen));
        OnPropertyChanged(nameof(NavigationOverlayOpacity));
        OnPropertyChanged(nameof(NavigationOverlayMargin));
        OnPropertyChanged(nameof(IsBackgroundInteractionEnabled));
        OnPropertyChanged(nameof(NavigationToggleName));
    }
}
