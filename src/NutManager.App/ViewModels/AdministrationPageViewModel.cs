using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.App.ViewModels;

public sealed partial class AdministrationPageViewModel : PageViewModel
{
    private const string UnavailableText = "Indisponível";
    private readonly ILocalNutInstallationDetector? _installationDetector;
    private readonly INutConfigurationFilePipeline? _configurationPipeline;
    private NutConfigurationFileSnapshot? _loadedSnapshot;
    private NutConfigurationPreparedChange? _preparedChange;
    private IReadOnlyList<NutConfigurationEntryViewModel> _entries = Array.Empty<NutConfigurationEntryViewModel>();
    private int _draftVersion;
    private int _preparedDraftVersion = -1;

    public AdministrationPageViewModel()
        : this(null, null)
    {
    }

    public AdministrationPageViewModel(
        ILocalNutInstallationDetector? installationDetector,
        INutConfigurationFilePipeline? configurationPipeline)
        : base("Administração", "Edite entradas existentes da configuração local do NUT com revisão e confirmação explícita.")
    {
        _installationDetector = installationDetector;
        _configurationPipeline = configurationPipeline;
        ConfigurationFiles = new ObservableCollection<NutConfigurationFileItemViewModel>(CreateFileItems());
        Sections = Array.Empty<NutConfigurationSectionViewModel>();
        PreviewLines = Array.Empty<NutConfigurationPreviewLineViewModel>();
    }

    public ObservableCollection<NutConfigurationFileItemViewModel> ConfigurationFiles { get; }

    [ObservableProperty]
    private IReadOnlyList<NutConfigurationSectionViewModel> _sections;

    [ObservableProperty]
    private IReadOnlyList<NutConfigurationPreviewLineViewModel> _previewLines;

    [ObservableProperty]
    private NutConfigurationFileItemViewModel? _selectedFile;

    [ObservableProperty]
    private bool _isDetectingInstallation;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPreviewConfirmed;

    [ObservableProperty]
    private string _installationStatusText = "Nenhuma instalação NUT local encontrada";

    [ObservableProperty]
    private string _installationDirectoryText = UnavailableText;

    [ObservableProperty]
    private string _configurationDirectoryText = UnavailableText;

    [ObservableProperty]
    private string _installationVersionText = UnavailableText;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isCriticalResult;

    [ObservableProperty]
    private string? _backupPath;

    [ObservableProperty]
    private string? _recoveryPath;

    public string EditingScopeText => "Esta versão edita entradas existentes. Criação e remoção de entradas serão tratadas separadamente.";

    public string SelectedFileName => SelectedFile?.FileName ?? UnavailableText;

    public string SelectedFileStatusText => SelectedFile?.StatusText ?? "Nenhum arquivo selecionado";

    public string SelectedFileEncodingText => _loadedSnapshot is null ? UnavailableText : ToEncodingText(_loadedSnapshot.Encoding);

    public bool HasLoadedFile => _loadedSnapshot is not null;

    public bool HasNoLoadedFile => !HasLoadedFile;

    public bool HasDraftChanges => _entries.Any(entry => entry.IsChanged);

    public bool HasPreview => _preparedChange is not null;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool HasBackupPath => !string.IsNullOrWhiteSpace(BackupPath);

    public bool HasRecoveryPath => !string.IsNullOrWhiteSpace(RecoveryPath);

    public bool CanReview => HasLoadedFile && HasDraftChanges && !IsBusy;

    public bool CanApply => HasPreview && _preparedDraftVersion == _draftVersion && IsPreviewConfirmed && !IsBusy;

    public bool CanDiscard => HasDraftChanges || HasPreview;

    public bool CanReload => SelectedFile is not null && !HasDraftChanges && !IsBusy;

    public bool CanDetectInstallation => !IsDetectingInstallation;

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await RefreshInstallationAsync(cancellationToken);

