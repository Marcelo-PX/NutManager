using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NutManager.Core.Configuration;
using NutManager.Core.Configuration.Semantic;
using NutManager.Core.Services;
using NutManager.Core.Validation;
using NutManager.Infrastructure.Configuration;
using NutManager.Infrastructure.Remote.Smb;
using NutManager.Infrastructure.Remote.Ssh;
using Xunit;

namespace NutManager.Tests.Configuration;

public sealed class SemanticConfigurationFrameworkTests
{
    private readonly NutConfigurationParser _parser = new();

    [Fact]
    public void BuiltInRegistryExposesRepresentativeSchemasWithoutInventedDefaults()
    {
        var registry = NutConfigurationSchemaRegistry.CreateBuiltIn();

        Assert.Equal(5, registry.FileSchemas.Count);
        Assert.Equal("driver", registry.GetField("Ups.Driver")?.Name);
        Assert.Equal(NutConfigurationFieldKind.SecretChange, registry.GetField("UpsdUsers.Password")?.FieldKind);
        Assert.Equal(["driverpath", "maxretry", "retrydelay"],
            registry.GetFields(NutConfigurationFileKind.UpsConf, NutConfigurationFieldScope.Global).Select(field => field.Name));
    }

    [Fact]
    public void RegistryRejectsDuplicateFileKindsAndSemanticIds()
    {
        var schema = Schema(NutConfigurationFileKind.NutConf, Field(NutConfigurationFileKind.NutConf, "A", "A"));
        Assert.Throws<ArgumentException>(() => new NutConfigurationSchemaRegistry([schema, schema]));
        var other = Schema(NutConfigurationFileKind.UpsdConf, Field(NutConfigurationFileKind.UpsdConf, "A", "LISTEN", NutConfigurationEntryKind.Directive));
        Assert.Throws<ArgumentException>(() => new NutConfigurationSchemaRegistry([schema, other]));
    }

    [Fact]
    public void FileSchemaRejectsConflictingDescriptorsForSameEntry()
    {
        Assert.Throws<ArgumentException>(() => Schema(NutConfigurationFileKind.NutConf,
            Field(NutConfigurationFileKind.NutConf, "A", "MODE"),
            Field(NutConfigurationFileKind.NutConf, "B", "mode")));
    }

    [Fact]
    public void DriverSchemaLookupIsCaseInsensitiveAndImmutableFromInput()
    {
        var fields = new List<NutConfigurationFieldDescriptor> { Field(NutConfigurationFileKind.UpsConf, "Ups.Port", "port", scope: NutConfigurationFieldScope.Section) };
        var registry = new NutConfigurationSchemaRegistry([], [new("nutdrv_qx", "Driver.Help", "Serial", fields, ["q1"])]);
        fields.Clear();

        Assert.Single(registry.GetDriverSchema("NUTDRV_QX")!.Fields);
    }

    [Fact]
    public void ProjectionDistinguishesExplicitMissingRequiredAutomaticAndExplicitAuto()
    {
        var schema = Schema(NutConfigurationFileKind.UpsConf,
            Field(NutConfigurationFileKind.UpsConf, "Required", "driver", required: true, scope: NutConfigurationFieldScope.Section),
            Field(NutConfigurationFileKind.UpsConf, "Omitted", "port", automatic: NutConfigurationAutomaticPolicy.OmitDirective, scope: NutConfigurationFieldScope.Section),
            Field(NutConfigurationFileKind.UpsConf, "AutoToken", "protocol", automatic: NutConfigurationAutomaticPolicy.ExplicitAutoToken, autoToken: "auto", scope: NutConfigurationFieldScope.Section));
        var projection = new NutConfigurationSemanticProjector().Project(_parser.Parse(NutConfigurationFileKind.UpsConf, "[ups]\nprotocol = auto\n"), schema);

        Assert.Equal(NutConfigurationSemanticState.MissingRequired, projection.Fields.Single(field => field.Descriptor.SemanticId == "Required").State);
        Assert.Equal(NutConfigurationSemanticState.AutomaticByOmission, projection.Fields.Single(field => field.Descriptor.SemanticId == "Omitted").State);
        Assert.Equal(NutConfigurationSemanticState.ExplicitAutoToken, projection.Fields.Single(field => field.Descriptor.SemanticId == "AutoToken").State);
        Assert.Contains(projection.Issues, issue => issue.Code == "Semantic.Required");
    }

