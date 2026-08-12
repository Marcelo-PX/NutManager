using NutManager.App.Localization;
using NutManager.App.ViewModels;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using Xunit;

namespace NutManager.Tests;

public sealed class ServerGeneralConfigurationEditorViewModelTests
{
    [Fact]
    public void GeneralEditorProjectsBasicAdvancedAndCustomWithoutChangingTheDocument()
    {
        const string text = "# keep\nMODE=standalone\nNUT_DEBUG_LEVEL=0\nVENDOR=value\n";
        using var editor = new NutGeneralConfigurationEditorViewModel(Snapshot(NutConfigurationFileKind.NutConf, "nut.conf", text), Strings(), true);

        Assert.Contains(editor.BasicFields, field => field.Descriptor.SemanticId == "Nut.Mode");
        Assert.Contains(editor.AdvancedFields, field => field.Descriptor.SemanticId == "Nut.DebugLevel");
        Assert.Contains(editor.CustomParameters, field => field.Name == "VENDOR");
        Assert.False(editor.HasChanges);
        Assert.Equal(text, editor.Draft.Materialize().Serialize());
    }

    [Fact]
    public void GeneralEditorValidatesModeAndProducesExistingPipelinePreview()
    {
        using var editor = new NutGeneralConfigurationEditorViewModel(Snapshot(NutConfigurationFileKind.NutConf, "nut.conf", "MODE=standalone\n"), Strings(), true);
        var mode = editor.BasicFields.Single(field => field.Descriptor.SemanticId == "Nut.Mode");
        mode.DraftValue = "netserver";
        var pipeline = new PreviewPipeline();

        var generated = editor.Prepare(pipeline);

        Assert.True(editor.CanReview);
        Assert.Equal("MODE=netserver\n", generated.PreparedChange.CandidateText);
        Assert.Equal(1, pipeline.Calls);
        Assert.True(generated.SemanticReview.Changes.Single().Activation == Core.Configuration.Semantic.NutConfigurationActivation.ServiceRestart);
    }

    [Fact]
    public void ServerEditorAddsEditsAndRemovesListenRowsWithoutFabricatingDefaults()
    {
        using var empty = new UpsdConfigurationEditorViewModel(Snapshot(NutConfigurationFileKind.UpsdConf, "upsd.conf", string.Empty), Strings(), true);
        Assert.True(empty.HasNoListeners);
        Assert.False(empty.HasChanges);
        Assert.Equal(string.Empty, empty.Draft.Materialize().Serialize());

        empty.NewListenAddress = "::1";
        empty.AddListenerCommand.Execute(null);
        Assert.Single(empty.Listeners);
        Assert.Contains("LISTEN ::1", empty.Draft.Materialize().Serialize());
        empty.Listeners[0].Address = "*";
        empty.Listeners[0].PortText = "3493";
        empty.Listeners[0].SaveCommand.Execute(null);
        Assert.Contains("LISTEN * 3493", empty.Draft.Materialize().Serialize());
        Assert.Contains(empty.ValidationIssues, issue => issue.Message.Contains("firewall", StringComparison.OrdinalIgnoreCase));
        empty.Listeners[0].RemoveCommand.Execute(null);
        Assert.DoesNotContain("LISTEN", empty.Draft.Materialize().Serialize());
    }

    [Fact]
    public void ReadOnlyEditorsCanViewButCannotMutateOrReview()
    {
        using var general = new NutGeneralConfigurationEditorViewModel(Snapshot(NutConfigurationFileKind.NutConf, "nut.conf", "MODE=standalone\n"), Strings(), false);
        using var server = new UpsdConfigurationEditorViewModel(Snapshot(NutConfigurationFileKind.UpsdConf, "upsd.conf", "LISTEN 127.0.0.1\n"), Strings(), false);

        Assert.False(general.CanEdit);
        Assert.False(server.CanEdit);
        general.BasicFields.Single().DraftValue = "netserver";
        server.NewListenAddress = "::1";
        server.AddListenerCommand.Execute(null);

        Assert.Equal("MODE=standalone\n", general.Draft.Materialize().Serialize());
        Assert.Equal("LISTEN 127.0.0.1\n", server.Draft.Materialize().Serialize());
        Assert.False(general.CanReview);
        Assert.False(server.CanReview);
    }