    public async Task RefreshInstallationAsync(CancellationToken cancellationToken = default)
    {
        if (_installationDetector is null)
        {
            ApplyInstallation(NutInstallationInfo.NotDetected());
            SetStatus("A detecção local não está disponível.");
            return;
        }

        IsDetectingInstallation = true;
        SetStatus(null);
        try
        {
            ApplyInstallation(await _installationDetector.DetectAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("A detecção da instalação foi cancelada.");
        }
        catch (Exception)
        {
            ApplyInstallation(NutInstallationInfo.NotDetected());
            SetStatus("Não foi possível detectar a instalação local do NUT.");
        }
        finally
        {
            IsDetectingInstallation = false;
        }
    }

    public async Task InspectInstallationDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (_installationDetector is null)
        {
            SetStatus("A detecção local não está disponível.");
            return;
        }

        IsDetectingInstallation = true;
        SetStatus(null);
        try
        {
            ApplyInstallation(await _installationDetector.InspectDirectoryAsync(directory, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("A inspeção da pasta foi cancelada.");
        }
        catch (Exception)
        {
            SetStatus("Não foi possível inspecionar a pasta selecionada.");
        }
        finally
        {
            IsDetectingInstallation = false;
        }
    }

    public async Task SelectFileAsync(NutConfigurationFileItemViewModel? file, CancellationToken cancellationToken = default)
    {
        if (file is null || ReferenceEquals(file, SelectedFile))
        {
            return;
        }

        if (HasDraftChanges || HasPreview)
        {
            SetStatus("Aplique ou descarte as alterações antes de trocar de arquivo.");
            OnPropertyChanged(nameof(SelectedFile));
            return;
        }

        if (!file.CanLoad)
        {
            SetStatus(file.State switch
            {
                NutConfigurationFileState.Missing => "O arquivo não existe neste diretório.",
                NutConfigurationFileState.AccessDenied => "Permissão insuficiente. A elevação administrativa será tratada pela etapa de administração do Windows.",
                _ => "O arquivo não está disponível para carregamento."
            });
            return;
        }

        SelectedFile = file;
        await LoadSelectedFileAsync(cancellationToken);
    }

    public async Task ReviewChangesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanReview || _loadedSnapshot is null || _configurationPipeline is null)
        {
            return;
        }

        IsBusy = true;
        InvalidatePreview();
        SetStatus(null);
        try
        {
            var reloaded = await _configurationPipeline.LoadAsync(
                _loadedSnapshot.TargetPath,
                _loadedSnapshot.FileKind,
                cancellationToken);
            if (reloaded.Status != NutConfigurationLoadStatus.Success || reloaded.Snapshot is null)
            {
                SetLoadFailureStatus(reloaded.Status);
                return;
            }

            if (!MatchesLoadedSnapshot(reloaded.Snapshot, _loadedSnapshot))
            {
                SetStatus("O arquivo foi alterado externamente desde que foi carregado.");
                return;
            }

            if (!TryApplyDrafts(reloaded.Snapshot.Document))
            {
                SetStatus("O arquivo foi alterado externamente ou não é mais compatível com as alterações em edição.");
                return;
            }

            var prepared = _configurationPipeline.Prepare(reloaded.Snapshot);
            if (!prepared.HasChanges)
            {
                SetStatus("Não há alterações para revisar.");
                return;
            }

            _preparedChange = prepared;
            _preparedDraftVersion = _draftVersion;
            PreviewLines = prepared.Preview.Lines
                .Select(line => new NutConfigurationPreviewLineViewModel(line.LineNumber, line.OriginalText, line.CandidateText, line.IsRedacted))
                .ToArray();
            NotifyWorkflowPropertiesChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("A revisão das alterações foi cancelada.");
        }
        catch (Exception)
        {
            SetStatus("Não foi possível preparar a revisão das alterações.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyChangesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApply || _preparedChange is null || _configurationPipeline is null)
        {
            return;
        }

        IsBusy = true;
        SetStatus(null);
        BackupPath = null;
        RecoveryPath = null;
        try
        {
            var result = await _configurationPipeline.ApplyAsync(_preparedChange, cancellationToken);
            BackupPath = result.BackupPath;
            RecoveryPath = result.RecoveryPath;
            ApplyResultStatus(result);

            if (result.Status == NutConfigurationApplyStatus.Success)
            {
                var successMessage = StatusMessage;
                await LoadSelectedFileAsync(CancellationToken.None, preserveStatus: true);
                SetStatus(successMessage);
            }
            else
            {
                InvalidatePreview();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("A aplicação das alterações foi cancelada.");
        }
        catch (Exception)
        {
            SetStatus("Não foi possível aplicar a configuração.", critical: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ReloadSelectedFileAsync(CancellationToken cancellationToken = default)
    {
        if (HasDraftChanges)
        {
            SetStatus("Há alterações locais. Descarte-as antes de recarregar o arquivo.");
            return;
        }

        await LoadSelectedFileAsync(cancellationToken);
    }

    public async Task DiscardChangesAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedFile is null)
        {
            return;
        }

        InvalidatePreview();
        foreach (var entry in _entries)
        {
            entry.ResetDraft();
        }

        await LoadSelectedFileAsync(cancellationToken);
    }

    [RelayCommand]
    private Task DetectInstallationAsync() => RefreshInstallationAsync();

    [RelayCommand]
    private Task ReviewAsync() => ReviewChangesAsync();

    [RelayCommand]
    private Task ApplyAsync() => ApplyChangesAsync();

    [RelayCommand]
    private Task ReloadAsync() => ReloadSelectedFileAsync();

    [RelayCommand]
    private Task DiscardAsync() => DiscardChangesAsync();

    private async Task LoadSelectedFileAsync(CancellationToken cancellationToken, bool preserveStatus = false)
    {
        if (SelectedFile is not { CanLoad: true, FullPath: { } path } file || _configurationPipeline is null)
        {
            return;
        }

        IsBusy = true;
        if (!preserveStatus)
        {
            SetStatus(null);
            BackupPath = null;
            RecoveryPath = null;
        }

        try
        {
            var result = await _configurationPipeline.LoadAsync(path, file.FileKind, cancellationToken);
            if (result.Status != NutConfigurationLoadStatus.Success || result.Snapshot is null)
            {
                ClearLoadedDocument();
                SetLoadFailureStatus(result.Status);
                return;
            }

            _loadedSnapshot = result.Snapshot;
            file.SetLoaded();
            BuildEntries(result.Snapshot.Document);
            InvalidatePreview();
            OnPropertyChanged(nameof(SelectedFileEncodingText));
            OnPropertyChanged(nameof(HasLoadedFile));
            OnPropertyChanged(nameof(HasNoLoadedFile));
            NotifyWorkflowPropertiesChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("O carregamento do arquivo foi cancelado.");
        }
        catch (Exception)
        {
            ClearLoadedDocument();
            SetStatus("Não foi possível carregar o arquivo de configuração.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyInstallation(NutInstallationInfo installation)
    {
        InstallationStatusText = installation.IsDetected
            ? "Instalação NUT encontrada"
            : "Nenhuma instalação NUT local encontrada";
        InstallationDirectoryText = installation.InstallationDirectory ?? UnavailableText;
        ConfigurationDirectoryText = installation.ConfigurationDirectory ?? UnavailableText;
        InstallationVersionText = installation.Version ?? UnavailableText;

        var filesByName = installation.ConfigurationFiles
            .GroupBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var file in ConfigurationFiles)
        {
            filesByName.TryGetValue(file.FileName, out var info);
            file.ApplyInstallationInfo(info);
        }
    }

    private void BuildEntries(NutConfigurationDocument document)
    {
        foreach (var entry in _entries)
        {
            entry.PropertyChanged -= OnEntryPropertyChanged;
        }

        var groups = new List<NutConfigurationSectionViewModel>();
        NutConfigurationSectionViewModel? currentGroup = null;
        var entries = new List<NutConfigurationEntryViewModel>();
        var rawNodeCount = 0;

        for (var index = 0; index < document.Nodes.Count; index++)
        {
            var node = document.Nodes[index];
            if (node is NutSectionNode section)
            {
                currentGroup = new NutConfigurationSectionViewModel(section.Name);
                groups.Add(currentGroup);
                continue;
            }

            NutConfigurationEntryViewModel? entry = node switch
            {
                NutConfigurationAssignmentNode assignment => NutConfigurationEntryViewModel.ForAssignment(index, assignment),
                NutConfigurationDirectiveNode directive => NutConfigurationEntryViewModel.ForDirective(index, directive),
                _ => null
            };
            if (entry is null)
            {
                rawNodeCount++;
                continue;
            }

            currentGroup ??= CreateGeneralGroup(groups);
            currentGroup.Entries.Add(entry);
            entries.Add(entry);
            entry.PropertyChanged += OnEntryPropertyChanged;
        }

        foreach (var group in groups)
        {
            group.SetRawContentSummary(rawNodeCount);
        }

        _entries = entries;
        Sections = groups;
        _draftVersion++;
        NotifyWorkflowPropertiesChanged();
    }

    private static NutConfigurationSectionViewModel CreateGeneralGroup(ICollection<NutConfigurationSectionViewModel> groups)
    {
        var group = new NutConfigurationSectionViewModel("Geral");
        groups.Add(group);
        return group;
    }

    private void ClearLoadedDocument()
    {
        foreach (var entry in _entries)
        {
            entry.PropertyChanged -= OnEntryPropertyChanged;
        }

        _loadedSnapshot = null;
        _entries = Array.Empty<NutConfigurationEntryViewModel>();
        Sections = Array.Empty<NutConfigurationSectionViewModel>();
        InvalidatePreview();
        OnPropertyChanged(nameof(SelectedFileEncodingText));
        OnPropertyChanged(nameof(HasLoadedFile));
        OnPropertyChanged(nameof(HasNoLoadedFile));
        NotifyWorkflowPropertiesChanged();
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(NutConfigurationEntryViewModel.DraftValue))
        {
            return;
        }

        _draftVersion++;
        InvalidatePreview();
        NotifyWorkflowPropertiesChanged();
    }

    private bool TryApplyDrafts(NutConfigurationDocument document)
    {
        foreach (var entry in _entries.Where(entry => entry.IsChanged))
        {
            if (entry.NodeIndex < 0 || entry.NodeIndex >= document.Nodes.Count || !entry.TryApply(document.Nodes[entry.NodeIndex]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesLoadedSnapshot(NutConfigurationFileSnapshot current, NutConfigurationFileSnapshot loaded) =>
        current.OriginalLength == loaded.OriginalLength &&
        string.Equals(current.OriginalFingerprint, loaded.OriginalFingerprint, StringComparison.Ordinal) &&
        current.FileKind == loaded.FileKind;

    private void InvalidatePreview()
    {
        _preparedChange = null;
        _preparedDraftVersion = -1;
        PreviewLines = Array.Empty<NutConfigurationPreviewLineViewModel>();
        IsPreviewConfirmed = false;
        NotifyWorkflowPropertiesChanged();
    }

    private void ApplyResultStatus(NutConfigurationApplyResult result)
    {
        switch (result.Status)
        {
            case NutConfigurationApplyStatus.Success:
                SetStatus("Configuração aplicada com sucesso.");
                break;
            case NutConfigurationApplyStatus.NoChanges:
                SetStatus("Não há alterações para aplicar.");
                break;
            case NutConfigurationApplyStatus.TargetNotFound:
                SetStatus("O arquivo não existe neste diretório.");
                break;
            case NutConfigurationApplyStatus.ChangedExternally:
                SetStatus("O arquivo foi alterado externamente desde que foi carregado.");
                break;
            case NutConfigurationApplyStatus.ChangedExternallyRollbackFailed:
                SetStatus("O arquivo foi alterado externamente e a recuperação exige atenção manual.", critical: true);
                break;
            case NutConfigurationApplyStatus.CandidateValidationFailed:
                SetStatus("A validação da configuração candidata falhou.");
                break;
            case NutConfigurationApplyStatus.TempWriteFailed:
                SetStatus("Não foi possível preparar o arquivo temporário.");
                break;
            case NutConfigurationApplyStatus.ReplaceFailed:
                SetStatus("Não foi possível substituir o arquivo de configuração.");
                break;
            case NutConfigurationApplyStatus.PostApplyValidationFailedRolledBack:
                SetStatus("A validação falhou e a configuração original foi restaurada.");
                break;
            case NutConfigurationApplyStatus.PostApplyValidationFailedRollbackFailed:
                SetStatus("A validação falhou e a configuração pode necessitar recuperação manual.", critical: true);
                break;
            case NutConfigurationApplyStatus.VerificationFailedRolledBack:
                SetStatus("A verificação falhou e a configuração original foi restaurada.");
                break;
            case NutConfigurationApplyStatus.VerificationFailedRollbackFailed:
                SetStatus("A verificação falhou e a configuração pode necessitar recuperação manual.", critical: true);
                break;
            case NutConfigurationApplyStatus.Cancelled:
                SetStatus("A aplicação das alterações foi cancelada.");
                break;
            default:
                SetStatus("Não foi possível aplicar a configuração.", critical: true);
                break;
        }
    }

    private void SetLoadFailureStatus(NutConfigurationLoadStatus status) =>
        SetStatus(status switch
        {
            NutConfigurationLoadStatus.TargetNotFound => "O arquivo não existe neste diretório.",
            NutConfigurationLoadStatus.AccessDenied => "Permissão insuficiente. A elevação administrativa será tratada pela etapa de administração do Windows.",
            NutConfigurationLoadStatus.UnsupportedEncoding => "A codificação do arquivo não é suportada.",
            NutConfigurationLoadStatus.Cancelled => "O carregamento do arquivo foi cancelado.",
            _ => "Não foi possível carregar o arquivo de configuração."
        });

    private void SetStatus(string? message, bool critical = false)
    {
        StatusMessage = message;
        IsCriticalResult = critical;
    }

    private void NotifyWorkflowPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasDraftChanges));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(CanReview));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanDiscard));
        OnPropertyChanged(nameof(CanReload));
    }

    private static IReadOnlyList<NutConfigurationFileItemViewModel> CreateFileItems() =>
    [
        new("Geral", "nut.conf", "nut.conf", NutConfigurationFileKind.NutConf),
        new("UPS e drivers", "ups.conf", "ups.conf", NutConfigurationFileKind.UpsConf),
        new("Servidor", "upsd.conf", "upsd.conf", NutConfigurationFileKind.UpsdConf),
        new("Usuários", "upsd.users", "upsd.users", NutConfigurationFileKind.UpsdUsers),
        new("Monitoramento", "upsmon.conf", "upsmon.conf", NutConfigurationFileKind.UpsmonConf)
    ];

    private static string ToEncodingText(NutConfigurationTextEncoding encoding) => encoding switch
    {
        NutConfigurationTextEncoding.Utf8 => "UTF-8",
        NutConfigurationTextEncoding.Utf8Bom => "UTF-8 com BOM",
        NutConfigurationTextEncoding.Utf16LittleEndian => "UTF-16 LE",
        NutConfigurationTextEncoding.Utf16BigEndian => "UTF-16 BE",
        _ => UnavailableText
    };

    partial void OnSelectedFileChanged(NutConfigurationFileItemViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedFileName));
        OnPropertyChanged(nameof(SelectedFileStatusText));
        OnPropertyChanged(nameof(CanReload));
    }

    partial void OnIsBusyChanged(bool value) => NotifyWorkflowPropertiesChanged();

    partial void OnIsDetectingInstallationChanged(bool value) => OnPropertyChanged(nameof(CanDetectInstallation));

    partial void OnIsPreviewConfirmedChanged(bool value) => OnPropertyChanged(nameof(CanApply));

    partial void OnBackupPathChanged(string? value) => OnPropertyChanged(nameof(HasBackupPath));

    partial void OnRecoveryPathChanged(string? value) => OnPropertyChanged(nameof(HasRecoveryPath));

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatusMessage));
}

