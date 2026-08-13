using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NutManager.App.Localization;
using NutManager.Core.Configuration;
using NutManager.Core.Configuration.Semantic;
using NutManager.Core.Validation;

namespace NutManager.App.ViewModels;

public interface ISemanticConfigurationEditor : IDisposable
{
    event Action? Changed;
    NutConfigurationSemanticDraft Draft { get; }
    bool HasChanges { get; }
    bool CanReview { get; }
    NutConfigurationGeneratedPreview Prepare(INutConfigurationFilePipeline pipeline);
    void Reset();
}

public abstract partial class ServerGeneralConfigurationEditorViewModel : ObservableObject, ISemanticConfigurationEditor
{
    private readonly NutConfigurationFileSnapshot _snapshot;
    private readonly NutConfigurationFileSchema _schema;
    private readonly NutManagerLocalizer _strings;
    private readonly HashSet<string> _inputErrors = new(StringComparer.Ordinal);
    private NutConfigurationSemanticDraft _draft;

    protected ServerGeneralConfigurationEditorViewModel(
        NutConfigurationFileSnapshot snapshot,
        NutConfigurationFileSchema schema,
        NutManagerLocalizer strings,
        bool canEdit,
        NutConfigurationSemanticValidator? validator = null)
    {
        if (snapshot.FileKind != schema.FileKind) throw new ArgumentException("The snapshot and schema must match.", nameof(schema));
        _snapshot = snapshot;
        _schema = schema;
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        CanEdit = canEdit;
        _draft = CreateDraft(snapshot.Document, validator);
        BasicFields = [];
        AdvancedFields = [];
        TlsFields = [];
        CustomParameters = [];
        Rebuild();
    }

    public event Action? Changed;
    public NutManagerLocalizer Strings => _strings;
    public bool CanEdit { get; }
    public NutConfigurationSemanticDraft Draft => _draft;
    public bool HasChanges => _draft.IsModified;
    public bool HasInputErrors => _inputErrors.Count > 0;
    public bool CanReview => CanEdit && HasChanges && !HasInputErrors && !Validation.HasErrors;
    public NutConfigurationSemanticValidationResult Validation => _draft.Validation;
    public ObservableCollection<ServerConfigurationFieldViewModel> BasicFields { get; }
    public ObservableCollection<ServerConfigurationFieldViewModel> AdvancedFields { get; }
    public ObservableCollection<ServerConfigurationFieldViewModel> TlsFields { get; }
    public ObservableCollection<ServerCustomParameterViewModel> CustomParameters { get; }
    public bool HasAdvancedFields => AdvancedFields.Count > 0;
    public bool HasTlsFields => TlsFields.Count > 0;
    public bool HasCustomParameters => CustomParameters.Count > 0;
    public bool HasValidationIssues => Validation.Issues.Count > 0;
    public IReadOnlyList<UpsValidationIssueViewModel> ValidationIssues => Validation.Issues.Select(issue => new UpsValidationIssueViewModel(
        Localize(issue.ResourceKey, issue.Code), issue.Section, issue.Severity == ValidationSeverity.Error)).ToArray();

    [ObservableProperty] private bool _showAdvanced;
    [ObservableProperty] private string _newCustomName = string.Empty;
    [ObservableProperty] private string _newCustomValue = string.Empty;
    [ObservableProperty] private string? _operationMessage;

    public NutConfigurationGeneratedPreview Prepare(INutConfigurationFilePipeline pipeline) =>
        NutConfigurationGeneratedPreviewFactory.Prepare(pipeline, _snapshot, _draft);

    public void Reset()
    {
        _draft.Dispose();
        _draft = CreateDraft(_snapshot.Document, CreateValidator());
        _inputErrors.Clear();
        OperationMessage = null;
        Rebuild();
        Changed?.Invoke();
    }

    public NutConfigurationMutationResult SetField(NutConfigurationFieldDescriptor descriptor, string value)
    {
        if (!CanEdit) return Unsupported();
        var parsed = descriptor.Codec.Parse(value, descriptor.SemanticId);
        if (!parsed.IsValid || parsed.Value is null)
        {
            _inputErrors.Add(descriptor.SemanticId);
            OperationMessage = Localize(parsed.Issues.FirstOrDefault()?.ResourceKey ?? "Semantic.Validation.Text", "Invalid value");
            NotifyState();
            Changed?.Invoke();
            return new(NutConfigurationMutationStatus.ValidationFailed, "Value.Invalid");
        }
        _inputErrors.Remove(descriptor.SemanticId);
        return Complete(_draft.Set(descriptor.SemanticId, parsed.Value));
    }

