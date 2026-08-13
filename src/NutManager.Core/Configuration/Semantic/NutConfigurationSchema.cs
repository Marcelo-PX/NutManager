using System.Globalization;
using NutManager.Core.Validation;

namespace NutManager.Core.Configuration.Semantic;

public enum NutConfigurationFieldKind { Text, Integer, Decimal, Boolean, Choice, Path, Host, Port, SecretChange, RepeatedRow, CustomParameter }
public enum NutConfigurationFieldScope { Global, Section, Repeated }
public enum NutConfigurationEntryKind { Assignment, Directive }
public enum NutConfigurationAutomaticPolicy { OmitDirective, ExplicitAutoToken, DetectedAndPersisted, NotSupported }
public enum NutConfigurationSemanticState { Explicit, AutomaticByOmission, ExplicitAutoToken, MissingRequired, Unsupported, CustomUnknown }
public enum NutConfigurationApplicability { Applicable, Unsupported }
public enum NutConfigurationActivation { None, Reload, ServiceRestart, SeparateExplicitAction }
public enum NutSensitiveFieldState { NotConfigured, Configured, ReplacementPending, RemovalPending }

public sealed record NutConfigurationFieldPresentation(
    string GroupResourceKey,
    string? UnitResourceKey = null,
    bool IsAdvanced = false,
    bool IsRisky = false,
    decimal? Minimum = null,
    decimal? Maximum = null,
    string? DocumentationUri = null);

public sealed record NutConfigurationSemanticContext(
    string? SelectedDriver = null,
    IReadOnlyDictionary<string, string>? Values = null)
{
    public string? GetValue(string key) => Values is not null && Values.TryGetValue(key, out var value) ? value : null;
}

public interface INutConfigurationValueCodec
{
    FieldValidationResult<object> Parse(string value, string semanticId);
    FieldValidationResult<string> Serialize(object value, string semanticId);
}

public sealed class NutConfigurationValueCodec : INutConfigurationValueCodec
{
    private readonly Func<string, string, FieldValidationResult<object>> _parse;
    private readonly Func<object, string, FieldValidationResult<string>> _serialize;

    private NutConfigurationValueCodec(
        Func<string, string, FieldValidationResult<object>> parse,
        Func<object, string, FieldValidationResult<string>> serialize)
    {
        _parse = parse;
        _serialize = serialize;
    }

    public FieldValidationResult<object> Parse(string value, string semanticId) => _parse(value, semanticId);
    public FieldValidationResult<string> Serialize(object value, string semanticId) => _serialize(value, semanticId);

    public static INutConfigurationValueCodec Text { get; } = new NutConfigurationValueCodec(
        (value, _) => new(value, []),
        (value, field) => value is string text
            ? new(text, [])
            : Invalid<string>(field, "Semantic.Value.Text", "Semantic.Validation.Text"));

    public static INutConfigurationValueCodec Integer { get; } = new NutConfigurationValueCodec(
        (value, field) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? new(parsed, [])
            : Invalid<object>(field, "Semantic.Value.Integer", "Semantic.Validation.Integer"),
        (value, field) => value is int integer
            ? new(integer.ToString(CultureInfo.InvariantCulture), [])
            : Invalid<string>(field, "Semantic.Value.Integer", "Semantic.Validation.Integer"));

    public static INutConfigurationValueCodec Decimal { get; } = new NutConfigurationValueCodec(
        (value, field) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? new(parsed, [])
            : Invalid<object>(field, "Semantic.Value.Decimal", "Semantic.Validation.Decimal"),
        (value, field) => value is decimal number
            ? new(number.ToString(CultureInfo.InvariantCulture), [])
            : Invalid<string>(field, "Semantic.Value.Decimal", "Semantic.Validation.Decimal"));

    public static INutConfigurationValueCodec Choice(params string[] values)
    {
        var allowed = values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new NutConfigurationValueCodec(
            (value, field) => allowed.Contains(value)
                ? new(value, [])
                : Invalid<object>(field, "Semantic.Value.Choice", "Semantic.Validation.Choice"),
            (value, field) => value is string text && allowed.Contains(text)
                ? new(values.First(item => string.Equals(item, text, StringComparison.OrdinalIgnoreCase)), [])
                : Invalid<string>(field, "Semantic.Value.Choice", "Semantic.Validation.Choice"));
    }

