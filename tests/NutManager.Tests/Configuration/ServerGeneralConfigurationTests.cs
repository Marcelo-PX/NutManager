using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NutManager.App.Localization;
using NutManager.Core.Configuration;
using NutManager.Core.Configuration.Semantic;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Core.Validation;
using NutManager.Infrastructure.Configuration;
using NutManager.Infrastructure.Remote.Smb;
using NutManager.Infrastructure.Remote.Ssh;
using Xunit;

namespace NutManager.Tests.Configuration;

public sealed class ServerGeneralConfigurationTests
{
    private readonly NutConfigurationParser _parser = new();

    [Fact]
    public void NutConfProductionSchemaUsesReleaseModesAndRequiredMissingState()
    {
        var schema = NutServerConfigurationCatalog.CreateNutConfSchema();
        var mode = schema.FindField("Nut.Mode")!;
        var projection = new NutConfigurationSemanticProjector().Project(_parser.Parse(NutConfigurationFileKind.NutConf, string.Empty), schema);

        Assert.Equal(["none", "standalone", "netserver", "netclient"], mode.Choices.Select(choice => choice.TechnicalValue));
        Assert.True(mode.Required);
        Assert.Equal(NutConfigurationSemanticState.MissingRequired, projection.Fields.Single(field => field.Descriptor == mode).State);
        Assert.Contains(projection.Issues, issue => issue.Code == "Semantic.Required");
    }

    [Theory]
    [InlineData("")]
    [InlineData("# keep\r\nUNKNOWN=value")]
    [InlineData("# keep\n\nUNKNOWN=value\n")]
    public void InsertingNutConfModeUsesOfficialGrammarAndPreservesSurroundings(string original)
    {
        using var draft = Draft(NutConfigurationFileKind.NutConf, original, NutServerConfigurationCatalog.CreateNutConfSchema());

        Assert.True(draft.Set("Nut.Mode", "standalone").Succeeded);
        var result = draft.Materialize().Serialize();

        Assert.Contains("MODE=standalone", result, StringComparison.Ordinal);
        Assert.DoesNotContain("MODE =", result, StringComparison.Ordinal);
        Assert.Contains(original.Trim(), result, StringComparison.Ordinal);
        Assert.Equal(result, _parser.Parse(NutConfigurationFileKind.NutConf, result).Serialize());
    }

    [Fact]
    public void NutConfWhitespaceAroundEqualsIsPreservedAsRawInsteadOfSilentlyNormalized()
    {
        const string text = "MODE = standalone\nMODE=netclient\n";
        var document = _parser.Parse(NutConfigurationFileKind.NutConf, text);

        Assert.IsType<NutRawNode>(document.Nodes[0]);
        Assert.Single(document.FindAssignments("MODE"));
        Assert.Equal(text, document.Serialize());
    }

    [Fact]
    public void NutDebugSyslogPreservesDocumentedOtherValuesWithoutInventingAClosedEnum()
    {
        using var draft = Draft(NutConfigurationFileKind.NutConf,
            "MODE=standalone\nNUT_DEBUG_SYSLOG=package-specific\n", NutServerConfigurationCatalog.CreateNutConfSchema());

        var field = draft.Projection.Fields.Single(item => item.Descriptor.SemanticId == "Nut.DebugSyslog");

        Assert.Equal("package-specific", field.Value);
        Assert.False(draft.Validation.HasErrors);
        Assert.Equal("MODE=standalone\nNUT_DEBUG_SYSLOG=package-specific\n", draft.Materialize().Serialize());
    }

    [Fact]
    public void ModeTechnicalTokensAreExactAndInvalidCaseIsPreservedForExplicitCorrection()
    {
        const string text = "MODE=STANDALONE\n";
        using var draft = Draft(NutConfigurationFileKind.NutConf, text, NutServerConfigurationCatalog.CreateNutConfSchema());

        Assert.True(draft.Validation.HasErrors);
        Assert.Equal(text, draft.Materialize().Serialize());
        Assert.True(draft.Set("Nut.Mode", "standalone").Succeeded);
        Assert.Equal("MODE=standalone\n", draft.Materialize().Serialize());
    }

