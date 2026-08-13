using NutManager.Core.Validation;

namespace NutManager.Core.Configuration.Semantic;

/// <summary>
/// Production schema for the NUT 2.8.5 upsd.users(5) grammar.
///
/// Every section is one NUT user, so the managed fields are all section-scoped and the section
/// operations already provided by the draft cover add, rename and remove. The password is declared
/// change-only: the projector drops the value of a sensitive field entirely, so a configured
/// password never reaches a view model, a review item or a preview line.
///
/// Only SET and FSD are managed as actions and only ALL is managed as a blanket instant-command
/// grant. Anything else the file already contains survives untouched as an unmanaged parameter,
/// because a NUT release can add tokens at any time and losing one would silently drop a
/// permission the administrator granted deliberately.
/// </summary>
public static class NutUpsdUsersConfigurationCatalog
{
    public const string Documentation = "https://networkupstools.org/historic/v2.8.5/docs/man/upsd.users.html";

    /// <summary>Tokens this editor understands inside an <c>actions</c> list.</summary>
    public static IReadOnlyList<string> ManagedActions { get; } = ["SET", "FSD"];

    /// <summary>The blanket instant-command grant. Any other token is a specific command name.</summary>
    public const string AllInstantCommands = "ALL";

    public const string UpsmonPrimary = "primary";
    public const string UpsmonSecondary = "secondary";

    /// <summary>
    /// Historic spellings kept readable by upsd. They are accepted on read and preserved as written
    /// unless the administrator picks a different role, at which point the modern spelling is used.
    /// </summary>
    public static IReadOnlyDictionary<string, string> UpsmonRoleAliases { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["primary"] = UpsmonPrimary,
            ["master"] = UpsmonPrimary,
            ["secondary"] = UpsmonSecondary,
            ["slave"] = UpsmonSecondary
        };

    public static NutConfigurationFileSchema CreateSchema() => new(
        NutConfigurationFileKind.UpsdUsers,
        [
            Field("UpsdUsers.Password", "password", NutConfigurationEntryKind.Assignment,
                NutConfigurationFieldKind.SecretChange, 100, sensitive: true),
            Field("UpsdUsers.Actions", "actions", NutConfigurationEntryKind.Assignment,
                NutConfigurationFieldKind.Text, 200, codec: NutUpsdUserActions.Codec, risky: true, tokenList: true),
            Field("UpsdUsers.InstantCommands", "instcmds", NutConfigurationEntryKind.Assignment,
                NutConfigurationFieldKind.Text, 300, codec: NutUpsdUserInstantCommands.Codec, risky: true, tokenList: true),
            Field("UpsdUsers.UpsmonRole", "upsmon", NutConfigurationEntryKind.Directive,
                NutConfigurationFieldKind.Choice, 400,
                codec: NutConfigurationValueCodec.AliasChoice(UpsmonRoleAliases),
                choices: [new(UpsmonPrimary, "UpsdUsers.Role.Primary"), new(UpsmonSecondary, "UpsdUsers.Role.Secondary")],
                risky: true)
        ],
        new NutConfigurationSectionSchema("UpsdUsers.User", "UpsdUsers.User.Label"));

    private static NutConfigurationFieldDescriptor Field(
        string id,
        string name,
        NutConfigurationEntryKind entryKind,
        NutConfigurationFieldKind kind,
        int order,
        bool sensitive = false,
        bool risky = false,
        INutConfigurationValueCodec? codec = null,
        IReadOnlyList<NutConfigurationChoice>? choices = null,
        bool tokenList = false) =>
        new(NutConfigurationFileKind.UpsdUsers, id, entryKind, name, NutConfigurationFieldScope.Section,
            $"{id}.Label", $"{id}.Help", kind,
            sensitive: sensitive,
            insertionOrder: order,
            activation: NutConfigurationActivation.Reload,
            codec: codec,
            choices: choices,
            presentation: new NutConfigurationFieldPresentation("UpsdUsers.Group.Permissions",
                IsRisky: risky, DocumentationUri: Documentation),
            valueIsTokenList: tokenList);
}

