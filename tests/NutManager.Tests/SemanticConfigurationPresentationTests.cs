using System.Security.Cryptography;
using System.Text;
using NutManager.App.Localization;
using NutManager.App.ViewModels;
using NutManager.Core.Configuration;
using NutManager.Core.Configuration.Semantic;
using NutManager.Core.Models;
using NutManager.Infrastructure.Configuration;
using Xunit;

namespace NutManager.Tests;

public sealed class SemanticConfigurationPresentationTests
{
    [Fact]
    public void OfficialCulturesHaveExactSemanticResourceParity()
    {
        var pt = NutManagerLocalizer.GetAvailableKeys(UiLanguagePreference.PtBr).Where(key => key.StartsWith("Semantic.", StringComparison.Ordinal)).ToHashSet();
        var en = NutManagerLocalizer.GetAvailableKeys(UiLanguagePreference.EnUs).Where(key => key.StartsWith("Semantic.", StringComparison.Ordinal)).ToHashSet();

        Assert.NotEmpty(pt);
        Assert.Equal(pt.Order(), en.Order());
    }

    [Fact]
    public void ReviewPresentationIsReadOnlyLocalizedAndDoesNotExposeCandidateText()
    {
        var document = new NutConfigurationParser().Parse(NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        var field = new NutConfigurationFieldDescriptor(NutConfigurationFileKind.NutConf, "Nut.Mode", NutConfigurationEntryKind.Assignment,
            "MODE", NutConfigurationFieldScope.Global, "Semantic.Field.Nut.Mode.Label", "Semantic.Field.Nut.Mode.Help");
        var schema = new NutConfigurationFileSchema(NutConfigurationFileKind.NutConf, [field]);
        using var draft = new NutConfigurationSemanticDraft(document, schema);
        Assert.True(draft.Set("Nut.Mode", "netserver").Succeeded);
        var bytes = Encoding.UTF8.GetBytes(document.OriginalText);
        var snapshot = new NutConfigurationFileSnapshot("C:\\temp\\nut.conf", NutConfigurationFileKind.NutConf, document,
            NutConfigurationTextEncoding.Utf8, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
        var generated = NutConfigurationGeneratedPreviewFactory.Prepare(new NutConfigurationFilePipeline(), snapshot, draft);

        var presentation = new SemanticConfigurationReviewViewModel(generated, draft.Projection,
            new NutManagerLocalizer(UiLanguagePreference.PtBr));

        Assert.Equal("Modo do NUT", Assert.Single(presentation.Items).Label);
        Assert.Equal("Alterar", presentation.Items[0].Operation);
        Assert.True(presentation.HasPreviewLines);
        Assert.Null(typeof(SemanticConfigurationReviewViewModel).GetProperty("CandidateText"));
        Assert.DoesNotContain(typeof(SemanticConfigurationReviewViewModel).GetProperties(), property => property.CanWrite);
    }

    [Fact]
    public void MainWindowDrawerRemainsHiddenUntilSemanticReviewIsProvided()
    {
        var document = new NutConfigurationParser().Parse(NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        var field = new NutConfigurationFieldDescriptor(NutConfigurationFileKind.NutConf, "Nut.Mode", NutConfigurationEntryKind.Assignment,
            "MODE", NutConfigurationFieldScope.Global, "Semantic.Field.Nut.Mode.Label", "Semantic.Field.Nut.Mode.Help");
        using var draft = new NutConfigurationSemanticDraft(document, new NutConfigurationFileSchema(NutConfigurationFileKind.NutConf, [field]));
        Assert.True(draft.Set("Nut.Mode", "netserver").Succeeded);
        var bytes = Encoding.UTF8.GetBytes(document.OriginalText);
        var snapshot = new NutConfigurationFileSnapshot("C:\\temp\\nut.conf", NutConfigurationFileKind.NutConf, document,
            NutConfigurationTextEncoding.Utf8, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
        var generated = NutConfigurationGeneratedPreviewFactory.Prepare(new NutConfigurationFilePipeline(), snapshot, draft);
        var review = new SemanticConfigurationReviewViewModel(generated, draft.Projection, new NutManagerLocalizer(UiLanguagePreference.PtBr));
        var window = new MainWindowViewModel();

        Assert.False(window.IsReviewDrawerVisible);
        window.SetSemanticReview(review);
        Assert.True(window.IsReviewDrawerVisible);
        Assert.True(window.IsReviewDrawerInline);
        Assert.Same(review, window.ReviewDrawerContent);
        window.UpdateLayoutWidth(800);
        Assert.True(window.IsReviewDrawerOverlay);
        Assert.False(window.IsBackgroundInteractionEnabled);
        window.CloseReviewDrawerCommand.Execute(null);
        Assert.False(window.IsReviewDrawerVisible);
        Assert.True(window.IsBackgroundInteractionEnabled);
    }

    [Fact]
    public void TechnicalNutTokensRemainInvariantAcrossPresentationCultures()
    {
        var pt = new NutManagerLocalizer(UiLanguagePreference.PtBr);
        var en = new NutManagerLocalizer(UiLanguagePreference.EnUs);
        foreach (var token in new[] { "nut.conf", "ups.conf", "LISTEN", "MONITOR", "runtimecal" })
        {
            Assert.Equal(token, pt.Get(token));
            Assert.Equal(token, en.Get(token));
        }
    }
}
