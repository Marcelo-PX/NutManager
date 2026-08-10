using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NutManager.App.Services;
using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.App.ViewModels;

public sealed partial class SettingsPageViewModel : PageViewModel
{
    private readonly IApplicationSettingsStore? _settingsStore;
    private readonly IManagedNutServerProfileStore? _profileStore;
    private readonly ManagedNutServerProfileUpdateService? _profileMutator;
    private readonly bool _usesManagedProfileEndpoint;
    private ApplicationSettings _confirmedSettings;
    private ManagedNutServerProfiles _confirmedProfiles;
    private Guid? _draftSourceId;
    private ManagedNutServerProfile? _draftBaseProfile;
    private bool _isCreatingProfile;
    private bool _suppressProfileSelection;
    private bool _canPersistThemeAutomatically = true;
    private bool _canPersistProfiles = true;

    public SettingsPageViewModel() : this(new ApplicationSettings(), null, null, null) { }

    public SettingsPageViewModel(ApplicationSettings settings, IApplicationSettingsStore? store)
        : this(settings, store, null, null)
    {
    }

    public SettingsPageViewModel(
        ApplicationSettings settings,
        IApplicationSettingsStore? settingsStore,
        ManagedNutServerProfiles? profiles,
        IManagedNutServerProfileStore? profileStore,
        ManagedNutServerProfileUpdateService? profileMutator = null)
        : base("Configurações", "Defina as preferências locais e os servidores NUT gerenciados.")
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settingsStore = settingsStore;
        _profileStore = profileStore;
        _profileMutator = profileStore is null ? null : profileMutator ?? new ManagedNutServerProfileUpdateService(profileStore);
        _usesManagedProfileEndpoint = profiles is not null;
        _confirmedSettings = settings;
        _confirmedProfiles = profiles ?? ManagedNutServerProfiles.CreateLegacyProfile(settings);
        ManagedProfiles = new ObservableCollection<ManagedNutServerProfile>(_confirmedProfiles.Profiles);
        ProfileDraft = new ManagedNutServerProfileDraftViewModel(_confirmedProfiles.ActiveProfile);
        ProfileDraft.PropertyChanged += OnProfileDraftPropertyChanged;
        _draftSourceId = _confirmedProfiles.ActiveProfileId;
        _draftBaseProfile = _confirmedProfiles.ActiveProfile;
        _selectedManagedProfile = _confirmedProfiles.ActiveProfile;
        Apply(settings);
        ThemeOptions = [new(ThemePreference.System, "Seguir o sistema"), new(ThemePreference.Light, "Claro"), new(ThemePreference.Dark, "Escuro")];
        SelectedThemeOption = ThemeOptions.Single(option => option.Preference == settings.Theme);
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public IReadOnlyList<NutManagementMode> ManagementModes { get; } = Enum.GetValues<NutManagementMode>();

    public IReadOnlyList<ManagedNutServerAccessMode> AccessModes { get; } = Enum.GetValues<ManagedNutServerAccessMode>();

    public IReadOnlyList<RemoteConfigurationTransportKind> ConfigurationTransports { get; } = Enum.GetValues<RemoteConfigurationTransportKind>();

    public IReadOnlyList<SmbAuthenticationMode> SmbAuthenticationModes { get; } = Enum.GetValues<SmbAuthenticationMode>();

    public ObservableCollection<ManagedNutServerProfile> ManagedProfiles { get; }

    public ManagedNutServerProfileDraftViewModel ProfileDraft { get; }

    [ObservableProperty] private string _host = "localhost";
    [ObservableProperty] private string _port = "3493";
    [ObservableProperty] private string? _preferredUpsName;
    [ObservableProperty] private string _pollingIntervalSeconds = "5";
    [ObservableProperty] private string _connectionTimeoutSeconds = "5";
    [ObservableProperty] private bool _mockMode = true;
    [ObservableProperty] private ThemeOption? _selectedThemeOption;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string? _saveError;
    [ObservableProperty] private string? _loadError;
    [ObservableProperty] private bool _isSaved;
    [ObservableProperty] private ManagedNutServerProfile? _selectedManagedProfile;
    [ObservableProperty] private bool _isSavingProfile;
    [ObservableProperty] private bool _isProfileSaved;
    [ObservableProperty] private string? _profileSaveError;
    [ObservableProperty] private string? _profileLoadError;
    [ObservableProperty] private string? _profileStatusMessage;

