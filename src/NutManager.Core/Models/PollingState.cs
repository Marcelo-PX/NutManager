namespace NutManager.Core.Models;

public sealed record PollingState(
    string? UpsName,
    UpsSnapshot? Snapshot,
    ConnectionState ConnectionState,
    DataFreshness DataFreshness,
    string? LastError)
{
    public static PollingState Unavailable { get; } = new(null, null, ConnectionState.Disconnected, DataFreshness.Unavailable, null);
}
