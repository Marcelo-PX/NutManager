using System.Globalization;
using System.Net;
using System.Text;
using NutManager.Core.Validation;

namespace NutManager.Core.Configuration.Semantic;

/// <summary>
/// Production schemas for the NUT 2.8.5 nut.conf(5) and upsd.conf(5) grammars.
/// Descriptors never materialize documented defaults unless the user explicitly chooses a value.
/// </summary>
public static class NutServerConfigurationCatalog
{
    public const string NutConfDocumentation = "https://networkupstools.org/historic/v2.8.5/docs/man/nut.conf.html";
    public const string UpsdConfDocumentation = "https://networkupstools.org/historic/v2.8.5/docs/man/upsd.conf.html";

    private static readonly INutConfigurationValueCodec BooleanCodec = NutConfigurationValueCodec.Boolean();
    private static readonly string[] NutModes = ["none", "standalone", "netserver", "netclient"];

    public static NutConfigurationFileSchema CreateNutConfSchema() => new(NutConfigurationFileKind.NutConf,
    [
        NutField("Nut.Mode", "MODE", NutConfigurationFieldKind.Choice, 100, required: true,
            codec: ExactChoice(NutModes), choices: Choices(NutModes), advanced: false,
            activation: NutConfigurationActivation.ServiceRestart),
        NutField("Nut.AllowNoDevice", "ALLOW_NO_DEVICE", NutConfigurationFieldKind.Boolean, 200, codec: BooleanCodec),
        NutField("Nut.AllowNotAllListeners", "ALLOW_NOT_ALL_LISTENERS", NutConfigurationFieldKind.Boolean, 300, codec: BooleanCodec),
        NutField("Nut.UpsdOptions", "UPSD_OPTIONS", NutConfigurationFieldKind.Text, 400, risky: true),
        NutField("Nut.UpsmonOptions", "UPSMON_OPTIONS", NutConfigurationFieldKind.Text, 500, risky: true),
        NutField("Nut.PoweroffWait", "POWEROFF_WAIT", NutConfigurationFieldKind.Text, 600, risky: true),
        NutField("Nut.PoweroffQuiet", "POWEROFF_QUIET", NutConfigurationFieldKind.Boolean, 700, codec: BooleanCodec),
        NutField("Nut.DebugLevel", "NUT_DEBUG_LEVEL", NutConfigurationFieldKind.Integer, 800,
            codec: NutConfigurationValueCodec.IntegerRange(0, int.MaxValue), risky: true),
        NutField("Nut.DebugPid", "NUT_DEBUG_PID", NutConfigurationFieldKind.Boolean, 900, codec: BooleanCodec),
        NutField("Nut.DebugProcessName", "NUT_DEBUG_PROCNAME", NutConfigurationFieldKind.Boolean, 1000, codec: BooleanCodec),
        NutField("Nut.DebugSyslog", "NUT_DEBUG_SYSLOG", NutConfigurationFieldKind.Text, 1100, risky: true),
        NutField("Nut.IgnoreProcessNameCheck", "NUT_IGNORE_CHECKPROCNAME", NutConfigurationFieldKind.Boolean, 1200, codec: BooleanCodec, risky: true),
        NutField("Nut.QuietInitUpsNotify", "NUT_QUIET_INIT_UPSNOTIFY", NutConfigurationFieldKind.Boolean, 1300, codec: BooleanCodec)
    ]);