public enum NutConfigurationFileState
{
    NotLoaded,
    Available,
    Missing,
    AccessDenied,
    Loaded,
    Error
}

public sealed partial class NutConfigurationFileItemViewModel : ObservableObject
{
    public NutConfigurationFileItemViewModel(string category, string title, string fileName, NutConfigurationFileKind fileKind)
    {
        Category = category;
        Title = title;
        FileName = fileName;
        FileKind = fileKind;
        State = NutConfigurationFileState.Missing;
    }

    public string Category { get; }

    public string Title { get; }

    public string FileName { get; }

    public NutConfigurationFileKind FileKind { get; }

    [ObservableProperty]
    private string? _fullPath;

    [ObservableProperty]
    private NutConfigurationFileState _state;

    public string StatusText => State switch
    {
        NutConfigurationFileState.Available => "Disponível",
        NutConfigurationFileState.Loaded => "Carregado",
        NutConfigurationFileState.AccessDenied => "Sem acesso",
        NutConfigurationFileState.Missing => "Ausente",
        NutConfigurationFileState.Error => "Erro",
        _ => "Não carregado"
    };

    public bool CanLoad => State is NutConfigurationFileState.Available or NutConfigurationFileState.Loaded;

    internal void ApplyInstallationInfo(NutConfigurationFileInfo? info)
    {
        FullPath = info?.FullPath;
        State = info switch
        {
            { Exists: true, IsReadable: true } => NutConfigurationFileState.Available,
            { Exists: true } => NutConfigurationFileState.AccessDenied,
            _ => NutConfigurationFileState.Missing
        };
    }

