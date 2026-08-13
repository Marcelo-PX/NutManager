using System.Globalization;
using NutManager.Core.Validation;

namespace NutManager.Core.Configuration.Semantic;

/// <summary>
/// Production schema for the NUT 2.8.5 upsmon.conf(5) grammar.
///
/// Nothing here materializes a documented default. upsmon has its own built-in values for most
/// timers, and writing one out because the editor happened to display it would turn an implicit
/// default into an explicit setting the administrator never chose - and would freeze it against
/// future NUT releases that change the default.
///
/// MONITOR carries a credential in the middle of an otherwise ordinary argument list, so it is
/// declared with an embedded-secret index rather than as a wholly sensitive field: the row stays
/// editable while that one token remains change-only.
/// </summary>
public static class NutUpsmonConfigurationCatalog
{
    public const string Documentation = "https://networkupstools.org/historic/v2.8.5/docs/man/upsmon.conf.html";

    /// <summary>MONITOR system powervalue username password type - the password is token three.</summary>
    public const int MonitorSecretTokenIndex = 3;

    public const string RolePrimary = "primary";
    public const string RoleSecondary = "secondary";

    public static IReadOnlyDictionary<string, string> RoleAliases { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["primary"] = RolePrimary,
            ["master"] = RolePrimary,
            ["secondary"] = RoleSecondary,
            ["slave"] = RoleSecondary
        };

    /// <summary>
    /// Events documented for 2.8.5. An event outside this list is preserved as an unmanaged entry
    /// rather than rejected, because NUT adds events between releases.
    /// </summary>
    public static IReadOnlyList<string> NotificationEvents { get; } =
    [
        "ONLINE", "ONBATT", "LOWBATT", "FSD", "COMMOK", "COMMBAD", "SHUTDOWN", "REPLBATT", "NOCOMM", "NOPARENT",
        "CAL", "NOTCAL", "OFF", "NOTOFF", "BYPASS", "NOTBYPASS", "ECO", "NOTECO", "ALARM", "NOTALARM",
        "OVER", "NOTOVER", "TRIM", "NOTTRIM", "BOOST", "NOTBOOST", "OTHER", "NOTOTHER", "SHUTDOWN_HOSTSYNC"
    ];

    public static IReadOnlyList<string> NotificationFlags { get; } = ["SYSLOG", "WALL", "EXEC", "IGNORE"];

