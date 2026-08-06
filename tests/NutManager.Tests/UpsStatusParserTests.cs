using NutManager.Core.Status;
using Xunit;

namespace NutManager.Tests;

public sealed class UpsStatusParserTests
{
    [Theory]
    [InlineData("OL", StatusSemanticState.Online, StatusSeverity.Normal)]
    [InlineData("OB", StatusSemanticState.OnBattery, StatusSeverity.Warning)]
    [InlineData("LB", StatusSemanticState.LowBattery, StatusSeverity.Critical)]
    [InlineData("RB", StatusSemanticState.ReplaceBattery, StatusSeverity.Warning)]
    [InlineData("CHRG", StatusSemanticState.Charging, StatusSeverity.Informational)]
    [InlineData("DISCHRG", StatusSemanticState.Discharging, StatusSeverity.Warning)]
    [InlineData("BYPASS", StatusSemanticState.Bypass, StatusSeverity.Warning)]
    [InlineData("OFF", StatusSemanticState.OutputOff, StatusSeverity.Critical)]
    [InlineData("OVER", StatusSemanticState.Overloaded, StatusSeverity.Critical)]
    [InlineData("CAL", StatusSemanticState.Calibration, StatusSeverity.Informational)]
    public void InterpretsKnownTokens(string token, StatusSemanticState state, StatusSeverity severity)
    {
        var result = Assert.Single(UpsStatusParser.Parse(token));

        Assert.Equal(token, result.OriginalToken);
        Assert.Equal(state, result.State);
        Assert.Equal(severity, result.Severity);
        Assert.True(result.IsKnown);
    }

    [Fact]
    public void PreservesOrderAcrossMultipleTokens()
    {
        var tokens = UpsStatusParser.Parse("OL OB LB");

        Assert.Collection(
            tokens,
            token => Assert.Equal("OL", token.OriginalToken),
            token => Assert.Equal("OB", token.OriginalToken),
            token => Assert.Equal("LB", token.OriginalToken));
    }

    [Fact]
    public void RecognizesLowercaseTokensWithoutChangingTheirOriginalText()
    {
        var result = Assert.Single(UpsStatusParser.Parse("ob"));

        Assert.Equal("ob", result.OriginalToken);
        Assert.Equal(StatusSemanticState.OnBattery, result.State);
        Assert.True(result.IsKnown);
    }

    [Fact]
    public void PreservesUnknownTokens()
    {
        var result = Assert.Single(UpsStatusParser.Parse("VENDOR_TOKEN"));

        Assert.Equal("VENDOR_TOKEN", result.OriginalToken);
        Assert.Equal(StatusSemanticState.Unknown, result.State);
        Assert.Equal(StatusSeverity.Unknown, result.Severity);
        Assert.False(result.IsKnown);
    }

    [Fact]
    public void PreservesRepeatedTokens()
    {
        var tokens = UpsStatusParser.Parse("OB OB");

        Assert.Equal(2, tokens.Count);
        Assert.All(tokens, token => Assert.Equal("OB", token.OriginalToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t  ")]
    public void ReturnsAnEmptyResultForMissingStatus(string? status)
    {
        Assert.Empty(UpsStatusParser.Parse(status));
    }
}
