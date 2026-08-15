using NutManager.App.ViewModels;
using NutManager.Core.Models;
using NutManager.Core.Status;
using NutManager.Infrastructure.Mock;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// T27A dashboard projections. The visual redesign must never invent a reading, so these tests pin
/// the contract that an absent NUT variable stays absent instead of becoming a substituted value.
/// </summary>
public sealed class OverviewDashboardPresentationTests
{
    private static readonly DateTimeOffset ReferenceTime = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly NutEndpoint Endpoint = new("mock.nut.local");

    [Fact]
    public void EmptyDashboardReportsUnavailableAndNeverFabricatesReferenceValues()
    {
        var viewModel = new OverviewPageViewModel();

        Assert.Null(viewModel.BatteryPercent);
        Assert.False(viewModel.HasBatteryPercent);
        Assert.Equal(0d, viewModel.BatteryBarValue);
        Assert.Null(viewModel.LoadPercent);
        Assert.False(viewModel.HasRuntime);
        Assert.False(viewModel.HasFrequency);
        Assert.False(viewModel.HasBatteryVoltage);
        Assert.False(viewModel.HasLoadPowerText);
        Assert.Null(viewModel.LoadPowerText);
        Assert.Null(viewModel.RuntimeRawText);
        Assert.False(viewModel.HasPrimaryStatus);
        Assert.True(viewModel.IsStatusUnavailable);

        // Values printed in the approved mock-up must never appear without a real reading.
        var rendered = string.Join(
            '|',
            viewModel.BatteryValueText,
            viewModel.LoadValueText,
            viewModel.RuntimeValueText,
            viewModel.InputVoltageText,
            viewModel.OutputVoltageText,
            viewModel.TemperatureText,
            viewModel.DriverText,
            viewModel.UpsTypeText);
        Assert.DoesNotContain("100", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("127", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("120", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("60.1", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("1h", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectedDashboardProjectsRealSnapshotReadings()
    {
        var client = new MockNutClient(MockScenario.Online, ReferenceTime);
        var viewModel = new OverviewPageViewModel(client, Endpoint, "mockups", client.ConnectionState, client.DataFreshness);

        await viewModel.InitializeAsync();

        Assert.NotNull(viewModel.Snapshot);
        Assert.Equal(viewModel.Snapshot!.BatteryChargePercentage, (decimal?)viewModel.BatteryPercent);
        Assert.Equal(viewModel.Snapshot.LoadPercentage, (decimal?)viewModel.LoadPercent);
        Assert.True(viewModel.HasPrimaryStatus);
        Assert.Equal("mockups", viewModel.SelectedUpsText);
        Assert.Equal("mock.nut.local:3493", viewModel.EndpointText);
    }

    [Fact]
    public void PartialSnapshotKeepsMissingReadingsMissingWhileShowingPresentOnes()
    {
        var viewModel = new OverviewPageViewModel
        {
            // Only battery charge is reported; every other reading is genuinely absent.
            Snapshot = new UpsSnapshot(
                new UpsIdentity("ups1"),
                [],
                new Dictionary<string, UpsVariable>(),
                ReferenceTime,
                DataSource.Live,
                batteryChargePercentage: 42m)
        };

        Assert.True(viewModel.HasBatteryPercent);
        Assert.Equal(42d, viewModel.BatteryBarValue);
        Assert.Equal("warning", viewModel.BatterySeverityClass);
        Assert.False(viewModel.HasBatteryVoltage);
        Assert.Null(viewModel.LoadPercent);
        Assert.False(viewModel.HasRuntime);
        Assert.False(viewModel.HasFrequency);
        Assert.False(viewModel.HasTemperature);
        Assert.False(viewModel.HasUpsType);
        Assert.False(viewModel.HasDriverVersion);
    }

    [Theory]
    [InlineData(null, "unavailable")]
    [InlineData(10, "critical")]
    [InlineData(35, "warning")]
    [InlineData(95, "healthy")]
    public void BatterySeverityClassFollowsChargeWithoutReplacingTheNumericReading(int? charge, string expected)
    {
        var viewModel = new OverviewPageViewModel
        {
            Snapshot = new UpsSnapshot(
                new UpsIdentity("ups1"),
                [],
                new Dictionary<string, UpsVariable>(),
                ReferenceTime,
                DataSource.Live,
                batteryChargePercentage: charge)
        };

        Assert.Equal(expected, viewModel.BatterySeverityClass);
    }

    [Fact]
    public void OptionalPowerAndRawRuntimeAppearOnlyWhenTheVariablesAreReported()
    {
        var viewModel = new OverviewPageViewModel
        {
            Snapshot = new UpsSnapshot(
                new UpsIdentity("ups1"),
                [],
                new Dictionary<string, UpsVariable>
                {
                    ["ups.realpower"] = new("ups.realpower", "62"),
                    ["ups.power"] = new("ups.power", "273"),
                    ["battery.runtime"] = new("battery.runtime", "6120"),
                    ["driver.name"] = new("driver.name", "nutdrv_qx")
                },
                ReferenceTime,
                DataSource.Live)
        };

        Assert.True(viewModel.HasLoadPowerText);
        Assert.Equal("62 W / 273 VA", viewModel.LoadPowerText);
        Assert.Equal("battery.runtime 6120 s", viewModel.RuntimeRawText);
        Assert.Equal("nutdrv_qx", viewModel.DriverText);
    }

    [Fact]
    public void PrimaryStatusUsesTheMostSevereReportedToken()
    {
        var viewModel = new OverviewPageViewModel
        {
            Snapshot = new UpsSnapshot(
                new UpsIdentity("ups1"),
                [
                    new UpsStatusToken("OB", StatusSemanticState.OnBattery, StatusSeverity.Warning, true),
                    new UpsStatusToken("LB", StatusSemanticState.LowBattery, StatusSeverity.Critical, true)
                ],
                new Dictionary<string, UpsVariable>(),
                ReferenceTime,
                DataSource.Live)
        };
        viewModel.StatusItems =
        [
            new OverviewStatusItemViewModel("OB", "Em bateria", "Aviso"),
            new OverviewStatusItemViewModel("LB", "Bateria baixa", "Crítico")
        ];

        Assert.True(viewModel.IsStatusCritical);
        Assert.False(viewModel.IsStatusHealthy);
        Assert.Equal("OB", viewModel.PrimaryStatusToken);

        // The badge prints OB, so the icon beside it has to be the battery. Severity is the most
        // severe token; the power source has to follow the token actually shown, or the badge would
        // draw a plug next to the word for running on battery.
        Assert.True(viewModel.IsRunningOnBattery);
        Assert.False(viewModel.IsRunningOnMains);
    }

    [Theory]
    [InlineData("OL", StatusSemanticState.Online, true, false)]
    [InlineData("OB", StatusSemanticState.OnBattery, false, true)]
    [InlineData("LB", StatusSemanticState.LowBattery, false, true)]
    [InlineData("DISCHRG", StatusSemanticState.Discharging, false, true)]
    // Neither flag is set for a state that is neither, so the badge never claims a power source
    // the UPS did not report.
    [InlineData("BYPASS", StatusSemanticState.Bypass, false, false)]
    [InlineData("OFF", StatusSemanticState.OutputOff, false, false)]
    [InlineData("WAT", StatusSemanticState.Unknown, false, false)]
    public void ThePowerSourceIconFollowsTheStateOfTheTokenOnTheBadge(
        string token,
        StatusSemanticState state,
        bool onMains,
        bool onBattery)
    {
        var viewModel = new OverviewPageViewModel
        {
            Snapshot = new UpsSnapshot(
                new UpsIdentity("ups1"),
                [new UpsStatusToken(token, state, StatusSeverity.Normal, state != StatusSemanticState.Unknown)],
                new Dictionary<string, UpsVariable>(),
                ReferenceTime,
                DataSource.Live)
        };

        Assert.Equal(onMains, viewModel.IsRunningOnMains);
        Assert.Equal(onBattery, viewModel.IsRunningOnBattery);
        // The two are never both true: one badge cannot report two power sources.
        Assert.False(viewModel.IsRunningOnMains && viewModel.IsRunningOnBattery);
    }

    [Fact]
    public void AnEmptyDashboardShowsNoPowerSourceIconAtAll()
    {
        var viewModel = new OverviewPageViewModel();

        Assert.False(viewModel.IsRunningOnMains);
        Assert.False(viewModel.IsRunningOnBattery);
    }
}