    public static NutConfigurationFileSchema CreateSchema() => new(
        NutConfigurationFileKind.UpsmonConf,
        [
            new NutConfigurationFieldDescriptor(
                NutConfigurationFileKind.UpsmonConf, "Upsmon.Monitor", NutConfigurationEntryKind.Directive, "MONITOR",
                NutConfigurationFieldScope.Repeated, "Upsmon.Monitor.Label", "Upsmon.Monitor.Help",
                NutConfigurationFieldKind.RepeatedRow, insertionOrder: 100,
                activation: NutConfigurationActivation.ServiceRestart,
                codec: NutMonitorEntry.Codec,
                secretTokenIndex: MonitorSecretTokenIndex,
                presentation: new("Upsmon.Group.Monitoring", DocumentationUri: Documentation)),

            Field("Upsmon.MinSupplies", "MINSUPPLIES", 200, "Upsmon.Group.Shutdown",
                NutConfigurationFieldKind.Integer, codec: NutConfigurationValueCodec.IntegerRange(0, int.MaxValue)),
            Field("Upsmon.ShutdownCommand", "SHUTDOWNCMD", 300, "Upsmon.Group.Shutdown",
                NutConfigurationFieldKind.Text, codec: QuotedArgumentCodec, risky: true),
            Field("Upsmon.PowerDownFlag", "POWERDOWNFLAG", 400, "Upsmon.Group.Shutdown",
                NutConfigurationFieldKind.Path, codec: QuotedArgumentCodec),
            Field("Upsmon.FinalDelay", "FINALDELAY", 500, "Upsmon.Group.Shutdown",
                NutConfigurationFieldKind.Integer, codec: NutConfigurationValueCodec.IntegerRange(0, int.MaxValue), unit: "Config.Unit.Seconds"),
            Field("Upsmon.HostSync", "HOSTSYNC", 600, "Upsmon.Group.Shutdown",
                NutConfigurationFieldKind.Integer, codec: NutConfigurationValueCodec.IntegerRange(0, int.MaxValue), unit: "Config.Unit.Seconds"),

            Field("Upsmon.PollFrequency", "POLLFREQ", 700, "Upsmon.Group.Polling",
                NutConfigurationFieldKind.Integer, codec: NutConfigurationValueCodec.IntegerRange(1, int.MaxValue), unit: "Config.Unit.Seconds"),
            Field("Upsmon.PollFrequencyAlert", "POLLFREQALERT", 800, "Upsmon.Group.Polling",
                NutConfigurationFieldKind.Integer, codec: NutConfigurationValueCodec.IntegerRange(1, int.MaxValue), unit: "Config.Unit.Seconds"),
            Field("Upsmon.DeadTime", "DEADTIME", 900, "Upsmon.Group.Polling",
                NutConfigurationFieldKind.Integer, codec: NutConfigurationValueCodec.IntegerRange(1, int.MaxValue), unit: "Config.Unit.Seconds"),
            Field("Upsmon.NoCommWarnTime", "NOCOMMWARNTIME", 1000, "Upsmon.Group.Polling",
                NutConfigurationFieldKind.Integer, codec: NutConfigurationValueCodec.IntegerRange(0, int.MaxValue), unit: "Config.Unit.Seconds"),
            Field("Upsmon.ReplaceBatteryWarnTime", "RBWARNTIME", 1100, "Upsmon.Group.Polling",
                NutConfigurationFieldKind.Integer, codec: NutConfigurationValueCodec.IntegerRange(0, int.MaxValue), unit: "Config.Unit.Seconds"),

            Field("Upsmon.NotifyCommand", "NOTIFYCMD", 1200, "Upsmon.Group.Notifications",
                NutConfigurationFieldKind.Text, codec: QuotedArgumentCodec, risky: true),

            new NutConfigurationFieldDescriptor(
                NutConfigurationFileKind.UpsmonConf, "Upsmon.NotifyFlag", NutConfigurationEntryKind.Directive, "NOTIFYFLAG",
                NutConfigurationFieldScope.Repeated, "Upsmon.NotifyFlag.Label", "Upsmon.NotifyFlag.Help",
                NutConfigurationFieldKind.RepeatedRow, insertionOrder: 1300,
                activation: NutConfigurationActivation.ServiceRestart, codec: NutNotificationFlagEntry.Codec,
                presentation: new("Upsmon.Group.Notifications", DocumentationUri: Documentation)),
            new NutConfigurationFieldDescriptor(
                NutConfigurationFileKind.UpsmonConf, "Upsmon.NotifyMessage", NutConfigurationEntryKind.Directive, "NOTIFYMSG",
                NutConfigurationFieldScope.Repeated, "Upsmon.NotifyMessage.Label", "Upsmon.NotifyMessage.Help",
                NutConfigurationFieldKind.RepeatedRow, insertionOrder: 1400,
                activation: NutConfigurationActivation.ServiceRestart, codec: NutNotificationMessageEntry.Codec,
                presentation: new("Upsmon.Group.Notifications", DocumentationUri: Documentation))
        ]);

    private static NutConfigurationFieldDescriptor Field(
        string id,
        string name,
        int order,
        string group,
        NutConfigurationFieldKind kind,
        INutConfigurationValueCodec? codec = null,
        string? unit = null,
        bool risky = false) =>
        new(NutConfigurationFileKind.UpsmonConf, id, NutConfigurationEntryKind.Directive, name,
            NutConfigurationFieldScope.Global, $"{id}.Label", $"{id}.Help", kind,
            insertionOrder: order,
            activation: NutConfigurationActivation.ServiceRestart,
            codec: codec,
            presentation: new(group, unit, IsRisky: risky, DocumentationUri: Documentation));