    public bool IsProfileDraftDirty => _isCreatingProfile || (_draftBaseProfile is not null && !ProfileDraft.Matches(_draftBaseProfile));

    public bool IsCreatingProfile => _isCreatingProfile;

    public bool CanDeleteSelectedProfile => CanPersistProfiles && SelectedManagedProfile is not null && ManagedProfiles.Count > 1 && SelectedManagedProfile.Id != _confirmedProfiles.ActiveProfileId && !IsProfileDraftDirty;

    public bool CanActivateSelectedProfile => CanPersistProfiles && SelectedManagedProfile is not null && SelectedManagedProfile.Id != _confirmedProfiles.ActiveProfileId && !IsProfileDraftDirty;

    public bool CanForgetTrustedHostKey => CanPersistProfiles && !IsProfileDraftDirty && SelectedManagedProfile is { Management.Mode: NutManagementMode.Remote, Management.TrustedHostKeyFingerprint: not null };

    public bool IsSelectedProfileActive => SelectedManagedProfile?.Id == _confirmedProfiles.ActiveProfileId;

    public string ActiveProfileName => _confirmedProfiles.ActiveProfile.Name;

    public bool HasProfileLoadError => !string.IsNullOrWhiteSpace(ProfileLoadError);

    public bool CanPersistProfiles => _profileStore is not null && _canPersistProfiles && !IsSavingProfile;

    public event Action<ThemePreference>? ThemeChanged;

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
            SaveError = "Não foi possível salvar as configurações gerais.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void CreateLocalProfile()
    {
        if (IsProfileDraftDirty)
        {
            ProfileSaveError = "Salve ou descarte as alterações do perfil antes de criar outro perfil.";
            return;
        }

        BeginCreate(ManagedNutServerProfileDraftViewModel.CreateLocal());
    }

    [RelayCommand]
    private void CreateRemoteProfile()
    {
        if (IsProfileDraftDirty)
        {
            ProfileSaveError = "Salve ou descarte as alterações do perfil antes de criar outro perfil.";
            return;
        }

        BeginCreate(ManagedNutServerProfileDraftViewModel.CreateRemote());
    }