/// <summary>
/// The <c>actions</c> list, split into the two tokens this editor manages and everything else.
/// Unmanaged tokens keep their original spelling and their position relative to one another, so a
/// future NUT action survives a round trip through the graphical editor untouched.
/// </summary>
public sealed record NutUpsdUserActions(bool AllowSet, bool AllowForcedShutdown, IReadOnlyList<string> Unmanaged)
{
    public static INutConfigurationValueCodec Codec { get; } = NutConfigurationValueCodec.Create(
        (value, _) => new(Parse(value), []),
        (value, field) => value is NutUpsdUserActions actions
            ? new(actions.ToNutValue(), [])
            : new(default, [new FieldValidationIssue(field, "UpsdUsers.Actions.Invalid", ValidationSeverity.Error, "UpsdUsers.Validation.Actions")]));

    public static NutUpsdUserActions Parse(string value)
    {
        var set = false;
        var fsd = false;
        var unmanaged = new List<string>();
        foreach (var token in Split(value))
        {
            if (string.Equals(token, "SET", StringComparison.OrdinalIgnoreCase)) set = true;
            else if (string.Equals(token, "FSD", StringComparison.OrdinalIgnoreCase)) fsd = true;
            else unmanaged.Add(token);
        }

        return new(set, fsd, unmanaged);
    }

    /// <summary>
    /// Managed tokens first in a stable order, then whatever the file already had. upsd accepts the
    /// list in any order, so a fixed order for the managed pair keeps diffs small.
    /// </summary>
    public string ToNutValue()
    {
        var tokens = new List<string>();
        if (AllowSet) tokens.Add("SET");
        if (AllowForcedShutdown) tokens.Add("FSD");
        tokens.AddRange(Unmanaged);
        return string.Join(' ', tokens);
    }

    public bool IsEmpty => !AllowSet && !AllowForcedShutdown && Unmanaged.Count == 0;

    internal static IEnumerable<string> Split(string value) =>
        value.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// The <c>instcmds</c> list. NUT treats the single token <c>ALL</c> as a blanket grant, so the
/// three states an administrator actually reasons about are none, all, and a specific list.
/// </summary>
public sealed record NutUpsdUserInstantCommands(bool All, IReadOnlyList<string> Commands)
{
    public static INutConfigurationValueCodec Codec { get; } = NutConfigurationValueCodec.Create(
        (value, _) => new(Parse(value), []),
        (value, field) => value is NutUpsdUserInstantCommands commands && commands.ToNutValue() is { Length: > 0 } text
            ? new(text, [])
            : new(default, [new FieldValidationIssue(field, "UpsdUsers.InstantCommands.Invalid", ValidationSeverity.Error, "UpsdUsers.Validation.InstantCommands")]));

    public static NutUpsdUserInstantCommands Parse(string value)
    {
        var tokens = NutUpsdUserActions.Split(value).ToArray();
        return tokens.Any(token => string.Equals(token, NutUpsdUsersConfigurationCatalog.AllInstantCommands, StringComparison.OrdinalIgnoreCase))
            ? new(true, tokens.Where(token => !string.Equals(token, NutUpsdUsersConfigurationCatalog.AllInstantCommands, StringComparison.OrdinalIgnoreCase)).ToArray())
            : new(false, tokens);
    }

    /// <summary>
    /// ALL is written on its own. Specific commands recorded alongside it are kept so the grant can
    /// be narrowed later without the administrator having to retype the list, but they add nothing
    /// while ALL is in force and are not written out.
    /// </summary>
    public string ToNutValue() => All
        ? NutUpsdUsersConfigurationCatalog.AllInstantCommands
        : string.Join(' ', Commands);

    public bool IsEmpty => !All && Commands.Count == 0;
}
