using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NutManager.App.Localization;
using NutManager.Core.Administration;
using NutManager.Core.Configuration;
using NutManager.Core.Configuration.Semantic;
using NutManager.Core.Validation;

namespace NutManager.App.ViewModels;

public sealed partial class UpsConfigurationEditorViewModel : ObservableObject, ISemanticConfigurationEditor
{
    private readonly NutConfigurationFileSnapshot _snapshot;
    private readonly NutConfigurationFileSchema _schema;
    private readonly NutDriverCatalog _catalog;
    private readonly NutManagerLocalizer _strings;
    private readonly IReadOnlyList<NutComPortInfo> _comPorts;
    private readonly bool _driverDetectionAvailable;
    private readonly HashSet<string> _inputErrors = new(StringComparer.Ordinal);
    private NutConfigurationSemanticDraft _draft;

    public UpsConfigurationEditorViewModel(
        NutConfigurationFileSnapshot snapshot,
        IEnumerable<string>? installedDriverNames,
        IReadOnlyList<NutComPortInfo>? comPorts,
        NutManagerLocalizer strings)
    {
        if (snapshot.FileKind != NutConfigurationFileKind.UpsConf) throw new ArgumentException("An ups.conf snapshot is required.", nameof(snapshot));
        _snapshot = snapshot;
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _driverDetectionAvailable = installedDriverNames is not null;
        _schema = NutUpsConfigurationCatalog.CreateFileSchema();
        var registry = new NutConfigurationSchemaRegistry([_schema], NutUpsConfigurationCatalog.CreateDriverSchemas());
        var configuredDriverNames = snapshot.Document.Nodes.OfType<NutConfigurationAssignmentNode>()
            .Where(node => string.Equals(node.Name, "driver", StringComparison.OrdinalIgnoreCase)).Select(node => node.Value);
        _catalog = NutDriverCatalog.Create(registry, installedDriverNames, configuredDriverNames);
        _comPorts = comPorts ?? [];
        Sections = new ObservableCollection<string>();
        Fields = new ObservableCollection<UpsConfigurationFieldViewModel>();
        BasicFields = new ObservableCollection<UpsConfigurationFieldViewModel>();
        AdvancedFields = new ObservableCollection<UpsConfigurationFieldViewModel>();
        UnsupportedFields = new ObservableCollection<UpsConfigurationFieldViewModel>();
        CustomParameters = new ObservableCollection<UpsCustomParameterViewModel>();
        DriverOptions = _catalog.Entries.Select(entry => new UpsDriverOptionViewModel(
            entry.DriverId,
            Localize(entry.DisplayNameResourceKey, entry.DriverId),
            Localize(entry.DescriptionResourceKey, entry.DriverId),
            entry.IsInstalled,
            entry.HasStructuredOptions,
            entry.Transports.Select(TransportText).ToArray())).ToArray();
        ComPortOptions = _comPorts.Where(port => port.IsPresent).Select(port => new UpsComPortOptionViewModel(
            port.PortName, port.FriendlyName ?? port.PortName, port.Manufacturer)).ToArray();
        _draft = CreateDraft(snapshot.Document);
        Rebuild();
    }

    public event Action? Changed;
    public ObservableCollection<string> Sections { get; }
    public ObservableCollection<UpsConfigurationFieldViewModel> Fields { get; }
    public ObservableCollection<UpsConfigurationFieldViewModel> BasicFields { get; }
    public ObservableCollection<UpsConfigurationFieldViewModel> AdvancedFields { get; }
    public ObservableCollection<UpsConfigurationFieldViewModel> GlobalFields { get; } = [];
    public ObservableCollection<UpsConfigurationFieldViewModel> UnsupportedFields { get; }
    public ObservableCollection<UpsCustomParameterViewModel> CustomParameters { get; }
    public IReadOnlyList<UpsDriverOptionViewModel> DriverOptions { get; }
    public IReadOnlyList<UpsComPortOptionViewModel> ComPortOptions { get; }
    public NutManagerLocalizer Strings => _strings;
    public NutConfigurationSemanticDraft Draft => _draft;
    public NutConfigurationSemanticValidationResult Validation => _draft.Validation;
    public bool HasChanges => _draft.IsModified;
    public bool CanReview => HasChanges && !Validation.HasErrors && !HasInputErrors;
    public bool HasInputErrors => _inputErrors.Count > 0;
    public bool HasSections => Sections.Count > 0;
    public bool HasUnsupportedFields => UnsupportedFields.Count > 0;
    public bool HasGlobalFields => GlobalFields.Count > 0;
    public bool HasCustomParameters => CustomParameters.Count > 0;
    public bool HasValidationIssues => Validation.Issues.Count > 0;
    public IReadOnlyList<UpsValidationIssueViewModel> ValidationIssues => Validation.Issues.Select(issue => new UpsValidationIssueViewModel(
        _strings.Get(issue.ResourceKey), issue.Section, issue.Severity == ValidationSeverity.Error)).ToArray();

