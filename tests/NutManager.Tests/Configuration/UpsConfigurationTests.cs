using System.Globalization;
using NutManager.Core.Configuration;
using NutManager.Core.Configuration.Semantic;
using NutManager.Core.Validation;
using Xunit;

namespace NutManager.Tests.Configuration;

public sealed class UpsConfigurationTests
{
    private readonly NutConfigurationParser _parser = new();

    [Fact]
    public void BuiltInCatalogHasOnlyDocumentedProductionDriversAndFiltersDeterministically()
    {
        var registry = NutConfigurationSchemaRegistry.CreateBuiltIn();
        var catalog = NutDriverCatalog.Create(registry, ["nutdrv_qx.exe", "customdriver.exe"], ["legacydriver"]);

        Assert.Equal(["nutdrv_qx", "snmp-ups", "usbhid-ups"], registry.DriverSchemas.Select(driver => driver.DriverId));
        Assert.True(catalog.Find("nutdrv_qx")!.IsInstalled);
        Assert.False(catalog.Find("usbhid-ups")!.IsInstalled);
        Assert.False(catalog.Find("legacydriver")!.HasStructuredOptions);
        Assert.Equal(["nutdrv_qx", "usbhid-ups"], catalog.Search(transport: NutDriverTransport.Usb).Select(driver => driver.DriverId));
        Assert.Single(catalog.Search("snmp"));
    }

    [Fact]
    public void NutdrvQxSchemaContainsDocumentedProtocolRuntimeAndBatteryOptions()
    {
        var schema = NutConfigurationSchemaRegistry.CreateBuiltIn().GetDriverSchema("nutdrv_qx")!;

        Assert.Contains("q1", schema.SupportedProtocols);
        Assert.Contains(schema.Fields, field => field.Name == "runtimecal" && field.Codec is NutRuntimeCalibrationCodec);
        Assert.Contains(schema.Fields, field => field.Name == "default.battery.voltage.high");
        Assert.Contains(schema.Fields, field => field.Name == "battery_voltage_reports_one_pack" && field.EntryKind == NutConfigurationEntryKind.Directive);
        Assert.DoesNotContain(schema.Fields, field => field.Name is "baudrate" or "parity");
    }

    [Fact]
    public void UsbAndSnmpSchemasDoNotExposeInapplicableOptions()
    {
        var registry = NutConfigurationSchemaRegistry.CreateBuiltIn();
        var usb = registry.GetDriverSchema("usbhid-ups")!;
        var snmp = registry.GetDriverSchema("snmp-ups")!;

        Assert.Contains(NutUpsConfigurationCatalog.CreateFileSchema().Fields, field =>
            field.Name == "vendorid" && field.Applicability(new("usbhid-ups")) == NutConfigurationApplicability.Applicable);
        Assert.Contains(usb.Fields, field => field.Name == "winhid");
        Assert.DoesNotContain(usb.Fields, field => field.Name == "runtimecal");
        Assert.Contains(snmp.Fields, field => field.Name == "snmp_version");
        Assert.Contains(snmp.Fields, field => field.Name == "authPassword" && field.Sensitive);
        Assert.DoesNotContain(snmp.Fields, field => field.Name == "protocol");
    }

    [Theory]
    [InlineData("240,100,720,50", 240, 100, 720, 50)]
    [InlineData("300,75,900,25", 300, 75, 900, 25)]
    public void RuntimeCalibrationRoundTripsOfficialFourValueSyntax(
        string text, long highSeconds, decimal highLoad, long lowSeconds, decimal lowLoad)
    {
        var result = NutRuntimeCalibration.Parse(text);

        Assert.True(result.IsValid);
        Assert.Equal(TimeSpan.FromSeconds(highSeconds), result.Value!.HighLoadRuntime);
        Assert.Equal(highLoad, result.Value.HighLoadPercentage);
        Assert.Equal(TimeSpan.FromSeconds(lowSeconds), result.Value.LowLoadRuntime);
        Assert.Equal(lowLoad, result.Value.LowLoadPercentage);
        Assert.Equal(text, result.Value.ToNutValue());
    }