    internal void SetLoaded() => State = NutConfigurationFileState.Loaded;

    partial void OnStateChanged(NutConfigurationFileState value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanLoad));
    }
}

public sealed class NutConfigurationSectionViewModel
{
    public NutConfigurationSectionViewModel(string name)
    {
        Name = name;
        Entries = new ObservableCollection<NutConfigurationEntryViewModel>();
    }

    public string Name { get; }

    public ObservableCollection<NutConfigurationEntryViewModel> Entries { get; }

    public string RawContentSummary { get; private set; } = "Comentários e conteúdo avançado serão preservados.";

    internal void SetRawContentSummary(int rawNodeCount) => RawContentSummary = rawNodeCount == 0
        ? "Comentários e conteúdo avançado serão preservados."
        : $"{rawNodeCount} linhas de comentários/conteúdo avançado serão preservadas.";
}

public sealed partial class NutConfigurationEntryViewModel : ObservableObject
{
    private readonly string? _originalValue;
    private readonly string? _sectionName;
    private readonly bool _isAssignment;

    private NutConfigurationEntryViewModel(
        int nodeIndex,
        string name,
        string? sectionName,
        string originalValue,
        bool isAssignment,
        bool isSensitive)
    {
        NodeIndex = nodeIndex;
        Name = name;
        _sectionName = sectionName;
        _isAssignment = isAssignment;
        IsSensitive = isSensitive;
        _originalValue = isSensitive ? null : originalValue;
        DraftValue = isSensitive ? string.Empty : originalValue;
    }