    [ObservableProperty] private string? _selectedSection;
    [ObservableProperty] private string _newSectionName = string.Empty;
    [ObservableProperty] private string _renameSectionName = string.Empty;
    [ObservableProperty] private string _newCustomName = string.Empty;
    [ObservableProperty] private string _newCustomValue = string.Empty;
    [ObservableProperty] private string? _operationMessage;
    [ObservableProperty] private long _runtimeHighSeconds = 240;
    [ObservableProperty] private decimal _runtimeHighLoad = 100;
    [ObservableProperty] private long _runtimeLowSeconds = 720;
    [ObservableProperty] private decimal _runtimeLowLoad = 50;
    [ObservableProperty] private bool _showAdvanced;

    public string SelectedDriver => CurrentAssignment("driver") ?? string.Empty;
    public string SelectedPort => CurrentAssignment("port") ?? string.Empty;
    public string SelectedDriverDescription => _catalog.Find(SelectedDriver) is { } driver
        ? Localize(driver.DescriptionResourceKey, driver.DriverId)
        : _strings.Get("Ups.Driver.Unknown.Description");
    public string SelectedDriverAvailability => !_driverDetectionAvailable
        ? _strings.Get("Ups.Editor.DriverDetectionUnavailable")
        : _catalog.Find(SelectedDriver) is { IsInstalled: true }
            ? _strings.Get("Ups.Editor.DriverDetected")
            : _strings.Get("Ups.Editor.DriverNotDetected");
    public bool IsRuntimeCalibrationAvailable => string.Equals(SelectedDriver, "nutdrv_qx", StringComparison.OrdinalIgnoreCase);

    public NutConfigurationGeneratedPreview Prepare(INutConfigurationFilePipeline pipeline) =>
        NutConfigurationGeneratedPreviewFactory.Prepare(pipeline, _snapshot, _draft);

    public void Reset()
    {
        _draft.Dispose();
        _draft = CreateDraft(_snapshot.Document);
        _inputErrors.Clear();
        OperationMessage = null;
        Rebuild();
        RaiseChanged();
    }

    public NutConfigurationMutationResult SetField(NutConfigurationFieldDescriptor descriptor, string value, string? section)
    {
        var inputKey = InputKey(descriptor, section);
        var parsed = descriptor.Codec.Parse(value, descriptor.SemanticId);
        if (!parsed.IsValid || parsed.Value is null)
        {
            _inputErrors.Add(inputKey);
            OperationMessage = _strings.Get(parsed.Issues.FirstOrDefault()?.ResourceKey ?? "Semantic.Validation.Text");
            NotifyState();
            Changed?.Invoke();
            return new(NutConfigurationMutationStatus.ValidationFailed, "Value.Invalid");
        }
        _inputErrors.Remove(inputKey);
        var result = _draft.Set(descriptor.SemanticId, parsed.Value, section);
        CompleteMutation(result, rebuild: descriptor.SemanticId == "Ups.Driver");
        return result;
    }

    public NutConfigurationMutationResult SetFlag(NutConfigurationFieldDescriptor descriptor, bool enabled, string? section)
    {
        if (section is null) return new(NutConfigurationMutationStatus.ValidationFailed, "Section.Required");
        var result = enabled ? _draft.Set(descriptor.SemanticId, string.Empty, section) : _draft.SetAutomatic(descriptor.SemanticId, section: section);
        CompleteMutation(result);
        return result;
    }

    public NutConfigurationMutationResult SetAutomatic(NutConfigurationFieldDescriptor descriptor, string section)
    {
        var result = _draft.SetAutomatic(descriptor.SemanticId, section: section);
        CompleteMutation(result);
        return result;
    }