    [Fact]
    public void ExistingUnquotedNutConfAssignmentGainsMinimumRequiredQuotesWhenValueNeedsThem()
    {
        using var draft = Draft(NutConfigurationFileKind.NutConf, "MODE=standalone\nUPSD_OPTIONS=old\n",
            NutServerConfigurationCatalog.CreateNutConfSchema());

        Assert.True(draft.Set("Nut.UpsdOptions", "-D -F").Succeeded);

        Assert.Equal("MODE=standalone\nUPSD_OPTIONS=\"-D -F\"\n", draft.Materialize().Serialize());
    }

    [Fact]
    public void ExistingNutConfLexicalStyleAndUnknownContentRemainUntouched()
    {
        const string original = "# comment\r\nMODE=standalone\r\nFUTURE=\"keep me\"\r\nNUT_DEBUG_LEVEL=0";
        using var draft = Draft(NutConfigurationFileKind.NutConf, original, NutServerConfigurationCatalog.CreateNutConfSchema());

        Assert.True(draft.Set("Nut.Mode", "netserver").Succeeded);

        Assert.Equal("# comment\r\nMODE=netserver\r\nFUTURE=\"keep me\"\r\nNUT_DEBUG_LEVEL=0", draft.Materialize().Serialize());
        Assert.Contains(draft.Projection.CustomParameters, parameter => parameter.Name == "FUTURE");
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("NO", false)]
    [InlineData("off", false)]
    [InlineData("0", false)]
    public void BooleanAliasesProjectSemanticallyAndChangedValuesUseCanonicalTokens(string token, bool expected)
    {
        var schema = NutServerConfigurationCatalog.CreateNutConfSchema();
        using var draft = Draft(NutConfigurationFileKind.NutConf, $"MODE=standalone\nALLOW_NO_DEVICE={token}\n", schema);

        Assert.Equal(expected, draft.Projection.Fields.Single(field => field.Descriptor.SemanticId == "Nut.AllowNoDevice").Value);
        Assert.True(draft.Set("Nut.AllowNoDevice", !expected).Succeeded);
        Assert.Contains($"ALLOW_NO_DEVICE={(!expected ? "true" : "false")}", draft.Materialize().Serialize());
    }

    [Theory]
    [InlineData("none")]
    [InlineData("standalone")]
    [InlineData("netserver")]
    [InlineData("netclient")]
    public void EveryReleaseModeSerializesCultureInvariant(string mode)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            string? first = null;
            foreach (var culture in new[] { "pt-BR", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                using var draft = Draft(NutConfigurationFileKind.NutConf, "MODE=none\n", NutServerConfigurationCatalog.CreateNutConfSchema());
                Assert.True(draft.Set("Nut.Mode", mode).Succeeded);
                first ??= draft.Materialize().Serialize();
                Assert.Equal(first, draft.Materialize().Serialize());
            }
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Theory]
    [InlineData("127.0.0.1", null)]
    [InlineData("127.0.0.1", 3493)]
    [InlineData("::1", null)]
    [InlineData("::1", 3493)]
    [InlineData("*", null)]
    [InlineData("*", 3493)]
    [InlineData("server.example", 65535)]
    public void ListenCodecSupportsReleaseAddressGrammarAndOptionalPort(string address, int? port)
    {
        var endpoint = new NutUpsdListenEndpoint(address, port);
        var serialized = NutUpsdListenEndpoint.Codec.Serialize(endpoint, "Upsd.Listen");
        var parsed = NutUpsdListenEndpoint.Parse(serialized.Value!);

        Assert.True(serialized.IsValid);
        Assert.Equal(endpoint, parsed.Value);
    }

    [Theory]
    [InlineData("host 0")]
    [InlineData("host 65536")]
    [InlineData("host -1")]
    [InlineData("host text")]
    [InlineData("host 3493 extra")]
    [InlineData("bad host 3493")]
    [InlineData("host\n3493")]
    [InlineData("\"unterminated")]
    public void ListenCodecRejectsInvalidPortArgumentsQuotesAndInjection(string input)
    {
        Assert.False(NutUpsdListenEndpoint.Parse(input).IsValid);
    }

