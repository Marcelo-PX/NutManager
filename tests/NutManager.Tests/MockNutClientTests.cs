using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Core.Status;
using NutManager.Infrastructure.Mock;
using Xunit;

namespace NutManager.Tests;

public sealed class MockNutClientTests
{
    private static readonly DateTimeOffset ReferenceTime = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly NutEndpoint Endpoint = new("mock.nut.local");

    [Fact]
    public void ImplementsTheSharedNutClientContract()
    {
        var client = CreateClient(MockScenario.Online);

        Assert.IsAssignableFrom<INutClient>(client);
    }

    [Theory]
    [InlineData(MockScenario.Online, ConnectionState.Connected, DataFreshness.Fresh)]
    [InlineData(MockScenario.OnBattery, ConnectionState.Connected, DataFreshness.Fresh)]
    [InlineData(MockScenario.LowBattery, ConnectionState.Connected, DataFreshness.Fresh)]
    [InlineData(MockScenario.Overloaded, ConnectionState.Connected, DataFreshness.Fresh)]
    [InlineData(MockScenario.ReplaceBattery, ConnectionState.Connected, DataFreshness.Fresh)]
    [InlineData(MockScenario.MissingOptionalValues, ConnectionState.Connected, DataFreshness.Fresh)]
    [InlineData(MockScenario.Disconnected, ConnectionState.Disconnected, DataFreshness.Unavailable)]
    [InlineData(MockScenario.StaleData, ConnectionState.Connected, DataFreshness.Stale)]
    [InlineData(MockScenario.UnknownStatusToken, ConnectionState.Connected, DataFreshness.Fresh)]
    public void ExposesTheExpectedConnectionState(
        MockScenario scenario,
        ConnectionState connectionState,
        DataFreshness freshness)
    {
        var client = CreateClient(scenario);

        Assert.Equal(connectionState, client.ConnectionState);
        Assert.Equal(freshness, client.DataFreshness);
    }

    [Theory]
    [InlineData(MockScenario.Online)]
    [InlineData(MockScenario.OnBattery)]
    [InlineData(MockScenario.LowBattery)]
    [InlineData(MockScenario.Overloaded)]
    [InlineData(MockScenario.ReplaceBattery)]
    [InlineData(MockScenario.MissingOptionalValues)]
    [InlineData(MockScenario.StaleData)]
    [InlineData(MockScenario.UnknownStatusToken)]
    public async Task ListsOnlyTheStableMockUpsForAvailableScenarios(MockScenario scenario)
    {
        var devices = await CreateClient(scenario).ListUpsAsync(Endpoint, CancellationToken.None);

        var device = Assert.Single(devices);
        Assert.Equal("mockups", device.Name);
        Assert.Equal("NutManager", device.Manufacturer);
        Assert.Equal("Simulated UPS 1500", device.Model);
    }

    [Fact]
    public async Task OnlineUsesCompleteMetricsAndOnlineStatus()
    {
        var snapshot = await GetSnapshotAsync(MockScenario.Online);

        Assert.Equal(new[] { "OL" }, snapshot.StatusTokens.Select(token => token.OriginalToken));
        Assert.All(snapshot.StatusTokens, token => Assert.True(token.IsKnown));
        Assert.NotNull(snapshot.InputVoltage);
        Assert.NotNull(snapshot.OutputVoltage);
        Assert.NotNull(snapshot.LoadPercentage);
        Assert.NotNull(snapshot.Frequency);
        Assert.NotNull(snapshot.Temperature);
        Assert.NotNull(snapshot.BatteryVoltage);
        Assert.NotNull(snapshot.BatteryChargePercentage);
        Assert.NotNull(snapshot.Runtime);
    }

    [Fact]
    public async Task OnBatteryIncludesOnBatteryAndDischarging()
    {
        var snapshot = await GetSnapshotAsync(MockScenario.OnBattery);

        Assert.Equal(new[] { "OB", "DISCHRG" }, snapshot.StatusTokens.Select(token => token.OriginalToken));
    }

    [Fact]
    public async Task LowBatteryIncludesTokensInTheExpectedOrder()
    {
        var snapshot = await GetSnapshotAsync(MockScenario.LowBattery);

        Assert.Equal(new[] { "OB", "LB", "DISCHRG" }, snapshot.StatusTokens.Select(token => token.OriginalToken));
    }

    [Fact]
    public async Task OverloadedReportsALoadAboveOneHundredPercent()
    {
        var snapshot = await GetSnapshotAsync(MockScenario.Overloaded);

        Assert.Contains(snapshot.StatusTokens, token => token.State == StatusSemanticState.Overloaded);
        Assert.True(snapshot.LoadPercentage > 100m);
    }

    [Fact]
    public async Task ReplaceBatteryIncludesTheReplaceBatteryToken()
    {
        var snapshot = await GetSnapshotAsync(MockScenario.ReplaceBattery);

        Assert.Contains(snapshot.StatusTokens, token => token.State == StatusSemanticState.ReplaceBattery);
    }