    public NutConfigurationMutationResult SetBoolean(NutConfigurationFieldDescriptor descriptor, bool value) =>
        !CanEdit ? Unsupported() : Complete(_draft.Set(descriptor.SemanticId, value));

    public NutConfigurationMutationResult SetAutomatic(NutConfigurationFieldDescriptor descriptor) =>
        !CanEdit ? Unsupported() : Complete(_draft.SetAutomatic(descriptor.SemanticId));

    public NutConfigurationMutationResult ReplaceSensitive(string semanticId, ReadOnlySpan<char> value)
    {
        if (!CanEdit || value.IsEmpty) return new(NutConfigurationMutationStatus.ValidationFailed, "Sensitive.ValueRequired");
        using var sensitive = new NutSensitiveValue(value);
        return Complete(_draft.ReplaceSensitive(semanticId, sensitive));
    }

    [RelayCommand]
    private void AddCustomParameter()
    {
        if (!CanEdit) return;
        if (_schema.Fields.Any(field => field.EntryKind == CustomEntryKind &&
            string.Equals(field.Name, NewCustomName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            OperationMessage = _strings.Get("Config.Validation.CustomKnown");
            return;
        }
        Complete(_draft.AddCustomParameter(CustomEntryKind, NewCustomName.Trim(), NewCustomValue));
        if (OperationMessage is null) { NewCustomName = string.Empty; NewCustomValue = string.Empty; }
    }

    public void SaveCustom(ServerCustomParameterViewModel parameter)
    {
        if (!CanEdit) return;
        Complete(_draft.EditCustomParameter(parameter.EntryKind, parameter.Name, parameter.Occurrence, parameter.DraftValue));
    }

    public void RemoveCustom(ServerCustomParameterViewModel parameter)
    {
        if (!CanEdit) return;
        Complete(_draft.RemoveCustomParameter(parameter.EntryKind, parameter.Name, parameter.Occurrence));
    }

    protected virtual NutConfigurationEntryKind CustomEntryKind => _snapshot.FileKind == NutConfigurationFileKind.NutConf
        ? NutConfigurationEntryKind.Assignment : NutConfigurationEntryKind.Directive;

    protected virtual NutConfigurationSemanticValidator CreateValidator() => new();

    protected virtual void RebuildSpecialFields() { }

    protected NutConfigurationMutationResult Complete(NutConfigurationMutationResult result, bool rebuild = true)
    {
        OperationMessage = result.Succeeded ? null : Localize(result.Code switch
        {
            "Target.Ambiguous" => "Semantic.Validation.DuplicateSingleton",
            _ => "Config.Validation.MutationFailed"
        }, result.Code ?? "Mutation failed");
        if (!result.Succeeded) { NotifyState(); return result; }
        if (rebuild) Rebuild(); else NotifyState();
        Changed?.Invoke();
        return result;
    }

    protected void Rebuild()
    {
        BasicFields.Clear();
        AdvancedFields.Clear();
        TlsFields.Clear();
        CustomParameters.Clear();
        foreach (var field in _draft.Projection.Fields.Where(field => field.Descriptor.Scope != NutConfigurationFieldScope.Repeated))
        {
            var row = new ServerConfigurationFieldViewModel(field, this, _strings);
            if (field.Descriptor.Presentation?.GroupResourceKey == "Config.Tls.Title") TlsFields.Add(row);
            else if (row.IsAdvanced) AdvancedFields.Add(row);
            else BasicFields.Add(row);
        }
        foreach (var parameter in _draft.Projection.CustomParameters)
            CustomParameters.Add(new(parameter, this, _strings, CanEdit));
        RebuildSpecialFields();
        OnPropertyChanged(nameof(HasAdvancedFields));
        OnPropertyChanged(nameof(HasTlsFields));
        OnPropertyChanged(nameof(HasCustomParameters));
        NotifyState();
    }

    protected string Localize(string key, string fallback)
    {
        var value = _strings.Get(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    protected void NotifyState()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(CanReview));
        OnPropertyChanged(nameof(HasInputErrors));
        OnPropertyChanged(nameof(Validation));
        OnPropertyChanged(nameof(ValidationIssues));
        OnPropertyChanged(nameof(HasValidationIssues));
    }

    private NutConfigurationSemanticDraft CreateDraft(NutConfigurationDocument document, NutConfigurationSemanticValidator? validator) =>
        new(document, _schema, validator: validator ?? CreateValidator());

    private static NutConfigurationMutationResult Unsupported() =>
        new(NutConfigurationMutationStatus.UnsupportedOperation, "Profile.ReadOnly");

    public void Dispose() => _draft.Dispose();
}

public sealed class NutGeneralConfigurationEditorViewModel : ServerGeneralConfigurationEditorViewModel
{
    public NutGeneralConfigurationEditorViewModel(
        NutConfigurationFileSnapshot snapshot,
        NutManagerLocalizer strings,
        bool canEdit)
        : base(snapshot, NutServerConfigurationCatalog.CreateNutConfSchema(), strings, canEdit)
    {
    }
}

public sealed partial class UpsdConfigurationEditorViewModel : ServerGeneralConfigurationEditorViewModel
{
    public UpsdConfigurationEditorViewModel(
        NutConfigurationFileSnapshot snapshot,
        NutManagerLocalizer strings,
        bool canEdit)
        : base(snapshot, NutServerConfigurationCatalog.CreateUpsdConfSchema(), strings, canEdit,
            new NutConfigurationSemanticValidator(documentRules: [new NutServerConfigurationDocumentValidationRule()]))
    {
        Listeners = [];
        RebuildSpecialFields();
    }

    public ObservableCollection<UpsdListenRowViewModel> Listeners { get; private set; } = [];
    public bool HasListeners => Listeners.Count > 0;
    public bool HasNoListeners => !HasListeners;

    [ObservableProperty] private string _newListenAddress = string.Empty;
    [ObservableProperty] private string _newListenPort = string.Empty;

    [RelayCommand]
    private void AddListener()
    {
        if (!CanEdit) return;
        var parsed = ParseListener(NewListenAddress, NewListenPort);
        if (!parsed.IsValid || parsed.Value is null)
        {
            OperationMessage = Localize(parsed.Issues.First().ResourceKey, parsed.Issues.First().Code);
            return;
        }
        var result = Complete(Draft.AddRepeated("Upsd.Listen", parsed.Value));
        if (result.Succeeded) { NewListenAddress = string.Empty; NewListenPort = string.Empty; }
    }

    public void SaveListener(UpsdListenRowViewModel row)
    {
        if (!CanEdit) return;
        var parsed = ParseListener(row.Address, row.PortText);
        if (!parsed.IsValid || parsed.Value is null)
        {
            OperationMessage = Localize(parsed.Issues.First().ResourceKey, parsed.Issues.First().Code);
            return;
        }
        Complete(Draft.EditRepeated("Upsd.Listen", row.RowId, parsed.Value));
    }

    public void RemoveListener(UpsdListenRowViewModel row)
    {
        if (CanEdit) Complete(Draft.RemoveRepeated("Upsd.Listen", row.RowId));
    }

    public NutConfigurationMutationResult ReplaceCertificateIdentity(string identity, ReadOnlySpan<char> password)
    {
        if (string.IsNullOrWhiteSpace(identity) || password.IsEmpty)
            return new(NutConfigurationMutationStatus.ValidationFailed, "Sensitive.ValueRequired");
        var identityToken = NutDirectiveArgumentTokenizer.Quote(identity.Trim());
        var passwordToken = NutDirectiveArgumentTokenizer.QuoteSensitive(password);
        var composite = new char[identityToken.Length + 1 + passwordToken.Length];
        try
        {
            identityToken.AsSpan().CopyTo(composite);
            composite[identityToken.Length] = ' ';
            passwordToken.AsSpan().CopyTo(composite.AsSpan(identityToken.Length + 1));
            return ReplaceSensitive("Upsd.CertificateIdentity", composite);
        }
        finally { Array.Clear(passwordToken); Array.Clear(composite); }
    }

    public NutConfigurationMutationResult RemoveCertificateIdentity() =>
        CanEdit ? Complete(Draft.SetAutomatic("Upsd.CertificateIdentity")) :
            new(NutConfigurationMutationStatus.UnsupportedOperation, "Profile.ReadOnly");

    protected override NutConfigurationSemanticValidator CreateValidator() =>
        new(documentRules: [new NutServerConfigurationDocumentValidationRule()]);

    protected override void RebuildSpecialFields()
    {
        if (Listeners is null) return;
        Listeners.Clear();
        foreach (var field in Draft.Projection.Fields.Where(field => field.Descriptor.SemanticId == "Upsd.Listen" && field.Value is NutUpsdListenEndpoint))
            Listeners.Add(new(field.StableRowId!, field.Occurrence!.Value, (NutUpsdListenEndpoint)field.Value!, this, CanEdit));
        OnPropertyChanged(nameof(HasListeners));
        OnPropertyChanged(nameof(HasNoListeners));
    }

    private static FieldValidationResult<NutUpsdListenEndpoint> ParseListener(string address, string port)
    {
        var arguments = string.IsNullOrWhiteSpace(port) ? address.Trim() : $"{address.Trim()} {port.Trim()}";
        return NutUpsdListenEndpoint.Parse(arguments);
    }
}

public sealed partial class ServerConfigurationFieldViewModel : ObservableObject
{
    private readonly ServerGeneralConfigurationEditorViewModel _owner;
    private readonly string _committedValue;
    private bool _initializing = true;

    public ServerConfigurationFieldViewModel(
        NutConfigurationSemanticField field,
        ServerGeneralConfigurationEditorViewModel owner,
        NutManagerLocalizer strings)
    {
        Descriptor = field.Descriptor;
        _owner = owner;
        Strings = strings;
        Label = Localize(strings, Descriptor.LabelResourceKey, Descriptor.Name);
        Help = Localize(strings, Descriptor.HelpResourceKey, Descriptor.Name);
        TechnicalName = Descriptor.Name;
        Unit = Descriptor.Presentation?.UnitResourceKey is { } unit ? strings.Get(unit) : null;
        IsAdvanced = Descriptor.Presentation?.IsAdvanced == true;
        IsRisky = Descriptor.Presentation?.IsRisky == true;
        IsBoolean = Descriptor.FieldKind == NutConfigurationFieldKind.Boolean;
        IsSensitive = Descriptor.Sensitive;
        Choices = Descriptor.Choices.Select(choice => new UpsFieldChoiceViewModel(choice.TechnicalValue,
            Localize(strings, choice.ResourceKey, choice.TechnicalValue))).ToArray();
        HasChoices = Choices.Count > 0;
        _draftValue = field.Value switch
        {
            bool boolean => boolean ? "true" : "false",
            null => string.Empty,
            _ => Convert.ToString(field.Value, CultureInfo.InvariantCulture) ?? string.Empty
        };
        _committedValue = _draftValue;
        _booleanValue = field.Value is true;
        _isConfigured = field.State == NutConfigurationSemanticState.Explicit;
        SensitiveStateText = strings.Get(field.SensitiveState switch
        {
            NutSensitiveFieldState.Configured => "Semantic.Sensitive.Configured",
            NutSensitiveFieldState.ReplacementPending => "Semantic.Sensitive.ReplacementPending",
            NutSensitiveFieldState.RemovalPending => "Semantic.Sensitive.RemovalPending",
            _ => "Ups.Editor.SensitiveNotConfigured"
        });
        _initializing = false;
    }

    public NutConfigurationFieldDescriptor Descriptor { get; }
    public NutManagerLocalizer Strings { get; }
    public string Label { get; }
    public string Help { get; }
    public string TechnicalName { get; }
    public string? Unit { get; }
    public string SensitiveStateText { get; }
    public bool CanEdit => _owner.CanEdit;
    public bool IsAdvanced { get; }
    public bool IsRisky { get; }
    public bool IsBoolean { get; }
    public bool IsBooleanConfigured => IsBoolean && IsConfigured;
    public bool IsSensitive { get; }
    public bool HasChoices { get; }
    public bool IsText => !IsBoolean && !IsSensitive && !HasChoices;
    public bool SupportsAutomatic => Descriptor.AutomaticPolicy == NutConfigurationAutomaticPolicy.OmitDirective;
    public IReadOnlyList<UpsFieldChoiceViewModel> Choices { get; }

    [ObservableProperty] private string _draftValue;
    [ObservableProperty] private bool _booleanValue;
    [ObservableProperty] private bool _isConfigured;

    partial void OnDraftValueChanged(string value)
    {
        // A ComboBox coerces SelectedValue to null while it materializes, before its items can be
        // matched against the current value, and the two-way binding pushes that null back here.
        // Taking it for an edit rewrote the draft and forced a rebuild, whose fresh ComboBox did
        // the same thing on its next materialization: an endless rebuild loop that froze the
        // screen and left the file permanently reported as modified. A choice is cleared through
        // "use automatic", never by selecting nothing, so a null can only be that coercion.
        if (value is null)
        {
            RestoreDraftValue();
            return;
        }

        if (!_initializing && CanEdit) _owner.SetField(Descriptor, value);
    }

    /// <summary>
    /// Puts the field's own value back without registering an edit, so the control re-binds to it
    /// instead of settling on the empty selection it briefly proposed.
    /// </summary>
    private void RestoreDraftValue()
    {
        if (_initializing) return;
        _initializing = true;
        DraftValue = _committedValue;
        _initializing = false;
    }

    partial void OnBooleanValueChanged(bool value)
    {
        if (!_initializing && CanEdit) _owner.SetBoolean(Descriptor, value);
    }

    partial void OnIsConfiguredChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBooleanConfigured));
        if (_initializing || !CanEdit || !IsBoolean) return;
        if (value) _owner.SetBoolean(Descriptor, BooleanValue);
        else _owner.SetAutomatic(Descriptor);
    }

    public NutConfigurationMutationResult SetAutomatic() => _owner.SetAutomatic(Descriptor);

    private static string Localize(NutManagerLocalizer strings, string key, string fallback)
    {
        var value = strings.Get(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}

public sealed partial class UpsdListenRowViewModel : ObservableObject
{
    private readonly UpsdConfigurationEditorViewModel _owner;
    public UpsdListenRowViewModel(string rowId, int occurrence, NutUpsdListenEndpoint endpoint, UpsdConfigurationEditorViewModel owner, bool canEdit)
    {
        RowId = rowId;
        Occurrence = occurrence;
        _address = endpoint.Address;
        _portText = endpoint.Port?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _owner = owner;
        CanEdit = canEdit;
        Strings = owner.Strings;
    }
    public string RowId { get; }
    public int Occurrence { get; }
    public bool CanEdit { get; }
    public NutManagerLocalizer Strings { get; }
    [ObservableProperty] private string _address;
    [ObservableProperty] private string _portText;
    [RelayCommand] private void Save() => _owner.SaveListener(this);
    [RelayCommand] private void Remove() => _owner.RemoveListener(this);
}

public sealed partial class ServerCustomParameterViewModel : ObservableObject
{
    private readonly ServerGeneralConfigurationEditorViewModel _owner;
    public ServerCustomParameterViewModel(
        NutConfigurationCustomParameter parameter,
        ServerGeneralConfigurationEditorViewModel owner,
        NutManagerLocalizer strings,
        bool canEdit)
    {
        RowId = parameter.RowId;
        EntryKind = parameter.EntryKind;
        Name = parameter.Name;
        Occurrence = parameter.Occurrence;
        Sensitive = parameter.Sensitive;
        _draftValue = parameter.SafeValue ?? string.Empty;
        _owner = owner;
        CanEdit = canEdit;
        Warning = strings.Get("Semantic.Custom.LimitedValidation");
        SaveText = strings.Get("Config.Custom.Save");
        RemoveText = strings.Get("Ups.Editor.Remove");
    }
    public string RowId { get; }
    public NutConfigurationEntryKind EntryKind { get; }
    public string Name { get; }
    public int Occurrence { get; }
    public bool Sensitive { get; }
    public bool CanEdit { get; }
    public string Warning { get; }
    public string SaveText { get; }
    public string RemoveText { get; }
    [ObservableProperty] private string _draftValue;
    [RelayCommand] private void Save() => _owner.SaveCustom(this);
    [RelayCommand] private void Remove() => _owner.RemoveCustom(this);
}
