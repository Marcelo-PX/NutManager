using System.Collections.ObjectModel;
using NutManager.Core.Status;

namespace NutManager.Core.Models;

public sealed record UpsSnapshot
{
    public UpsSnapshot(
        UpsIdentity identity,
        IReadOnlyList<UpsStatusToken> statusTokens,
        IReadOnlyDictionary<string, UpsVariable> variables,
        DateTimeOffset lastSuccessfulUpdate,
        DataSource source,
        decimal? inputVoltage = null,
        decimal? outputVoltage = null,
        decimal? loadPercentage = null,
        decimal? frequency = null,
        decimal? temperature = null,
        decimal? batteryVoltage = null,
        decimal? batteryChargePercentage = null,
        TimeSpan? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(statusTokens);
        ArgumentNullException.ThrowIfNull(variables);

        Identity = identity;
        StatusTokens = Array.AsReadOnly(statusTokens.ToArray());
        Variables = new ReadOnlyDictionary<string, UpsVariable>(
            new Dictionary<string, UpsVariable>(variables));
        LastSuccessfulUpdate = lastSuccessfulUpdate;
        Source = source;
        InputVoltage = inputVoltage;
        OutputVoltage = outputVoltage;
        LoadPercentage = loadPercentage;
        Frequency = frequency;
        Temperature = temperature;
        BatteryVoltage = batteryVoltage;
        BatteryChargePercentage = batteryChargePercentage;
        Runtime = runtime;
    }

    public UpsIdentity Identity { get; }

    public IReadOnlyList<UpsStatusToken> StatusTokens { get; }

    public IReadOnlyDictionary<string, UpsVariable> Variables { get; }

    public DateTimeOffset LastSuccessfulUpdate { get; }

    public DataSource Source { get; }

    public decimal? InputVoltage { get; }

    public decimal? OutputVoltage { get; }

    public decimal? LoadPercentage { get; }

    public decimal? Frequency { get; }

    public decimal? Temperature { get; }

    public decimal? BatteryVoltage { get; }

    public decimal? BatteryChargePercentage { get; }

    public TimeSpan? Runtime { get; }
}
