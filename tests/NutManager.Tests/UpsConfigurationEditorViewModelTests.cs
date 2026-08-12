using NutManager.App.Localization;
using NutManager.App.ViewModels;
using NutManager.Core.Administration;
using NutManager.Core.Configuration;
using NutManager.Core.Configuration.Semantic;
using NutManager.Core.Models;
using Xunit;

namespace NutManager.Tests;

public sealed class UpsConfigurationEditorViewModelTests
{
    [Fact]
    public void LoadsExistingUpsAndDriverAwareFieldsWithoutChangingTheDocument()
    {
        const string text = "# keep\n[NOBREAK]\ndriver = nutdrv_qx\nport = COM4\nprotocol = q1\nfuture_option = preserve\n";
        using var editor = CreateEditor(text, ["nutdrv_qx"]);

        Assert.Equal("NOBREAK", editor.SelectedSection);
        Assert.Equal("nutdrv_qx", editor.SelectedDriver);
        Assert.Contains(editor.Fields, field => field.Descriptor.SemanticId == "Ups.Protocol");
        Assert.Contains(editor.CustomParameters, parameter => parameter.Name == "future_option");
        Assert.False(editor.HasChanges);
        Assert.Equal(text, editor.Draft.Materialize().Serialize());
    }

    [Fact]
    public void AddRenameAndRemoveSectionAreExplicitAndAtomic()
    {
        using var editor = CreateEditor("[UPS]\ndriver = nutdrv_qx\nport = COM4\n", ["nutdrv_qx"]);
        editor.NewSectionName = "SECOND";
        editor.AddSectionCommand.Execute(null);

        Assert.Contains("SECOND", editor.Sections);
        editor.RenameSectionName = "RENAMED";
        editor.RenameSectionCommand.Execute(null);
        Assert.Contains("RENAMED", editor.Sections);
        Assert.DoesNotContain("SECOND", editor.Sections);
        editor.RemoveSectionCommand.Execute(null);
        Assert.DoesNotContain("RENAMED", editor.Sections);
        Assert.Contains("[UPS]", editor.Draft.Materialize().Serialize());
    }

