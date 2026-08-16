namespace NutManager.Core.Administration;

/// <summary>
/// Outcome of asking a remote Windows Service Control Manager about the NUT service.
///
/// These are kept apart from the service's own state on purpose. "The query failed" and "the service
/// is stopped" are different facts, and a product that folds them together tells an administrator the
/// NUT server is down when the only thing that actually happened is that a firewall dropped an RPC
/// call. The NUT protocol probe is the source of truth for whether NUT works; this one only ever adds
/// operational detail beside it.
/// </summary>
public enum RemoteWindowsServiceProbeState
{
    /// <summary>The SCM answered and a single NUT service was identified.</summary>
    Success,

    /// <summary>The current Windows identity may not query the remote SCM (Win32 5).</summary>
    AccessDenied,

    /// <summary>The remote SCM could not be reached at all (Win32 1722), typically RPC or firewall.</summary>
    RpcUnavailable,

    /// <summary>The SCM answered but carries no service NutManager recognises as NUT (Win32 1060).</summary>
    ServiceNotFound,

    /// <summary>More than one plausible NUT service exists, so none is chosen silently.</summary>
    AmbiguousService,

    /// <summary>The query did not return within its budget. The RPC itself may still be running.</summary>
    TimedOut,

    /// <summary>Remote SCM queries are not available on this platform or for this profile.</summary>
    Unsupported,

    /// <summary>The query failed for a reason with no more specific mapping.</summary>
    UnknownFailure
}

/// <summary>
/// One read-only observation of the Windows service running NUT on a remote host.
///
/// Every field is what the SCM reported at <see cref="ObservedAt"/>, or null when it did not report
/// it. Nothing here is inferred: a missing process id stays missing rather than becoming zero, and an
/// unidentified service stays unidentified rather than defaulting to a guessed name.
/// </summary>
/// <param name="Host">The host queried, taken from the profile's NUT endpoint.</param>
/// <param name="ProbeState">Whether the query itself succeeded, and how it failed when it did not.</param>
/// <param name="ServiceName">The SCM service name, when one was identified.</param>
/// <param name="DisplayName">The SCM display name, when one was identified.</param>
/// <param name="ServiceState">The service's own state, independent of the probe outcome.</param>
/// <param name="ProcessId">The process id the SCM reported for a running service, when available.</param>
/// <param name="ExecutableName">The executable's file name only — never the full command line.</param>
/// <param name="Win32ErrorCode">The numeric Windows error, preserved for diagnostics.</param>
/// <param name="ObservedAt">When the observation was taken, so a stale reading can be labelled.</param>
/// <param name="CandidateServiceNames">The names seen when the identification was ambiguous.</param>
public sealed record RemoteWindowsNutServiceSnapshot(
    string Host,
    RemoteWindowsServiceProbeState ProbeState,
    string? ServiceName,
    string? DisplayName,
    NutServiceState ServiceState,
    int? ProcessId,
    string? ExecutableName,
    int? Win32ErrorCode,
    DateTimeOffset ObservedAt,
    IReadOnlyList<string>? CandidateServiceNames = null)
{
    /// <summary>Whether the SCM answered well enough to have identified a service.</summary>
    public bool IsQuerySuccessful => ProbeState == RemoteWindowsServiceProbeState.Success;

    /// <summary>
    /// Whether a process is actually running. A service can be Running with no process id when the
    /// SCM declines to report one, so this asks about the id rather than assuming it from the state.
    /// </summary>
    public bool HasProcess => ProcessId is > 0;

    public static RemoteWindowsNutServiceSnapshot Failure(
        string host,
        RemoteWindowsServiceProbeState state,
        DateTimeOffset observedAt,
        int? win32ErrorCode = null,
        IReadOnlyList<string>? candidates = null) =>
        new(host, state, null, null, NutServiceState.Unknown, null, null, win32ErrorCode, observedAt, candidates);
}
