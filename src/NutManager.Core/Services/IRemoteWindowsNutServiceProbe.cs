using NutManager.Core.Administration;

namespace NutManager.Core.Services;

/// <summary>
/// Asks a remote Windows Service Control Manager about the NUT service, read-only.
///
/// Deliberately narrow: there is one verb, and it observes. T34 monitors a remote service and never
/// controls one, so no start, stop, restart or configuration change enters through here. Keeping the
/// interface at a single query is what makes that boundary something a reader can check rather than
/// something the documentation merely claims — a mutation would need a new member, in a review.
///
/// Failures arrive as a snapshot carrying a probe state, not as exceptions. A remote SCM query fails
/// for ordinary environmental reasons — a firewall, a domain trust, an account without rights — and
/// those are results the interface reports, not faults the caller must catch.
/// </summary>
public interface IRemoteWindowsNutServiceProbe
{
    /// <summary>
    /// Queries <paramref name="host"/> using the process's current Windows identity. No credential is
    /// collected, prompted for, or read from any store: whatever the running user can already see is
    /// exactly what this returns.
    /// </summary>
    Task<RemoteWindowsNutServiceSnapshot> ProbeAsync(string host, CancellationToken cancellationToken);
}