    public static INutConfigurationValueCodec Boolean(string trueToken = "true", string falseToken = "false") => new NutConfigurationValueCodec(
        (value, field) => ParseBoolean(value) is { } parsed
            ? new(parsed, [])
            : Invalid<object>(field, "Semantic.Value.Boolean", "Semantic.Validation.Boolean"),
        (value, field) => value is bool boolean
            ? new(boolean ? trueToken : falseToken, [])
            : value is string text && ParseBoolean(text) is { } parsed
                ? new(parsed ? trueToken : falseToken, [])
                : Invalid<string>(field, "Semantic.Value.Boolean", "Semantic.Validation.Boolean"));

    public static INutConfigurationValueCodec AliasChoice(IReadOnlyDictionary<string, string> aliases) => new NutConfigurationValueCodec(
        (value, field) => aliases.TryGetValue(value, out var canonical)
            ? new(canonical, [])
            : Invalid<object>(field, "Semantic.Value.Choice", "Semantic.Validation.Choice"),
        (value, field) => value is string text && aliases.TryGetValue(text, out var canonical)
            ? new(canonical, [])
            : Invalid<string>(field, "Semantic.Value.Choice", "Semantic.Validation.Choice"));

    public static INutConfigurationValueCodec Create(
        Func<string, string, FieldValidationResult<object>> parse,
        Func<object, string, FieldValidationResult<string>> serialize) => new NutConfigurationValueCodec(parse, serialize);

    public static INutConfigurationValueCodec IntegerRange(int minimum, int maximum) => new NutConfigurationValueCodec(
        (value, field) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= minimum && parsed <= maximum
            ? new(parsed, [])
            : Invalid<object>(field, "Semantic.Value.IntegerRange", "Semantic.Validation.IntegerRange"),
        (value, field) => value is int integer && integer >= minimum && integer <= maximum
            ? new(integer.ToString(CultureInfo.InvariantCulture), [])
            : Invalid<string>(field, "Semantic.Value.IntegerRange", "Semantic.Validation.IntegerRange"));

    public static INutConfigurationValueCodec DecimalRange(decimal minimumExclusive, decimal maximumInclusive) => new NutConfigurationValueCodec(
        (value, field) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed > minimumExclusive && parsed <= maximumInclusive
            ? new(parsed, [])
            : Invalid<object>(field, "Semantic.Value.DecimalRange", "Semantic.Validation.DecimalRange"),
        (value, field) => value is decimal number && number > minimumExclusive && number <= maximumInclusive
            ? new(number.ToString(CultureInfo.InvariantCulture), [])
            : Invalid<string>(field, "Semantic.Value.DecimalRange", "Semantic.Validation.DecimalRange"));

    private static FieldValidationResult<T> Invalid<T>(string field, string code, string resourceKey) =>
        new(default, [new FieldValidationIssue(field, code, ValidationSeverity.Error, resourceKey)]);

    private static bool? ParseBoolean(string value) => value.Trim().ToLowerInvariant() switch
    {
        "true" or "yes" or "on" or "1" => true,
        "false" or "no" or "off" or "0" => false,
        _ => null
    };
}

public sealed record NutConfigurationChoice(string TechnicalValue, string ResourceKey);