    [Fact]
    public void ListenMutationsTargetExactOccurrencesAndPreserveCommentsDuplicatesAndFormatting()
    {
        const string text = "# primary\r\nLISTEN    127.0.0.1    3493\r\n# network\r\nLISTEN 192.168.1.10 3493\r\nLISTEN 192.168.1.10 3493";
        using var draft = Draft(NutConfigurationFileKind.UpsdConf, text, NutServerConfigurationCatalog.CreateUpsdConfSchema());

        Assert.True(draft.EditRepeated("Upsd.Listen", 1, new NutUpsdListenEndpoint("::1", null)).Succeeded);
        Assert.True(draft.AddRepeated("Upsd.Listen", new NutUpsdListenEndpoint("*", 3493)).Succeeded);
        Assert.True(draft.RemoveRepeated("Upsd.Listen", 0).Succeeded);

        Assert.Equal("# primary\r\n# network\r\nLISTEN ::1\r\nLISTEN 192.168.1.10 3493\r\nLISTEN * 3493", draft.Materialize().Serialize());
        Assert.Equal([NutConfigurationSemanticChangeOperation.EditRepeatedRow, NutConfigurationSemanticChangeOperation.AddRepeatedRow,
            NutConfigurationSemanticChangeOperation.RemoveRepeatedRow], draft.Review.Changes.Select(change => change.Operation));
    }

    [Fact]
    public void RepeatedRowIdentityRemainsDeterministicAcrossDraftMaterialization()
    {
        using var draft = Draft(NutConfigurationFileKind.UpsdConf, "LISTEN A\nLISTEN B\nLISTEN C\n",
            NutServerConfigurationCatalog.CreateUpsdConfSchema());
        var ids = draft.Projection.Fields.Where(field => field.Descriptor.SemanticId == "Upsd.Listen").Select(field => field.RowId).ToArray();

        Assert.True(draft.EditRepeated("Upsd.Listen", 1, new NutUpsdListenEndpoint("B.example", null)).Succeeded);
        Assert.Equal(ids, draft.Projection.Fields.Where(field => field.Descriptor.SemanticId == "Upsd.Listen").Select(field => field.RowId));
        Assert.Contains("LISTEN B.example", draft.Materialize().Serialize());
    }

    [Fact]
    public void StableRepeatedRowIdentityTargetsTheSameNodeAfterEarlierRowRemoval()
    {
        using var draft = Draft(NutConfigurationFileKind.UpsdConf, "LISTEN A\nLISTEN B\nLISTEN C\n",
            NutServerConfigurationCatalog.CreateUpsdConfSchema());
        var rows = draft.Projection.Fields.Where(field => field.Descriptor.SemanticId == "Upsd.Listen").ToArray();

        Assert.True(draft.EditRepeated("Upsd.Listen", rows[1].StableRowId!, new NutUpsdListenEndpoint("B.example", null)).Succeeded);
        Assert.True(draft.RemoveRepeated("Upsd.Listen", rows[0].StableRowId!).Succeeded);
        Assert.True(draft.EditRepeated("Upsd.Listen", rows[2].StableRowId!, new NutUpsdListenEndpoint("C.example", null)).Succeeded);

        Assert.Equal("LISTEN B.example\nLISTEN C.example\n", draft.Materialize().Serialize());
    }

    [Fact]
    public void EditingBRemovingAThenEditingCStillTargetsTheIntendedRows()
    {
        using var draft = Draft(NutConfigurationFileKind.UpsdConf, "LISTEN A\nLISTEN B\nLISTEN C\n",
            NutServerConfigurationCatalog.CreateUpsdConfSchema());

        Assert.True(draft.EditRepeated("Upsd.Listen", 1, new NutUpsdListenEndpoint("B.example", null)).Succeeded);
        Assert.True(draft.RemoveRepeated("Upsd.Listen", 0).Succeeded);
        Assert.True(draft.EditRepeated("Upsd.Listen", 1, new NutUpsdListenEndpoint("C.example", null)).Succeeded);

        Assert.Equal("LISTEN B.example\nLISTEN C.example\n", draft.Materialize().Serialize());
    }