    public int NodeIndex { get; }

    public int LineNumber => NodeIndex + 1;

    public string Name { get; }

    public string SectionName => _sectionName ?? "Geral";

    public string EntryTypeText => _isAssignment ? "Atribuição" : "Diretiva";

    public bool IsSensitive { get; }

    public bool IsNotSensitive => !IsSensitive;

    public string InputLabel => IsSensitive
        ? _isAssignment ? "Nova senha" : "Novos argumentos completos"
        : "Valor";

    public string SensitiveHint => IsSensitive
        ? _isAssignment
            ? "Valor sensível configurado. Deixe vazio para não alterar."
            : "Configuração sensível existente. A substituição abrange os argumentos completos da diretiva."
        : string.Empty;

    [ObservableProperty]
    private string _draftValue;

    public bool IsChanged => IsSensitive
        ? !string.IsNullOrEmpty(DraftValue)
        : !string.Equals(_originalValue, DraftValue, StringComparison.Ordinal);

    public static NutConfigurationEntryViewModel ForAssignment(int nodeIndex, NutConfigurationAssignmentNode assignment) =>
        new(nodeIndex, assignment.Name, assignment.SectionName, assignment.Value, isAssignment: true, assignment.IsSensitive);

    public static NutConfigurationEntryViewModel ForDirective(int nodeIndex, NutConfigurationDirectiveNode directive) =>
        new(nodeIndex, directive.Name, directive.SectionName, directive.Arguments, isAssignment: false, directive.IsSensitive);