    [RelayCommand]
    private async Task SaveProfileAsync(CancellationToken cancellationToken = default)
    {
        var mutator = _profileMutator;
        if (mutator is null || !CanPersistProfiles)
        {
            ProfileSaveError = "Os perfis não podem ser salvos até que o problema de carregamento seja resolvido.";
            return;
        }

        IsSavingProfile = true;
        IsProfileSaved = false;
        ProfileSaveError = null;
        ProfileStatusMessage = null;
        try
        {
            var updated = ProfileDraft.CreateProfile();
            var document = _isCreatingProfile
                ? await mutator.CreateProfileAsync(updated, cancellationToken)
                : _draftBaseProfile is null
                    ? null
                    : await mutator.SaveExistingProfileAsync(_draftBaseProfile, updated, cancellationToken);
            if (document is null)
            {
                ProfileSaveError = "O perfil foi atualizado por outro fluxo. Recarregue o perfil antes de salvar.";
                return;
            }

            ApplyConfirmedProfiles(document, updated.Id);
            _isCreatingProfile = false;
            IsProfileSaved = true;
            ProfileStatusMessage = "Perfil salvo. Alterações no servidor ativo serão aplicadas ao reiniciar o NutManager.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ProfileSaveError = "Não foi possível validar ou salvar o perfil.";
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
        var mutator = _profileMutator;
        if (SelectedManagedProfile is null || mutator is null || !CanPersistProfiles)
        {
            return;
        }

        if (IsProfileDraftDirty)
        {
            ProfileSaveError = "Salve ou descarte as alterações do perfil antes de excluir um perfil.";
            return;
        }

        if (SelectedManagedProfile.Id == _confirmedProfiles.ActiveProfileId)
        {
            ProfileSaveError = "Ative outro perfil antes de excluir o perfil ativo.";
            return;
        }

        if (ManagedProfiles.Count <= 1)
        {
            ProfileSaveError = "Não é possível excluir o último perfil.";
            return;
        }

        try
        {
            var document = await mutator.DeleteProfileAsync(SelectedManagedProfile.Id, cancellationToken);
            if (document is null)
            {
                ProfileSaveError = "O perfil foi alterado por outro fluxo. Recarregue a lista antes de excluir.";
                return;
            }

            ApplyConfirmedProfiles(document, document.ActiveProfileId);
            ProfileStatusMessage = "Perfil removido.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ProfileSaveError = "Não foi possível excluir o perfil.";
        }
    }

    [RelayCommand]
    private async Task ActivateSelectedProfileAsync(CancellationToken cancellationToken = default)
    {
        var mutator = _profileMutator;
        if (SelectedManagedProfile is null || mutator is null || !CanPersistProfiles || SelectedManagedProfile.Id == _confirmedProfiles.ActiveProfileId)
        {
            return;
        }

        if (IsProfileDraftDirty)
        {
            ProfileSaveError = "Salve ou descarte as alterações do perfil antes de tornar outro perfil ativo.";
            return;
        }

        try
        {
            var document = await mutator.ActivateProfileAsync(SelectedManagedProfile.Id, cancellationToken);
            if (document is null)
            {
                ProfileSaveError = "O perfil foi alterado por outro fluxo. Recarregue a lista antes de ativar.";
                return;
            }

            ApplyConfirmedProfiles(document, SelectedManagedProfile.Id);
            ProfileStatusMessage = "Perfil ativo salvo. Reinicie o NutManager para aplicar a conexão.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ProfileSaveError = "Não foi possível tornar o perfil ativo.";
        }
    }

    [RelayCommand]
    private async Task ForgetTrustedHostKeyAsync(CancellationToken cancellationToken = default)
    {
        var mutator = _profileMutator;
        var profile = SelectedManagedProfile;
        if (mutator is null || profile is null || !CanForgetTrustedHostKey)
        {
            return;
        }

        IsSavingProfile = true;
        ProfileSaveError = null;
        try
        {
            var updated = await mutator.ForgetTrustedHostKeyAsync(profile, cancellationToken);
            var document = updated is null ? null : await mutator.LoadCurrentAsync(cancellationToken);
            if (document is null || updated is null)
            {
                ProfileSaveError = "A chave confiável do host foi alterada por outro fluxo. Atualize o perfil antes de tentar novamente.";
                return;
            }

            ApplyConfirmedProfiles(document, updated.Id);
            ProfileStatusMessage = "A chave confiável do host foi removida. Uma nova chave deverá ser revisada antes da próxima conexão.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            ProfileSaveError = "Não foi possível remover a chave confiável do host.";
        }
        finally
        {
            IsSavingProfile = false;
            NotifyProfilePropertiesChanged();
        }
    }

    [RelayCommand]
    private void DiscardProfileDraft()
    {
        var profile = _draftSourceId is { } id
            ? ManagedProfiles.FirstOrDefault(candidate => candidate.Id == id)
            : _confirmedProfiles.ActiveProfile;
        if (profile is not null)
        {
            ProfileDraft.Apply(profile);
            _isCreatingProfile = false;
            _draftSourceId = profile.Id;
            _draftBaseProfile = profile;
            _suppressProfileSelection = true;
            SelectedManagedProfile = profile;
            _suppressProfileSelection = false;
            ProfileSaveError = null;
            NotifyProfilePropertiesChanged();
        }
    }

    public ApplicationSettings CreateSettings()
    {
        var legacyEndpoint = _usesManagedProfileEndpoint ? _confirmedProfiles.ActiveProfile.Monitoring : new NutMonitoringProfile(Host, int.Parse(Port, System.Globalization.CultureInfo.InvariantCulture), PreferredUpsName);
        return new ApplicationSettings(
            host: legacyEndpoint.Host,
            port: legacyEndpoint.Port,
            preferredUpsName: legacyEndpoint.PreferredUpsName,
            pollingInterval: TimeSpan.FromSeconds(double.Parse(PollingIntervalSeconds, System.Globalization.CultureInfo.InvariantCulture)),
            connectionTimeout: TimeSpan.FromSeconds(double.Parse(ConnectionTimeoutSeconds, System.Globalization.CultureInfo.InvariantCulture)),
            theme: SelectedThemeOption?.Preference ?? ThemePreference.System,
            mockMode: MockMode);
    }

    public void Apply(ApplicationSettings settings)
    {
        Host = settings.Host;
        Port = settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PreferredUpsName = settings.PreferredUpsName;
        PollingIntervalSeconds = settings.PollingInterval.TotalSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        ConnectionTimeoutSeconds = settings.ConnectionTimeout.TotalSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        MockMode = settings.MockMode;
    }

    public async Task PersistThemeAsync(ThemePreference theme, CancellationToken cancellationToken = default)
    {
        if (_settingsStore is null || !_canPersistThemeAutomatically)
        {
            return;
        }

        var settings = new ApplicationSettings(
            _confirmedSettings.SchemaVersion,
            _usesManagedProfileEndpoint ? _confirmedProfiles.ActiveProfile.Monitoring.Host : _confirmedSettings.Host,
            _usesManagedProfileEndpoint ? _confirmedProfiles.ActiveProfile.Monitoring.Port : _confirmedSettings.Port,
            _usesManagedProfileEndpoint ? _confirmedProfiles.ActiveProfile.Monitoring.PreferredUpsName : _confirmedSettings.PreferredUpsName,
            _confirmedSettings.PollingInterval,
            _confirmedSettings.ConnectionTimeout,
            theme,
            _confirmedSettings.MockMode);
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
            SaveError = "Não foi possível salvar o tema.";
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
        OnPropertyChanged(nameof(CanPersistProfiles));
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

    partial void OnSelectedManagedProfileChanged(ManagedNutServerProfile? value)
    {
        if (_suppressProfileSelection)
        {
            return;
        }

        if (IsProfileDraftDirty)
        {
            ProfileSaveError = "Salve ou descarte as alterações do perfil antes de selecionar outro perfil.";
            _suppressProfileSelection = true;
            SelectedManagedProfile = _draftSourceId is { } id ? ManagedProfiles.FirstOrDefault(profile => profile.Id == id) : null;
            _suppressProfileSelection = false;
            return;
        }

        if (value is not null)
        {
            _isCreatingProfile = false;
            _draftSourceId = value.Id;
            _draftBaseProfile = value;
            ProfileDraft.Apply(value);
            ProfileSaveError = null;
            NotifyProfilePropertiesChanged();
        }
    }

    private void BeginCreate(ManagedNutServerProfileDraftViewModel draft)
    {
        ProfileDraft.CopyFrom(draft);
        _isCreatingProfile = true;
        _draftSourceId = null;
        _draftBaseProfile = null;
        _suppressProfileSelection = true;
        SelectedManagedProfile = null;
        _suppressProfileSelection = false;
        ProfileSaveError = null;
        ProfileStatusMessage = "Preencha o perfil e salve-o para adicioná-lo à lista.";
        NotifyProfilePropertiesChanged();
    }

    private void ApplyConfirmedProfiles(ManagedNutServerProfiles document, Guid selectedId)
    {
        _confirmedProfiles = document;
        ManagedProfiles.Clear();
        foreach (var profile in document.Profiles)
        {
            ManagedProfiles.Add(profile);
        }

        var selected = ManagedProfiles.Single(profile => profile.Id == selectedId);
        _draftSourceId = selected.Id;
        _draftBaseProfile = selected;
        _isCreatingProfile = false;
        ProfileDraft.Apply(selected);
        _suppressProfileSelection = true;
        SelectedManagedProfile = selected;
        _suppressProfileSelection = false;
        NotifyProfilePropertiesChanged();
    }

    private void OnProfileDraftPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs) => NotifyProfilePropertiesChanged();

    private void NotifyProfilePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsProfileDraftDirty));
        OnPropertyChanged(nameof(IsCreatingProfile));
        OnPropertyChanged(nameof(CanDeleteSelectedProfile));
        OnPropertyChanged(nameof(CanActivateSelectedProfile));
        OnPropertyChanged(nameof(CanForgetTrustedHostKey));
        OnPropertyChanged(nameof(CanPersistProfiles));
        OnPropertyChanged(nameof(IsSelectedProfileActive));
        OnPropertyChanged(nameof(ActiveProfileName));
    }
}
