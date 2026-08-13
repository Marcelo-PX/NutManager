using NutManager.App.Localization;
using NutManager.App.ViewModels;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// A ComboBox coerces its SelectedValue to null while it materializes, before its items can be
/// matched against the bound value, and a two-way binding pushes that null into the field view
/// model. Taking it for an edit rewrote the draft and forced a rebuild, whose fresh ComboBox
/// repeated the coercion — an endless rebuild loop that froze the configuration screen and left
/// every file reported as modified the moment it was opened.
/// </summary>
public sealed class SemanticFieldCoercionTests
{
    private const string NutConf = "MODE=netserver\r\n";
    private const string UpsConf = "[NOBREAK]\r\n\tdriver = nutdrv_qx\r\n\tport = COM4\r\n\tprotocol = q1\r\n";

    [Fact]
    public void CoercedNullOnAGeneralFieldIsNotAnEditAndTheValueSurvives()
    {
        var editor = CreateGeneralEditor(NutConf);
        var mode = editor.BasicFields.Single(field => field.TechnicalName == "MODE");
        Assert.Equal("netserver", mode.DraftValue);
        Assert.False(editor.HasChanges);

        mode.DraftValue = null!;

        Assert.Equal("netserver", mode.DraftValue);
        Assert.False(editor.HasChanges);
        Assert.False(editor.Draft.IsModified);
    }

    [Fact]
    public void CoercedNullOnAUpsFieldIsNotAnEditAndTheValueSurvives()
    {
        var editor = CreateUpsEditor(UpsConf);
        var protocol = editor.Fields.Single(field => field.Descriptor.Name == "protocol");
        Assert.Equal("q1", protocol.DraftValue);
        Assert.False(editor.HasChanges);

        protocol.DraftValue = null!;

        Assert.Equal("q1", protocol.DraftValue);
        Assert.False(editor.HasChanges);
        Assert.False(editor.Draft.IsModified);
    }

    [Fact]
    public void RepeatedCoercionSettlesInsteadOfLooping()
    {
        // The loop was unbounded: every rebuild produced a control that coerced again. Replaying the
        // coercion many times must leave the editor exactly where it started.
        var editor = CreateGeneralEditor(NutConf);

        for (var i = 0; i < 200; i++)
        {
            editor.BasicFields.Single(field => field.TechnicalName == "MODE").DraftValue = null!;
        }

        Assert.Equal("netserver", editor.BasicFields.Single(field => field.TechnicalName == "MODE").DraftValue);
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public void ARealEditIsStillRegistered()
    {
        var editor = CreateGeneralEditor(NutConf);
        var mode = editor.BasicFields.Single(field => field.TechnicalName == "MODE");

        mode.DraftValue = "standalone";

        Assert.True(editor.HasChanges);
        Assert.True(editor.Draft.IsModified);
    }

    [Fact]
    public void ARealEditOnAUpsFieldIsStillRegistered()
    {
        var editor = CreateUpsEditor(UpsConf);

        editor.Fields.Single(field => field.Descriptor.Name == "protocol").DraftValue = "megatec";

        Assert.True(editor.HasChanges);
        Assert.True(editor.Draft.IsModified);
    }

    private static NutGeneralConfigurationEditorViewModel CreateGeneralEditor(string text) =>
        new(Snapshot(text, NutConfigurationFileKind.NutConf), Strings(), true);

    private static UpsConfigurationEditorViewModel CreateUpsEditor(string text) =>
        new(Snapshot(text, NutConfigurationFileKind.UpsConf), null, [], Strings());

    private static NutManagerLocalizer Strings() => new(UiLanguagePreference.PtBr);

    private static NutConfigurationFileSnapshot Snapshot(string text, NutConfigurationFileKind kind) =>
        new(
            kind == NutConfigurationFileKind.NutConf ? "/etc/nut.conf" : "/etc/ups.conf",
            kind,
            new NutConfigurationParser().Parse(kind, text),
            NutConfigurationTextEncoding.Utf8,
            "fingerprint",
            text.Length);
}