public sealed class NutConfigurationFieldDescriptor
{
    public NutConfigurationFieldDescriptor(
        NutConfigurationFileKind fileKind,
        string semanticId,
        NutConfigurationEntryKind entryKind,
        string name,
        NutConfigurationFieldScope scope,
        string labelResourceKey,
        string helpResourceKey,
        NutConfigurationFieldKind fieldKind = NutConfigurationFieldKind.Text,
        bool required = false,
        bool sensitive = false,
        NutConfigurationAutomaticPolicy automaticPolicy = NutConfigurationAutomaticPolicy.NotSupported,
        string? explicitAutoToken = null,
        int insertionOrder = 0,
        NutConfigurationActivation activation = NutConfigurationActivation.None,
        INutConfigurationValueCodec? codec = null,
        Func<NutConfigurationSemanticContext, NutConfigurationApplicability>? applicability = null,
        IReadOnlyList<NutConfigurationChoice>? choices = null,
        NutConfigurationFieldPresentation? presentation = null,
        int? secretTokenIndex = null,
        bool valueIsTokenList = false)
    {
        if (string.IsNullOrWhiteSpace(semanticId)) throw new ArgumentException("A semantic ID is required.", nameof(semanticId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A configuration entry name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(labelResourceKey)) throw new ArgumentException("A label resource key is required.", nameof(labelResourceKey));
        if (string.IsNullOrWhiteSpace(helpResourceKey)) throw new ArgumentException("A help resource key is required.", nameof(helpResourceKey));
        if (automaticPolicy == NutConfigurationAutomaticPolicy.ExplicitAutoToken && string.IsNullOrWhiteSpace(explicitAutoToken))
            throw new ArgumentException("Explicit-auto fields require a technical auto token.", nameof(explicitAutoToken));
        if (sensitive && fieldKind != NutConfigurationFieldKind.SecretChange)
            throw new ArgumentException("Sensitive fields must use the change-only field kind.", nameof(fieldKind));
        // A composite row carries its secret as one token among several, so the row as a whole is
        // editable while that single token stays change-only. Marking the descriptor sensitive as
        // well would make the entire row opaque and there would be nothing left to edit.
        if (secretTokenIndex is not null && sensitive)
            throw new ArgumentException("A composite row with an embedded secret is not wholly sensitive.", nameof(secretTokenIndex));
        if (secretTokenIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(secretTokenIndex), "A secret token index cannot be negative.");

        FileKind = fileKind;
        SemanticId = semanticId;
        EntryKind = entryKind;
        Name = name;
        Scope = scope;
        LabelResourceKey = labelResourceKey;
        HelpResourceKey = helpResourceKey;
        FieldKind = fieldKind;
        Required = required;
        Sensitive = sensitive;
        AutomaticPolicy = automaticPolicy;
        ExplicitAutoToken = explicitAutoToken;
        InsertionOrder = insertionOrder;
        Activation = activation;
        Codec = codec ?? NutConfigurationValueCodec.Text;
        Applicability = applicability ?? (_ => NutConfigurationApplicability.Applicable);
        Choices = choices?.ToArray() ?? [];
        Presentation = presentation;
        SecretTokenIndex = secretTokenIndex;
        ValueIsTokenList = valueIsTokenList;
    }

    public NutConfigurationFileKind FileKind { get; }
    public string SemanticId { get; }
    public NutConfigurationEntryKind EntryKind { get; }
    public string Name { get; }
    public NutConfigurationFieldScope Scope { get; }
    public string LabelResourceKey { get; }
    public string HelpResourceKey { get; }
    public NutConfigurationFieldKind FieldKind { get; }
    public bool Required { get; }
    public bool Sensitive { get; }
    public NutConfigurationAutomaticPolicy AutomaticPolicy { get; }
    public string? ExplicitAutoToken { get; }
    public int InsertionOrder { get; }
    public NutConfigurationActivation Activation { get; }
    public INutConfigurationValueCodec Codec { get; }
    public Func<NutConfigurationSemanticContext, NutConfigurationApplicability> Applicability { get; }
    public IReadOnlyList<NutConfigurationChoice> Choices { get; }
    public NutConfigurationFieldPresentation? Presentation { get; }

    /// <summary>
    /// Position of the secret inside a whitespace-separated argument list, for directives such as
    /// upsmon.conf MONITOR that carry a credential alongside ordinary values. The projector blanks
    /// that token before the codec ever sees the line, so the secret cannot reach a view model,
    /// and the draft splices the stored token back in when the row's other values are edited.
    /// </summary>
    public int? SecretTokenIndex { get; }

    public bool HasEmbeddedSecret => SecretTokenIndex is not null;

    /// <summary>
    /// The value is a whitespace-separated list of tokens rather than one value that happens to
    /// contain spaces, as with upsd.users <c>actions</c> and <c>instcmds</c>. Such a value must not
    /// be quoted on write: NUT would read the quoted run as a single token and the permissions
    /// would silently stop matching.
    /// </summary>
    public bool ValueIsTokenList { get; }
}

public sealed record NutConfigurationSectionSchema(string SemanticId, string LabelResourceKey, bool UniqueNames = true);

public sealed class NutConfigurationFileSchema
{
    public NutConfigurationFileSchema(
        NutConfigurationFileKind fileKind,
        IEnumerable<NutConfigurationFieldDescriptor> fields,
        NutConfigurationSectionSchema? sections = null)
    {
        FileKind = fileKind;
        Fields = fields?.OrderBy(field => field.InsertionOrder).ThenBy(field => field.SemanticId, StringComparer.Ordinal).ToArray()
            ?? throw new ArgumentNullException(nameof(fields));
        if (Fields.Any(field => field.FileKind != fileKind)) throw new ArgumentException("Every field must belong to the schema file kind.", nameof(fields));
        if (Fields.GroupBy(field => field.SemanticId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("Semantic IDs must be unique within a file schema.", nameof(fields));
        if (Fields.GroupBy(field => (field.EntryKind, Name: field.Name.ToUpperInvariant(), field.Scope)).Any(group => group.Count() > 1))
            throw new ArgumentException("Conflicting descriptors cannot manage the same entry and scope.", nameof(fields));
        Sections = sections;
    }

    public NutConfigurationFileKind FileKind { get; }
    public IReadOnlyList<NutConfigurationFieldDescriptor> Fields { get; }
    public NutConfigurationSectionSchema? Sections { get; }
    public NutConfigurationFieldDescriptor? FindField(string semanticId) =>
        Fields.SingleOrDefault(field => string.Equals(field.SemanticId, semanticId, StringComparison.Ordinal));
}

public sealed class NutDriverConfigurationSchema
{
    public NutDriverConfigurationSchema(
        string driverId,
        string helpResourceKey,
        string connectionType,
        IEnumerable<NutConfigurationFieldDescriptor> fields,
        IEnumerable<string>? supportedProtocols = null,
        string? displayNameResourceKey = null,
        string? descriptionResourceKey = null,
        NutDriverCategory category = NutDriverCategory.Ups,
        IEnumerable<NutDriverTransport>? transports = null,
        string? documentationUri = null)
    {
        if (string.IsNullOrWhiteSpace(driverId)) throw new ArgumentException("A driver ID is required.", nameof(driverId));
        if (string.IsNullOrWhiteSpace(helpResourceKey)) throw new ArgumentException("A help resource key is required.", nameof(helpResourceKey));
        if (string.IsNullOrWhiteSpace(connectionType)) throw new ArgumentException("A connection type is required.", nameof(connectionType));
        DriverId = driverId;
        HelpResourceKey = helpResourceKey;
        ConnectionType = connectionType;
        Fields = fields?.ToArray() ?? throw new ArgumentNullException(nameof(fields));
        SupportedProtocols = supportedProtocols?.ToArray() ?? [];
        DisplayNameResourceKey = displayNameResourceKey ?? $"Ups.Driver.{driverId}.Name";
        DescriptionResourceKey = descriptionResourceKey ?? helpResourceKey;
        Category = category;
        Transports = transports?.Distinct().ToArray() ?? [];
        DocumentationUri = documentationUri;
    }

    public string DriverId { get; }
    public string HelpResourceKey { get; }
    public string ConnectionType { get; }
    public IReadOnlyList<NutConfigurationFieldDescriptor> Fields { get; }
    public IReadOnlyList<string> SupportedProtocols { get; }
    public string DisplayNameResourceKey { get; }
    public string DescriptionResourceKey { get; }
    public NutDriverCategory Category { get; }
    public IReadOnlyList<NutDriverTransport> Transports { get; }
    public string? DocumentationUri { get; }
}

public enum NutDriverCategory { Ups, PowerDistribution, Simulation, Other }
public enum NutDriverTransport { Serial, Usb, Network, Snmp, Modbus, Other }