    [Fact]
    public void CertificateIdentityPasswordStaysAtViewBoundaryAndIsRedactedFromPresentation()
    {
        const string secret = "T27_SUPER_SECRET_CERT_123";
        using var editor = new UpsdConfigurationEditorViewModel(Snapshot(NutConfigurationFileKind.UpsdConf, "upsd.conf",
            "CERTIDENT identity old-secret\n"), Strings(), true);

        editor.ReplaceCertificateIdentity("new identity", secret.AsSpan());

        Assert.DoesNotContain(editor.GetType().GetProperties(), property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(secret, editor.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, editor.ValidationIssues.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, editor.Draft.Review.ToString(), StringComparison.Ordinal);
        Assert.Contains(secret, editor.Draft.Materialize().Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void KnownProductionDirectiveCannotBeAddedThroughCustomRoute()
    {
        using var editor = new UpsdConfigurationEditorViewModel(Snapshot(NutConfigurationFileKind.UpsdConf, "upsd.conf", "MAXAGE 15\n"), Strings(), true);
        editor.NewCustomName = "MAXAGE";
        editor.NewCustomValue = "30";

        editor.AddCustomParameterCommand.Execute(null);

        Assert.False(editor.HasChanges);
        Assert.NotNull(editor.OperationMessage);
        Assert.Equal("MAXAGE 15\n", editor.Draft.Materialize().Serialize());
    }

    [Fact]
    public void CustomParameterWithNewlineInjectionIsRejectedThroughTheEditorCommand()
    {
        using var editor = new UpsdConfigurationEditorViewModel(Snapshot(NutConfigurationFileKind.UpsdConf, "upsd.conf", string.Empty), Strings(), true);
        editor.NewCustomName = "CUSTOMDIRECTIVE";
        editor.NewCustomValue = "value\nLISTEN 0.0.0.0 9999";

        editor.AddCustomParameterCommand.Execute(null);

        Assert.False(editor.HasChanges);
        Assert.NotNull(editor.OperationMessage);
        Assert.Equal(string.Empty, editor.Draft.Materialize().Serialize());
    }

    [Fact]
    public void CustomParameterWithControlCharacterInNameIsRejectedThroughTheEditorCommand()
    {
        using var editor = new NutGeneralConfigurationEditorViewModel(Snapshot(NutConfigurationFileKind.NutConf, "nut.conf", "MODE=standalone\n"), Strings(), true);
        editor.NewCustomName = "BAD\tNAME";
        editor.NewCustomValue = "value";

        editor.AddCustomParameterCommand.Execute(null);

        Assert.False(editor.HasChanges);
        Assert.NotNull(editor.OperationMessage);
        Assert.Equal("MODE=standalone\n", editor.Draft.Materialize().Serialize());
    }

    [Fact]
    public void OmittedBooleanAndExplicitFalseRemainDistinctInPresentationAndCandidate()
    {
        using var editor = new NutGeneralConfigurationEditorViewModel(Snapshot(NutConfigurationFileKind.NutConf, "nut.conf",
            "MODE=standalone\n"), Strings(), true);
        var field = editor.AdvancedFields.Single(item => item.Descriptor.SemanticId == "Nut.AllowNoDevice");

        Assert.False(field.IsConfigured);
        Assert.False(field.IsBooleanConfigured);
        field.IsConfigured = true;

        Assert.True(field.IsBooleanConfigured);
        Assert.Contains("ALLOW_NO_DEVICE=false", editor.Draft.Materialize().Serialize());

        field.IsConfigured = false;
        Assert.DoesNotContain("ALLOW_NO_DEVICE", editor.Draft.Materialize().Serialize());
    }

    private static NutManagerLocalizer Strings() => new(UiLanguagePreference.PtBr);

    private static NutConfigurationFileSnapshot Snapshot(NutConfigurationFileKind kind, string file, string text) => new(
        $"C:\\NUT\\etc\\{file}", kind, new NutConfigurationParser().Parse(kind, text),
        NutConfigurationTextEncoding.Utf8, "original", text.Length);

    private sealed class PreviewPipeline : INutConfigurationFilePipeline
    {
        public int Calls { get; private set; }
        public Task<NutConfigurationLoadResult> LoadAsync(string targetPath, NutConfigurationFileKind fileKind, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public NutConfigurationPreparedChange Prepare(NutConfigurationFileSnapshot snapshot)
        {
            Calls++;
            var text = snapshot.Document.Serialize();
            return new(snapshot, text, System.Text.Encoding.UTF8.GetBytes(text), "candidate",
                new NutConfigurationChangePreview(snapshot.TargetPath, "candidate", [new(1, string.Empty, text, false)]));
        }
        public Task<NutConfigurationApplyResult> ApplyAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
