using NutManager.App.Localization;
using NutManager.App.ViewModels;
using NutManager.Core.Models;
using NutManager.Core.Status;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// Devices selection after the top "available UPS" table was removed. Multi-UPS support must stay
/// intact: the picker simply moves into the device header and only appears when it is meaningful.
/// </summary>
public sealed class DevicesSelectionPresentationTests
{
    private static readonly DateTimeOffset ReferenceTime = new(2026, 8, 12, 16, 24, 16, TimeSpan.Zero);

    [Fact]
    public void SingleDeviceHidesThePickerButKeepsTheDeviceAvailable()
    {
        var viewModel = new DevicesPageViewModel { Devices = [new UpsIdentity("NOBREAK", "UPSBrasil 3 kVA")] };

        Assert.True(viewModel.HasDevices);
        Assert.False(viewModel.HasMultipleDevices);
    }

    [Fact]
    public void MultipleDevicesKeepTheSelectionMechanism()
    {
        var viewModel = new DevicesPageViewModel
        {
            Devices = [new UpsIdentity("NOBREAK"), new UpsIdentity("NOBREAK2")]
        };

        Assert.True(viewModel.HasMultipleDevices);

        viewModel.SelectedDevice = viewModel.Devices[1];
        Assert.True(viewModel.HasSelectedDevice);
        Assert.Equal("NOBREAK2", viewModel.SelectedDevice!.Name);
    }

    [Fact]
    public void NoDevicesShowsTheEmptyStateWithoutAPicker()
    {
        var viewModel = new DevicesPageViewModel();

        Assert.True(viewModel.HasNoDevices);
        Assert.False(viewModel.HasMultipleDevices);
    }

    [Fact]
    public void DescriptionSurvivesTheRemovalOfTheTopTable()
    {
        // The description used to be a table column; it now lives under the device name.
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
        Assert.Equal("OL", viewModel.StatusTokensValue);
    }

    [Fact]
    public void CommunicationFieldsComeFromTheSnapshotAndAreOmittedWhenAbsent()
    {
        var withoutInterval = new DevicesPageViewModel
        {
            SelectedSnapshot = new UpsSnapshot(
                new UpsIdentity("NOBREAK"), [],
                new Dictionary<string, UpsVariable> { ["driver.name"] = new("driver.name", "nutdrv_qx") },
                ReferenceTime, DataSource.Live)
        };

        // No poll interval reported: the field must not be invented.
        Assert.False(withoutInterval.HasPollInterval);
        Assert.Null(withoutInterval.PollIntervalValue);
        Assert.Equal("nutdrv_qx", withoutInterval.SelectedDeviceDriver);

        var withInterval = new DevicesPageViewModel
        {
            SelectedSnapshot = new UpsSnapshot(
                new UpsIdentity("NOBREAK"), [],
                new Dictionary<string, UpsVariable>
                {
                    ["driver.parameter.pollinterval"] = new("driver.parameter.pollinterval", "2"),
                    ["driver.version.internal"] = new("driver.version.internal", "0.53")
                },
                ReferenceTime, DataSource.Live)
        };

        // The unit lives in the value, so the label stays short enough not to collide.
        Assert.True(withInterval.HasPollInterval);
        Assert.Equal("2 s", withInterval.PollIntervalValue);
        Assert.Equal("0.53", withInterval.DriverVersionValue);
    }

    [Theory]
    [InlineData(UiLanguagePreference.PtBr, "Intervalo de polling")]
    [InlineData(UiLanguagePreference.EnUs, "Polling interval")]
    public void PollingIntervalLabelIsLocalizedAndCarriesNoUnit(UiLanguagePreference language, string expected)
    {
        var label = new NutManagerLocalizer(language).Get("Devices.PollingInterval");

        Assert.Equal(expected, label);
        Assert.DoesNotContain("(", label, StringComparison.Ordinal);
    }
}
