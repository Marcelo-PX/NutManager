namespace NutManager.Core.Models;

public enum ManagedNutConnectionTestStatus
{
    Success,
    EndpointUnreachable,
    Timeout,
    ProtocolError,
    NoUpsDiscovered,
    PreferredUpsMissing,
    Cancelled,
    Failed
}

public sealed record ManagedNutConnectionTestResult(
    ManagedNutConnectionTestStatus Status,
    IReadOnlyList<string> DiscoveredUpsNames)
{
    public bool IsSuccess => Status == ManagedNutConnectionTestStatus.Success;
}
