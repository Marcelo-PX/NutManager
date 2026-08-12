using NutManager.App.ViewModels;
using NutManager.Core.Models;
using NutManager.Core.Status;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// Devices presentation rules: no contradictory state text, and serial ports shown in their
/// friendly form while configuration keeps the exact stored value.
/// </summary>
public sealed class DevicesPresentationTests
{
    private static readonly DateTimeOffset ReferenceTime = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(@"\\.\COM4", "COM4")]
    [InlineData(@"\\.\COM12", "COM12")]
    [InlineData("COM4", "COM4")]
    [InlineData("auto", "auto")]
    [InlineData("/dev/ttyUSB0", "/dev/ttyUSB0")]
    // Not a COM device path: the original value must survive untouched.
    [InlineData(@"\\.\PhysicalDrive0", @"\\.\PhysicalDrive0")]
    [InlineData(@"\\.\pipe\nut", @"\\.\pipe\nut")]
    [InlineData("", "")]
    public void FriendlyPortStripsOnlyTheComDeviceNamespace(string stored, string expected) =>
        Assert.Equal(expected, NutPortPresentation.Friendly(stored));

    [Fact]
    public void FriendlyPortIsPresentationOnlyAndNeverMutatesTheStoredValue()
    {
        const string stored = @"\\.\COM4";
        var presented = NutPortPresentation.Friendly(stored);

        Assert.Equal("COM4", presented);
        Assert.Equal(@"\\.\COM4", stored);
    }

    [Fact]
    public void OnlineDeviceDoesNotAdvertiseAnUnavailableDescription()
    {
        // Regression: the card subtitle printed "unavailable" beside an online status badge.
        var viewModel = new DevicesPageViewModel
        {
            SelectedSnapshot = new UpsSnapshot(
                new UpsIdentity("NOBREAK"),
                [new UpsStatusToken("OL", StatusSemanticState.Online, StatusSeverity.Normal, true)],
                new Dictionary<string, UpsVariable>(),
                ReferenceTime,
                DataSource.Live)
        };

        Assert.False(viewModel.HasSelectedDeviceDescription);
        Assert.True(viewModel.IsSelectedDeviceHealthy);
    }

    [Fact]
    public void ReportedDescriptionIsShownAsTheSubtitle()
    {
        var viewModel = new DevicesPageViewModel
        {
            SelectedSnapshot = new UpsSnapshot(
                new UpsIdentity("NOBREAK", "UPSBrasil 3 kVA"),
                [new UpsStatusToken("OL", StatusSemanticState.Online, StatusSeverity.Normal, true)],
                new Dictionary<string, UpsVariable>(),
                ReferenceTime,
                DataSource.Live)
        };

        Assert.True(viewModel.HasSelectedDeviceDescription);
        Assert.Equal("UPSBrasil 3 kVA", viewModel.SelectedDeviceDescription);
    }

    [Fact]
    public void SelectedDevicePortIsPresentedWithoutTheDeviceNamespace()
    {
        var viewModel = new DevicesPageViewModel
        {
            SelectedSnapshot = new UpsSnapshot(
                new UpsIdentity("NOBREAK"),
                [],
                new Dictionary<string, UpsVariable>
                {
                    ["driver.parameter.port"] = new("driver.parameter.port", @"\\.\COM4")
                },
                ReferenceTime,
                DataSource.Live)
        };

        Assert.Equal("COM4", viewModel.SelectedDevicePort);
    }
}
