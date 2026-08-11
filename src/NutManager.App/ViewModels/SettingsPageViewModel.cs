using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NutManager.App.Localization;
using NutManager.App.Services;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Core.Validation;

namespace NutManager.App.ViewModels;

public sealed partial class SettingsPageViewModel : PageViewModel
{
    private readonly IApplicationSettingsStore? _settingsStore;
    private readonly IManagedNutServerProfileStore? _profileStore;
    private readonly ManagedNutServerProfileUpdateService? _profileMutator;
    private readonly IRemoteCredentialStore? _credentialStore;
    private readonly IManagedNutConnectionTester? _connectionTester;
    private readonly Guid _runtimeProfileId;
    private readonly string _runtimeProfileName;
    private ApplicationSettings _confirmedSettings;
    private ManagedNutServerProfiles _confirmedProfiles;
    private Guid? _draftSourceId;
    private ManagedNutServerProfile? _draftBaseProfile;
    private PendingProfileAction? _pendingProfileAction;
    private ManagedNutServerProfile? _selectedManagedProfile;
    private ManagedProfileCardViewModel? _selectedProfileCard;
    private ManagedNutServerProfileValidationResult _profileValidation = new(null, []);
    private bool _isCreatingProfile;
    private bool _canPersistThemeAutomatically = true;
    private bool _canPersistProfiles = true;
    private bool _isApplyingVisualPreferences;
    private long _draftVersion;

    public SettingsPageViewModel()
        : this(new ApplicationSettings(), null, null, null)
    {
    }

    public SettingsPageViewModel(ApplicationSettings settings, IApplicationSettingsStore? store)
        : this(settings, store, null, null)
    {
    }

    public SettingsPageViewModel(
        ApplicationSettings settings,
        IApplicationSettingsStore? settingsStore,
        ManagedNutServerProfiles? profiles,
        IManagedNutServerProfileStore? profileStore,
        ManagedNutServerProfileUpdateService? profileMutator = null,
        IRemoteCredentialStore? credentialStore = null,
        IManagedNutConnectionTester? connectionTester = null,
        Guid? runtimeProfileId = null)
        : base(
            Localize(settings, "Settings.Title"),
            Localize(settings, "Settings.Description"))
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settingsStore = settingsStore;
        _profileStore = profileStore;
        _profileMutator = profileStore is null ? null : profileMutator ?? new ManagedNutServerProfileUpdateService(profileStore);
        _credentialStore = credentialStore;
        _connectionTester = connectionTester;
        _confirmedSettings = settings;
        _confirmedProfiles = profiles ?? ManagedNutServerProfiles.CreateLegacyProfile(settings);
        _runtimeProfileId = runtimeProfileId ?? _confirmedProfiles.ActiveProfileId;
        _runtimeProfileName = _confirmedProfiles.Profiles.Single(profile => profile.Id == _runtimeProfileId).Name;
        Localizer = new NutManagerLocalizer(settings.Language);

        ManagedProfiles = new ObservableCollection<ManagedNutServerProfile>(_confirmedProfiles.Profiles);
        ManagedProfileCards = new ObservableCollection<ManagedProfileCardViewModel>();
        ProfileDraft = new ManagedNutServerProfileDraftViewModel(_confirmedProfiles.ActiveProfile);
        ProfileDraft.PropertyChanged += OnProfileDraftPropertyChanged;
        _draftSourceId = _confirmedProfiles.ActiveProfileId;
        _draftBaseProfile = _confirmedProfiles.ActiveProfile;
        _selectedManagedProfile = _confirmedProfiles.ActiveProfile;

        ThemeOptions =
        [
            new ThemeOption(ThemePreference.System, Localizer.Get("Theme.System")),
            new ThemeOption(ThemePreference.Light, Localizer.Get("Theme.Light")),
            new ThemeOption(ThemePreference.Dark, Localizer.Get("Theme.Dark"))
        ];
        LanguageOptions =
        [
            new PresentationOption<UiLanguagePreference>(UiLanguagePreference.PtBr, Localizer.Get("Language.PtBr")),
            new PresentationOption<UiLanguagePreference>(UiLanguagePreference.EnUs, Localizer.Get("Language.EnUs"))
        ];
        SidebarOptions =
        [
            new PresentationOption<SidebarPreference>(SidebarPreference.Expanded, Localizer.Get("Sidebar.Expanded")),
            new PresentationOption<SidebarPreference>(SidebarPreference.Collapsed, Localizer.Get("Sidebar.Collapsed"))
        ];
        ManagementModeOptions =
        [
            new PresentationOption<NutManagementMode>(NutManagementMode.Local, Localizer.Get("Management.Local")),
            new PresentationOption<NutManagementMode>(NutManagementMode.Remote, Localizer.Get("Management.Remote"))
        ];
        AccessModeOptions =
        [
            new PresentationOption<ManagedNutServerAccessMode>(ManagedNutServerAccessMode.ReadOnly, Localizer.Get("Access.ReadOnly")),
            new PresentationOption<ManagedNutServerAccessMode>(ManagedNutServerAccessMode.Manage, Localizer.Get("Access.Manage"))
        ];
        ConfigurationTransportOptions =
        [
            new PresentationOption<RemoteConfigurationTransportKind>(RemoteConfigurationTransportKind.SshSftp, Localizer.Get("Transport.Sftp")),
            new PresentationOption<RemoteConfigurationTransportKind>(RemoteConfigurationTransportKind.Smb, Localizer.Get("Transport.Smb"))
        ];
        SshAuthenticationOptions =
        [
            new PresentationOption<SshAuthenticationMode>(SshAuthenticationMode.Password, Localizer.Get("SshAuth.Password")),
            new PresentationOption<SshAuthenticationMode>(SshAuthenticationMode.PrivateKey, Localizer.Get("SshAuth.PrivateKey"))
        ];
        SmbAuthenticationOptions =
        [
            new PresentationOption<SmbAuthenticationMode>(SmbAuthenticationMode.CurrentWindowsIdentity, Localizer.Get("SmbAuth.CurrentWindowsIdentity")),
            new PresentationOption<SmbAuthenticationMode>(SmbAuthenticationMode.ExplicitCredentials, Localizer.Get("SmbAuth.ExplicitCredentials"))
        ];