    [Fact]
    public void ApplicabilityProjectsUnsupportedWithoutRemovingExistingValue()
    {
        var field = new NutConfigurationFieldDescriptor(NutConfigurationFileKind.NutConf, "Nut.Mode", NutConfigurationEntryKind.Assignment,
            "MODE", NutConfigurationFieldScope.Global, "Mode.Label", "Mode.Help", applicability: _ => NutConfigurationApplicability.Unsupported);
        var document = _parser.Parse(NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        var projection = new NutConfigurationSemanticProjector().Project(document, Schema(NutConfigurationFileKind.NutConf, field));

        Assert.Equal(NutConfigurationSemanticState.Unsupported, Assert.Single(projection.Fields).State);
        Assert.Equal("MODE=standalone\n", document.Serialize());
    }

    [Fact]
    public void DuplicateSingletonIsPreservedAndReportedAmbiguous()
    {
        var document = _parser.Parse(NutConfigurationFileKind.NutConf, "MODE=standalone\nMODE=netserver\n");
        var schema = Schema(NutConfigurationFileKind.NutConf, Field(NutConfigurationFileKind.NutConf, "Nut.Mode", "MODE"));
        var projection = new NutConfigurationSemanticProjector().Project(document, schema);

        Assert.Equal(2, projection.Fields.Count);
        Assert.Contains(projection.Issues, issue => issue.Code == "Semantic.DuplicateSingleton");
        using var draft = new NutConfigurationSemanticDraft(document, schema);
        Assert.Equal(NutConfigurationMutationStatus.AmbiguousTarget, draft.Set("Nut.Mode", "other").Status);
        Assert.Equal("MODE=standalone\nMODE=netserver\n", draft.Materialize().Serialize());
    }

    [Fact]
    public void UnknownRecognizedEntriesProjectAsCustomAndRawNodesRemainStructural()
    {
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, "# keep\nfuture_option = value\nVENDOR_EXTENSION foo bar\n[ups]\ndriver = nutdrv_qx\n");
        var schema = Schema(NutConfigurationFileKind.UpsConf,
            Field(NutConfigurationFileKind.UpsConf, "Ups.Driver", "driver", scope: NutConfigurationFieldScope.Section));
        var projection = new NutConfigurationSemanticProjector().Project(document, schema);

        Assert.Contains(projection.CustomParameters, parameter => parameter.Name == "future_option" && parameter.State == NutConfigurationSemanticState.CustomUnknown);
        Assert.DoesNotContain(projection.CustomParameters, parameter => parameter.Name == "VENDOR_EXTENSION");
        Assert.Contains(document.Nodes.OfType<NutRawNode>(), node => node.RawText == "VENDOR_EXTENSION foo bar");
    }

    [Fact]
    public void SetKnownFieldPreservesMessyUnknownContentCommentsQuotesAndWhitespace()
    {
        const string original = "# top\r\ndriverpath = \"C:/NUT/custom\"\r\nfuture = keep\r\n\r\n[UPS]\r\n# driver comment\r\n    driver   =   \"nutdrv_qx\"    \r\n    odd_key = 'unchanged'\r\n";
        const string expected = "# top\r\ndriverpath = \"C:/NUT/custom\"\r\nfuture = keep\r\n\r\n[UPS]\r\n# driver comment\r\n    driver   =   \"usbhid-ups\"    \r\n    odd_key = 'unchanged'\r\n";
        using var draft = new NutConfigurationSemanticDraft(_parser.Parse(NutConfigurationFileKind.UpsConf, original),
            Schema(NutConfigurationFileKind.UpsConf, Field(NutConfigurationFileKind.UpsConf, "Ups.Driver", "driver", scope: NutConfigurationFieldScope.Section)));

        Assert.True(draft.Set("Ups.Driver", "usbhid-ups", "UPS").Succeeded);
        Assert.Equal(expected, draft.Materialize().Serialize());
    }

    [Fact]
    public void DraftDoesNotShareMutableDocumentAndResetDiscardsChanges()
    {
        const string originalText = "MODE=standalone\n";
        var original = _parser.Parse(NutConfigurationFileKind.NutConf, originalText);
        using var draft = new NutConfigurationSemanticDraft(original,
            Schema(NutConfigurationFileKind.NutConf, Field(NutConfigurationFileKind.NutConf, "Nut.Mode", "MODE")));
        Assert.True(draft.Set("Nut.Mode", "netserver").Succeeded);
        Assert.Equal(originalText, original.Serialize());
        Assert.Equal("MODE=netserver\n", draft.Materialize().Serialize());
        Assert.Equal("MODE=netserver\n", draft.Materialize().Serialize());
        draft.Reset();
        Assert.False(draft.IsModified);
        Assert.Equal(originalText, draft.Materialize().Serialize());
    }

    [Theory]
    [InlineData("MODE=standalone", "MODE=netserver")]
    [InlineData("MODE=standalone\n", "MODE=netserver\n")]
    [InlineData("MODE=standalone\r\n", "MODE=netserver\r\n")]
    public void SetPreservesEofAndLineEnding(string original, string expected)
    {
        using var draft = NutConfDraft(original);
        Assert.True(draft.Set("Nut.Mode", "netserver").Succeeded);
        Assert.Equal(expected, draft.Materialize().Serialize());
    }

    [Fact]
    public void InsertPreservesNoFinalNewlineAndUsesPreferredOrderWithoutReorderingUnknown()
    {
        var schema = Schema(NutConfigurationFileKind.UpsConf,
            Field(NutConfigurationFileKind.UpsConf, "Ups.Driver", "driver", order: 100, scope: NutConfigurationFieldScope.Section),
            Field(NutConfigurationFileKind.UpsConf, "Ups.Port", "port", order: 200, scope: NutConfigurationFieldScope.Section),
            Field(NutConfigurationFileKind.UpsConf, "Ups.Desc", "desc", order: 300, scope: NutConfigurationFieldScope.Section));
        using var draft = new NutConfigurationSemanticDraft(_parser.Parse(NutConfigurationFileKind.UpsConf,
            "[ups]\r\ndriver = nutdrv_qx\r\nfuture = keep\r\ndesc = Rack"), schema);

        Assert.True(draft.Set("Ups.Port", "COM4", "ups").Succeeded);
        Assert.Equal("[ups]\r\ndriver = nutdrv_qx\r\nfuture = keep\r\nport = COM4\r\ndesc = Rack", draft.Materialize().Serialize());
    }