    [Fact]
    public async Task MissingOptionalValuesKeepsAbsentMetricsAndVariablesMissing()
    {
        var snapshot = await GetSnapshotAsync(MockScenario.MissingOptionalValues);

        Assert.Null(snapshot.Frequency);
        Assert.Null(snapshot.Temperature);
        Assert.Null(snapshot.BatteryVoltage);
        Assert.Null(snapshot.BatteryChargePercentage);
        Assert.Null(snapshot.Runtime);
        Assert.DoesNotContain("input.frequency", snapshot.Variables.Keys);
        Assert.DoesNotContain("ups.temperature", snapshot.Variables.Keys);
        Assert.DoesNotContain("battery.voltage", snapshot.Variables.Keys);
        Assert.DoesNotContain("battery.charge", snapshot.Variables.Keys);
        Assert.DoesNotContain("battery.runtime", snapshot.Variables.Keys);
    }

    [Fact]
    public async Task DisconnectedFailsWithTheStableMockException()
    {
        var client = CreateClient(MockScenario.Disconnected);

        var listException = await Assert.ThrowsAsync<MockNutClientDisconnectedException>(
            () => client.ListUpsAsync(Endpoint, CancellationToken.None));
        var snapshotException = await Assert.ThrowsAsync<MockNutClientDisconnectedException>(
            () => client.GetSnapshotAsync(Endpoint, "mockups", CancellationToken.None));

        Assert.Equal("The deterministic mock NUT client is disconnected.", listException.Message);
        Assert.Equal(listException.Message, snapshotException.Message);
    }

    [Fact]
    public async Task StaleDataUsesTheReferenceTimeMinusFifteenMinutes()
    {
        var snapshot = await GetSnapshotAsync(MockScenario.StaleData);

        Assert.Equal(ReferenceTime.AddMinutes(-15), snapshot.LastSuccessfulUpdate);
    }

    [Fact]
    public async Task UnknownStatusTokenIsPreserved()
    {
        var snapshot = await GetSnapshotAsync(MockScenario.UnknownStatusToken);

        var unknownToken = Assert.Single(snapshot.StatusTokens, token => !token.IsKnown);
        Assert.Equal("VENDOR_TOKEN", unknownToken.OriginalToken);
        Assert.Equal(StatusSemanticState.Unknown, unknownToken.State);
        Assert.Equal(new[] { "OL", "VENDOR_TOKEN" }, snapshot.StatusTokens.Select(token => token.OriginalToken));
    }

    [Theory]
    [InlineData(MockScenario.Online)]
    [InlineData(MockScenario.OnBattery)]
    [InlineData(MockScenario.LowBattery)]
    [InlineData(MockScenario.Overloaded)]
    [InlineData(MockScenario.ReplaceBattery)]
    [InlineData(MockScenario.MissingOptionalValues)]
    [InlineData(MockScenario.StaleData)]
    [InlineData(MockScenario.UnknownStatusToken)]
    public async Task AllAvailableSnapshotsAreMarkedAsSimulated(MockScenario scenario)
    {
        var snapshot = await GetSnapshotAsync(scenario);

        Assert.Equal(DataSource.Simulated, snapshot.Source);
    }

    [Fact]
    public async Task SameScenarioAndReferenceTimeProduceEquivalentData()
    {
        var first = await GetSnapshotAsync(MockScenario.OnBattery);
        var second = await GetSnapshotAsync(MockScenario.OnBattery);

        Assert.Equal(first.Identity, second.Identity);
        Assert.Equal(first.LastSuccessfulUpdate, second.LastSuccessfulUpdate);
        Assert.Equal(first.Source, second.Source);
        Assert.Equal(first.StatusTokens, second.StatusTokens);
        Assert.Equal(first.Variables, second.Variables);
    }

    [Fact]
    public async Task RespectsCancellationBeforeProducingAResult()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var client = CreateClient(MockScenario.Online);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ListUpsAsync(Endpoint, cancellationTokenSource.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetSnapshotAsync(Endpoint, "mockups", cancellationTokenSource.Token));
    }

    [Fact]
    public async Task RejectsAnUnknownUpsNameUsingOrdinalComparison()
    {
        var client = CreateClient(MockScenario.Online);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetSnapshotAsync(Endpoint, "MOCKUPS", CancellationToken.None));
    }

    [Fact]
    public async Task ReturnedCollectionsCannotModifyTheInternalCatalog()
    {
        var client = CreateClient(MockScenario.Online);
        var catalog = await client.ListUpsAsync(Endpoint, CancellationToken.None);
        var readOnlyCatalog = Assert.IsAssignableFrom<ICollection<UpsIdentity>>(catalog);

        Assert.Throws<NotSupportedException>(() => readOnlyCatalog.Add(new UpsIdentity("other-ups")));

        var subsequentCatalog = await client.ListUpsAsync(Endpoint, CancellationToken.None);
        Assert.Equal("mockups", Assert.Single(subsequentCatalog).Name);
    }

    [Fact]
    public async Task ReturnedVariablesCannotBeChanged()
    {
        var snapshot = await GetSnapshotAsync(MockScenario.Online);
        var variables = Assert.IsAssignableFrom<IDictionary<string, UpsVariable>>(snapshot.Variables);

        Assert.Throws<NotSupportedException>(
            () => variables.Add("ups.status", new UpsVariable("ups.status", "OFF")));
    }

    private static MockNutClient CreateClient(MockScenario scenario) => new(scenario, ReferenceTime);

    private static Task<UpsSnapshot> GetSnapshotAsync(MockScenario scenario) =>
        CreateClient(scenario).GetSnapshotAsync(Endpoint, "mockups", CancellationToken.None);
}
