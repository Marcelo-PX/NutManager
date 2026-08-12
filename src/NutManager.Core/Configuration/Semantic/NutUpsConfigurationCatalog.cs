namespace NutManager.Core.Configuration.Semantic;

public enum NutDriverCompatibilityLevel { Documented, InstalledWithoutSchema }

public sealed record NutDriverCatalogEntry(
    string DriverId,
    string DisplayNameResourceKey,
    string DescriptionResourceKey,
    NutDriverCategory Category,
    IReadOnlyList<NutDriverTransport> Transports,
    NutDriverCompatibilityLevel Compatibility,
    bool IsInstalled,
    NutDriverConfigurationSchema? Schema,
    string? DocumentationUri)
{
    public bool HasStructuredOptions => Schema is not null;
}

public sealed class NutDriverCatalog
{
    private readonly IReadOnlyList<NutDriverCatalogEntry> _entries;

    public NutDriverCatalog(IEnumerable<NutDriverCatalogEntry> entries)
    {
        var materialized = entries?.OrderBy(entry => entry.DriverId, StringComparer.OrdinalIgnoreCase).ToArray()
            ?? throw new ArgumentNullException(nameof(entries));
        if (materialized.GroupBy(entry => entry.DriverId, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new ArgumentException("Driver IDs must be unique.", nameof(entries));
        _entries = materialized;
    }

    public IReadOnlyList<NutDriverCatalogEntry> Entries => _entries;

    public IReadOnlyList<NutDriverCatalogEntry> Search(
        string? search = null,
        NutDriverTransport? transport = null,
        NutDriverCategory? category = null) => _entries
        .Where(entry => string.IsNullOrWhiteSpace(search) || entry.DriverId.Contains(search, StringComparison.OrdinalIgnoreCase))
        .Where(entry => transport is null || entry.Transports.Contains(transport.Value))
        .Where(entry => category is null || entry.Category == category.Value)
        .ToArray();

    public NutDriverCatalogEntry? Find(string driverId) =>
        _entries.SingleOrDefault(entry => string.Equals(entry.DriverId, driverId, StringComparison.OrdinalIgnoreCase));

    public static NutDriverCatalog Create(
        NutConfigurationSchemaRegistry registry,
        IEnumerable<string>? installedDriverNames = null,
        IEnumerable<string>? configuredDriverNames = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var installed = (installedDriverNames ?? []).Where(IsValidDriverName)
            .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = registry.DriverSchemas.Select(schema => new NutDriverCatalogEntry(
            schema.DriverId,
            schema.DisplayNameResourceKey,
            schema.DescriptionResourceKey,
            schema.Category,
            schema.Transports,
            NutDriverCompatibilityLevel.Documented,
            installed.Contains(schema.DriverId),
            schema,
            schema.DocumentationUri)).ToList();
        foreach (var driver in installed.Where(name => entries.All(entry => !string.Equals(entry.DriverId, name, StringComparison.OrdinalIgnoreCase))))
        {
            entries.Add(new(driver, "Ups.Driver.Unknown.Name", "Ups.Driver.Unknown.Description", NutDriverCategory.Other,
                [NutDriverTransport.Other], NutDriverCompatibilityLevel.InstalledWithoutSchema, true, null, null));
        }

        foreach (var driver in (configuredDriverNames ?? []).Where(IsValidDriverName)
            .Where(name => entries.All(entry => !string.Equals(entry.DriverId, name, StringComparison.OrdinalIgnoreCase))))
        {
            entries.Add(new(driver, "Ups.Driver.Unknown.Name", "Ups.Driver.Unknown.Description", NutDriverCategory.Other,
                [NutDriverTransport.Other], NutDriverCompatibilityLevel.InstalledWithoutSchema, installed.Contains(driver), null, null));
        }

        return new(entries);
    }

    public static bool IsValidDriverName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var candidate = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        return candidate.Length > 0 && candidate.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');
    }
}

public static class NutUpsConfigurationCatalog
{
    public const string UpsConfDocumentation = "https://networkupstools.org/docs/man/ups.conf.html";
    public const string NutdrvQxDocumentation = "https://networkupstools.org/docs/man/nutdrv_qx.html";
    public const string UsbHidDocumentation = "https://networkupstools.org/docs/man/usbhid-ups.html";
    public const string SnmpDocumentation = "https://networkupstools.org/docs/man/snmp-ups.html";