    [Theory]
    [InlineData("0,100,720,50", "Runtimecal.Runtime.Positive")]
    [InlineData("240,50,720,50", "Runtimecal.Load.Order")]
    [InlineData("240,101,720,50", "Runtimecal.Load.Range")]
    [InlineData("240;100;720;50", "Runtimecal.Format")]
    public void RuntimeCalibrationRejectsUnsafeOrMalformedValues(string text, string code)
    {
        var result = NutRuntimeCalibration.Parse(text);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == code);
    }

    [Fact]
    public void RuntimeCalibrationSerializationIsCultureInvariant()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            var value = new NutRuntimeCalibration(TimeSpan.FromSeconds(240), 99.5m, TimeSpan.FromSeconds(720), 49.5m);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var pt = value.ToNutValue();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var en = value.ToNutValue();

            Assert.Equal("240,99.5,720,49.5", pt);
            Assert.Equal(pt, en);
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void PresenceFlagsAreParsedAndMutatedWithoutInventingAssignmentSyntax()
    {
        const string original = "[UPS]\n    driver = nutdrv_qx\n    port = COM4\n    ignorelb\n    future = keep\n";
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, original);
        var schema = NutUpsConfigurationCatalog.CreateFileSchema();
        using var draft = new NutConfigurationSemanticDraft(document, schema, new("nutdrv_qx"));

        Assert.Contains(document.Nodes.OfType<NutConfigurationDirectiveNode>(), node => node.Name == "ignorelb" && node.Arguments == string.Empty);
        Assert.True(draft.SetAutomatic("Ups.IgnoreLowBattery", section: "UPS").Succeeded);
        var serialized = draft.Materialize().Serialize();

        Assert.DoesNotContain("ignorelb", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("future = keep", serialized);
    }

    [Fact]
    public void DriverContextShowsOnlyApplicableOptionsAndPreservesOldDriverValues()
    {
        const string original = "[UPS]\ndriver = nutdrv_qx\nport = COM4\nprotocol = q1\nruntimecal = 240,100,720,50\nvendorid = 0463\n";
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, original);
        var schema = NutUpsConfigurationCatalog.CreateFileSchema();
        using var draft = new NutConfigurationSemanticDraft(document, schema, new("usbhid-ups"));

        Assert.Equal(NutConfigurationSemanticState.Unsupported,
            draft.Projection.Fields.Single(field => field.Descriptor.SemanticId == "Ups.Protocol" && field.Section == "UPS").State);
        Assert.Equal(NutConfigurationSemanticState.Explicit,
            draft.Projection.Fields.Single(field => field.Descriptor.SemanticId == "Ups.VendorId" && field.Section == "UPS").State);
        Assert.Equal(original, draft.Materialize().Serialize());
    }

    [Fact]
    public void DocumentValidationRequiresDriverAndPortAndRejectsUnsupportedRuntimeCalibration()
    {
        var registry = NutConfigurationSchemaRegistry.CreateBuiltIn();
        var catalog = NutDriverCatalog.Create(registry, ["usbhid-ups"]);
        var validator = new NutConfigurationSemanticValidator(documentRules: [new NutUpsConfigurationDocumentValidationRule(catalog)]);
        var schema = NutUpsConfigurationCatalog.CreateFileSchema();
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, "[UPS]\ndriver = usbhid-ups\nruntimecal = 240,100,720,50\n");
        var projection = new NutConfigurationSemanticProjector().Project(document, schema, new("usbhid-ups"));

        var result = validator.Validate(document, projection);

        Assert.Contains(result.Issues, issue => issue.Code == "Ups.port.Required");
        Assert.Contains(result.Issues, issue => issue.Code == "Ups.Runtimecal.Unsupported");
    }

    [Fact]
    public void IgnoreLowBatteryRequiresAnExplicitLowThreshold()
    {
        var registry = NutConfigurationSchemaRegistry.CreateBuiltIn();
        var validator = new NutConfigurationSemanticValidator(documentRules:
            [new NutUpsConfigurationDocumentValidationRule(NutDriverCatalog.Create(registry, ["nutdrv_qx"]))]);
        var schema = NutUpsConfigurationCatalog.CreateFileSchema();
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, "[UPS]\ndriver = nutdrv_qx\nport = COM4\nignorelb\n");
        var projection = new NutConfigurationSemanticProjector().Project(document, schema, new("nutdrv_qx"));

        Assert.Contains(validator.Validate(document, projection).Issues, issue => issue.Code == "Ups.IgnoreLb.ThresholdRequired");
    }

    [Theory]
    [InlineData("v3", "authNoPriv", false, true, "Ups.Snmp.AuthPasswordRequired")]
    [InlineData("v3", "authPriv", true, false, "Ups.Snmp.PrivPasswordRequired")]
    public void SnmpV3CrossFieldValidationRequiresDocumentedCredentials(
        string version, string level, bool authPassword, bool privacyPassword, string expectedCode)
    {
        var text = $"[UPS]\ndriver = snmp-ups\nport = 192.0.2.1\nsnmp_version = {version}\nsecLevel = {level}\nsecName = manager\n" +
            (authPassword ? "authPassword = SUPER_SECRET_123\n" : string.Empty) +
            (privacyPassword ? "privPassword = SUPER_SECRET_456\n" : string.Empty);
        var registry = NutConfigurationSchemaRegistry.CreateBuiltIn();
        var catalog = NutDriverCatalog.Create(registry, ["snmp-ups"]);
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, text);
        var schema = NutUpsConfigurationCatalog.CreateFileSchema();
        var projection = new NutConfigurationSemanticProjector().Project(document, schema, new("snmp-ups"));
        var result = new NutConfigurationSemanticValidator(documentRules: [new NutUpsConfigurationDocumentValidationRule(catalog)]).Validate(document, projection);

        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
        Assert.DoesNotContain("SUPER_SECRET", projection.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeEstimateDistinguishesMissingInvalidAndRealZero()
    {
        var missing = new NutRuntimeEstimate(null, NutRuntimeValueSource.Unavailable, null, null, null, null,
            NutRuntimeCalibrationState.NotConfigured, NutRuntimeConfidence.Unknown, ErrorCode: "Missing");
        var zero = new NutRuntimeEstimate(TimeSpan.Zero, NutRuntimeValueSource.ReportedByUps, DateTimeOffset.UnixEpoch, 0, null, null,
            NutRuntimeCalibrationState.Configured, NutRuntimeConfidence.High, RawValue: "0");

        Assert.False(missing.IsAvailable);
        Assert.True(zero.IsAvailable);
        Assert.Equal(TimeSpan.Zero, zero.Runtime);
    }

    [Fact]
    public void UnknownDocumentedDriverInputIsPreservedWithLimitedWarningWhileInvalidPathIsRejected()
    {
        var registry = NutConfigurationSchemaRegistry.CreateBuiltIn();
        var catalog = NutDriverCatalog.Create(registry);
        var validator = new NutConfigurationSemanticValidator(documentRules: [new NutUpsConfigurationDocumentValidationRule(catalog)]);
        var schema = NutUpsConfigurationCatalog.CreateFileSchema();
        var unknown = _parser.Parse(NutConfigurationFileKind.UpsConf, "[UPS]\ndriver = future-driver\nport = custom\n");
        var invalid = _parser.Parse(NutConfigurationFileKind.UpsConf, "[UPS]\ndriver = ..\\evil\nport = custom\n");

        Assert.Contains(validator.Validate(unknown, new NutConfigurationSemanticProjector().Project(unknown, schema, new("future-driver"))).Issues,
            issue => issue.Code == "Ups.Driver.Unverified" && issue.Severity == ValidationSeverity.Warning);
        Assert.Contains(validator.Validate(invalid, new NutConfigurationSemanticProjector().Project(invalid, schema, new("..\\evil"))).Issues,
            issue => issue.Code == "Ups.Driver.Invalid" && issue.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void NumericDescriptorsExposeDocumentedBoundsToPresentationMetadata()
    {
        var schema = NutUpsConfigurationCatalog.CreateFileSchema();
        var poll = schema.FindField("Ups.PollInterval")!;
        var charge = schema.FindField("Ups.OverrideBatteryChargeLow")!;

        Assert.Equal(1m, poll.Presentation!.Minimum);
        Assert.Equal(int.MaxValue, poll.Presentation.Maximum);
        Assert.Equal(0m, charge.Presentation!.Minimum);
        Assert.Equal(100m, charge.Presentation.Maximum);
    }
}