    [Fact]
    public void RuntimeAssistantChangesOnlyDraftAndNeverStartsANutOperation()
    {
        using var editor = CreateEditor("[UPS]\ndriver = nutdrv_qx\nport = COM4\n", ["nutdrv_qx"]);
        editor.RuntimeHighSeconds = 240;
        editor.RuntimeHighLoad = 100;
        editor.RuntimeLowSeconds = 720;
        editor.RuntimeLowLoad = 50;

        editor.UseRuntimeCalibrationCommand.Execute(null);

        Assert.True(editor.HasChanges);
        Assert.Contains("runtimecal = 240,100,720,50", editor.Draft.Materialize().Serialize());
        Assert.DoesNotContain(editor.GetType().GetConstructors().SelectMany(constructor => constructor.GetParameters()),
            parameter => parameter.ParameterType.Name.Contains("Process", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LocalComMetadataIsOfferedButNoPortIsOpened()
    {
        using var editor = new UpsConfigurationEditorViewModel(
            Snapshot("[UPS]\ndriver = nutdrv_qx\nport = COM4\n"),
            ["nutdrv_qx"],
            [new NutComPortInfo("COM4", "USB Serial", "Vendor", "ID", "OK", 0, true)],
            new NutManagerLocalizer(UiLanguagePreference.PtBr));

        Assert.Single(editor.ComPortOptions);
        Assert.Equal("COM4", editor.ComPortOptions[0].PortName);
        Assert.DoesNotContain(editor.GetType().GetMethods(), method => method.Name.Contains("OpenPort", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("nutdrv_qx", "COM4", true)]
    [InlineData("usbhid-ups", "auto", true)]
    [InlineData("snmp-ups", "COM4", false)]
    public void PortSuggestionsRespectDriverTransport(string driver, string expectedChoice, bool expected)
    {
        using var editor = new UpsConfigurationEditorViewModel(
            Snapshot($"[UPS]\ndriver = {driver}\nport = configured\n"),
            [driver],
            [new NutComPortInfo("COM4", "USB Serial", "Vendor", "ID", "OK", 0, true)],
            new NutManagerLocalizer(UiLanguagePreference.PtBr));

        var port = editor.Fields.Single(field => field.Descriptor.SemanticId == "Ups.Port");
        Assert.Equal(expected, port.Choices.Any(choice => choice.TechnicalValue == expectedChoice));
        Assert.True(port.AllowsTechnicalInput);
    }

    [Fact]
    public void InstalledUnknownDriverRemainsSelectableWithLimitedSchema()
    {
        using var editor = CreateEditor("[UPS]\ndriver = vendor_driver\nport = custom\n", ["vendor_driver"]);

        var driver = Assert.Single(editor.DriverOptions, option => option.DriverId == "vendor_driver");
        Assert.True(driver.IsInstalled);
        Assert.False(driver.HasStructuredOptions);
        Assert.Contains(editor.ValidationIssues, issue => issue.Message.Contains("limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemoteContextDoesNotClaimAConfiguredDriverWasNotDetectedLocally()
    {
        using var editor = new UpsConfigurationEditorViewModel(
            Snapshot("[UPS]\ndriver = nutdrv_qx\nport = COM4\n"),
            installedDriverNames: null,
            comPorts: [],
            new NutManagerLocalizer(UiLanguagePreference.PtBr));

        Assert.Contains("não se aplica", editor.SelectedDriverAvailability, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(editor.Validation.Issues, issue => issue.Code == "Ups.Driver.NotDetected");
    }

    [Fact]
    public void ReviewUsesExistingPipelineAndKeepsGeneratedTextReadOnly()
    {
        using var editor = CreateEditor("[UPS]\ndriver = nutdrv_qx\nport = COM4\n", ["nutdrv_qx"]);
        var description = editor.Fields.Single(field => field.Descriptor.SemanticId == "Ups.Description");
        description.DraftValue = "Rack UPS";
        var pipeline = new PreviewPipeline();

        var generated = editor.Prepare(pipeline);

        Assert.Equal(1, pipeline.PrepareCalls);
        Assert.Contains("desc = \"Rack UPS\"", generated.PreparedChange.CandidateText);
        Assert.True(generated.SemanticReview.HasChanges);
        Assert.DoesNotContain(typeof(SemanticConfigurationReviewViewModel).GetProperties(), property => property.CanWrite);
    }

    [Fact]
    public void InvalidTypedInputBlocksReviewAndDoesNotPartiallyMutateTheDraft()
    {
        using var editor = CreateEditor("[UPS]\ndriver = nutdrv_qx\nport = COM4\npollinterval = 5\n", ["nutdrv_qx"]);
        var description = editor.Fields.Single(field => field.Descriptor.SemanticId == "Ups.Description");
        description.DraftValue = "Rack";
        var beforeInvalid = editor.Draft.Materialize().Serialize();
        var poll = editor.Fields.Single(field => field.Descriptor.SemanticId == "Ups.PollInterval");

        poll.DraftValue = "not-an-integer";

        Assert.True(editor.HasInputErrors);
        Assert.False(editor.CanReview);
        Assert.Equal(beforeInvalid, editor.Draft.Materialize().Serialize());
        poll.DraftValue = "6";
        Assert.False(editor.HasInputErrors);
        Assert.True(editor.CanReview);
    }

    [Fact]
    public void SensitiveReplacementIsChangeOnlyAndNeverExposesSecretInPresentation()
    {
        const string secret = "SUPER_SECRET_123";
        using var editor = CreateEditor("[UPS]\ndriver = snmp-ups\nport = 192.0.2.1\nsnmp_version = v3\nsecLevel = authNoPriv\nsecName = manager\nauthPassword = old-secret\n", ["snmp-ups"]);
        var password = editor.Fields.Single(field => field.Descriptor.SemanticId == "Ups.SnmpAuthPassword");

        password.ReplaceSensitive(secret.AsSpan());

        Assert.True(editor.HasChanges);
        Assert.DoesNotContain(secret, password.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, editor.ValidationIssues.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(editor.GetType().GetProperties(), property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        var review = editor.Draft.Review.Changes.Single(change => change.SemanticId == "Ups.SnmpAuthPassword");
        Assert.True(review.Sensitive);
        Assert.DoesNotContain(secret, review.ToString(), StringComparison.Ordinal);
        Assert.Contains(secret, editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void BasicAndAdvancedModesProjectWithoutDeletingUnsupportedDriverOptions()
    {
        const string text = "[UPS]\ndriver = nutdrv_qx\nport = COM4\nruntimecal = 240,100,720,50\n";
        using var editor = CreateEditor(text, ["nutdrv_qx"]);

        Assert.NotEmpty(editor.BasicFields);
        Assert.Contains(editor.AdvancedFields, field => field.Descriptor.SemanticId == "Ups.RuntimeCalibration");
        Assert.False(editor.ShowAdvanced);
        editor.ShowAdvanced = true;
        Assert.Equal(text, editor.Draft.Materialize().Serialize());
    }

    [Fact]
    public void EveryProductionDescriptorHasLocalizedLabelAndHelpInBothOfficialCultures()
    {
        var schema = NutUpsConfigurationCatalog.CreateFileSchema();
        foreach (var language in new[] { UiLanguagePreference.PtBr, UiLanguagePreference.EnUs })
        {
            var localizer = new NutManagerLocalizer(language);
            foreach (var field in schema.Fields)
            {
                Assert.NotEqual(field.LabelResourceKey, localizer.Get(field.LabelResourceKey));
                Assert.NotEqual(field.HelpResourceKey, localizer.Get(field.HelpResourceKey));
            }
        }
    }

    private static UpsConfigurationEditorViewModel CreateEditor(string text, IReadOnlyList<string> installed) => new(
        Snapshot(text), installed, [], new NutManagerLocalizer(UiLanguagePreference.PtBr));

    private static NutConfigurationFileSnapshot Snapshot(string text) => new(
        "C:\\NUT\\etc\\ups.conf", NutConfigurationFileKind.UpsConf,
        new NutConfigurationParser().Parse(NutConfigurationFileKind.UpsConf, text),
        NutConfigurationTextEncoding.Utf8, "original", text.Length);

    private sealed class PreviewPipeline : INutConfigurationFilePipeline
    {
        public int PrepareCalls { get; private set; }
        public Task<NutConfigurationLoadResult> LoadAsync(string targetPath, NutConfigurationFileKind fileKind, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public NutConfigurationPreparedChange Prepare(NutConfigurationFileSnapshot snapshot)
        {
            PrepareCalls++;
            var text = snapshot.Document.Serialize();
            return new(snapshot, text, System.Text.Encoding.UTF8.GetBytes(text), "candidate",
                new NutConfigurationChangePreview(snapshot.TargetPath, "candidate", [new(1, string.Empty, text, false)]));
        }
        public Task<NutConfigurationApplyResult> ApplyAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