    public static NutConfigurationFileSchema CreateFileSchema(string? selectedDriver = null)
    {
        var fields = BaseFields().ToList();
        foreach (var driver in CreateDriverSchemas())
        {
            foreach (var field in driver.Fields)
            {
                if (fields.All(existing => !string.Equals(existing.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
                    fields.Add(ForDriver(field, driver.DriverId));
            }
        }

        _ = selectedDriver;
        return new(NutConfigurationFileKind.UpsConf, fields, new("Ups.Section", "Semantic.Section.Ups"));
    }

    public static IReadOnlyList<NutDriverConfigurationSchema> CreateDriverSchemas() =>
    [
        new(
            "nutdrv_qx",
            "Ups.Driver.nutdrv_qx.Help",
            "SerialOrUsb",
            NutdrvQxFields(),
            ["bestups", "hunnox", "masterguard", "mecer", "megatec", "megatec/old", "mustek", "q1", "voltronic", "voltronic-qs", "voltronic-qs-hex", "zinto"],
            "Ups.Driver.nutdrv_qx.Name",
            "Ups.Driver.nutdrv_qx.Description",
            NutDriverCategory.Ups,
            [NutDriverTransport.Serial, NutDriverTransport.Usb],
            NutdrvQxDocumentation),
        new(
            "usbhid-ups",
            "Ups.Driver.usbhid-ups.Help",
            "Usb",
            UsbHidFields(),
            displayNameResourceKey: "Ups.Driver.usbhid-ups.Name",
            descriptionResourceKey: "Ups.Driver.usbhid-ups.Description",
            transports: [NutDriverTransport.Usb],
            documentationUri: UsbHidDocumentation),
        new(
            "snmp-ups",
            "Ups.Driver.snmp-ups.Help",
            "Snmp",
            SnmpFields(),
            displayNameResourceKey: "Ups.Driver.snmp-ups.Name",
            descriptionResourceKey: "Ups.Driver.snmp-ups.Description",
            transports: [NutDriverTransport.Network, NutDriverTransport.Snmp],
            documentationUri: SnmpDocumentation)
    ];

    private static IEnumerable<NutConfigurationFieldDescriptor> BaseFields()
    {
        yield return Field("Ups.DriverPath", "driverpath", NutConfigurationFieldScope.Global, NutConfigurationFieldKind.Path, 10, required: false, group: "Ups.Group.Global");
        yield return Integer("Ups.MaxRetry", "maxretry", NutConfigurationFieldScope.Global, 0, int.MaxValue, 20, "Ups.Group.Global");
        yield return Integer("Ups.RetryDelay", "retrydelay", NutConfigurationFieldScope.Global, 0, int.MaxValue, 30, "Ups.Group.Global", "Ups.Unit.Seconds");
        yield return Field("Ups.Driver", "driver", NutConfigurationFieldScope.Section, NutConfigurationFieldKind.Choice, 100, required: true, group: "Ups.Group.Identity");
        yield return Field("Ups.Port", "port", NutConfigurationFieldScope.Section, NutConfigurationFieldKind.Text, 200, required: true, group: "Ups.Group.Connection");
        yield return Field("Ups.Description", "desc", NutConfigurationFieldScope.Section, NutConfigurationFieldKind.Text, 300, group: "Ups.Group.Identity");
        yield return Field("Ups.VendorId", "vendorid", NutConfigurationFieldScope.Section, NutConfigurationFieldKind.Text, 220,
            group: "Ups.Group.DeviceMatch", applicability: context => IsDriver(context, "nutdrv_qx", "usbhid-ups"));
        yield return Field("Ups.ProductId", "productid", NutConfigurationFieldScope.Section, NutConfigurationFieldKind.Text, 230,
            group: "Ups.Group.DeviceMatch", applicability: context => IsDriver(context, "nutdrv_qx", "usbhid-ups"));
        yield return Field("Ups.Vendor", "vendor", NutConfigurationFieldScope.Section, NutConfigurationFieldKind.Text, 240,
            group: "Ups.Group.DeviceMatch", advanced: true, applicability: context => IsDriver(context, "nutdrv_qx", "usbhid-ups"));
        yield return Field("Ups.Product", "product", NutConfigurationFieldScope.Section, NutConfigurationFieldKind.Text, 250,
            group: "Ups.Group.DeviceMatch", advanced: true, applicability: context => IsDriver(context, "nutdrv_qx", "usbhid-ups"));
        yield return Field("Ups.Serial", "serial", NutConfigurationFieldScope.Section, NutConfigurationFieldKind.Text, 260,
            group: "Ups.Group.DeviceMatch", applicability: context => IsDriver(context, "nutdrv_qx", "usbhid-ups"));
        yield return Integer("Ups.PollInterval", "pollinterval", NutConfigurationFieldScope.Section, 1, int.MaxValue, 400, "Ups.Group.Behavior", "Ups.Unit.Seconds");
        yield return Integer("Ups.MaxStartDelay", "maxstartdelay", NutConfigurationFieldScope.Section, 0, int.MaxValue, 410, "Ups.Group.Behavior", "Ups.Unit.Seconds");
        yield return Integer("Ups.PollFrequency", "pollfreq", NutConfigurationFieldScope.Section, 1, int.MaxValue, 415, "Ups.Group.Behavior", "Ups.Unit.Seconds", advanced: true,
            applicability: context => IsDriver(context, "nutdrv_qx", "usbhid-ups", "snmp-ups"));
        yield return Choice("Ups.Synchronous", "synchronous", ["yes", "no"], 420, "Ups.Group.Behavior", advanced: true);
        yield return Flag("Ups.IgnoreLowBattery", "ignorelb", 430, "Ups.Group.Battery", risky: true);
        yield return Decimal("Ups.OverrideBatteryChargeLow", "override.battery.charge.low", 0, 100, 440, "Ups.Group.Battery", "Ups.Unit.Percent", risky: true);
        yield return Integer("Ups.OverrideBatteryRuntimeLow", "override.battery.runtime.low", NutConfigurationFieldScope.Section, 0, int.MaxValue, 450, "Ups.Group.Battery", "Ups.Unit.Seconds", risky: true);
    }

    private static IEnumerable<NutConfigurationFieldDescriptor> NutdrvQxFields()
    {
        yield return Choice("Ups.Protocol", "protocol",
            ["bestups", "hunnox", "masterguard", "mecer", "megatec", "megatec/old", "mustek", "q1", "voltronic", "voltronic-qs", "voltronic-qs-hex", "zinto"],
            210, "Ups.Group.Connection", automatic: NutConfigurationAutomaticPolicy.OmitDirective);
        yield return Integer("Ups.OnDelay", "ondelay", NutConfigurationFieldScope.Section, 0, 599940, 460, "Ups.Group.Behavior", "Ups.Unit.Seconds", risky: true);
        yield return Integer("Ups.OffDelay", "offdelay", NutConfigurationFieldScope.Section, 0, 5940, 470, "Ups.Group.Behavior", "Ups.Unit.Seconds", risky: true);
        yield return Decimal("Ups.DefaultBatteryVoltageHigh", "default.battery.voltage.high", 0, decimal.MaxValue, 500, "Ups.Group.Battery", "Ups.Unit.Volts");
        yield return Decimal("Ups.DefaultBatteryVoltageLow", "default.battery.voltage.low", 0, decimal.MaxValue, 510, "Ups.Group.Battery", "Ups.Unit.Volts");
        yield return Decimal("Ups.DefaultBatteryVoltageNominal", "default.battery.voltage.nominal", 0, decimal.MaxValue, 520, "Ups.Group.Battery", "Ups.Unit.Volts");
        yield return Decimal("Ups.OverrideBatteryVoltageNominal", "override.battery.voltage.nominal", 0, decimal.MaxValue, 530, "Ups.Group.Battery", "Ups.Unit.Volts", risky: true);
        yield return Integer("Ups.OverrideBatteryPacks", "override.battery.packs", NutConfigurationFieldScope.Section, 1, int.MaxValue, 540, "Ups.Group.Battery", risky: true);
        yield return Flag("Ups.BatteryVoltageReportsOnePack", "battery_voltage_reports_one_pack", 550, "Ups.Group.Battery", advanced: true);
        yield return new(NutConfigurationFileKind.UpsConf, "Ups.RuntimeCalibration", NutConfigurationEntryKind.Assignment, "runtimecal",
            NutConfigurationFieldScope.Section, "Ups.Field.RuntimeCalibration.Label", "Ups.Field.RuntimeCalibration.Help",
            NutConfigurationFieldKind.Text, automaticPolicy: NutConfigurationAutomaticPolicy.OmitDirective, insertionOrder: 560,
            activation: NutConfigurationActivation.SeparateExplicitAction, codec: NutRuntimeCalibrationCodec.Instance,
            presentation: new("Ups.Group.Runtime", IsAdvanced: true, IsRisky: true, DocumentationUri: NutdrvQxDocumentation));
        yield return Integer("Ups.ChargeTime", "chargetime", NutConfigurationFieldScope.Section, 1, int.MaxValue, 570, "Ups.Group.Runtime", "Ups.Unit.Seconds", advanced: true);
        yield return Decimal("Ups.IdleLoad", "idleload", 0, 100, 580, "Ups.Group.Runtime", "Ups.Unit.Percent", advanced: true);
    }

    private static IEnumerable<NutConfigurationFieldDescriptor> UsbHidFields()
    {
        yield return Field("Ups.Bus", "bus", NutConfigurationFieldScope.Section, NutConfigurationFieldKind.Text, 270, group: "Ups.Group.DeviceMatch", advanced: true, risky: true);
        yield return Field("Ups.Device", "device", NutConfigurationFieldScope.Section, NutConfigurationFieldKind.Text, 280, group: "Ups.Group.DeviceMatch", advanced: true, risky: true);
        yield return Flag("Ups.PollOnly", "pollonly", 440, "Ups.Group.Behavior", advanced: true);
        yield return Integer("Ups.WaitBeforeReconnect", "waitbeforereconnect", NutConfigurationFieldScope.Section, 0, int.MaxValue, 450, "Ups.Group.Behavior", "Ups.Unit.Seconds", advanced: true);
        yield return Flag("Ups.WindowsHid", "winhid", 460, "Ups.Group.Connection", advanced: true);
    }

    private static IEnumerable<NutConfigurationFieldDescriptor> SnmpFields()
    {
        yield return Choice("Ups.SnmpMibs", "mibs", ["auto", "ietf", "mge", "apcc", "netvision", "pw", "pxgx_ups", "cyberpower"], 220, "Ups.Group.Connection", automatic: NutConfigurationAutomaticPolicy.ExplicitAutoToken, autoToken: "auto");
        yield return Choice("Ups.SnmpVersion", "snmp_version", ["v1", "v2c", "v3"], 230, "Ups.Group.Connection");
        yield return Sensitive("Ups.SnmpCommunity", "community", 240, "Ups.Group.Security");
        yield return Integer("Ups.SnmpRetries", "snmp_retries", NutConfigurationFieldScope.Section, 0, int.MaxValue, 250, "Ups.Group.Behavior");
        yield return Integer("Ups.SnmpTimeout", "snmp_timeout", NutConfigurationFieldScope.Section, 1, int.MaxValue, 260, "Ups.Group.Behavior", "Ups.Unit.Seconds");
        yield return Choice("Ups.SnmpSecurityLevel", "secLevel", ["noAuthNoPriv", "authNoPriv", "authPriv"], 270, "Ups.Group.Security");
        yield return Field("Ups.SnmpSecurityName", "secName", NutConfigurationFieldScope.Section, NutConfigurationFieldKind.Text, 280, group: "Ups.Group.Security");
        yield return Choice("Ups.SnmpAuthProtocol", "authProtocol", ["MD5", "SHA"], 290, "Ups.Group.Security", advanced: true);
        yield return Sensitive("Ups.SnmpAuthPassword", "authPassword", 300, "Ups.Group.Security");
        yield return Choice("Ups.SnmpPrivacyProtocol", "privProtocol", ["DES", "AES"], 310, "Ups.Group.Security", advanced: true);
        yield return Sensitive("Ups.SnmpPrivacyPassword", "privPassword", 320, "Ups.Group.Security");
    }

    private static NutConfigurationFieldDescriptor Field(
        string id, string name, NutConfigurationFieldScope scope, NutConfigurationFieldKind kind, int order,
        bool required = false, string group = "Ups.Group.Advanced", bool advanced = false, bool risky = false,
        NutConfigurationAutomaticPolicy automatic = NutConfigurationAutomaticPolicy.NotSupported, string? autoToken = null,
        INutConfigurationValueCodec? codec = null,
        Func<NutConfigurationSemanticContext, NutConfigurationApplicability>? applicability = null) =>
        new(NutConfigurationFileKind.UpsConf, id, NutConfigurationEntryKind.Assignment, name, scope,
            FieldResource(id, "Label"), FieldResource(id, "Help"), kind, required,
            automaticPolicy: automatic, explicitAutoToken: autoToken, insertionOrder: order,
            activation: NutConfigurationActivation.SeparateExplicitAction, codec: codec, applicability: applicability,
            presentation: new(group, IsAdvanced: advanced, IsRisky: risky, DocumentationUri: UpsConfDocumentation));

    private static NutConfigurationFieldDescriptor Integer(
        string id, string name, NutConfigurationFieldScope scope, int min, int max, int order, string group,
        string? unit = null, bool advanced = false, bool risky = false,
        Func<NutConfigurationSemanticContext, NutConfigurationApplicability>? applicability = null) =>
        Field(id, name, scope, NutConfigurationFieldKind.Integer, order, group: group, advanced: advanced, risky: risky,
            codec: NutConfigurationValueCodec.IntegerRange(min, max), applicability: applicability).WithPresentation(unit, min, max);

    private static NutConfigurationFieldDescriptor Decimal(
        string id, string name, decimal minExclusive, decimal maxInclusive, int order, string group,
        string? unit = null, bool advanced = false, bool risky = false) =>
        Field(id, name, NutConfigurationFieldScope.Section, NutConfigurationFieldKind.Decimal, order, group: group, advanced: advanced, risky: risky,
            codec: NutConfigurationValueCodec.DecimalRange(minExclusive, maxInclusive)).WithPresentation(unit, minExclusive, maxInclusive);

    private static NutConfigurationFieldDescriptor Choice(
        string id, string name, string[] choices, int order, string group, bool advanced = false,
        NutConfigurationAutomaticPolicy automatic = NutConfigurationAutomaticPolicy.NotSupported, string? autoToken = null) =>
        new(NutConfigurationFileKind.UpsConf, id, NutConfigurationEntryKind.Assignment, name, NutConfigurationFieldScope.Section,
            FieldResource(id, "Label"), FieldResource(id, "Help"), NutConfigurationFieldKind.Choice,
            automaticPolicy: automatic, explicitAutoToken: autoToken, insertionOrder: order,
            activation: NutConfigurationActivation.SeparateExplicitAction, codec: NutConfigurationValueCodec.Choice(choices),
            choices: choices.Select(value => new NutConfigurationChoice(value, $"Ups.Choice.{id[4..]}.{value}")).ToArray(),
            presentation: new(group, IsAdvanced: advanced, DocumentationUri: UpsConfDocumentation));

    private static NutConfigurationFieldDescriptor Flag(string id, string name, int order, string group, bool advanced = false, bool risky = false) =>
        new(NutConfigurationFileKind.UpsConf, id, NutConfigurationEntryKind.Directive, name, NutConfigurationFieldScope.Section,
            FieldResource(id, "Label"), FieldResource(id, "Help"), NutConfigurationFieldKind.Boolean,
            automaticPolicy: NutConfigurationAutomaticPolicy.OmitDirective, insertionOrder: order,
            activation: NutConfigurationActivation.SeparateExplicitAction, codec: NutConfigurationValueCodec.Choice(string.Empty),
            presentation: new(group, IsAdvanced: advanced, IsRisky: risky, DocumentationUri: UpsConfDocumentation));

    private static NutConfigurationFieldDescriptor Sensitive(string id, string name, int order, string group) =>
        new(NutConfigurationFileKind.UpsConf, id, NutConfigurationEntryKind.Assignment, name, NutConfigurationFieldScope.Section,
            FieldResource(id, "Label"), FieldResource(id, "Help"), NutConfigurationFieldKind.SecretChange,
            sensitive: true, insertionOrder: order, activation: NutConfigurationActivation.SeparateExplicitAction,
            presentation: new(group, IsAdvanced: true, DocumentationUri: SnmpDocumentation));

    private static NutConfigurationFieldDescriptor WithPresentation(
        this NutConfigurationFieldDescriptor descriptor,
        string? unit,
        decimal? minimum,
        decimal? maximum) => new(
            descriptor.FileKind, descriptor.SemanticId, descriptor.EntryKind, descriptor.Name, descriptor.Scope,
            descriptor.LabelResourceKey, descriptor.HelpResourceKey, descriptor.FieldKind, descriptor.Required, descriptor.Sensitive,
            descriptor.AutomaticPolicy, descriptor.ExplicitAutoToken, descriptor.InsertionOrder, descriptor.Activation,
            descriptor.Codec, descriptor.Applicability, descriptor.Choices,
            descriptor.Presentation is { } presentation
                ? presentation with { UnitResourceKey = unit, Minimum = minimum, Maximum = maximum }
                : null);

    private static NutConfigurationFieldDescriptor ForDriver(NutConfigurationFieldDescriptor descriptor, string driverId) => new(
        descriptor.FileKind, descriptor.SemanticId, descriptor.EntryKind, descriptor.Name, descriptor.Scope,
        descriptor.LabelResourceKey, descriptor.HelpResourceKey, descriptor.FieldKind, descriptor.Required, descriptor.Sensitive,
        descriptor.AutomaticPolicy, descriptor.ExplicitAutoToken, descriptor.InsertionOrder, descriptor.Activation,
        descriptor.Codec,
        context => IsDriver(context, driverId),
        descriptor.Choices, descriptor.Presentation);

    private static NutConfigurationApplicability IsDriver(NutConfigurationSemanticContext context, params string[] driverIds) =>
        context.SelectedDriver is not null && driverIds.Contains(context.SelectedDriver, StringComparer.OrdinalIgnoreCase)
            ? NutConfigurationApplicability.Applicable
            : NutConfigurationApplicability.Unsupported;

    private static string FieldResource(string semanticId, string suffix) =>
        $"Ups.Field.{(semanticId.StartsWith("Ups.", StringComparison.Ordinal) ? semanticId[4..] : semanticId)}.{suffix}";
}