        _isApplyingVisualPreferences = true;
        Apply(settings);
        SelectedThemeOption = ThemeOptions.Single(option => option.Preference == settings.Theme);
        SelectedLanguageOption = LanguageOptions.Single(option => option.Value == settings.Language);
        SelectedSidebarOption = SidebarOptions.Single(option => option.Value == settings.SidebarPreference);
        _isApplyingVisualPreferences = false;
        RebuildProfileCards();
        RefreshProfileValidation();
    }

    public NutManagerLocalizer Localizer { get; private set; }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public IReadOnlyList<PresentationOption<UiLanguagePreference>> LanguageOptions { get; }

    public IReadOnlyList<PresentationOption<SidebarPreference>> SidebarOptions { get; }

    public IReadOnlyList<PresentationOption<NutManagementMode>> ManagementModeOptions { get; }

    public IReadOnlyList<PresentationOption<ManagedNutServerAccessMode>> AccessModeOptions { get; }

    public IReadOnlyList<PresentationOption<RemoteConfigurationTransportKind>> ConfigurationTransportOptions { get; }

    public IReadOnlyList<PresentationOption<SshAuthenticationMode>> SshAuthenticationOptions { get; }

    public IReadOnlyList<PresentationOption<SmbAuthenticationMode>> SmbAuthenticationOptions { get; }

    public ObservableCollection<ManagedNutServerProfile> ManagedProfiles { get; }

    public ObservableCollection<ManagedProfileCardViewModel> ManagedProfileCards { get; }

    public ManagedNutServerProfileDraftViewModel ProfileDraft { get; }

    public ManagedNutServerProfile? SelectedManagedProfile
    {
        get => _selectedManagedProfile;
        set => RequestProfileSelection(value);
    }

    public ManagedProfileCardViewModel? SelectedProfileCard
    {
        get => _selectedProfileCard;
        set => RequestProfileSelection(value?.Profile);
    }

    public PresentationOption<NutManagementMode> SelectedManagementModeOption
    {
        get => ManagementModeOptions.Single(option => option.Value == ProfileDraft.ManagementMode);
        set => ProfileDraft.ManagementMode = value.Value;
    }

    public PresentationOption<ManagedNutServerAccessMode> SelectedAccessModeOption
    {
        get => AccessModeOptions.Single(option => option.Value == ProfileDraft.AccessMode);
        set => ProfileDraft.AccessMode = value.Value;
    }

    public PresentationOption<RemoteConfigurationTransportKind> SelectedConfigurationTransportOption
    {
        get => ConfigurationTransportOptions.Single(option => option.Value == ProfileDraft.ConfigurationTransport);
        set => ProfileDraft.ConfigurationTransport = value.Value;
    }

    public PresentationOption<SshAuthenticationMode> SelectedSshAuthenticationOption
    {
        get => SshAuthenticationOptions.Single(option => option.Value == ProfileDraft.SshAuthenticationMode);
        set => ProfileDraft.SshAuthenticationMode = value.Value;
    }

    public PresentationOption<SmbAuthenticationMode> SelectedSmbAuthenticationOption
    {
        get => SmbAuthenticationOptions.Single(option => option.Value == ProfileDraft.SmbAuthenticationMode);
        set => ProfileDraft.SmbAuthenticationMode = value.Value;
    }

    public string AppearanceTitle => Localizer.Get("Appearance.Title");
    public string AppearanceThemeLabel => Localizer.Get("Appearance.Theme");
    public string AppearanceLanguageLabel => Localizer.Get("Appearance.Language");
    public string AppearanceSidebarLabel => Localizer.Get("Appearance.Sidebar");
    public string RestartLanguageMessage => Localizer.Get("Appearance.RestartRequired");
    public string ManagedServersTitle => Localizer.Get("Profiles.Title");
    public string NewServerText => Localizer.Get("Profiles.NewServer");
    public string EditorTitle => IsCreatingProfile ? Localizer.Get("Profiles.NewServer") : Localizer.Get("Profiles.EditorTitle");
    public string MonitoringSectionTitle => Localizer.Get("Profiles.MonitoringSection");
    public string ManagementSectionTitle => Localizer.Get("Profiles.ManagementSection");
    public string TransportSectionTitle => Localizer.Get("Profiles.TransportSection");
    public string NameLabel => Localizer.Get("Profiles.Name");
    public string MonitoringHostLabel => Localizer.Get("Profiles.MonitoringHost");
    public string MonitoringPortLabel => Localizer.Get("Profiles.MonitoringPort");
    public string PreferredUpsLabel => Localizer.Get("Profiles.PreferredUps");
    public string ManagementModeLabel => Localizer.Get("Profiles.ManagementMode");
    public string AccessModeLabel => Localizer.Get("Profiles.AccessMode");
    public string TransportLabel => Localizer.Get("Profiles.Transport");
    public string ManagementHostLabel => Localizer.Get("Profiles.ManagementHost");
    public string SshPortLabel => Localizer.Get("Profiles.SshPort");
    public string SshUsernameLabel => Localizer.Get("Profiles.SshUsername");
    public string SshAuthenticationLabel => Localizer.Get("Profiles.SshAuthentication");
    public string PrivateKeyLabel => Localizer.Get("Profiles.PrivateKey");
    public string SelectPrivateKeyText => Localizer.Get("Profiles.SelectPrivateKey");
    public string SelectPrivateKeyDialogTitle => Localizer.Get("Profiles.SelectPrivateKeyDialog");
    public string PrivateKeyMetadataHelp => Localizer.Get("Profiles.PrivateKeyMetadataHelp");
    public string RemoteDirectoryLabel => Localizer.Get("Profiles.RemoteDirectory");
    public string TrustedHostKeyLabel => Localizer.Get("Profiles.TrustedHostKey");
    public string ForgetHostKeyText => Localizer.Get("Profiles.ForgetHostKey");
    public string ProtectedSecretHelp => Localizer.Get("Profiles.ProtectedSecretHelp");
    public string SmbShareLabel => Localizer.Get("Profiles.SmbShare");
    public string SmbAuthenticationLabel => Localizer.Get("Profiles.SmbAuthentication");
    public string SmbUsernameLabel => Localizer.Get("Profiles.SmbUsername");
    public string SmbDirectoryLabel => Localizer.Get("Profiles.SmbDirectory");
    public string SmbSecretHelp => Localizer.Get("Profiles.SmbSecretHelp");
    public string StoredCredentialLabel => Localizer.Get("Profiles.StoredCredential");
    public string ForgetCredentialText => Localizer.Get("Profiles.ForgetCredential");
    public string SaveProfileText => Localizer.Get("Common.Save");
    public string DiscardProfileText => Localizer.Get("Common.Discard");
    public string ContinueEditingText => Localizer.Get("DirtyDraft.ContinueEditing");
    public string SaveAndContinueText => Localizer.Get("DirtyDraft.Save");
    public string DiscardAndContinueText => Localizer.Get("DirtyDraft.Discard");
    public string DirtyDraftTitle => Localizer.Get("DirtyDraft.Title");
    public string DirtyDraftMessage => Localizer.Get("DirtyDraft.Message");
    public string TestConnectionText => Localizer.Get("ConnectionTest.Action");
    public string ActivateProfileText => Localizer.Get("Profiles.Activate");
    public string DeleteProfileText => Localizer.Get("Profiles.Delete");
    public string GeneralSettingsTitle => Localizer.Get("Settings.GeneralTitle");
    public string ConnectionTimeoutLabel => Localizer.Get("Settings.ConnectionTimeout");
    public string PollingIntervalLabel => Localizer.Get("Settings.PollingInterval");
    public string MockModeLabel => Localizer.Get("Settings.MockMode");
    public string SaveSettingsText => Localizer.Get("Settings.Save");
    public string SavingText => Localizer.Get("Common.Saving");
    public string SettingsSavedText => Localizer.Get("Settings.SaveSuccess");
    public string ProfileSavingText => Localizer.Get("Profiles.Saving");
    public string RuntimeProfileLabel => Localizer.Get("Profiles.RuntimeProfile");
    public string PersistedActiveProfileLabel => Localizer.Get("Profiles.PersistedActiveProfile");
    public string LocalManagementHelp => Localizer.Get("Profiles.LocalManagementHelp");
    public string RuntimeProfileName => _runtimeProfileName;
    public string PersistedActiveProfileName => _confirmedProfiles.ActiveProfile.Name;
    public string RestartRequiredTitle => Localizer.Get("Profiles.RestartRequiredTitle");
    public string RestartRequiredMessage => Localizer.Get("Profiles.RestartRequiredMessage");

    [ObservableProperty] private string _pollingIntervalSeconds = "5";
    [ObservableProperty] private string _connectionTimeoutSeconds = "5";
    [ObservableProperty] private bool _mockMode;
    [ObservableProperty] private ThemeOption? _selectedThemeOption;
    [ObservableProperty] private PresentationOption<UiLanguagePreference>? _selectedLanguageOption;
    [ObservableProperty] private PresentationOption<SidebarPreference>? _selectedSidebarOption;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string? _saveError;
    [ObservableProperty] private string? _loadError;
    [ObservableProperty] private bool _isSaved;
    [ObservableProperty] private bool _isSavingProfile;
    [ObservableProperty] private bool _isProfileSaved;
    [ObservableProperty] private string? _profileSaveError;
    [ObservableProperty] private string? _profileLoadError;
    [ObservableProperty] private string? _profileStatusMessage;
    [ObservableProperty] private RemoteCredentialStoreStatus _storedCredentialStatus = RemoteCredentialStoreStatus.NotFound;
    [ObservableProperty] private bool _isDirtyDraftDecisionVisible;
    [ObservableProperty] private bool _isTestingConnection;
    [ObservableProperty] private string? _connectionTestResultText;
    [ObservableProperty] private ProfileOperationTone _connectionTestTone = ProfileOperationTone.Neutral;
    [ObservableProperty] private ManagedNutConnectionTestStatus? _connectionTestStatus;

    public bool IsLanguageRestartRequired { get; private set; }

    public bool IsProfileDraftDirty => _isCreatingProfile || (_draftBaseProfile is not null && !ProfileDraft.Matches(_draftBaseProfile));

    public bool IsCreatingProfile => _isCreatingProfile;

    public bool CanPersistProfiles => _profileStore is not null && _canPersistProfiles && !IsSavingProfile;

    public bool CanSaveProfile => CanPersistProfiles && IsProfileDraftDirty && !_profileValidation.HasErrors;

    public bool CanDeleteSelectedProfile => CanPersistProfiles && SelectedManagedProfile is not null && ManagedProfiles.Count > 1 && SelectedManagedProfile.Id != _confirmedProfiles.ActiveProfileId;

    public bool CanActivateSelectedProfile => CanPersistProfiles && SelectedManagedProfile is not null && SelectedManagedProfile.Id != _confirmedProfiles.ActiveProfileId;

    public bool CanForgetTrustedHostKey => CanPersistProfiles && !IsProfileDraftDirty && SelectedManagedProfile is { Management.Mode: NutManagementMode.Remote, Management.TrustedHostKeyFingerprint: not null };

    public bool IsSelectedProfileActive => SelectedManagedProfile?.Id == _confirmedProfiles.ActiveProfileId;

    public string ActiveProfileName => _confirmedProfiles.ActiveProfile.Name;

    public bool IsActiveProfileRestartRequired => _confirmedProfiles.ActiveProfileId != _runtimeProfileId;

    public bool HasProfileLoadError => !string.IsNullOrWhiteSpace(ProfileLoadError);

    public bool HasProfileStatusMessage => !string.IsNullOrWhiteSpace(ProfileStatusMessage);

    public bool HasProfileSaveError => !string.IsNullOrWhiteSpace(ProfileSaveError);

    public bool HasSaveError => !string.IsNullOrWhiteSpace(SaveError);

    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);

    public bool CanForgetStoredCredential => CanPersistProfiles && SelectedManagedProfile is not null && GetCredentialKind(SelectedManagedProfile) is not null;

    public bool CanTestConnection => _connectionTester is not null && !IsTestingConnection &&
        ManagedNutServerProfileValidator.ValidateHost(ProfileDraft.MonitoringHost).IsValid &&
        ManagedNutServerProfileValidator.ValidatePort(ProfileDraft.MonitoringPort).IsValid;

    public bool HasConnectionTestResult => !string.IsNullOrWhiteSpace(ConnectionTestResultText);

    public bool IsConnectionTestCritical => ConnectionTestTone == ProfileOperationTone.Critical && HasConnectionTestResult;

    public bool IsConnectionTestHealthy => ConnectionTestTone == ProfileOperationTone.Healthy && HasConnectionTestResult;

    public bool IsConnectionTestWarning => ConnectionTestTone == ProfileOperationTone.Warning && HasConnectionTestResult;

    public bool IsConnectionTestNeutral => ConnectionTestTone == ProfileOperationTone.Neutral && HasConnectionTestResult;

    public string StoredCredentialText => SelectedManagedProfile is { } profile && GetCredentialKind(profile) is { }
        ? StoredCredentialStatus switch
        {
            RemoteCredentialStoreStatus.Success => Localizer.Get("Credential.StoredYes"),
            RemoteCredentialStoreStatus.NotFound => Localizer.Get("Credential.StoredNo"),
            RemoteCredentialStoreStatus.Unsupported or RemoteCredentialStoreStatus.CredentialStoreUnavailable => Localizer.Get("Credential.Unavailable"),
            _ => Localizer.Get("Credential.QueryFailed")
        }
        : Localizer.Get("Credential.NotRequired");

    public IReadOnlyList<LocalizedValidationIssueViewModel> ProfileValidationIssues { get; private set; } = [];
    public IReadOnlyList<LocalizedValidationIssueViewModel> NameValidationIssues => IssuesFor(ManagedProfileFields.Name);
    public IReadOnlyList<LocalizedValidationIssueViewModel> MonitoringHostValidationIssues => IssuesFor(ManagedProfileFields.MonitoringHost);
    public IReadOnlyList<LocalizedValidationIssueViewModel> MonitoringPortValidationIssues => IssuesFor(ManagedProfileFields.MonitoringPort);
    public IReadOnlyList<LocalizedValidationIssueViewModel> PreferredUpsValidationIssues => IssuesFor(ManagedProfileFields.PreferredUpsName);
    public IReadOnlyList<LocalizedValidationIssueViewModel> ManagementHostValidationIssues => IssuesFor(ManagedProfileFields.ManagementHost);
    public IReadOnlyList<LocalizedValidationIssueViewModel> SshPortValidationIssues => IssuesFor(ManagedProfileFields.SshPort);
    public IReadOnlyList<LocalizedValidationIssueViewModel> SshUsernameValidationIssues => IssuesFor(ManagedProfileFields.SshUsername);
    public IReadOnlyList<LocalizedValidationIssueViewModel> SshPrivateKeyValidationIssues => IssuesFor(ManagedProfileFields.SshPrivateKeyPath);
    public IReadOnlyList<LocalizedValidationIssueViewModel> RemoteDirectoryValidationIssues => IssuesFor(ManagedProfileFields.RemoteConfigurationDirectory);
    public IReadOnlyList<LocalizedValidationIssueViewModel> SmbShareValidationIssues => IssuesFor(ManagedProfileFields.SmbSharePath);
    public IReadOnlyList<LocalizedValidationIssueViewModel> SmbDirectoryValidationIssues => IssuesFor(ManagedProfileFields.SmbConfigurationDirectory);
    public IReadOnlyList<LocalizedValidationIssueViewModel> SmbUsernameValidationIssues => IssuesFor(ManagedProfileFields.SmbUsername);

    public event Action<ThemePreference>? ThemeChanged;
    public event Action<SidebarPreference>? SidebarPreferenceChanged;

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        IsSaving = true;
        IsSaved = false;
        SaveError = null;
        try
        {
            var settings = CreateSettings();
            if (_settingsStore is not null)
            {
                await _settingsStore.SaveAsync(settings, cancellationToken);
            }

            _confirmedSettings = settings;
            _canPersistThemeAutomatically = true;
            IsSaved = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            SaveError = Localizer.Get("Settings.SaveError");
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void NewServer()
    {
        if (QueueIfDirty(new PendingProfileAction(PendingProfileActionKind.NewProfile)))
        {
            return;
        }

        BeginCreate();
    }

    [RelayCommand]
    private async Task SaveProfileAsync(CancellationToken cancellationToken = default) =>
        _ = await SaveProfileCoreAsync(cancellationToken);

    private async Task<bool> SaveProfileCoreAsync(CancellationToken cancellationToken)
    {
        var mutator = _profileMutator;
        RefreshProfileValidation();
        if (mutator is null || !CanPersistProfiles)
        {
            ProfileSaveError = Localizer.Get("Profiles.PersistenceBlocked");
            return false;
        }

        if (_profileValidation.HasErrors || _profileValidation.Profile is null)
        {
            ProfileSaveError = Localizer.Get("Validation.Profile.FixErrors");
            return false;
        }

        IsSavingProfile = true;
        IsProfileSaved = false;
        ProfileSaveError = null;
        ProfileStatusMessage = null;
        try
        {
            var updated = _profileValidation.Profile;
            var document = _isCreatingProfile
                ? await mutator.CreateProfileAsync(updated, cancellationToken)
                : _draftBaseProfile is null
                    ? null
                    : await mutator.SaveExistingProfileAsync(_draftBaseProfile, updated, cancellationToken);
            if (document is null)
            {
                ProfileSaveError = Localizer.Get("Profiles.ConcurrentChange");
                return false;
            }

            ApplyConfirmedProfiles(document, updated.Id);
            IsProfileSaved = true;
            ProfileStatusMessage = Localizer.Get("Profiles.SaveSuccess");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ManagedProfilePersistenceAfterCredentialRemovalException)
        {
            ProfileSaveError = Localizer.Get("Profiles.SaveAfterCredentialRemovalFailed");
            return false;
        }
        catch (ManagedProfileCredentialRemovalException)
        {
            ProfileSaveError = Localizer.Get("Profiles.CredentialRemovalFailed");
            return false;
        }
        catch (Exception)
        {
            ProfileSaveError = Localizer.Get("Profiles.SaveFailed");
            return false;
        }
        finally
        {
            IsSavingProfile = false;
            NotifyProfilePropertiesChanged();
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedProfileAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedManagedProfile is null)
        {
            return;
        }

        if (QueueIfDirty(new PendingProfileAction(PendingProfileActionKind.DeleteProfile, SelectedManagedProfile.Id)))
        {
            return;
        }

        await DeleteProfileCoreAsync(SelectedManagedProfile.Id, cancellationToken);
    }

    private async Task DeleteProfileCoreAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var mutator = _profileMutator;
        if (mutator is null || !CanPersistProfiles || profileId == _confirmedProfiles.ActiveProfileId || ManagedProfiles.Count <= 1)
        {
            return;
        }

        try
        {
            var document = await mutator.DeleteProfileAsync(profileId, cancellationToken);
            if (document is null)
            {
                ProfileSaveError = Localizer.Get("Profiles.DeleteConcurrentChange");
                return;
            }

            ApplyConfirmedProfiles(document, document.ActiveProfileId);
            ProfileStatusMessage = Localizer.Get("Profiles.DeleteSuccess");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ManagedProfilePersistenceAfterCredentialRemovalException)
        {
            ProfileSaveError = Localizer.Get("Profiles.DeleteAfterCredentialRemovalFailed");
        }
        catch (ManagedProfileCredentialRemovalException)
        {
            ProfileSaveError = Localizer.Get("Profiles.DeleteCredentialRemovalFailed");
        }
        catch (Exception)
        {
            ProfileSaveError = Localizer.Get("Profiles.DeleteFailed");
        }
    }

    [RelayCommand]
    private async Task ActivateSelectedProfileAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedManagedProfile is null)
        {
            return;
        }

        if (QueueIfDirty(new PendingProfileAction(PendingProfileActionKind.ActivateProfile, SelectedManagedProfile.Id)))
        {
            return;
        }

        await ActivateProfileCoreAsync(SelectedManagedProfile.Id, cancellationToken);
    }

    private async Task ActivateProfileCoreAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var mutator = _profileMutator;
        if (mutator is null || !CanPersistProfiles || profileId == _confirmedProfiles.ActiveProfileId)
        {
            return;
        }

        try
        {
            var document = await mutator.ActivateProfileAsync(profileId, cancellationToken);
            if (document is null)
            {
                ProfileSaveError = Localizer.Get("Profiles.ActivateConcurrentChange");
                return;
            }

            ApplyConfirmedProfiles(document, profileId);
            ProfileStatusMessage = Localizer.Get("Profiles.ActivateSuccess");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ProfileSaveError = Localizer.Get("Profiles.ActivateFailed");
        }
    }

    [RelayCommand]
    private async Task SaveDirtyDraftAndContinueAsync(CancellationToken cancellationToken = default)
    {
        var pending = _pendingProfileAction;
        if (pending is null || !await SaveProfileCoreAsync(cancellationToken))
        {
            return;
        }

        ClearPendingProfileAction();
        await ExecutePendingProfileActionAsync(pending, cancellationToken);
    }

    [RelayCommand]
    private async Task DiscardDirtyDraftAndContinueAsync(CancellationToken cancellationToken = default)
    {
        var pending = _pendingProfileAction;
        if (pending is null)
        {
            return;
        }

        DiscardProfileDraftCore();
        ClearPendingProfileAction();
        await ExecutePendingProfileActionAsync(pending, cancellationToken);
    }

    [RelayCommand]
    private void ContinueEditing() => ClearPendingProfileAction();

    [RelayCommand]
    private void DiscardProfileDraft() => DiscardProfileDraftCore();

    [RelayCommand]
    private async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var tester = _connectionTester;
        var host = ManagedNutServerProfileValidator.ValidateHost(ProfileDraft.MonitoringHost);
        var port = ManagedNutServerProfileValidator.ValidatePort(ProfileDraft.MonitoringPort);
        if (tester is null || host.Value is null || port.HasErrors)
        {
            ConnectionTestStatus = ManagedNutConnectionTestStatus.Failed;
            ConnectionTestTone = ProfileOperationTone.Critical;
            ConnectionTestResultText = Localizer.Get("ConnectionTest.InvalidFields");
            return;
        }

        var version = _draftVersion;
        IsTestingConnection = true;
        ConnectionTestResultText = Localizer.Get("ConnectionTest.Running");
        ConnectionTestTone = ProfileOperationTone.Warning;
        try
        {
            var endpoint = new NutEndpoint(host.Value, port.Value, _confirmedSettings.ConnectionTimeout);
            var result = await tester.TestAsync(endpoint, NormalizeOptional(ProfileDraft.PreferredUpsName), cancellationToken);
            if (version != _draftVersion)
            {
                return;
            }

            ConnectionTestStatus = result.Status;
            ConnectionTestTone = result.Status switch
            {
                ManagedNutConnectionTestStatus.Success => ProfileOperationTone.Healthy,
                ManagedNutConnectionTestStatus.Cancelled => ProfileOperationTone.Neutral,
                _ => ProfileOperationTone.Critical
            };
            ConnectionTestResultText = Localizer.Get(result.Status switch
            {
                ManagedNutConnectionTestStatus.Success => "ConnectionTest.Success",
                ManagedNutConnectionTestStatus.EndpointUnreachable => "ConnectionTest.Unreachable",
                ManagedNutConnectionTestStatus.Timeout => "ConnectionTest.Timeout",
                ManagedNutConnectionTestStatus.ProtocolError => "ConnectionTest.ProtocolError",
                ManagedNutConnectionTestStatus.NoUpsDiscovered => "ConnectionTest.NoUps",
                ManagedNutConnectionTestStatus.PreferredUpsMissing => "ConnectionTest.PreferredUpsMissing",
                ManagedNutConnectionTestStatus.Cancelled => "ConnectionTest.Cancelled",
                _ => "ConnectionTest.Failed"
            });
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    [RelayCommand]
    private async Task ForgetTrustedHostKeyAsync(CancellationToken cancellationToken = default)
    {
        var profile = SelectedManagedProfile;
        if (_profileMutator is null || profile is null || !CanForgetTrustedHostKey)
        {
            return;
        }

        IsSavingProfile = true;
        ProfileSaveError = null;
        try
        {
            var updated = await _profileMutator.ForgetTrustedHostKeyAsync(profile, cancellationToken);
            var document = updated is null ? null : await _profileMutator.LoadCurrentAsync(cancellationToken);
            if (document is null || updated is null)
            {
                ProfileSaveError = Localizer.Get("Profiles.HostKeyConcurrentChange");
                return;
            }

            ApplyConfirmedProfiles(document, updated.Id);
            ProfileStatusMessage = Localizer.Get("Profiles.HostKeyForgotten");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ProfileSaveError = Localizer.Get("Profiles.HostKeyForgetFailed");
        }
        finally
        {
            IsSavingProfile = false;
            NotifyProfilePropertiesChanged();
        }
    }

    [RelayCommand]
    private async Task ForgetStoredCredentialAsync(CancellationToken cancellationToken = default)
    {
        var profile = SelectedManagedProfile;
        var kind = profile is null ? null : GetCredentialKind(profile);
        if (_profileMutator is null || profile is null || kind is null || !CanForgetStoredCredential)
        {
            return;
        }

        var result = await _profileMutator.ForgetCredentialAsync(profile.Id, kind.Value, cancellationToken);
        StoredCredentialStatus = result.IsSuccess ? RemoteCredentialStoreStatus.NotFound : result.Status;
        ProfileStatusMessage = result.IsSuccess ? Localizer.Get("Credential.ForgetSuccess") : Localizer.Get("Credential.ForgetFailed");
    }

    public async Task RefreshStoredCredentialStatusAsync(CancellationToken cancellationToken = default)
    {
        var profile = SelectedManagedProfile;
        var kind = profile is null ? null : GetCredentialKind(profile);
        if (kind is null)
        {
            StoredCredentialStatus = RemoteCredentialStoreStatus.NotFound;
            return;
        }

        if (_credentialStore is null)
        {
            StoredCredentialStatus = RemoteCredentialStoreStatus.Unsupported;
            return;
        }

        var result = await _credentialStore.ContainsAsync(profile!.Id, kind.Value, cancellationToken);
        if (SelectedManagedProfile?.Id == profile.Id)
        {
            StoredCredentialStatus = result.Status;
        }
    }

    public ApplicationSettings CreateSettings() => new(
        pollingInterval: TimeSpan.FromSeconds(double.Parse(PollingIntervalSeconds, CultureInfo.InvariantCulture)),
        connectionTimeout: TimeSpan.FromSeconds(double.Parse(ConnectionTimeoutSeconds, CultureInfo.InvariantCulture)),
        theme: SelectedThemeOption?.Preference ?? ThemePreference.System,
        mockMode: MockMode,
        language: SelectedLanguageOption?.Value ?? UiLanguagePreference.PtBr,
        sidebarPreference: SelectedSidebarOption?.Value ?? SidebarPreference.Expanded);

    public void Apply(ApplicationSettings settings)
    {
        PollingIntervalSeconds = settings.PollingInterval.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        ConnectionTimeoutSeconds = settings.ConnectionTimeout.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        MockMode = settings.MockMode;
        Localizer = new NutManagerLocalizer(settings.Language);
    }

    public async Task PersistThemeAsync(ThemePreference theme, CancellationToken cancellationToken = default)
    {
        if (_settingsStore is null || !_canPersistThemeAutomatically)
        {
            return;
        }

        var settings = CopyConfirmedSettings(theme: theme);
        try
        {
            await _settingsStore.SaveAsync(settings, cancellationToken);
            _confirmedSettings = settings;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            SaveError = Localizer.Get("Appearance.SaveError");
        }
    }

    public void SetLoadError(string message)
    {
        LoadError = message;
        _canPersistThemeAutomatically = false;
    }

    public void SetProfileLoadError(string message, bool blockPersistence = false)
    {
        ProfileLoadError = message;
        _canPersistProfiles = !blockPersistence;
        NotifyProfilePropertiesChanged();
    }

    public void ApplyTheme(ThemePreference theme)
    {
        var option = ThemeOptions.Single(option => option.Preference == theme);
        if (!Equals(SelectedThemeOption, option))
        {
            SelectedThemeOption = option;
        }
    }

    partial void OnSelectedThemeOptionChanged(ThemeOption? value)
    {
        if (value is not null)
        {
            ThemeChanged?.Invoke(value.Preference);
        }
    }

    public void ApplySidebarPreference(SidebarPreference preference)
    {
        var option = SidebarOptions.Single(option => option.Value == preference);
        if (!Equals(SelectedSidebarOption, option))
        {
            SelectedSidebarOption = option;
        }
    }

    partial void OnSelectedLanguageOptionChanged(PresentationOption<UiLanguagePreference>? value)
    {
        if (value is null || _isApplyingVisualPreferences)
        {
            return;
        }

        IsLanguageRestartRequired = value.Value != Localizer.Language;
        OnPropertyChanged(nameof(IsLanguageRestartRequired));
        _ = PersistVisualPreferencesAsync(value.Value, SelectedSidebarOption?.Value ?? SidebarPreference.Expanded);
    }

    partial void OnSelectedSidebarOptionChanged(PresentationOption<SidebarPreference>? value)
    {
        if (value is null || _isApplyingVisualPreferences)
        {
            return;
        }

        SidebarPreferenceChanged?.Invoke(value.Value);
        _ = PersistVisualPreferencesAsync(SelectedLanguageOption?.Value ?? UiLanguagePreference.PtBr, value.Value);
    }

    public async Task PersistVisualPreferencesAsync(
        UiLanguagePreference language,
        SidebarPreference sidebarPreference,
        CancellationToken cancellationToken = default)
    {
        if (_settingsStore is null)
        {
            return;
        }

        var settings = CopyConfirmedSettings(language: language, sidebarPreference: sidebarPreference);
        try
        {
            await _settingsStore.SaveAsync(settings, cancellationToken);
            _confirmedSettings = settings;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            SaveError = Localizer.Get("Appearance.SaveError");
        }
    }

    private ApplicationSettings CopyConfirmedSettings(
        ThemePreference? theme = null,
        UiLanguagePreference? language = null,
        SidebarPreference? sidebarPreference = null) => new(
            _confirmedSettings.SchemaVersion,
            _confirmedSettings.PollingInterval,
            _confirmedSettings.ConnectionTimeout,
            theme ?? _confirmedSettings.Theme,
            _confirmedSettings.MockMode,
            language ?? _confirmedSettings.Language,
            sidebarPreference ?? _confirmedSettings.SidebarPreference);

    private void RequestProfileSelection(ManagedNutServerProfile? value)
    {
        if (value?.Id == _selectedManagedProfile?.Id)
        {
            return;
        }

        if (value is not null && QueueIfDirty(new PendingProfileAction(PendingProfileActionKind.SelectProfile, value.Id)))
        {
            NotifySelectionChanged();
            return;
        }

        if (value is not null)
        {
            SelectProfile(value);
        }
    }

    private void SelectProfile(ManagedNutServerProfile profile)
    {
        _isCreatingProfile = false;
        _draftSourceId = profile.Id;
        _draftBaseProfile = profile;
        ProfileDraft.Apply(profile);
        _selectedManagedProfile = profile;
        _selectedProfileCard = ManagedProfileCards.FirstOrDefault(card => card.Profile.Id == profile.Id);
        ProfileSaveError = null;
        NotifySelectionChanged();
        NotifyProfilePropertiesChanged();
        _ = RefreshStoredCredentialStatusAsync();
    }

    private void BeginCreate()
    {
        ProfileDraft.CopyFrom(ManagedNutServerProfileDraftViewModel.CreateNew());
        _isCreatingProfile = true;
        _draftSourceId = null;
        _draftBaseProfile = null;
        _selectedManagedProfile = null;
        _selectedProfileCard = null;
        ProfileSaveError = null;
        ProfileStatusMessage = Localizer.Get("Profiles.NewServerHelp");
        NotifySelectionChanged();
        NotifyProfilePropertiesChanged();
        _ = RefreshStoredCredentialStatusAsync();
    }

    private void DiscardProfileDraftCore()
    {
        var profile = _draftSourceId is { } id
            ? ManagedProfiles.FirstOrDefault(candidate => candidate.Id == id)
            : _confirmedProfiles.ActiveProfile;
        if (profile is not null)
        {
            SelectProfile(profile);
        }
    }

    private bool QueueIfDirty(PendingProfileAction action)
    {
        if (!IsProfileDraftDirty)
        {
            return false;
        }

        _pendingProfileAction = action;
        IsDirtyDraftDecisionVisible = true;
        return true;
    }

    private void ClearPendingProfileAction()
    {
        _pendingProfileAction = null;
        IsDirtyDraftDecisionVisible = false;
    }

    private async Task ExecutePendingProfileActionAsync(PendingProfileAction action, CancellationToken cancellationToken)
    {
        switch (action.Kind)
        {
            case PendingProfileActionKind.NewProfile:
                BeginCreate();
                break;
            case PendingProfileActionKind.SelectProfile when action.ProfileId is { } selectedId:
                SelectProfile(ManagedProfiles.Single(profile => profile.Id == selectedId));
                break;
            case PendingProfileActionKind.DeleteProfile when action.ProfileId is { } deletedId:
                await DeleteProfileCoreAsync(deletedId, cancellationToken);
                break;
            case PendingProfileActionKind.ActivateProfile when action.ProfileId is { } activeId:
                await ActivateProfileCoreAsync(activeId, cancellationToken);
                break;
        }
    }

    private void ApplyConfirmedProfiles(ManagedNutServerProfiles document, Guid selectedId)
    {
        _confirmedProfiles = document;
        ManagedProfiles.Clear();
        foreach (var profile in document.Profiles)
        {
            ManagedProfiles.Add(profile);
        }

        RebuildProfileCards();
        SelectProfile(ManagedProfiles.Single(profile => profile.Id == selectedId));
    }

    private void RebuildProfileCards()
    {
        ManagedProfileCards.Clear();
        foreach (var profile in ManagedProfiles)
        {
            ManagedProfileCards.Add(new ManagedProfileCardViewModel(
                profile,
                $"{profile.Monitoring.Host}:{profile.Monitoring.Port.ToString(CultureInfo.InvariantCulture)}",
                Localizer.Get(profile.Management.Mode == NutManagementMode.Local ? "Management.Local" : "Management.Remote"),
                Localizer.Get(profile.AccessMode == ManagedNutServerAccessMode.Manage ? "Access.Manage" : "Access.ReadOnly"),
                profile.Management.Mode == NutManagementMode.Remote
                    ? Localizer.Get(profile.Management.ConfigurationTransport == RemoteConfigurationTransportKind.Smb ? "Transport.Smb" : "Transport.Sftp")
                    : null,
                profile.Id == _confirmedProfiles.ActiveProfileId,
                Localizer.Get("Profiles.ActiveBadge")));
        }

        _selectedProfileCard = _selectedManagedProfile is null
            ? null
            : ManagedProfileCards.FirstOrDefault(card => card.Profile.Id == _selectedManagedProfile.Id);
        NotifySelectionChanged();
    }

    private void OnProfileDraftPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        _draftVersion++;
        ConnectionTestResultText = null;
        ConnectionTestStatus = null;
        OnPropertyChanged(nameof(SelectedManagementModeOption));
        OnPropertyChanged(nameof(SelectedAccessModeOption));
        OnPropertyChanged(nameof(SelectedConfigurationTransportOption));
        OnPropertyChanged(nameof(SelectedSshAuthenticationOption));
        OnPropertyChanged(nameof(SelectedSmbAuthenticationOption));
        RefreshProfileValidation();
        NotifyProfilePropertiesChanged();
    }

    private void RefreshProfileValidation()
    {
        _profileValidation = ProfileDraft.Validate(_confirmedProfiles.Profiles);
        ProfileValidationIssues = _profileValidation.Issues
            .Select(issue => new LocalizedValidationIssueViewModel(
                issue.Field,
                issue.Code,
                issue.Severity,
                Localizer.Get(issue.ResourceKey)))
            .ToArray();
        OnPropertyChanged(nameof(ProfileValidationIssues));
        OnPropertyChanged(nameof(NameValidationIssues));
        OnPropertyChanged(nameof(MonitoringHostValidationIssues));
        OnPropertyChanged(nameof(MonitoringPortValidationIssues));
        OnPropertyChanged(nameof(PreferredUpsValidationIssues));
        OnPropertyChanged(nameof(ManagementHostValidationIssues));
        OnPropertyChanged(nameof(SshPortValidationIssues));
        OnPropertyChanged(nameof(SshUsernameValidationIssues));
        OnPropertyChanged(nameof(SshPrivateKeyValidationIssues));
        OnPropertyChanged(nameof(RemoteDirectoryValidationIssues));
        OnPropertyChanged(nameof(SmbShareValidationIssues));
        OnPropertyChanged(nameof(SmbDirectoryValidationIssues));
        OnPropertyChanged(nameof(SmbUsernameValidationIssues));
        OnPropertyChanged(nameof(CanSaveProfile));
        OnPropertyChanged(nameof(CanTestConnection));
    }

    private IReadOnlyList<LocalizedValidationIssueViewModel> IssuesFor(string field) =>
        ProfileValidationIssues.Where(issue => issue.Field == field).ToArray();

    private void NotifyProfilePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsProfileDraftDirty));
        OnPropertyChanged(nameof(IsCreatingProfile));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(CanDeleteSelectedProfile));
        OnPropertyChanged(nameof(CanActivateSelectedProfile));
        OnPropertyChanged(nameof(CanForgetTrustedHostKey));
        OnPropertyChanged(nameof(CanPersistProfiles));
        OnPropertyChanged(nameof(CanSaveProfile));
        OnPropertyChanged(nameof(IsSelectedProfileActive));
        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(RuntimeProfileName));
        OnPropertyChanged(nameof(PersistedActiveProfileName));
        OnPropertyChanged(nameof(IsActiveProfileRestartRequired));
        OnPropertyChanged(nameof(CanForgetStoredCredential));
        OnPropertyChanged(nameof(StoredCredentialText));
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedManagedProfile));
        OnPropertyChanged(nameof(SelectedProfileCard));
    }

    partial void OnStoredCredentialStatusChanged(RemoteCredentialStoreStatus value) =>
        OnPropertyChanged(nameof(StoredCredentialText));

    partial void OnIsTestingConnectionChanged(bool value)
    {
        OnPropertyChanged(nameof(CanTestConnection));
    }

    partial void OnConnectionTestResultTextChanged(string? value) =>
        NotifyConnectionTestPresentationChanged();

    partial void OnConnectionTestToneChanged(ProfileOperationTone value) =>
        NotifyConnectionTestPresentationChanged();

    partial void OnProfileStatusMessageChanged(string? value) =>
        OnPropertyChanged(nameof(HasProfileStatusMessage));

    partial void OnProfileSaveErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasProfileSaveError));

    partial void OnSaveErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasSaveError));

    partial void OnLoadErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasLoadError));

    partial void OnProfileLoadErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasProfileLoadError));

    private static RemoteCredentialKind? GetCredentialKind(ManagedNutServerProfile profile)
    {
        var management = profile.Management;
        if (management.Mode != NutManagementMode.Remote)
        {
            return null;
        }

        if (management.ConfigurationTransport == RemoteConfigurationTransportKind.Smb)
        {
            return management.SmbAuthenticationMode == SmbAuthenticationMode.ExplicitCredentials
                ? RemoteCredentialKind.SmbPassword
                : null;
        }

        return management.SshAuthenticationMode == SshAuthenticationMode.PrivateKey && !string.IsNullOrWhiteSpace(management.SshPrivateKeyPath)
            ? RemoteCredentialKind.SshPrivateKeyPassphrase
            : RemoteCredentialKind.SshPassword;
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Localize(ApplicationSettings settings, string key) =>
        new NutManagerLocalizer(settings?.Language ?? UiLanguagePreference.PtBr).Get(key);

    private void NotifyConnectionTestPresentationChanged()
    {
        OnPropertyChanged(nameof(HasConnectionTestResult));
        OnPropertyChanged(nameof(IsConnectionTestCritical));
        OnPropertyChanged(nameof(IsConnectionTestHealthy));
        OnPropertyChanged(nameof(IsConnectionTestWarning));
        OnPropertyChanged(nameof(IsConnectionTestNeutral));
    }

    private sealed record PendingProfileAction(PendingProfileActionKind Kind, Guid? ProfileId = null);

    private enum PendingProfileActionKind
    {
        NewProfile,
        SelectProfile,
        DeleteProfile,
        ActivateProfile
    }
}

public sealed record LocalizedValidationIssueViewModel(
    string Field,
    string Code,
    ValidationSeverity Severity,
    string Message)
{
    public bool IsError => Severity == ValidationSeverity.Error;
    public bool IsWarning => Severity == ValidationSeverity.Warning;
    public bool IsInfo => Severity == ValidationSeverity.Info;
}

public sealed record ManagedProfileCardViewModel(
    ManagedNutServerProfile Profile,
    string Endpoint,
    string ManagementMode,
    string AccessMode,
    string? Transport,
    bool IsActive,
    string ActiveText)
{
    public string Name => Profile.Name;
    public bool HasTransport => !string.IsNullOrWhiteSpace(Transport);
}

public enum ProfileOperationTone
{
    Neutral,
    Healthy,
    Warning,
    Critical
}