    /// <summary>
    /// A single argument that may contain spaces. Quoting is produced by the shared tokenizer so a
    /// view model never has to build quotes itself, and an untouched value keeps the style it had.
    /// </summary>
    internal static INutConfigurationValueCodec QuotedArgumentCodec { get; } = NutConfigurationValueCodec.Create(
        // Parse reads two different things with one rule. From the file the argument arrives quoted
        // ("shutdown -h +0"), which tokenizes to a single token. From an edit box it arrives as the
        // bare value the user sees, and a shutdown command almost always has spaces, so it would
        // tokenize to several. Anything that is not one clean token is therefore the value itself;
        // Serialize adds the quotes back, which is also what repairs an unquoted line on edit.
        (value, _) => NutDirectiveArgumentTokenizer.Tokenize(value) is { IsValid: true, Tokens.Count: 1 } parsed
            ? new(parsed.Tokens[0], [])
            : new(value, []),
        (value, field) => value is string text && text.Length > 0 && !text.Any(character => character is '\r' or '\n')
            ? new(NutDirectiveArgumentTokenizer.Quote(text), [])
            : new(default, [new FieldValidationIssue(field, "Upsmon.Argument.Invalid", ValidationSeverity.Error, "Config.Validation.Argument.Invalid")]));
}

/// <summary>
/// One MONITOR line without its credential. The password token is blanked by the projector before
/// this record is ever built, so nothing that reaches a view model can carry it.
/// </summary>
public sealed record NutMonitorEntry(string System, int PowerValue, string Username, string Role)
{
    public static INutConfigurationValueCodec Codec { get; } = NutConfigurationValueCodec.Create(ParseValue, SerializeValue);

    /// <summary>True when the role is one this editor manages; anything else is preserved as written.</summary>
    public bool HasManagedRole => NutUpsmonConfigurationCatalog.RoleAliases.ContainsKey(Role);

    private static FieldValidationResult<object> ParseValue(string value, string field)
    {
        var tokens = NutUpsmonArguments.Split(value);
        if (tokens.Count < 5) return Invalid<object>(field, "Upsmon.Monitor.Incomplete", "Upsmon.Validation.MonitorIncomplete");
        if (!int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var power) || power < 0)
            return Invalid<object>(field, "Upsmon.Monitor.PowerValue", "Upsmon.Validation.MonitorPowerValue");
        return new(new NutMonitorEntry(tokens[0], power, tokens[2], tokens[4]), []);
    }

    /// <summary>
    /// Writes the row with a placeholder where the credential goes. The draft replaces that token
    /// with the stored or replacement secret while replaying, so this method never handles one.
    /// </summary>
    private static FieldValidationResult<string> SerializeValue(object value, string field)
    {
        if (value is not NutMonitorEntry entry) return Invalid<string>(field, "Upsmon.Monitor.Invalid", "Upsmon.Validation.MonitorIncomplete");
        if (string.IsNullOrWhiteSpace(entry.System)) return Invalid<string>(field, "Upsmon.Monitor.System", "Upsmon.Validation.MonitorSystem");
        if (entry.PowerValue < 0) return Invalid<string>(field, "Upsmon.Monitor.PowerValue", "Upsmon.Validation.MonitorPowerValue");
        if (string.IsNullOrWhiteSpace(entry.Username)) return Invalid<string>(field, "Upsmon.Monitor.Username", "Upsmon.Validation.MonitorUsername");
        if (string.IsNullOrWhiteSpace(entry.Role)) return Invalid<string>(field, "Upsmon.Monitor.Role", "Upsmon.Validation.MonitorRole");

        var parts = new[]
        {
            NutDirectiveArgumentTokenizer.Quote(entry.System),
            entry.PowerValue.ToString(CultureInfo.InvariantCulture),
            NutDirectiveArgumentTokenizer.Quote(entry.Username),
            NutEmbeddedSecret.Placeholder.Trim(),
            entry.Role
        };
        return new(string.Join(' ', parts), []);
    }

    private static FieldValidationResult<T> Invalid<T>(string field, string code, string resource) =>
        new(default, [new FieldValidationIssue(field, code, ValidationSeverity.Error, resource)]);
}