    public static NutConfigurationFileSchema CreateUpsdConfSchema() => new(NutConfigurationFileKind.UpsdConf,
    [
        UpsdField("Upsd.Listen", "LISTEN", NutConfigurationFieldKind.RepeatedRow, 100,
            scope: NutConfigurationFieldScope.Repeated, codec: NutUpsdListenEndpoint.Codec,
            activation: NutConfigurationActivation.ServiceRestart, advanced: false),
        UpsdField("Upsd.MaxAge", "MAXAGE", NutConfigurationFieldKind.Integer, 200,
            codec: NutConfigurationValueCodec.IntegerRange(1, int.MaxValue), unit: "Config.Unit.Seconds", activation: NutConfigurationActivation.Reload),
        UpsdField("Upsd.TrackingDelay", "TRACKINGDELAY", NutConfigurationFieldKind.Integer, 300,
            codec: NutConfigurationValueCodec.IntegerRange(0, int.MaxValue), unit: "Config.Unit.Seconds", activation: NutConfigurationActivation.Reload),
        UpsdField("Upsd.AllowNoDevice", "ALLOW_NO_DEVICE", NutConfigurationFieldKind.Boolean, 400, codec: BooleanCodec, activation: NutConfigurationActivation.Reload),
        UpsdField("Upsd.AllowNotAllListeners", "ALLOW_NOT_ALL_LISTENERS", NutConfigurationFieldKind.Boolean, 500, codec: BooleanCodec, activation: NutConfigurationActivation.Reload),
        UpsdField("Upsd.StatePath", "STATEPATH", NutConfigurationFieldKind.Path, 600, codec: SingleArgumentCodec),
        UpsdField("Upsd.MaxConnections", "MAXCONN", NutConfigurationFieldKind.Integer, 700,
            codec: NutConfigurationValueCodec.IntegerRange(1, int.MaxValue), activation: NutConfigurationActivation.Reload),
        UpsdField("Upsd.CertificateFile", "CERTFILE", NutConfigurationFieldKind.Path, 800, codec: SingleArgumentCodec, group: "Config.Tls.Title"),
        UpsdField("Upsd.CertificatePath", "CERTPATH", NutConfigurationFieldKind.Path, 900, codec: SingleArgumentCodec, group: "Config.Tls.Title"),
        UpsdField("Upsd.CertificateIdentity", "CERTIDENT", NutConfigurationFieldKind.SecretChange, 1000,
            sensitive: true, group: "Config.Tls.Title"),
        UpsdField("Upsd.CertificateRequest", "CERTREQUEST", NutConfigurationFieldKind.Choice, 1100,
            codec: NutConfigurationValueCodec.AliasChoice(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["0"] = "0", ["NO"] = "0", ["1"] = "1", ["REQUEST"] = "1", ["2"] = "2", ["REQUIRE"] = "2"
            }), choices: Choices("0", "1", "2"), group: "Config.Tls.Title"),
        UpsdField("Upsd.DisableWeakSsl", "DISABLE_WEAK_SSL", NutConfigurationFieldKind.Boolean, 1200,
            codec: BooleanCodec, group: "Config.Tls.Title"),
        UpsdField("Upsd.DebugMinimum", "DEBUG_MIN", NutConfigurationFieldKind.Integer, 1300,
            codec: NutConfigurationValueCodec.IntegerRange(0, int.MaxValue), risky: true, activation: NutConfigurationActivation.Reload)
    ]);

    private static NutConfigurationFieldDescriptor NutField(
        string id,
        string name,
        NutConfigurationFieldKind kind,
        int order,
        bool required = false,
        INutConfigurationValueCodec? codec = null,
        IReadOnlyList<NutConfigurationChoice>? choices = null,
        bool advanced = true,
        bool risky = false,
        NutConfigurationActivation activation = NutConfigurationActivation.ServiceRestart) =>
        new(NutConfigurationFileKind.NutConf, id, NutConfigurationEntryKind.Assignment, name,
            NutConfigurationFieldScope.Global, $"Config.Field.{id}.Label", $"Config.Field.{id}.Help", kind,
            required: required, automaticPolicy: required ? NutConfigurationAutomaticPolicy.NotSupported : NutConfigurationAutomaticPolicy.OmitDirective,
            insertionOrder: order, activation: activation, codec: codec, choices: choices,
            presentation: new("Config.General.Advanced", IsAdvanced: advanced, IsRisky: risky, DocumentationUri: NutConfDocumentation));

    private static NutConfigurationFieldDescriptor UpsdField(
        string id,
        string name,
        NutConfigurationFieldKind kind,
        int order,
        NutConfigurationFieldScope scope = NutConfigurationFieldScope.Global,
        INutConfigurationValueCodec? codec = null,
        IReadOnlyList<NutConfigurationChoice>? choices = null,
        bool sensitive = false,
        string group = "Config.Server.Behavior",
        string? unit = null,
        bool advanced = true,
        bool risky = false,
        NutConfigurationActivation activation = NutConfigurationActivation.ServiceRestart) =>
        new(NutConfigurationFileKind.UpsdConf, id, NutConfigurationEntryKind.Directive, name, scope,
            $"Config.Field.{id}.Label", $"Config.Field.{id}.Help", kind, sensitive: sensitive,
            automaticPolicy: NutConfigurationAutomaticPolicy.OmitDirective, insertionOrder: order,
            activation: activation, codec: codec, choices: choices,
            presentation: new(group, unit, advanced, risky, DocumentationUri: UpsdConfDocumentation));

    private static IReadOnlyList<NutConfigurationChoice> Choices(params string[] values) =>
        values.Select(value => new NutConfigurationChoice(value, $"Config.Choice.{value}")).ToArray();

    private static INutConfigurationValueCodec ExactChoice(IReadOnlyList<string> values) => NutConfigurationValueCodec.Create(
        (value, field) => values.Contains(value, StringComparer.Ordinal)
            ? new(value, [])
            : Invalid<object>(field, "Semantic.Value.Choice", "Semantic.Validation.Choice"),
        (value, field) => value is string text && values.Contains(text, StringComparer.Ordinal)
            ? new(text, [])
            : Invalid<string>(field, "Semantic.Value.Choice", "Semantic.Validation.Choice"));

    private static INutConfigurationValueCodec SingleArgumentCodec { get; } = NutConfigurationValueCodec.Create(
        (value, field) => NutDirectiveArgumentTokenizer.Tokenize(value) is { IsValid: true, Tokens.Count: 1 } parsed
            ? new(parsed.Tokens[0], [])
            : Invalid<object>(field, "Directive.Argument.Invalid", "Config.Validation.Argument.Invalid"),
        (value, field) => value is string text && text.Length > 0 && !text.Any(character => character is '\r' or '\n' || char.IsControl(character))
            ? new(NutDirectiveArgumentTokenizer.Quote(text), [])
            : Invalid<string>(field, "Directive.Argument.Invalid", "Config.Validation.Argument.Invalid"));

    private static FieldValidationResult<T> Invalid<T>(string field, string code, string resource) =>
        new(default, [new FieldValidationIssue(field, code, ValidationSeverity.Error, resource)]);
}

