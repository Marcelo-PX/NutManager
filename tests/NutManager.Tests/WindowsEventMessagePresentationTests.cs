using NutManager.App.ViewModels;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// NUT registers its Windows event source without a message DLL, so the Event Log wraps the real
/// text in a long "description cannot be found" notice. These tests pin that the payload is
/// extracted for display and that anything unrecognised is passed through untouched.
/// </summary>
public sealed class WindowsEventMessagePresentationTests
{
    private const string Wrapper =
        "The description for Event ID '-1073610751' in Source 'Network UPS Tools' cannot be found. " +
        "The local computer may not have the necessary registry information or message DLL files to " +
        "display the message, or you may not have permission to access them. The following " +
        "information is part of the event:";

    [Theory]
    [InlineData("upsmon - Communications with UPS nobreak@127.0.0.1 established")]
    [InlineData("upsd - Connected to UPS [NOBREAK]: nutdrv_qx-NOBREAK")]
    [InlineData("nutdrv_qx - Startup successful: nutdrv_qx.exe")]
    public void PayloadIsExtractedFromTheWindowsWrapper(string payload)
    {
        var raw = $"{Wrapper}'{payload}'";

        Assert.Equal(payload, NutEventMessagePresentation.Friendly(raw));
    }

    [Fact]
    public void DoubleQuotedPayloadIsAlsoUnwrapped() =>
        Assert.Equal("upsd started", NutEventMessagePresentation.Friendly($"{Wrapper}\"upsd started\""));

    [Theory]
    [InlineData("Foi instalado um serviço no sistema.", "Foi instalado um serviço no sistema.")]
    [InlineData("  Serviço iniciado.  ", "Serviço iniciado.")]
    public void MessagesWithoutTheWrapperArePassedThrough(string raw, string expected) =>
        Assert.Equal(expected, NutEventMessagePresentation.Friendly(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyMessagesProduceEmptyText(string? raw) =>
        Assert.Equal(string.Empty, NutEventMessagePresentation.Friendly(raw));

    [Fact]
    public void WrapperWithoutPayloadFallsBackToTheOriginalText()
    {
        // Never return an empty row: if the payload is missing, show what Windows gave us.
        var result = NutEventMessagePresentation.Friendly(Wrapper);

        Assert.Equal(Wrapper.Trim(), result);
    }
}
