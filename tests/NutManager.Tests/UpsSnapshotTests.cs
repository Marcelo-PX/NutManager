using NutManager.Core.Models;
using NutManager.Core.Status;
using Xunit;

namespace NutManager.Tests;

public sealed class UpsSnapshotTests
{
    [Fact]
    public void KeepsMissingNormalizedValuesAsNull()
    {
        var snapshot = CreateSnapshot(new Dictionary<string, UpsVariable>());

        Assert.Null(snapshot.InputVoltage);
        Assert.Null(snapshot.OutputVoltage);
        Assert.Null(snapshot.LoadPercentage);
        Assert.Null(snapshot.Frequency);
        Assert.Null(snapshot.Temperature);
        Assert.Null(snapshot.BatteryVoltage);
        Assert.Null(snapshot.BatteryChargePercentage);
        Assert.Null(snapshot.Runtime);
    }

    [Fact]
    public void PreservesTimestampAndSource()
    {
        var timestamp = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
        var snapshot = CreateSnapshot(new Dictionary<string, UpsVariable>(), timestamp, DataSource.Simulated);

        Assert.Equal(timestamp, snapshot.LastSuccessfulUpdate);
        Assert.Equal(DataSource.Simulated, snapshot.Source);
    }

    [Fact]
    public void DefensivelyCopiesTheVariablesDictionary()
    {
        var variables = new Dictionary<string, UpsVariable>
        {
            ["battery.charge"] = new UpsVariable("battery.charge", "95")
        };
        var snapshot = CreateSnapshot(variables);

        variables["battery.charge"] = new UpsVariable("battery.charge", "10");
        variables.Add("ups.status", new UpsVariable("ups.status", "OB"));

        Assert.Equal("95", snapshot.Variables["battery.charge"].Value);
        Assert.DoesNotContain("ups.status", snapshot.Variables.Keys);
    }

    private static UpsSnapshot CreateSnapshot(
        IReadOnlyDictionary<string, UpsVariable> variables,
        DateTimeOffset? timestamp = null,
        DataSource source = DataSource.Live) =>
        new(
            new UpsIdentity("ups-main"),
            UpsStatusParser.Parse("OL"),
            variables,
            timestamp ?? new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero),
            source);
}