/// <summary>One NOTIFYFLAG line: an event plus the flag set that applies to it.</summary>
public sealed record NutNotificationFlagEntry(string Event, IReadOnlyList<string> Flags)
{
    public static INutConfigurationValueCodec Codec { get; } = NutConfigurationValueCodec.Create(
        (value, field) =>
        {
            var tokens = NutUpsmonArguments.Split(value);
            return tokens.Count >= 2
                ? new(new NutNotificationFlagEntry(tokens[0], SplitFlags(tokens[1])), [])
                : new(default, [new FieldValidationIssue(field, "Upsmon.NotifyFlag.Invalid", ValidationSeverity.Error, "Upsmon.Validation.NotifyFlag")]);
        },
        (value, field) => value is NutNotificationFlagEntry entry && entry.Flags.Count > 0
            ? new($"{entry.Event} {string.Join('+', entry.Flags)}", [])
            : new(default, [new FieldValidationIssue(field, "Upsmon.NotifyFlag.Invalid", ValidationSeverity.Error, "Upsmon.Validation.NotifyFlag")]));

    /// <summary>IGNORE cannot be combined: upsmon takes it to mean the event produces nothing.</summary>
    public bool IsIgnored => Flags.Any(flag => string.Equals(flag, "IGNORE", StringComparison.OrdinalIgnoreCase));

    public bool Has(string flag) => Flags.Any(item => string.Equals(item, flag, StringComparison.OrdinalIgnoreCase));

    public bool IsManagedEvent => NutUpsmonConfigurationCatalog.NotificationEvents
        .Contains(Event, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> SplitFlags(string value) =>
        value.Split(['+', '|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>One NOTIFYMSG line: an event plus the message shown for it.</summary>
public sealed record NutNotificationMessageEntry(string Event, string Message)
{
    public static INutConfigurationValueCodec Codec { get; } = NutConfigurationValueCodec.Create(
        (value, field) =>
        {
            var tokens = NutUpsmonArguments.Split(value);
            return tokens.Count >= 2
                ? new(new NutNotificationMessageEntry(tokens[0], string.Join(' ', tokens.Skip(1))), [])
                : new(default, [new FieldValidationIssue(field, "Upsmon.NotifyMessage.Invalid", ValidationSeverity.Error, "Upsmon.Validation.NotifyMessage")]);
        },
        (value, field) => value is NutNotificationMessageEntry entry && entry.Message.Length > 0
            ? new($"{entry.Event} {NutDirectiveArgumentTokenizer.Quote(entry.Message)}", [])
            : new(default, [new FieldValidationIssue(field, "Upsmon.NotifyMessage.Invalid", ValidationSeverity.Error, "Upsmon.Validation.NotifyMessage")]));

    public bool IsManagedEvent => NutUpsmonConfigurationCatalog.NotificationEvents
        .Contains(Event, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Whitespace splitting that keeps quoted runs together and strips the quotes.</summary>
internal static class NutUpsmonArguments
{
    internal static IReadOnlyList<string> Split(string value)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < value.Length)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
            if (index >= value.Length) break;
            if (value[index] is '"' or '\'')
            {
                var quote = value[index++];
                var start = index;
                while (index < value.Length && (value[index] != quote || value[index - 1] == '\\')) index++;
                tokens.Add(value[start..index].Replace($"\\{quote}", quote.ToString()));
                if (index < value.Length) index++;
            }
            else
            {
                var start = index;
                while (index < value.Length && !char.IsWhiteSpace(value[index])) index++;
                tokens.Add(value[start..index]);
            }
        }

        return tokens;
    }
}