    [Fact]
    public void AutomaticPoliciesMaterializeDifferently()
    {
        var schema = Schema(NutConfigurationFileKind.UpsConf,
            Field(NutConfigurationFileKind.UpsConf, "Omit", "port", automatic: NutConfigurationAutomaticPolicy.OmitDirective, scope: NutConfigurationFieldScope.Section),
            Field(NutConfigurationFileKind.UpsConf, "Token", "protocol", automatic: NutConfigurationAutomaticPolicy.ExplicitAutoToken, autoToken: "auto", scope: NutConfigurationFieldScope.Section),
            Field(NutConfigurationFileKind.UpsConf, "Detected", "device", automatic: NutConfigurationAutomaticPolicy.DetectedAndPersisted, scope: NutConfigurationFieldScope.Section),
            Field(NutConfigurationFileKind.UpsConf, "None", "desc", automatic: NutConfigurationAutomaticPolicy.NotSupported, scope: NutConfigurationFieldScope.Section));
        using var draft = new NutConfigurationSemanticDraft(_parser.Parse(NutConfigurationFileKind.UpsConf,
            "[ups]\nport = COM4\nprotocol = q1\n"), schema);

        Assert.True(draft.SetAutomatic("Omit", section: "ups").Succeeded);
        Assert.True(draft.SetAutomatic("Token", section: "ups").Succeeded);
        Assert.Equal(NutConfigurationMutationStatus.ValidationFailed, draft.SetAutomatic("Detected", section: "ups").Status);
        Assert.True(draft.SetAutomatic("Detected", "COM5", "ups").Succeeded);
        Assert.Equal(NutConfigurationMutationStatus.UnsupportedOperation, draft.SetAutomatic("None", section: "ups").Status);
        Assert.Equal("[ups]\nprotocol = auto\ndevice = COM5\n", draft.Materialize().Serialize());
    }

    [Fact]
    public void OmitDirectiveAutomaticRemovesOnlyTheTargetedOccurrence()
    {
        var field = Field(NutConfigurationFileKind.UpsConf, "Port", "port", automatic: NutConfigurationAutomaticPolicy.OmitDirective,
            scope: NutConfigurationFieldScope.Section);
        using var draft = new NutConfigurationSemanticDraft(_parser.Parse(NutConfigurationFileKind.UpsConf, "[a]\nport = COM1\n[b]\nport = COM2\n"),
            Schema(NutConfigurationFileKind.UpsConf, field));
        Assert.True(draft.SetAutomatic("Port", section: "a").Succeeded);
        Assert.Equal("[a]\n[b]\nport = COM2\n", draft.Materialize().Serialize());
    }

    [Fact]
    public void ExplicitAutoTokenAutomaticPersistsDescriptorToken()
    {
        var field = Field(NutConfigurationFileKind.NutConf, "Mode", "MODE", automatic: NutConfigurationAutomaticPolicy.ExplicitAutoToken, autoToken: "auto");
        using var draft = new NutConfigurationSemanticDraft(_parser.Parse(NutConfigurationFileKind.NutConf, "MODE=standalone\n"),
            Schema(NutConfigurationFileKind.NutConf, field));
        Assert.True(draft.SetAutomatic("Mode").Succeeded);
        Assert.Equal("MODE=auto\n", draft.Materialize().Serialize());
        Assert.Equal(NutConfigurationSemanticState.ExplicitAutoToken, draft.Review.Changes.Single().NewState);
    }

    [Fact]
    public void DetectedAndPersistedAutomaticRequiresAndPersistsConcreteValue()
    {
        var field = Field(NutConfigurationFileKind.NutConf, "Detected", "VALUE", automatic: NutConfigurationAutomaticPolicy.DetectedAndPersisted);
        using var draft = new NutConfigurationSemanticDraft(_parser.Parse(NutConfigurationFileKind.NutConf, string.Empty), Schema(NutConfigurationFileKind.NutConf, field));
        Assert.Equal(NutConfigurationMutationStatus.ValidationFailed, draft.SetAutomatic("Detected").Status);
        Assert.True(draft.SetAutomatic("Detected", "detected-value").Succeeded);
        Assert.Equal("VALUE = detected-value", draft.Materialize().Serialize());
    }

    [Fact]
    public void NotSupportedAutomaticFailsWithoutMutation()
    {
        var field = Field(NutConfigurationFileKind.NutConf, "Manual", "VALUE");
        using var draft = new NutConfigurationSemanticDraft(_parser.Parse(NutConfigurationFileKind.NutConf, "VALUE=keep\n"), Schema(NutConfigurationFileKind.NutConf, field));
        Assert.Equal(NutConfigurationMutationStatus.UnsupportedOperation, draft.SetAutomatic("Manual").Status);
        Assert.Equal("VALUE=keep\n", draft.Materialize().Serialize());
    }

    [Fact]
    public void SectionAddRenameAndRemoveAreTargeted()
    {
        var schema = Schema(NutConfigurationFileKind.UpsConf, Field(NutConfigurationFileKind.UpsConf, "Ups.Driver", "driver", scope: NutConfigurationFieldScope.Section));
        using var draft = new NutConfigurationSemanticDraft(_parser.Parse(NutConfigurationFileKind.UpsConf,
            "# global\n[first]\n# comment\ndriver = one\nunknown = keep\n\n[last]\ndriver = two\n"), schema);

        Assert.True(draft.RenameSection("first", "renamed").Succeeded);
        Assert.True(draft.AddSection("third").Succeeded);
        Assert.True(draft.RemoveSection("last").Succeeded);
        Assert.Equal("# global\n[renamed]\n# comment\ndriver = one\nunknown = keep\n\n[third]\n", draft.Materialize().Serialize());
        Assert.Equal("renamed", draft.Materialize().Nodes.OfType<NutConfigurationAssignmentNode>().Single(node => node.Name == "driver").SectionName);
    }

    [Fact]
    public void FailedSectionRenameIsAtomic()
    {
        const string text = "[one]\nkey = 1\n[two]\nkey = 2\n";
        using var draft = new NutConfigurationSemanticDraft(_parser.Parse(NutConfigurationFileKind.UpsConf, text), Schema(NutConfigurationFileKind.UpsConf));

        Assert.Equal(NutConfigurationMutationStatus.Conflict, draft.RenameSection("one", "two").Status);
        Assert.Equal(text, draft.Materialize().Serialize());
        Assert.Equal(NutConfigurationMutationStatus.ValidationFailed, draft.RenameSection("one", "bad]\n[x").Status);
        Assert.Equal(text, draft.Materialize().Serialize());
    }

