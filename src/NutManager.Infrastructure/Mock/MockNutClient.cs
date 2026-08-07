using System.Collections.ObjectModel;
using System.Globalization;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Core.Status;

namespace NutManager.Infrastructure.Mock;

public sealed class MockNutClient : INutClient
{
    private const string UpsName = "mockups";
    private readonly DateTimeOffset _referenceTime;

    public MockNutClient(MockScenario scenario, DateTimeOffset referenceTime)
    {
        Scenario = scenario;
        _referenceTime = referenceTime;
        ConnectionState = scenario == MockScenario.Disconnected
            ? ConnectionState.Disconnected
            : ConnectionState.Connected;
        DataFreshness = scenario switch
        {
            MockScenario.Disconnected => DataFreshness.Unavailable,
            MockScenario.StaleData => DataFreshness.Stale,
            _ => DataFreshness.Fresh
        };
    }

    public MockScenario Scenario { get; }

    public ConnectionState ConnectionState { get; }

    public DataFreshness DataFreshness { get; }

    public Task<IReadOnlyList<UpsIdentity>> ListUpsAsync(
        NutEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(endpoint);
        ThrowIfDisconnected();

        IReadOnlyList<UpsIdentity> catalog = Array.AsReadOnly(new[] { CreateIdentity() });
        return Task.FromResult(catalog);
    }

    public Task<UpsSnapshot> GetSnapshotAsync(
        NutEndpoint endpoint,
        string upsName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(upsName);

        if (!string.Equals(upsName, UpsName, StringComparison.Ordinal))
        {
            throw new ArgumentException("The requested UPS is not available in the mock catalog.", nameof(upsName));
        }

        ThrowIfDisconnected();

        return Task.FromResult(CreateSnapshot());
    }

    private UpsSnapshot CreateSnapshot()
    {
        var scenarioData = GetScenarioData();
        var variables = CreateVariables(scenarioData);

        return new UpsSnapshot(
            CreateIdentity(),
            UpsStatusParser.Parse(scenarioData.Status),
            variables,
            scenarioData.LastSuccessfulUpdate,
            DataSource.Simulated,
            scenarioData.InputVoltage,
            scenarioData.OutputVoltage,
            scenarioData.LoadPercentage,
            scenarioData.Frequency,
            scenarioData.Temperature,
            scenarioData.BatteryVoltage,
            scenarioData.BatteryChargePercentage,
            scenarioData.Runtime);
    }

    private ScenarioData GetScenarioData() => Scenario switch
    {
        MockScenario.Online => CreateScenarioData("OL", 230.4m, 230m, 42m, 50m, 29m, 27.2m, 100m, 1800),
        MockScenario.OnBattery => CreateScenarioData("OB DISCHRG", 229.8m, 230m, 35m, 50m, 30m, 26.5m, 72m, 900),
        MockScenario.LowBattery => CreateScenarioData("OB LB DISCHRG", 229.6m, 230m, 30m, 50m, 30m, 24.1m, 11m, 180),
        MockScenario.Overloaded => CreateScenarioData("OL OVER", 230.1m, 230m, 118m, 50m, 34m, 27m, 94m, 1200),
        MockScenario.ReplaceBattery => CreateScenarioData("OL RB", 230.2m, 230m, 44m, 50m, 29m, 25.8m, 78m, 840),
        MockScenario.MissingOptionalValues => CreateScenarioData("OL", 230m, 230m, 40m, null, null, null, null, null),
        MockScenario.StaleData => CreateScenarioData("OL", 230.4m, 230m, 42m, 50m, 29m, 27.2m, 100m, 1800, _referenceTime.AddMinutes(-15)),
        MockScenario.UnknownStatusToken => CreateScenarioData("OL VENDOR_TOKEN", 230.4m, 230m, 42m, 50m, 29m, 27.2m, 100m, 1800),
        MockScenario.Disconnected => throw new MockNutClientDisconnectedException(),
        _ => throw new ArgumentOutOfRangeException(nameof(Scenario))
    };

    private ScenarioData CreateScenarioData(
        string status,
        decimal? inputVoltage,
        decimal? outputVoltage,
        decimal? loadPercentage,
        decimal? frequency,
        decimal? temperature,
        decimal? batteryVoltage,
        decimal? batteryChargePercentage,
        int? runtimeSeconds,
        DateTimeOffset? lastSuccessfulUpdate = null) =>
        new(
            status,
            inputVoltage,
            outputVoltage,
            loadPercentage,
            frequency,
            temperature,
            batteryVoltage,
            batteryChargePercentage,
            runtimeSeconds is null ? null : TimeSpan.FromSeconds(runtimeSeconds.Value),
            lastSuccessfulUpdate ?? _referenceTime);

    private static IReadOnlyDictionary<string, UpsVariable> CreateVariables(ScenarioData scenarioData)
    {
        var variables = new Dictionary<string, UpsVariable>(StringComparer.Ordinal)
        {
            ["ups.status"] = new UpsVariable("ups.status", scenarioData.Status),
            ["device.mfr"] = new UpsVariable("device.mfr", "NutManager"),
            ["device.model"] = new UpsVariable("device.model", "Simulated UPS 1500"),
            ["device.serial"] = new UpsVariable("device.serial", "MOCK-0001"),
            ["ups.description"] = new UpsVariable("ups.description", "Deterministic simulated UPS")
        };

        AddDecimalVariable(variables, "input.voltage", scenarioData.InputVoltage);
        AddDecimalVariable(variables, "output.voltage", scenarioData.OutputVoltage);
        AddDecimalVariable(variables, "ups.load", scenarioData.LoadPercentage);
        AddDecimalVariable(variables, "input.frequency", scenarioData.Frequency);
        AddDecimalVariable(variables, "ups.temperature", scenarioData.Temperature);
        AddDecimalVariable(variables, "battery.voltage", scenarioData.BatteryVoltage);
        AddDecimalVariable(variables, "battery.charge", scenarioData.BatteryChargePercentage);

        if (scenarioData.Runtime is not null)
        {
            var seconds = Convert.ToInt64(scenarioData.Runtime.Value.TotalSeconds);
            variables.Add("battery.runtime", new UpsVariable("battery.runtime", seconds.ToString(CultureInfo.InvariantCulture)));
        }

        return new ReadOnlyDictionary<string, UpsVariable>(variables);
    }

    private static void AddDecimalVariable(
        IDictionary<string, UpsVariable> variables,
        string name,
        decimal? value)
    {
        if (value is not null)
        {
            variables.Add(name, new UpsVariable(name, value.Value.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static UpsIdentity CreateIdentity() =>
        new(
            UpsName,
            "Deterministic simulated UPS",
            "NutManager",
            "Simulated UPS 1500",
            "MOCK-0001");

    private void ThrowIfDisconnected()
    {
        if (Scenario == MockScenario.Disconnected)
        {
            throw new MockNutClientDisconnectedException();
        }
    }

    private sealed record ScenarioData(
        string Status,
        decimal? InputVoltage,
        decimal? OutputVoltage,
        decimal? LoadPercentage,
        decimal? Frequency,
        decimal? Temperature,
        decimal? BatteryVoltage,
        decimal? BatteryChargePercentage,
        TimeSpan? Runtime,
        DateTimeOffset LastSuccessfulUpdate);
}