public sealed record NutUpsdListenEndpoint(string Address, int? Port)
{
    public static INutConfigurationValueCodec Codec { get; } = NutConfigurationValueCodec.Create(ParseValue, SerializeValue);

    public string ToNutArguments() => Port is null ? Address : $"{Address} {Port.Value.ToString(CultureInfo.InvariantCulture)}";

    public static FieldValidationResult<NutUpsdListenEndpoint> Parse(string value, string semanticId = "Upsd.Listen")
    {
        var tokens = NutDirectiveArgumentTokenizer.Tokenize(value);
        if (!tokens.IsValid || tokens.Tokens.Count is < 1 or > 2 || !IsValidAddress(tokens.Tokens[0]))
            return Invalid<NutUpsdListenEndpoint>(semanticId, "Upsd.Listen.Invalid", "Config.Validation.Listen.Invalid");
        int? port = null;
        if (tokens.Tokens.Count == 2)
        {
            if (!int.TryParse(tokens.Tokens[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 1 or > 65535)
                return Invalid<NutUpsdListenEndpoint>(semanticId, "Upsd.Listen.Port", "Config.Validation.Listen.Port");
            port = parsed;
        }
        return new(new(tokens.Tokens[0], port), []);
    }

    private static FieldValidationResult<object> ParseValue(string value, string semanticId)
    {
        var result = Parse(value, semanticId);
        return new(result.Value, result.Issues);
    }

    private static FieldValidationResult<string> SerializeValue(object value, string semanticId) => value switch
    {
        NutUpsdListenEndpoint endpoint when IsValidAddress(endpoint.Address) && endpoint.Port is null or >= 1 and <= 65535 =>
            new(endpoint.ToNutArguments(), []),
        string text when Parse(text, semanticId).Value is { } endpoint => new(endpoint.ToNutArguments(), []),
        _ => Invalid<string>(semanticId, "Upsd.Listen.Invalid", "Config.Validation.Listen.Invalid")
    };

    private static bool IsValidAddress(string value)
    {
        if (value is "*") return true;
        if (IPAddress.TryParse(value, out var address)) return address.AddressFamily is
            System.Net.Sockets.AddressFamily.InterNetwork or System.Net.Sockets.AddressFamily.InterNetworkV6;
        if (value.Length is < 1 or > 253 || value.StartsWith('.') || value.EndsWith('.')) return false;
        return value.Split('.').All(label => label.Length is > 0 and <= 63 && label[0] != '-' && label[^1] != '-' &&
            label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
    }

    private static FieldValidationResult<T> Invalid<T>(string field, string code, string resource) =>
        new(default, [new FieldValidationIssue(field, code, ValidationSeverity.Error, resource)]);
}

public sealed record NutDirectiveTokenizationResult(bool IsValid, IReadOnlyList<string> Tokens);

public static class NutDirectiveArgumentTokenizer
{
    public static NutDirectiveTokenizationResult Tokenize(string value)
    {
        if (value is null || value.Any(character => character is '\r' or '\n' || char.IsControl(character))) return new(false, []);
        var tokens = new List<string>();
        var token = new StringBuilder();
        char? quote = null;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (quote is not null)
            {
                if (character == '\\' && index + 1 < value.Length &&
                    (value[index + 1] == quote || value[index + 1] == '\\'))
                {
                    token.Append(value[++index]);
                    continue;
                }
                if (character == quote) quote = null;
                else token.Append(character);
                continue;
            }
            if (character is '\'' or '"') { quote = character; continue; }
            if (char.IsWhiteSpace(character))
            {
                if (token.Length > 0) { tokens.Add(token.ToString()); token.Clear(); }
                continue;
            }
            token.Append(character);
        }
        if (quote is not null) return new(false, []);
        if (token.Length > 0) tokens.Add(token.ToString());
        return new(true, tokens);
    }

    public static string Quote(string value) => value.Any(char.IsWhiteSpace) || value.Contains('"') || value.Contains('\\')
        ? $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
        : value;

    public static char[] QuoteSensitive(ReadOnlySpan<char> value)
    {
        var quote = false;
        var escapedLength = value.Length;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || character is '"' or '\\') quote = true;
            if (character is '"' or '\\') escapedLength++;
        }
        if (!quote) return value.ToArray();
        var result = new char[escapedLength + 2];
        var offset = 0;
        result[offset++] = '"';
        foreach (var character in value)
        {
            if (character is '"' or '\\') result[offset++] = '\\';
            result[offset++] = character;
        }
        result[offset] = '"';
        return result;
    }
}

public sealed class NutServerConfigurationDocumentValidationRule : INutConfigurationDocumentValidationRule
{
    public IReadOnlyList<NutConfigurationSemanticIssue> Validate(
        NutConfigurationDocument document,
        NutConfigurationSemanticProjection projection)
    {
        if (document.FileKind != NutConfigurationFileKind.UpsdConf) return [];
        var issues = new List<NutConfigurationSemanticIssue>();
        foreach (var field in projection.Fields.Where(field => field.Descriptor.SemanticId == "Upsd.Listen" && field.Value is NutUpsdListenEndpoint))
        {
            var endpoint = (NutUpsdListenEndpoint)field.Value!;
            if (endpoint.Address is "*" or "0.0.0.0" or "::" or "::0")
                issues.Add(new("Upsd.Listen.Wildcard", ValidationSeverity.Warning, "Config.Validation.Listen.Wildcard",
                    field.Descriptor.SemanticId, RowId: field.RowId));
        }
        return issues;
    }
}