    [Theory]
    [InlineData("first")]
    [InlineData("middle")]
    [InlineData("last")]
    public void RemoveSectionHonorsFirstMiddleAndLastBoundaries(string target)
    {
        const string text = "# global\r\n[first]\r\na = 1\r\n[middle]\r\n# owned\r\nb = 2\r\n[last]\r\nc = 3";
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, text);
        var mutator = new NutConfigurationDocumentMutator(document);
        Assert.True(mutator.RemoveSection(target).Succeeded);
        Assert.DoesNotContain($"[{target}]", document.Serialize(), StringComparison.Ordinal);
        foreach (var retained in new[] { "first", "middle", "last" }.Where(name => name != target))
            Assert.Contains($"[{retained}]", document.Serialize(), StringComparison.Ordinal);
        Assert.StartsWith("# global\r\n", document.Serialize(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("value", "changed", "key   =   changed   \n")]
    [InlineData("\"value\"", "a \"quote\"", "key   =   \"a \\\"quote\\\"\"   \n")]
    [InlineData("'value'", "a 'quote'", "key   =   'a \\'quote\\''   \n")]
    [InlineData("\"C:\\\\NUT\\\\etc\"", "D:\\NUT\\etc", "key   =   \"D:\\\\NUT\\\\etc\"   \n")]
    public void SetPreservesQuoteStyleAndSpacing(string rawValue, string value, string expected)
    {
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, $"[ups]\nkey   =   {rawValue}   \n");
        var mutator = new NutConfigurationDocumentMutator(document);
        Assert.True(mutator.SetAssignment("key", value, "ups").Succeeded);
        Assert.Equal("[ups]\n" + expected, document.Serialize());
    }

    [Fact]
    public void KnownFieldLineInjectionIsRejectedAtomically()
    {
        using var draft = NutConfDraft("MODE=standalone\n");
        Assert.Equal(NutConfigurationMutationStatus.ValidationFailed, draft.Set("Nut.Mode", "netserver\nOTHER=bad").Status);
        Assert.Equal("MODE=standalone\n", draft.Materialize().Serialize());
    }

    [Fact]
    public void RepeatedRowsEditAndRemoveExactOccurrenceWithoutDeduplication()
    {
        var descriptor = Field(NutConfigurationFileKind.UpsdConf, "Upsd.Listen", "LISTEN", NutConfigurationEntryKind.Directive,
            scope: NutConfigurationFieldScope.Repeated);
        using var draft = new NutConfigurationSemanticDraft(_parser.Parse(NutConfigurationFileKind.UpsdConf,
            "LISTEN 127.0.0.1\nLISTEN 127.0.0.1\nFUTURE keep\n"), Schema(NutConfigurationFileKind.UpsdConf, descriptor));

        Assert.True(draft.Set("Upsd.Listen", "::1", occurrence: 1).Succeeded);
        Assert.True(draft.AddRepeated("Upsd.Listen", "192.0.2.1").Succeeded);
        Assert.True(draft.RemoveRepeated("Upsd.Listen", 0).Succeeded);
        Assert.Equal("LISTEN ::1\nLISTEN 192.0.2.1\nFUTURE keep\n", draft.Materialize().Serialize());
    }

    [Fact]
    public void RepeatedProjectionKeepsSectionAndStablePerSectionRowIdentity()
    {
        var descriptor = Field(NutConfigurationFileKind.UpsdUsers, "User.Actions", "actions", NutConfigurationEntryKind.Directive,
            scope: NutConfigurationFieldScope.Repeated);
        var projection = new NutConfigurationSemanticProjector().Project(
            _parser.Parse(NutConfigurationFileKind.UpsdUsers, "[one]\nactions SET\nactions FSD\n[two]\nactions SET\n"),
            Schema(NutConfigurationFileKind.UpsdUsers, descriptor));

        Assert.Collection(projection.Fields,
            field => { Assert.Equal("User.Actions:one:0", field.RowId); Assert.Equal("one", field.Section); },
            field => { Assert.Equal("User.Actions:one:1", field.RowId); Assert.Equal("one", field.Section); },
            field => { Assert.Equal("User.Actions:two:0", field.RowId); Assert.Equal("two", field.Section); });
    }

