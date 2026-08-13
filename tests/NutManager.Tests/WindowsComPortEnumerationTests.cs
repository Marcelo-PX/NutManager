using NutManager.App.ViewModels;
using NutManager.Core.Administration;
using NutManager.Infrastructure.Platform.Windows;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// Passive COM enumeration and its presentation. Nothing here touches the registry, WMI or a real
/// port: the normalizer and ordering are pure, and port sets are supplied directly.
/// </summary>
public sealed class WindowsComPortEnumerationTests
{
    [Theory]
    [InlineData(@"COM4", "COM4")]
    [InlineData(@"\\.\COM4", "COM4")]
    [InlineData(@"  COM12  ", "COM12")]
    [InlineData(@"\\.\COM123", "COM123")]
    public void PortNamesFromEitherSourceNormalizeToTheSameValue(string raw, string expected)
    {
        Assert.True(WindowsComPortNormalizer.TryNormalize(raw, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("LPT1")]
    [InlineData("COM")]
    [InlineData("COM0")]
    [InlineData("COMx")]
    [InlineData(null)]
    [InlineData("")]
    public void NonComValuesAreRejected(string? raw) =>
        Assert.False(WindowsComPortNormalizer.TryNormalize(raw, out _));

    [Fact]
    public void PortNumbersAreExtractedForNaturalOrdering()
    {
        Assert.True(WindowsComPortNormalizer.TryGetNumber(@"\\.\COM4", out var four));
        Assert.True(WindowsComPortNormalizer.TryGetNumber("COM10", out var ten));

        Assert.Equal(4, four);
        Assert.Equal(10, ten);
        // Natural order, not text order: COM4 must precede COM10.
        Assert.True(four < ten);
    }

    [Fact]
    public void DetectedPortsAreOrderedNaturally()
    {
        var source = new FakeComPortSource("COM10", "COM4", "COM2");

        var ordered = source.GetPorts().Select(port => port.PortName).ToArray();

        Assert.Equal(["COM2", "COM4", "COM10"], ordered);
    }

    [Theory]
    // The configured value keeps its raw form; only the presentation is friendly.
    [InlineData(@"\\.\COM4", "COM4")]
    [InlineData("COM4", "COM4")]
    public void ConfiguredPortIsPresentedWithoutTheDeviceNamespace(string configured, string presented) =>
        Assert.Equal(presented, NutPortPresentation.Friendly(configured));

    [Fact]
    public void ConfiguredPortIsReportedAsDetectedWhenEnumerationContainsIt()
    {
        var detected = new FakeComPortSource("COM4").GetPorts();

        var present = detected.Any(port =>
            WindowsComPortNormalizer.TryNormalize(@"\\.\COM4", out var normalized) &&
            string.Equals(port.PortName, normalized, StringComparison.OrdinalIgnoreCase));

        Assert.True(present);
    }

    [Fact]
    public void ConfiguredPortIsReportedAsNotDetectedWhenEnumerationIsEmpty()
    {
        var detected = new FakeComPortSource().GetPorts();

        var present = detected.Any(port =>
            WindowsComPortNormalizer.TryNormalize(@"\\.\COM4", out var normalized) &&
            string.Equals(port.PortName, normalized, StringComparison.OrdinalIgnoreCase));

        Assert.False(present);
        Assert.Equal("COM4", NutPortPresentation.Friendly(@"\\.\COM4"));
    }

    /// <summary>Applies the same normalization and natural ordering as the real source.</summary>
    private sealed class FakeComPortSource(params string[] names) : IWindowsComPortSource
    {
        public IReadOnlyList<NutComPortInfo> GetPorts() => names
            .Select(name => WindowsComPortNormalizer.TryNormalize(name, out var normalized) ? normalized : null)
            .Where(name => name is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new NutComPortInfo(name!, null, null, null, null, null, true))
            .OrderBy(port => WindowsComPortNormalizer.TryGetNumber(port.PortName, out var number) ? number : int.MaxValue)
            .ToArray();
    }
}