    [Fact]
    public void WildcardProducesWarningWithoutDnsOrSocketIo()
    {
        var schema = NutServerConfigurationCatalog.CreateUpsdConfSchema();
        var document = _parser.Parse(NutConfigurationFileKind.UpsdConf, "LISTEN * 3493\n");
        var projection = new NutConfigurationSemanticProjector().Project(document, schema);
        var validation = new NutConfigurationSemanticValidator(documentRules: [new NutServerConfigurationDocumentValidationRule()]).Validate(document, projection);

        Assert.Contains(validation.Issues, issue => issue.Code == "Upsd.Listen.Wildcard" && issue.Severity == ValidationSeverity.Warning);
        Assert.DoesNotContain(typeof(NutUpsdListenEndpoint).Assembly.GetReferencedAssemblies(), assembly => assembly.Name == "System.Net.NameResolution");
    }

    [Fact]
    public void ServerProductionDescriptorsParseProjectMutateAndRemoveDeterministically()
    {
        const string text = "MAXAGE 15\nTRACKINGDELAY 3600\nALLOW_NO_DEVICE yes\nALLOW_NOT_ALL_LISTENERS no\nSTATEPATH \"C:\\NUT state\"\nMAXCONN 64\nCERTFILE server.pem\nCERTPATH certdb\nCERTREQUEST REQUEST\nDISABLE_WEAK_SSL on\nDEBUG_MIN 0\n";
        var schema = NutServerConfigurationCatalog.CreateUpsdConfSchema();
        using var draft = Draft(NutConfigurationFileKind.UpsdConf, text, schema);

        Assert.False(draft.Validation.HasErrors);
        Assert.True(draft.Set("Upsd.MaxAge", 30).Succeeded);
        Assert.True(draft.Set("Upsd.CertificateRequest", "2").Succeeded);
        Assert.True(draft.SetAutomatic("Upsd.DebugMinimum").Succeeded);
        var candidate = draft.Materialize().Serialize();

        Assert.Contains("MAXAGE 30", candidate);
        Assert.Contains("CERTREQUEST 2", candidate);
        Assert.DoesNotContain("DEBUG_MIN", candidate);
        Assert.Contains("STATEPATH \"C:\\NUT state\"", candidate);
    }

    [Fact]
    public void DirectiveTokenizerPreservesWindowsBackslashesAndQuotedEscapes()
    {
        var path = NutDirectiveArgumentTokenizer.Tokenize("\"C:\\NUT state\"");
        var escaped = NutDirectiveArgumentTokenizer.Tokenize("\"name \\\"quoted\\\" and \\\\ path\"");

        Assert.True(path.IsValid);
        Assert.Equal(@"C:\NUT state", path.Tokens.Single());
        Assert.True(escaped.IsValid);
        Assert.Equal("name \"quoted\" and \\ path", escaped.Tokens.Single());
    }

    [Fact]
    public void SensitiveDirectiveQuotingUsesClearableMemoryAndRoundTrips()
    {
        const string secret = "secret \\\"quoted\\\"";
        var quoted = NutDirectiveArgumentTokenizer.QuoteSensitive(secret.AsSpan());
        try
        {
            var parsed = NutDirectiveArgumentTokenizer.Tokenize(new string(quoted));
            Assert.True(parsed.IsValid);
            Assert.Equal(secret, parsed.Tokens.Single());
        }
        finally { Array.Clear(quoted); }

        Assert.All(quoted, character => Assert.Equal('\0', character));
    }