    [Fact]
    public void CustomParameterValidationRejectsInjectionAndPreservesLimitedWarning()
    {
        var valid = NutConfigurationSemanticValidator.ValidateCustomParameter(NutConfigurationEntryKind.Assignment, "vendor.option", "value", "ups");
        Assert.Single(valid);
        Assert.Equal(ValidationSeverity.Warning, valid[0].Severity);
        Assert.Contains(NutConfigurationSemanticValidator.ValidateCustomParameter(NutConfigurationEntryKind.Assignment, "bad\nname", "value", null),
            issue => issue.Severity == ValidationSeverity.Error);
        Assert.Contains(NutConfigurationSemanticValidator.ValidateCustomParameter(NutConfigurationEntryKind.Assignment, "name", "one\ntwo", null),
            issue => issue.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void CustomParameterMutationsAreScopedAndAtomic()
    {
        using var draft = new NutConfigurationSemanticDraft(_parser.Parse(NutConfigurationFileKind.UpsConf, "[ups]\nknown = keep\n"), Schema(NutConfigurationFileKind.UpsConf));
        Assert.True(draft.AddCustomParameter(NutConfigurationEntryKind.Assignment, "vendor", "one", "ups").Succeeded);
        Assert.True(draft.EditCustomParameter(NutConfigurationEntryKind.Assignment, "vendor", 0, "two", "ups").Succeeded);
        var beforeInvalid = draft.Materialize().Serialize();
        Assert.Equal(NutConfigurationMutationStatus.ValidationFailed,
            draft.AddCustomParameter(NutConfigurationEntryKind.Assignment, "escape", "x\nother = bad", "ups").Status);
        Assert.Equal(beforeInvalid, draft.Materialize().Serialize());
        Assert.True(draft.RemoveCustomParameter(NutConfigurationEntryKind.Assignment, "vendor", 0, "ups").Succeeded);
        Assert.Equal("[ups]\nknown = keep\n", draft.Materialize().Serialize());
    }

    [Fact]
    public void SensitiveProjectionAndReviewNeverExposeExistingOrReplacementSecret()
    {
        const string oldSecret = "SUPER_SECRET_123";
        const string newSecret = "SSH_SECRET_456";
        var descriptor = new NutConfigurationFieldDescriptor(NutConfigurationFileKind.UpsdUsers, "User.Password",
            NutConfigurationEntryKind.Assignment, "password", NutConfigurationFieldScope.Section, "Password.Label", "Password.Help",
            NutConfigurationFieldKind.SecretChange, sensitive: true);
        using var draft = new NutConfigurationSemanticDraft(_parser.Parse(NutConfigurationFileKind.UpsdUsers,
            $"[admin]\npassword = {oldSecret}\n"), Schema(NutConfigurationFileKind.UpsdUsers, descriptor));

        var projected = Assert.Single(draft.Projection.Fields);
        Assert.Null(projected.Value);
        Assert.Equal(NutSensitiveFieldState.Configured, projected.SensitiveState);
        using var replacement = new NutSensitiveValue(newSecret);
        Assert.Equal("<sensitive>", replacement.ToString());
        Assert.True(draft.ReplaceSensitive("User.Password", replacement, "admin").Succeeded);
        var reviewText = string.Join('|', draft.Review.Changes.Select(change => change.ToString()));
        Assert.DoesNotContain(oldSecret, reviewText, StringComparison.Ordinal);
        Assert.DoesNotContain(newSecret, reviewText, StringComparison.Ordinal);
        Assert.True(Assert.Single(draft.Review.Changes).Sensitive);
        Assert.Equal(NutSensitiveFieldState.ReplacementPending, Assert.Single(draft.Projection.Fields).SensitiveState);
    }

    [Fact]
    public void SensitiveGeneratedPreviewUsesExistingRedaction()
    {
        const string oldSecret = "SUPER_SECRET_123";
        const string newSecret = "SSH_SECRET_456";
        var document = _parser.Parse(NutConfigurationFileKind.UpsdUsers, $"[admin]\npassword = {oldSecret}\n");
        var descriptor = new NutConfigurationFieldDescriptor(NutConfigurationFileKind.UpsdUsers, "User.Password", NutConfigurationEntryKind.Assignment,
            "password", NutConfigurationFieldScope.Section, "Password.Label", "Password.Help", NutConfigurationFieldKind.SecretChange, sensitive: true);
        using var draft = new NutConfigurationSemanticDraft(document, Schema(NutConfigurationFileKind.UpsdUsers, descriptor));
        using var replacement = new NutSensitiveValue(newSecret);
        Assert.True(draft.ReplaceSensitive("User.Password", replacement, "admin").Succeeded);
        var snapshot = Snapshot("C:\\temp\\upsd.users", document, NutConfigurationTextEncoding.Utf8);

        var generated = NutConfigurationGeneratedPreviewFactory.Prepare(new NutConfigurationFilePipeline(), snapshot, draft);

        var display = string.Join('|', generated.PreparedChange.Preview.Lines.Select(line => $"{line.OriginalText}>{line.CandidateText}"));
        Assert.DoesNotContain(oldSecret, display, StringComparison.Ordinal);
        Assert.DoesNotContain(newSecret, display, StringComparison.Ordinal);
        Assert.Contains("<redacted>", display, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedPreviewShowsOnlyInsertedSemanticLineAndChangesFingerprint()
    {
        const string text = "[ups]\ndriver = nutdrv_qx\nfuture = keep\n";
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, text);
        var port = Field(NutConfigurationFileKind.UpsConf, "Ups.Port", "port", scope: NutConfigurationFieldScope.Section);
        using var draft = new NutConfigurationSemanticDraft(document, Schema(NutConfigurationFileKind.UpsConf, port));
        Assert.True(draft.Set("Ups.Port", "COM4", "ups").Succeeded);
        var snapshot = Snapshot("C:\\temp\\ups.conf", document, NutConfigurationTextEncoding.Utf8);

        var prepared = NutConfigurationGeneratedPreviewFactory.Prepare(new NutConfigurationFilePipeline(), snapshot, draft).PreparedChange;

        var line = Assert.Single(prepared.Preview.Lines);
        Assert.Equal(string.Empty, line.OriginalText);
        Assert.Equal("port = COM4", line.CandidateText);
        Assert.DoesNotContain("future = keep", line.CandidateText, StringComparison.Ordinal);
        Assert.NotEqual(snapshot.OriginalFingerprint, prepared.CandidateFingerprint);
        Assert.All(typeof(NutConfigurationChangePreview).GetProperties(), property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void FieldCrossFieldAndDocumentValidationStaySeparateAndCompose()
    {
        var document = _parser.Parse(NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        var projection = new NutConfigurationSemanticProjector().Project(document,
            Schema(NutConfigurationFileKind.NutConf, Field(NutConfigurationFileKind.NutConf, "Nut.Mode", "MODE")));
        var result = new NutConfigurationSemanticValidator([new FieldRule()], [new CrossRule()], [new DocumentRule()]).Validate(document, projection);

        Assert.Equal(["Cross", "Document", "Field"], result.Issues.Select(issue => issue.Code).Order().ToArray());
        Assert.True(result.HasErrors);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void TypedSerializationIsCultureInvariant(string culture)
    {
        using var scope = new CultureScope(culture);
        var field = new NutConfigurationFieldDescriptor(NutConfigurationFileKind.NutConf, "Number", NutConfigurationEntryKind.Assignment,
            "NUMBER", NutConfigurationFieldScope.Global, "Number.Label", "Number.Help", NutConfigurationFieldKind.Decimal,
            codec: NutConfigurationValueCodec.Decimal);
        using var draft = new NutConfigurationSemanticDraft(_parser.Parse(NutConfigurationFileKind.NutConf, "NUMBER=1.5\n"), Schema(NutConfigurationFileKind.NutConf, field));

        Assert.True(draft.Set("Number", 2.75m).Succeeded);
        Assert.Equal("NUMBER=2.75\n", draft.Materialize().Serialize());
    }

    [Theory]
    [InlineData(NutConfigurationTextEncoding.Utf8)]
    [InlineData(NutConfigurationTextEncoding.Utf8Bom)]
    [InlineData(NutConfigurationTextEncoding.Utf16LittleEndian)]
    [InlineData(NutConfigurationTextEncoding.Utf16BigEndian)]
    public async Task SemanticCandidatePreservesSnapshotEncodingThroughLocalPrepare(NutConfigurationTextEncoding encoding)
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "nut.conf");
            await File.WriteAllBytesAsync(path, Encode("MODE=standalone\r\n", encoding));
            var pipeline = new NutConfigurationFilePipeline();
            var load = await pipeline.LoadAsync(path, NutConfigurationFileKind.NutConf);
            using var draft = new NutConfigurationSemanticDraft(load.Snapshot!.Document,
                Schema(NutConfigurationFileKind.NutConf, Field(NutConfigurationFileKind.NutConf, "Nut.Mode", "MODE")));
            Assert.True(draft.Set("Nut.Mode", "netserver").Succeeded);

            var generated = NutConfigurationGeneratedPreviewFactory.Prepare(pipeline, load.Snapshot, draft);
            Assert.Equal(encoding, generated.PreparedChange.Snapshot.Encoding);
            Assert.Equal(Encode("MODE=netserver\r\n", encoding), generated.PreparedChange.CandidateBytes.ToArray());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LocalSemanticCandidateUsesExistingSafeWritePipeline()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "nut.conf");
            await File.WriteAllTextAsync(path, "MODE=standalone\n", new UTF8Encoding(false));
            var pipeline = new NutConfigurationFilePipeline();
            var load = await pipeline.LoadAsync(path, NutConfigurationFileKind.NutConf);
            using var draft = new NutConfigurationSemanticDraft(load.Snapshot!.Document,
                Schema(NutConfigurationFileKind.NutConf, Field(NutConfigurationFileKind.NutConf, "Nut.Mode", "MODE")));
            Assert.True(draft.Set("Nut.Mode", "netserver").Succeeded);
            var prepared = NutConfigurationGeneratedPreviewFactory.Prepare(pipeline, load.Snapshot, draft).PreparedChange;

            var applied = await pipeline.ApplyAsync(prepared);

            Assert.Equal(NutConfigurationApplyStatus.Success, applied.Status);
            Assert.Equal("MODE=netserver\n", await File.ReadAllTextAsync(path));
            Assert.NotNull(applied.BackupPath);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task UpsConfigurationCandidateUsesExistingLocalSafeWritePipeline()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "ups.conf");
            await File.WriteAllTextAsync(path, "[UPS]\r\ndriver = nutdrv_qx\r\nport = COM4\r\n", new UTF8Encoding(false));
            var pipeline = new NutConfigurationFilePipeline();
            var load = await pipeline.LoadAsync(path, NutConfigurationFileKind.UpsConf);
            using var draft = new NutConfigurationSemanticDraft(load.Snapshot!.Document,
                NutUpsConfigurationCatalog.CreateFileSchema(), new("nutdrv_qx"));
            Assert.True(draft.Set("Ups.Description", "Local rack", "UPS").Succeeded);

            var result = await pipeline.ApplyAsync(
                NutConfigurationGeneratedPreviewFactory.Prepare(pipeline, load.Snapshot, draft).PreparedChange);

            Assert.Equal(NutConfigurationApplyStatus.Success, result.Status);
            Assert.Contains("desc = \"Local rack\"", await File.ReadAllTextAsync(path));
            Assert.NotNull(result.BackupPath);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SemanticCandidateUsesExistingSftpOrSmbRemotePipeline(bool smb)
    {
        var directory = smb ? @"\\server\share\etc\nut" : "/etc/nut";
        IRemoteNutConfigurationPathPolicy policy = smb
            ? new SmbRemoteNutConfigurationPathPolicy(@"\\server\share")
            : SftpRemoteNutConfigurationPathPolicy.Instance;
        await using var session = new FakeRemoteSession(policy, directory);
        var target = policy.CombineDirectChild(directory, "nut.conf");
        session.SetFile(target, "MODE=standalone\n");
        var pipeline = new RemoteNutConfigurationFilePipeline(session, directory, true);
        var load = await pipeline.LoadAsync(target, NutConfigurationFileKind.NutConf);
        using var draft = new NutConfigurationSemanticDraft(load.Snapshot!.Document,
            Schema(NutConfigurationFileKind.NutConf, Field(NutConfigurationFileKind.NutConf, "Nut.Mode", "MODE")));
        Assert.True(draft.Set("Nut.Mode", "netserver").Succeeded);

        var result = await pipeline.ApplyAsync(NutConfigurationGeneratedPreviewFactory.Prepare(pipeline, load.Snapshot, draft).PreparedChange);

        Assert.Equal(NutConfigurationApplyStatus.Success, result.Status);
        Assert.Equal("MODE=netserver\n", session.GetText(target));
        Assert.Equal(1, session.CommitCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpsConfigurationCandidateUsesExistingSftpOrSmbRemotePipeline(bool smb)
    {
        var directory = smb ? @"\\server\share\etc\nut" : "/etc/nut";
        IRemoteNutConfigurationPathPolicy policy = smb
            ? new SmbRemoteNutConfigurationPathPolicy(@"\\server\share")
            : SftpRemoteNutConfigurationPathPolicy.Instance;
        await using var session = new FakeRemoteSession(policy, directory);
        var target = policy.CombineDirectChild(directory, "ups.conf");
        session.SetFile(target, "[UPS]\ndriver = nutdrv_qx\nport = COM4\n");
        var pipeline = new RemoteNutConfigurationFilePipeline(session, directory, true);
        var load = await pipeline.LoadAsync(target, NutConfigurationFileKind.UpsConf);
        using var draft = new NutConfigurationSemanticDraft(load.Snapshot!.Document,
            NutUpsConfigurationCatalog.CreateFileSchema(), new("nutdrv_qx"));
        Assert.True(draft.Set("Ups.Description", "Remote rack", "UPS").Succeeded);

        var result = await pipeline.ApplyAsync(
            NutConfigurationGeneratedPreviewFactory.Prepare(pipeline, load.Snapshot, draft).PreparedChange);

        Assert.Equal(NutConfigurationApplyStatus.Success, result.Status);
        Assert.Contains("desc = \"Remote rack\"", session.GetText(target));
        Assert.Equal(1, session.CommitCalls);
    }

    [Theory]
    [InlineData(NutConfigurationFileKind.NutConf, "# x\r\nMODE=standalone")]
    [InlineData(NutConfigurationFileKind.UpsConf, "[ups]\ndriver = nutdrv_qx\nfuture = keep\n")]
    [InlineData(NutConfigurationFileKind.UpsdConf, "LISTEN 127.0.0.1\r\nFUTURE x\r\n")]
    [InlineData(NutConfigurationFileKind.UpsdUsers, "[user]\npassword = secret\nactions = SET")]
    [InlineData(NutConfigurationFileKind.UpsmonConf, "MONITOR ups@localhost 1 user pass primary\nMINSUPPLIES 1\n")]
    public void AllFileKindsRemainExactWithoutMutation(NutConfigurationFileKind kind, string text)
    {
        var document = _parser.Parse(kind, text);
        using var draft = new NutConfigurationSemanticDraft(document, Schema(kind));
        Assert.Equal(text, draft.Materialize().Serialize());
    }

    [Fact]
    public void ReviewOrderingOperationsContextAndActivationAreDeterministic()
    {
        var driver = new NutConfigurationFieldDescriptor(NutConfigurationFileKind.UpsConf, "Ups.Driver", NutConfigurationEntryKind.Assignment,
            "driver", NutConfigurationFieldScope.Section, "Driver.Label", "Driver.Help", activation: NutConfigurationActivation.ServiceRestart);
        using var draft = new NutConfigurationSemanticDraft(_parser.Parse(NutConfigurationFileKind.UpsConf, "[ups]\ndriver = old\n"),
            Schema(NutConfigurationFileKind.UpsConf, driver));
        Assert.True(draft.Set("Ups.Driver", "new", "ups").Succeeded);
        Assert.True(draft.AddCustomParameter(NutConfigurationEntryKind.Assignment, "future", "value", "ups").Succeeded);

        Assert.Equal([NutConfigurationSemanticChangeOperation.Set, NutConfigurationSemanticChangeOperation.AddCustomParameter],
            draft.Review.Changes.Select(change => change.Operation).ToArray());
        Assert.Equal("Driver.Label", draft.Review.Changes[0].LabelResourceKey);
        Assert.Equal("ups", draft.Review.Changes[0].Section);
        Assert.Equal(NutConfigurationActivation.ServiceRestart, draft.Review.Changes[0].Activation);
    }

    private NutConfigurationSemanticDraft NutConfDraft(string text) => new(
        _parser.Parse(NutConfigurationFileKind.NutConf, text),
        Schema(NutConfigurationFileKind.NutConf, Field(NutConfigurationFileKind.NutConf, "Nut.Mode", "MODE")));

    private static NutConfigurationFileSchema Schema(NutConfigurationFileKind kind, params NutConfigurationFieldDescriptor[] fields) =>
        new(kind, fields, kind is NutConfigurationFileKind.UpsConf or NutConfigurationFileKind.UpsdUsers ? new("Section", "Section.Label") : null);

    private static NutConfigurationFieldDescriptor Field(
        NutConfigurationFileKind fileKind,
        string id,
        string name,
        NutConfigurationEntryKind entry = NutConfigurationEntryKind.Assignment,
        bool required = false,
        NutConfigurationAutomaticPolicy automatic = NutConfigurationAutomaticPolicy.NotSupported,
        string? autoToken = null,
        int order = 0,
        NutConfigurationFieldScope scope = NutConfigurationFieldScope.Global) =>
        new(fileKind, id, entry, name, scope, $"{id}.Label", $"{id}.Help", required: required,
            automaticPolicy: automatic, explicitAutoToken: autoToken, insertionOrder: order);

    private static NutConfigurationFileSnapshot Snapshot(string path, NutConfigurationDocument document, NutConfigurationTextEncoding encoding)
    {
        var bytes = Encode(document.OriginalText, encoding);
        return new(path, document.FileKind, document, encoding, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
    }

    private static byte[] Encode(string text, NutConfigurationTextEncoding encoding)
    {
        Encoding actual = encoding switch
        {
            NutConfigurationTextEncoding.Utf8 => new UTF8Encoding(false),
            NutConfigurationTextEncoding.Utf8Bom => new UTF8Encoding(true),
            NutConfigurationTextEncoding.Utf16LittleEndian => new UnicodeEncoding(false, true),
            NutConfigurationTextEncoding.Utf16BigEndian => new UnicodeEncoding(true, true),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };
        var content = actual.GetBytes(text);
        var preamble = actual.GetPreamble();
        return preamble.Concat(content).ToArray();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"NutManager-T25-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FieldRule : INutConfigurationFieldValidationRule
    {
        public IReadOnlyList<NutConfigurationSemanticIssue> Validate(NutConfigurationSemanticField field) =>
            [new("Field", ValidationSeverity.Info, "Field.Resource", field.Descriptor.SemanticId)];
    }

    private sealed class CrossRule : INutConfigurationCrossFieldValidationRule
    {
        public IReadOnlyList<NutConfigurationSemanticIssue> Validate(NutConfigurationSemanticProjection projection) =>
            [new("Cross", ValidationSeverity.Warning, "Cross.Resource", projection.FileKind.ToString())];
    }

    private sealed class DocumentRule : INutConfigurationDocumentValidationRule
    {
        public IReadOnlyList<NutConfigurationSemanticIssue> Validate(NutConfigurationDocument document, NutConfigurationSemanticProjection projection) =>
            [new("Document", ValidationSeverity.Error, "Document.Resource", document.FileKind.ToString())];
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
        public CultureScope(string name)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        }
        public void Dispose() { CultureInfo.CurrentCulture = _culture; CultureInfo.CurrentUICulture = _uiCulture; }
    }

    private sealed class FakeRemoteSession : IRemoteNutConfigurationSession
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly string _directory;
        public FakeRemoteSession(IRemoteNutConfigurationPathPolicy policy, string directory)
        {
            PathPolicy = policy;
            _directory = policy.NormalizeDirectory(directory);
            _files = new(policy is SmbRemoteNutConfigurationPathPolicy ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }
        public RemoteNutPlatform Platform => RemoteNutPlatform.Windows;
        public bool IsSafeWriteCapabilityValidFor(string configurationDirectory) => PathPolicy.PathsEqual(configurationDirectory, _directory);
        public string HomeDirectory => _directory;
        public IRemoteNutConfigurationPathPolicy PathPolicy { get; }
        public int CommitCalls { get; private set; }
        public void SetFile(string path, string text) => _files[PathPolicy.NormalizePath(path)] = Encoding.UTF8.GetBytes(text);
        public string GetText(string path) => Encoding.UTF8.GetString(_files[PathPolicy.NormalizePath(path)]);
        public Task<RemoteNutDirectoryListing> BrowseDirectoryAsync(string directory, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteNutDirectoryListing(directory, PathPolicy.GetParentDirectory(directory), []));
        public Task<RemoteNutDirectoryValidationResult> ValidateConfigurationDirectoryAsync(string directory, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteNutDirectoryValidationResult(RemoteNutTransportStatus.Success, directory, RemoteNutConfigurationFiles.AllNames));
        public Task<RemoteNutFileReadResult> ReadFileAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(_files.TryGetValue(PathPolicy.NormalizePath(path), out var bytes)
                ? new RemoteNutFileReadResult(RemoteNutTransportStatus.Success, bytes)
                : new RemoteNutFileReadResult(RemoteNutTransportStatus.NotFound));
        public Task<RemoteNutWriteCapabilityResult> ProbeSafeWriteCapabilityAsync(string directory, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteNutWriteCapabilityResult(true, RemoteNutPlatform.Windows));
        public void InvalidateSafeWriteCapability() { }
        public Task<RemoteNutFileReadResult> UploadCandidateAsync(RemoteNutCandidateUploadRequest request, CancellationToken cancellationToken = default)
        {
            var path = PathPolicy.CombineDirectChild(request.ConfigurationDirectory, request.TemporaryFileName);
            _files[path] = request.CandidateBytes.ToArray();
            return Task.FromResult(new RemoteNutFileReadResult(RemoteNutTransportStatus.Success, _files[path]));
        }
        public Task<RemoteNutTemporaryCleanupResult> DeleteGeneratedTemporaryFileAsync(string configurationDirectory, string temporaryFileName, CancellationToken cancellationToken = default)
        {
            _files.Remove(PathPolicy.CombineDirectChild(configurationDirectory, temporaryFileName));
            return Task.FromResult(new RemoteNutTemporaryCleanupResult(RemoteNutTransportStatus.Success));
        }
        public Task<RemoteNutCommitResult> CommitConfigurationAsync(RemoteNutConfigurationCommitRequest request, CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            var target = PathPolicy.CombineDirectChild(request.ConfigurationDirectory, request.TargetFileName);
            var temp = PathPolicy.CombineDirectChild(request.ConfigurationDirectory, request.TemporaryFileName);
            var backup = PathPolicy.CombineDirectChild(request.ConfigurationDirectory, request.BackupFileName);
            _files[backup] = _files[target];
            _files[target] = _files[temp];
            _files.Remove(temp);
            return Task.FromResult(new RemoteNutCommitResult(RemoteNutTransportStatus.Success, backup));
        }
        public Task<RemoteNutCommitResult> RollbackConfigurationAsync(RemoteNutConfigurationRollbackRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteNutCommitResult(RemoteNutTransportStatus.Failed));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