    internal void ResetDraft() => DraftValue = IsSensitive ? string.Empty : _originalValue ?? string.Empty;

    internal bool TryApply(NutConfigurationNode node)
    {
        if (_isAssignment && node is NutConfigurationAssignmentNode assignment &&
            string.Equals(assignment.Name, Name, StringComparison.Ordinal) &&
            string.Equals(assignment.SectionName, _sectionName, StringComparison.Ordinal) &&
            assignment.IsSensitive == IsSensitive)
        {
            assignment.SetValue(DraftValue);
            return true;
        }

        if (!_isAssignment && node is NutConfigurationDirectiveNode directive &&
            string.Equals(directive.Name, Name, StringComparison.Ordinal) &&
            string.Equals(directive.SectionName, _sectionName, StringComparison.Ordinal) &&
            directive.IsSensitive == IsSensitive)
        {
            directive.SetArguments(DraftValue);
            return true;
        }

        return false;
    }

    partial void OnDraftValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsChanged));
    }
}

public sealed class NutConfigurationPreviewLineViewModel
{
    public NutConfigurationPreviewLineViewModel(int lineNumber, string originalText, string candidateText, bool isRedacted)
    {
        LineNumber = lineNumber;
        OriginalText = originalText;
        CandidateText = candidateText;
        IsRedacted = isRedacted;
    }

    public int LineNumber { get; }

    public string OriginalText { get; }

    public string CandidateText { get; }

    public bool IsRedacted { get; }
}