    [Fact]
    public void CertificateIdentityIsChangeOnlyAndNeverEscapesReviewOrPreview()
    {
        const string oldSecret = "T27_OLD_SECRET_CERT_123";
        const string newSecret = "T27_SUPER_SECRET_CERT_123";
        var text = $"CERTIDENT \"server identity\" {oldSecret}\n";
        var document = _parser.Parse(NutConfigurationFileKind.UpsdConf, text);
        var schema = NutServerConfigurationCatalog.CreateUpsdConfSchema();
        using var draft = new NutConfigurationSemanticDraft(document, schema);
        using var sensitive = new NutSensitiveValue($"\"replacement\" {newSecret}".AsSpan());

        Assert.Null(draft.Projection.Fields.Single(field => field.Descriptor.SemanticId == "Upsd.CertificateIdentity").Value);
        Assert.True(draft.ReplaceSensitive("Upsd.CertificateIdentity", sensitive).Succeeded);
        var generated = NutConfigurationGeneratedPreviewFactory.Prepare(new NutConfigurationFilePipeline(), Snapshot(document), draft);

        Assert.DoesNotContain(oldSecret, draft.Projection.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(newSecret, draft.Review.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(newSecret, generated.PreparedChange.Preview.ToString(), StringComparison.Ordinal);
        Assert.All(generated.PreparedChange.Preview.Lines, line => { Assert.DoesNotContain(oldSecret, line.ToString(), StringComparison.Ordinal); Assert.DoesNotContain(newSecret, line.ToString(), StringComparison.Ordinal); });
        Assert.Contains(newSecret, draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateSingletonIsPreservedAndMutationFailsAtomicallyWhileListenDuplicatesRemainRepeated()
    {
        const string text = "MAXAGE 15\nMAXAGE 30\nLISTEN 127.0.0.1\nLISTEN 127.0.0.1\n";
        using var draft = Draft(NutConfigurationFileKind.UpsdConf, text, NutServerConfigurationCatalog.CreateUpsdConfSchema());

        Assert.Contains(draft.Validation.Issues, issue => issue.Code == "Semantic.DuplicateSingleton");
        Assert.Equal(NutConfigurationMutationStatus.AmbiguousTarget, draft.Set("Upsd.MaxAge", 45).Status);
        Assert.Equal(text, draft.Materialize().Serialize());
        Assert.Equal(2, draft.Projection.Fields.Count(field => field.Descriptor.SemanticId == "Upsd.Listen"));
    }

    [Fact]
    public void DuplicateModeAssignmentsArePreservedAndNotSilentlyResolvedToFirstOrLast()
    {
        const string text = "MODE=standalone\nMODE=netserver\n";
        using var draft = Draft(NutConfigurationFileKind.NutConf, text, NutServerConfigurationCatalog.CreateNutConfSchema());

        Assert.Contains(draft.Validation.Issues, issue => issue.Code == "Semantic.DuplicateSingleton" && issue.SemanticTarget == "Nut.Mode");
        Assert.Equal(NutConfigurationMutationStatus.AmbiguousTarget, draft.Set("Nut.Mode", "netclient").Status);
        Assert.Equal(text, draft.Materialize().Serialize());
        Assert.Equal(2, draft.Projection.Fields.Count(field => field.Descriptor.SemanticId == "Nut.Mode"));
    }

    [Fact]
    public void UnknownAssignmentsDirectivesCommentsAndLineEndingsSurviveKnownChanges()
    {
        const string nut = "# keep\r\nMODE=standalone\r\nVENDOR=value";
        const string upsd = "# keep\nFUTURE one two\nLISTEN    ::1\n";
        using var nutDraft = Draft(NutConfigurationFileKind.NutConf, nut, NutServerConfigurationCatalog.CreateNutConfSchema());
        using var upsdDraft = Draft(NutConfigurationFileKind.UpsdConf, upsd, NutServerConfigurationCatalog.CreateUpsdConfSchema());

        Assert.True(nutDraft.Set("Nut.Mode", "netserver").Succeeded);
        Assert.True(upsdDraft.EditRepeated("Upsd.Listen", 0, new NutUpsdListenEndpoint("::", null)).Succeeded);

        Assert.Equal("# keep\r\nMODE=netserver\r\nVENDOR=value", nutDraft.Materialize().Serialize());
        Assert.Equal("# keep\nFUTURE one two\nLISTEN    ::\n", upsdDraft.Materialize().Serialize());
    }

    [Theory]
    [InlineData(NutConfigurationFileKind.NutConf)]
    [InlineData(NutConfigurationFileKind.UpsdConf)]
    public async Task LocalSemanticCandidatesUseExistingBackupFingerprintAndSafeReplacement(NutConfigurationFileKind kind)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"NutManager-T27-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var fileName = kind == NutConfigurationFileKind.NutConf ? "nut.conf" : "upsd.conf";
            var path = Path.Combine(directory, fileName);
            await File.WriteAllTextAsync(path, kind == NutConfigurationFileKind.NutConf ? "MODE=standalone\n" : "LISTEN 127.0.0.1\n", new UTF8Encoding(false));
            var pipeline = new NutConfigurationFilePipeline();
            var loaded = await pipeline.LoadAsync(path, kind);
            using var draft = new NutConfigurationSemanticDraft(loaded.Snapshot!.Document,
                kind == NutConfigurationFileKind.NutConf ? NutServerConfigurationCatalog.CreateNutConfSchema() : NutServerConfigurationCatalog.CreateUpsdConfSchema());
            Assert.True(kind == NutConfigurationFileKind.NutConf
                ? draft.Set("Nut.Mode", "netserver").Succeeded
                : draft.AddRepeated("Upsd.Listen", new NutUpsdListenEndpoint("::1", null)).Succeeded);

            var result = await pipeline.ApplyAsync(NutConfigurationGeneratedPreviewFactory.Prepare(pipeline, loaded.Snapshot, draft).PreparedChange);

            Assert.Equal(NutConfigurationApplyStatus.Success, result.Status);
            Assert.NotNull(result.BackupPath);
            Assert.True(File.Exists(result.BackupPath));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(NutConfigurationFileKind.NutConf)]
    [InlineData(NutConfigurationFileKind.UpsdConf)]
    public async Task ExternalChangeBlocksSemanticApplyWithoutOverwrite(NutConfigurationFileKind kind)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"NutManager-T27-Race-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var fileName = kind == NutConfigurationFileKind.NutConf ? "nut.conf" : "upsd.conf";
            var original = kind == NutConfigurationFileKind.NutConf ? "MODE=standalone\n" : "LISTEN 127.0.0.1\n";
            var external = kind == NutConfigurationFileKind.NutConf ? "MODE=none\n" : "LISTEN *\n";
            var path = Path.Combine(directory, fileName);
            await File.WriteAllTextAsync(path, original, new UTF8Encoding(false));
            var pipeline = new NutConfigurationFilePipeline();
            var loaded = await pipeline.LoadAsync(path, kind);
            using var draft = new NutConfigurationSemanticDraft(loaded.Snapshot!.Document,
                kind == NutConfigurationFileKind.NutConf ? NutServerConfigurationCatalog.CreateNutConfSchema() : NutServerConfigurationCatalog.CreateUpsdConfSchema());
            Assert.True(kind == NutConfigurationFileKind.NutConf
                ? draft.Set("Nut.Mode", "netserver").Succeeded
                : draft.AddRepeated("Upsd.Listen", new NutUpsdListenEndpoint("::1", null)).Succeeded);
            var prepared = NutConfigurationGeneratedPreviewFactory.Prepare(pipeline, loaded.Snapshot, draft).PreparedChange;
            await File.WriteAllTextAsync(path, external, new UTF8Encoding(false));

            var result = await pipeline.ApplyAsync(prepared);

            Assert.Equal(NutConfigurationApplyStatus.ChangedExternally, result.Status);
            Assert.Equal(external, await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false, NutConfigurationFileKind.NutConf)]
    [InlineData(true, NutConfigurationFileKind.NutConf)]
    [InlineData(false, NutConfigurationFileKind.UpsdConf)]
    [InlineData(true, NutConfigurationFileKind.UpsdConf)]
    public async Task SemanticCandidatesUseExistingSftpAndSmbSafePipelines(bool smb, NutConfigurationFileKind kind)
    {
        var directory = smb ? @"\\server\share\etc\nut" : "/etc/nut";
        IRemoteNutConfigurationPathPolicy policy = smb
            ? new SmbRemoteNutConfigurationPathPolicy(@"\\server\share")
            : SftpRemoteNutConfigurationPathPolicy.Instance;
        await using var session = new FakeRemoteSession(policy, directory);
        var fileName = kind == NutConfigurationFileKind.NutConf ? "nut.conf" : "upsd.conf";
        var target = policy.CombineDirectChild(directory, fileName);
        session.SetFile(target, kind == NutConfigurationFileKind.NutConf ? "MODE=standalone\n" : "LISTEN 127.0.0.1\n");
        var pipeline = new RemoteNutConfigurationFilePipeline(session, directory, true);
        var load = await pipeline.LoadAsync(target, kind);
        using var draft = new NutConfigurationSemanticDraft(load.Snapshot!.Document,
            kind == NutConfigurationFileKind.NutConf ? NutServerConfigurationCatalog.CreateNutConfSchema() : NutServerConfigurationCatalog.CreateUpsdConfSchema());
        Assert.True(kind == NutConfigurationFileKind.NutConf
            ? draft.Set("Nut.Mode", "netserver").Succeeded
            : draft.AddRepeated("Upsd.Listen", new NutUpsdListenEndpoint("::1", null)).Succeeded);

        var result = await pipeline.ApplyAsync(NutConfigurationGeneratedPreviewFactory.Prepare(pipeline, load.Snapshot, draft).PreparedChange);

        Assert.Equal(NutConfigurationApplyStatus.Success, result.Status);
        Assert.Equal(1, session.CommitCalls);
    }

    [Fact]
    public void EveryProductionDescriptorHasLocalizedLabelAndHelpWithExactCultureParity()
    {
        var schemas = new[] { NutServerConfigurationCatalog.CreateNutConfSchema(), NutServerConfigurationCatalog.CreateUpsdConfSchema() };
        var pt = NutManagerLocalizer.GetAvailableKeys(UiLanguagePreference.PtBr);
        var en = NutManagerLocalizer.GetAvailableKeys(UiLanguagePreference.EnUs);
        Assert.Equal(pt.OrderBy(value => value, StringComparer.Ordinal), en.OrderBy(value => value, StringComparer.Ordinal));
        foreach (var descriptor in schemas.SelectMany(schema => schema.Fields))
            foreach (var language in new[] { UiLanguagePreference.PtBr, UiLanguagePreference.EnUs })
            {
                var localizer = new NutManagerLocalizer(language);
                Assert.NotEqual(descriptor.LabelResourceKey, localizer.Get(descriptor.LabelResourceKey));
                Assert.NotEqual(descriptor.HelpResourceKey, localizer.Get(descriptor.HelpResourceKey));
            }
    }

    private NutConfigurationSemanticDraft Draft(NutConfigurationFileKind kind, string text, NutConfigurationFileSchema schema) =>
        new(_parser.Parse(kind, text), schema, validator: kind == NutConfigurationFileKind.UpsdConf
            ? new NutConfigurationSemanticValidator(documentRules: [new NutServerConfigurationDocumentValidationRule()]) : null);

    private static NutConfigurationFileSnapshot Snapshot(NutConfigurationDocument document)
    {
        var bytes = Encoding.UTF8.GetBytes(document.OriginalText);
        return new("C:\\temp\\upsd.conf", document.FileKind, document, NutConfigurationTextEncoding.Utf8,
            Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
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
            var temporary = PathPolicy.CombineDirectChild(request.ConfigurationDirectory, request.TemporaryFileName);
            var backup = PathPolicy.CombineDirectChild(request.ConfigurationDirectory, request.BackupFileName);
            _files[backup] = _files[target];
            _files[target] = _files[temporary];
            _files.Remove(temporary);
            return Task.FromResult(new RemoteNutCommitResult(RemoteNutTransportStatus.Success, backup));
        }
        public Task<RemoteNutCommitResult> RollbackConfigurationAsync(RemoteNutConfigurationRollbackRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteNutCommitResult(RemoteNutTransportStatus.Failed));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