    public NutConfigurationMutationResult ReplaceSensitive(string semanticId, ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            OperationMessage = _strings.Get("Ups.Validation.Secret.Required");
            return new(NutConfigurationMutationStatus.ValidationFailed, "Sensitive.ValueRequired");
        }
        using var sensitive = new NutSensitiveValue(value);
        var result = _draft.ReplaceSensitive(semanticId, sensitive, SelectedSection);
        CompleteMutation(result);
        return result;
    }

    public NutConfigurationMutationResult RemoveField(NutConfigurationFieldDescriptor descriptor, string section)
    {
        var result = _draft.Remove(descriptor.SemanticId, section);
        CompleteMutation(result);
        return result;
    }

    [RelayCommand]
    private void AddSection()
    {
        if (!NutUpsConfigurationDocumentValidationRule.IsValidSectionName(NewSectionName))
        {
            OperationMessage = _strings.Get("Ups.Validation.Section.Invalid");
            return;
        }
        var result = _draft.AddSection(NewSectionName.Trim());
        var selected = NewSectionName.Trim();
        if (CompleteMutation(result)) { NewSectionName = string.Empty; SelectedSection = selected; }
    }

    [RelayCommand]
    private void RenameSection()
    {
        if (SelectedSection is null || !NutUpsConfigurationDocumentValidationRule.IsValidSectionName(RenameSectionName))
        {
            OperationMessage = _strings.Get("Ups.Validation.Section.Invalid");
            return;
        }
        var old = SelectedSection;
        var renamed = RenameSectionName.Trim();
        var result = _draft.RenameSection(old, renamed);
        if (CompleteMutation(result)) { RenameSectionName = string.Empty; SelectedSection = renamed; }
    }

    [RelayCommand]
    private void RemoveSection()
    {
        if (SelectedSection is null) return;
        var result = _draft.RemoveSection(SelectedSection);
        if (CompleteMutation(result)) SelectedSection = Sections.FirstOrDefault();
    }

    [RelayCommand]
    private void AddCustomParameter()
    {
        if (SelectedSection is null) return;
        var result = _draft.AddCustomParameter(NutConfigurationEntryKind.Assignment, NewCustomName.Trim(), NewCustomValue, SelectedSection);
        if (CompleteMutation(result)) { NewCustomName = string.Empty; NewCustomValue = string.Empty; }
    }

    public void RemoveCustom(UpsCustomParameterViewModel parameter)
    {
        var result = _draft.RemoveCustomParameter(parameter.EntryKind, parameter.Name, parameter.Occurrence, parameter.Section);
        CompleteMutation(result);
    }

    [RelayCommand]
    private void RemoveCustomParameter(UpsCustomParameterViewModel? parameter)
    {
        if (parameter is not null) RemoveCustom(parameter);
    }

    [RelayCommand]
    private void UseRuntimeCalibration()
    {
        if (SelectedSection is null || !IsRuntimeCalibrationAvailable)
        {
            OperationMessage = _strings.Get("Ups.Validation.Runtimecal.Unsupported");
            return;
        }
        var calibration = NutRuntimeCalibration.Validate(
            TimeSpan.FromSeconds(RuntimeHighSeconds), RuntimeHighLoad,
            TimeSpan.FromSeconds(RuntimeLowSeconds), RuntimeLowLoad);
        if (!calibration.IsValid || calibration.Value is null)
        {
            OperationMessage = _strings.Get(calibration.Issues[0].ResourceKey);
            return;
        }
        var result = _draft.Set("Ups.RuntimeCalibration", calibration.Value, SelectedSection);
        if (CompleteMutation(result)) OperationMessage = _strings.Get("Ups.Runtimecal.DraftUpdated");
    }

    partial void OnSelectedSectionChanged(string? value)
    {
        RenameSectionName = value ?? string.Empty;
        var driver = CurrentAssignment("driver", value);
        _draft.UpdateContext(new(driver));
        RebuildFields();
        NotifyState();
    }

    private NutConfigurationSemanticDraft CreateDraft(NutConfigurationDocument document)
    {
        var catalog = _catalog ?? NutDriverCatalog.Create(new NutConfigurationSchemaRegistry([_schema], NutUpsConfigurationCatalog.CreateDriverSchemas()));
        var validator = new NutConfigurationSemanticValidator(documentRules:
            [new NutUpsConfigurationDocumentValidationRule(catalog, _driverDetectionAvailable)]);
        return new(document, _schema, new(CurrentAssignment(document, "driver", document.Sections.FirstOrDefault()?.Name)), validator);
    }

    private bool CompleteMutation(NutConfigurationMutationResult result, bool rebuild = true)
    {
        OperationMessage = result.Succeeded ? null : _strings.Get(result.Code switch
        {
            "Section.Duplicate" => "Ups.Validation.Section.Duplicate",
            "Target.Ambiguous" => "Semantic.Validation.DuplicateSingleton",
            _ => "Ups.Validation.MutationFailed"
        });
        if (!result.Succeeded) return false;
        if (rebuild) Rebuild(); else NotifyState();
        RaiseChanged();
        return true;
    }

    private void Rebuild()
    {
        _inputErrors.Clear();
        var selected = SelectedSection;
        Sections.Clear();
        foreach (var section in _draft.Materialize().Sections.Select(section => section.Name)) Sections.Add(section);
        if (selected is null || !Sections.Contains(selected)) SelectedSection = Sections.FirstOrDefault();
        else RebuildFields();
        NotifyState();
    }

    private void RebuildFields()
    {
        Fields.Clear();
        BasicFields.Clear();
        AdvancedFields.Clear();
        GlobalFields.Clear();
        UnsupportedFields.Clear();
        CustomParameters.Clear();
        foreach (var field in _draft.Projection.Fields.Where(field => field.Section is null && field.Descriptor.Scope == NutConfigurationFieldScope.Global))
            GlobalFields.Add(new(field, this, _strings));
        if (SelectedSection is null) return;
        var driver = CurrentAssignment("driver", SelectedSection);
        _draft.UpdateContext(new(driver));
        foreach (var field in _draft.Projection.Fields.Where(field => string.Equals(field.Section, SelectedSection, StringComparison.OrdinalIgnoreCase)))
        {
            var row = new UpsConfigurationFieldViewModel(field, this, _strings,
                field.Descriptor.SemanticId == "Ups.Driver"
                    ? DriverOptions.Select(item => new UpsFieldChoiceViewModel(item.DriverId, item.DisplayName)).ToArray()
                    : field.Descriptor.SemanticId == "Ups.Port"
                        ? PortChoices(driver)
                    : null);
            if (field.State == NutConfigurationSemanticState.Unsupported) UnsupportedFields.Add(row);
            else
            {
                Fields.Add(row);
                if (row.IsAdvanced) AdvancedFields.Add(row); else BasicFields.Add(row);
            }
        }
        foreach (var parameter in _draft.Projection.CustomParameters.Where(parameter => string.Equals(parameter.Section, SelectedSection, StringComparison.OrdinalIgnoreCase)))
            CustomParameters.Add(new(parameter.RowId, parameter.EntryKind, parameter.Name, parameter.SafeValue,
                parameter.Section, parameter.Occurrence, parameter.Sensitive, _strings.Get("Ups.Editor.Remove")));
        OnPropertyChanged(nameof(SelectedDriver));
        OnPropertyChanged(nameof(SelectedPort));
        OnPropertyChanged(nameof(SelectedDriverDescription));
        OnPropertyChanged(nameof(SelectedDriverAvailability));
        OnPropertyChanged(nameof(IsRuntimeCalibrationAvailable));
        OnPropertyChanged(nameof(HasUnsupportedFields));
        OnPropertyChanged(nameof(HasCustomParameters));
        OnPropertyChanged(nameof(HasGlobalFields));
        BasicFieldGroups = UpsFieldGroupViewModel.From(BasicFields);
        AdvancedFieldGroups = UpsFieldGroupViewModel.From(AdvancedFields);
    }

    // Basic/Advanced is a presentation filter over the same draft; it changes no configuration.
    [RelayCommand]
    private void ShowBasicTab() => ShowAdvanced = false;

    [RelayCommand]
    private void ShowAdvancedTab() => ShowAdvanced = true;

    public bool IsBasicSelected => !ShowAdvanced;

    /// <summary>Validation state of the current draft, shown as a status chip in the header.</summary>
    public bool IsConfigurationValid => !Validation.HasErrors;

    public string ConfigurationStateText =>
        _strings.Get(IsConfigurationValid ? "Ups.Editor.StateValid" : "Ups.Editor.StateInvalid");

    /// <summary>Basic fields projected into the documented presentation sections of the form.</summary>
    [ObservableProperty]
    private IReadOnlyList<UpsFieldGroupViewModel> _basicFieldGroups = [];

    [ObservableProperty]
    private IReadOnlyList<UpsFieldGroupViewModel> _advancedFieldGroups = [];

    public bool HasAdvancedFieldGroups => AdvancedFieldGroups.Count > 0;

    partial void OnAdvancedFieldGroupsChanged(IReadOnlyList<UpsFieldGroupViewModel> value) =>
        OnPropertyChanged(nameof(HasAdvancedFieldGroups));

    private IReadOnlyList<UpsFieldChoiceViewModel>? PortChoices(string? driver)
    {
        if (string.Equals(driver, "usbhid-ups", StringComparison.OrdinalIgnoreCase))
            return [new("auto", "auto")];
        if (_catalog.Find(driver ?? string.Empty)?.Transports.Contains(NutDriverTransport.Serial) == true && ComPortOptions.Count > 0)
            return ComPortOptions.Select(item => new UpsFieldChoiceViewModel(item.PortName, item.FriendlyName)).ToArray();
        return null;
    }

    private string? CurrentAssignment(string name, string? section = null) => CurrentAssignment(_draft.Materialize(), name, section ?? SelectedSection);
    private static string? CurrentAssignment(NutConfigurationDocument document, string name, string? section) => document.Nodes
        .OfType<NutConfigurationAssignmentNode>()
        .FirstOrDefault(node => string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase) && string.Equals(node.SectionName, section, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string InputKey(NutConfigurationFieldDescriptor descriptor, string? section) =>
        $"{descriptor.SemanticId}\u001f{section ?? string.Empty}";

    private void RaiseChanged() { NotifyState(); Changed?.Invoke(); }
    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(CanReview));
        OnPropertyChanged(nameof(HasSections));
        OnPropertyChanged(nameof(Validation));
        OnPropertyChanged(nameof(ValidationIssues));
        OnPropertyChanged(nameof(HasValidationIssues));
        OnPropertyChanged(nameof(HasInputErrors));
        OnPropertyChanged(nameof(IsConfigurationValid));
        OnPropertyChanged(nameof(ConfigurationStateText));
    }

    partial void OnShowAdvancedChanged(bool value) => OnPropertyChanged(nameof(IsBasicSelected));

    private string Localize(string key, string fallback)
    {
        var value = _strings.Get(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private string TransportText(NutDriverTransport transport) => _strings.Get($"Ups.Transport.{transport}");

    public void Dispose() => _draft.Dispose();
}

public sealed partial class UpsConfigurationFieldViewModel : ObservableObject
{
    private readonly UpsConfigurationEditorViewModel _owner;

    public UpsConfigurationFieldViewModel(
        NutConfigurationSemanticField field,
        UpsConfigurationEditorViewModel owner,
        NutManagerLocalizer strings,
        IReadOnlyList<UpsFieldChoiceViewModel>? overrideChoices = null)
    {
        Descriptor = field.Descriptor;
        Section = field.Section;
        _owner = owner;
        Strings = strings;
        Label = Localize(strings, Descriptor.LabelResourceKey, Descriptor.Name);
        Help = Localize(strings, Descriptor.HelpResourceKey, strings.Get("Ups.Editor.DocumentedOptionHelp"));
        GroupKey = Descriptor.Presentation?.GroupResourceKey ?? "Ups.Group.Advanced";
        Group = strings.Get(GroupKey);
        Unit = Descriptor.Presentation?.UnitResourceKey is { } unit ? strings.Get(unit) : null;
        IsAdvanced = Descriptor.Presentation?.IsAdvanced == true;
        IsRisky = Descriptor.Presentation?.IsRisky == true;
        IsFlag = Descriptor.EntryKind == NutConfigurationEntryKind.Directive && Descriptor.FieldKind == NutConfigurationFieldKind.Boolean;
        IsSensitive = Descriptor.Sensitive;
        Choices = overrideChoices ?? Descriptor.Choices.Select(choice => new UpsFieldChoiceViewModel(
            choice.TechnicalValue,
            Localize(strings, choice.ResourceKey, choice.TechnicalValue))).ToArray();
        HasChoices = Choices.Count > 0;
        AllowsTechnicalInput = Descriptor.SemanticId is "Ups.Driver" or "Ups.Port";
        _draftValue = field.Value switch
        {
            NutRuntimeCalibration calibration => calibration.ToNutValue(),
            null => string.Empty,
            _ => Convert.ToString(field.Value, CultureInfo.InvariantCulture) ?? string.Empty
        };
        _isEnabled = field.State is NutConfigurationSemanticState.Explicit or NutConfigurationSemanticState.ExplicitAutoToken;
        StateText = strings.Get($"Semantic.State.{field.State}");
        AutomaticText = strings.Get("Ups.Editor.SetAutomatic");
        SensitiveStateText = strings.Get(field.SensitiveState switch
        {
            NutSensitiveFieldState.Configured => "Semantic.Sensitive.Configured",
            NutSensitiveFieldState.ReplacementPending => "Semantic.Sensitive.ReplacementPending",
            NutSensitiveFieldState.RemovalPending => "Semantic.Sensitive.RemovalPending",
            _ => "Ups.Editor.SensitiveNotConfigured"
        });
    }

    public NutConfigurationFieldDescriptor Descriptor { get; }
    public NutManagerLocalizer Strings { get; }
    public string? Section { get; }
    public string Label { get; }
    public string Help { get; }
    public string Group { get; }

    /// <summary>Untranslated group resource key, used to pick the section glyph.</summary>
    public string GroupKey { get; }
    public string? Unit { get; }
    public string StateText { get; }
    public string AutomaticText { get; }
    public string SensitiveStateText { get; }
    public bool IsAdvanced { get; }
    public bool IsBasic => !IsAdvanced;
    public bool IsRisky { get; }
    public bool IsFlag { get; }
    public bool IsValue => !IsFlag && !IsSensitive && (!HasChoices || AllowsTechnicalInput);
    public bool IsSensitive { get; }
    public bool HasChoices { get; }
    public bool AllowsTechnicalInput { get; }
    public bool IsSensitiveConfigured => IsSensitive && IsEnabled;
    public bool SupportsAutomatic => Descriptor.AutomaticPolicy is NutConfigurationAutomaticPolicy.OmitDirective or NutConfigurationAutomaticPolicy.ExplicitAutoToken;
    public IReadOnlyList<UpsFieldChoiceViewModel> Choices { get; }

    [ObservableProperty] private string _draftValue;
    [ObservableProperty] private bool _isEnabled;

    partial void OnDraftValueChanged(string value)
    {
        if (!IsFlag && !IsSensitive) _owner.SetField(Descriptor, value, Section);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (IsFlag) _owner.SetFlag(Descriptor, value, Section);
    }

    public NutConfigurationMutationResult ReplaceSensitive(ReadOnlySpan<char> value) =>
        _owner.ReplaceSensitive(Descriptor.SemanticId, value);

    public NutConfigurationMutationResult RemoveSensitive() =>
        Section is null
            ? new(NutConfigurationMutationStatus.ValidationFailed, "Section.Required")
            : _owner.RemoveField(Descriptor, Section);

    public NutConfigurationMutationResult SetAutomatic() =>
        Section is null
            ? new(NutConfigurationMutationStatus.ValidationFailed, "Section.Required")
            : _owner.SetAutomatic(Descriptor, Section);

    private static string Localize(NutManagerLocalizer strings, string key, string fallback)
    {
        var value = strings.Get(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}

public sealed record UpsDriverOptionViewModel(
    string DriverId,
    string DisplayName,
    string Description,
    bool IsInstalled,
    bool HasStructuredOptions,
    IReadOnlyList<string> Transports);

public sealed record UpsFieldChoiceViewModel(string TechnicalValue, string DisplayName);
public sealed record UpsComPortOptionViewModel(string PortName, string FriendlyName, string? Manufacturer);
public sealed record UpsValidationIssueViewModel(string Message, string? Section, bool IsError);
public sealed record UpsCustomParameterViewModel(
    string RowId,
    NutConfigurationEntryKind EntryKind,
    string Name,
    string? SafeValue,
    string? Section,
    int Occurrence,
    bool Sensitive,
    string RemoveText);
