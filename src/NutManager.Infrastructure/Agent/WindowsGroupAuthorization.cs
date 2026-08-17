using System.Runtime.Versioning;
using System.Security.Principal;
using NutManager.Core.Agent;

namespace NutManager.Infrastructure.Agent;

/// <summary>
/// Answers "may this caller control the service" by membership of one local group.
///
/// The group is pinned by SID at startup, and it is resolved <em>machine-qualified</em> on purpose: a
/// domain group that happens to share the name must never become the authority over a server's UPS.
/// If the group does not exist, <see cref="IsConfigured"/> is false and every control operation is
/// refused for as long as the agent runs. There is deliberately no widening rule underneath that —
/// not Administrators, not LocalSystem, not "the interactive user". A deployment that forgot to
/// create the group gets an agent that monitors and refuses to act, which is a visible mistake rather
/// than a silent open door.
///
/// The group is never created here. Creating a security principal is a deployment act performed by an
/// administrator who meant to perform it.
/// </summary>
public sealed class WindowsGroupAuthorization : INutAgentAuthorization
{
    public const string DefaultGroupName = "NutManager Operators";

    private readonly string _groupName;
    private readonly SecurityIdentifier? _groupSid;

    public WindowsGroupAuthorization(string? groupName = null)
    {
        _groupName = string.IsNullOrWhiteSpace(groupName) ? DefaultGroupName : groupName;

        if (!OperatingSystem.IsWindows())
        {
            ConfigurationFailure = "The agent only runs on Windows.";
            return;
        }

        (_groupSid, ConfigurationFailure) = WindowsAgentGroupMembership.ResolveLocalGroup(_groupName);
    }

    public string GroupName => _groupName;

    /// <summary>The pinned group's SID, for the transport to build its ACL from.</summary>
    public SecurityIdentifier? GroupSid => _groupSid;

    public bool IsConfigured => _groupSid is not null;

    public string? ConfigurationFailure { get; }

    /// <summary>
    /// The independent membership check the application service performs after the transport has
    /// already authenticated the caller. Two mechanisms answer the same question — the transport asks
    /// the caller's token, this asks Windows about the account — and both must agree before anything
    /// is controlled.
    ///
    /// Indirect membership counts: an administrator who puts a domain group into the operators group
    /// has authorized its members, and the check asks Windows to expand that rather than reading the
    /// direct member list and refusing the most ordinary enterprise deployment there is.
    /// </summary>
    public Task<bool> IsAuthorizedAsync(string identity, CancellationToken cancellationToken)
    {
        if (_groupSid is null || string.IsNullOrWhiteSpace(identity) || !OperatingSystem.IsWindows())
        {
            return Task.FromResult(false);
        }

        return WindowsAgentGroupMembership.IsMemberAsync(identity, _groupSid, cancellationToken);
    }
}

/// <summary>
/// The Windows-typed membership queries, behind one annotation.
///
/// Everything here reads. No account is created, no group is created, no membership is changed, and
/// no privilege is adjusted — a mutation would need an API that does not appear in this file.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsAgentGroupMembership
{
    /// <summary>
    /// Resolves the group on this machine. Machine-qualified so that only a local group can satisfy
    /// it, and reported as a failure rather than an exception so the agent can start, refuse control
    /// and say why.
    /// </summary>
    internal static (SecurityIdentifier? Sid, string? Failure) ResolveLocalGroup(string groupName)
    {
        try
        {
            var account = new NTAccount(Environment.MachineName, groupName);
            var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
            return (sid, null);
        }
        catch (IdentityNotMappedException)
        {
            return (null, $"The local group '{groupName}' does not exist on {Environment.MachineName}.");
        }
        catch (Exception exception)
        {
            return (null, $"The local group '{groupName}' could not be resolved ({exception.GetType().Name}).");
        }
    }

    // Scheduled here rather than in the guarded caller: a platform guard does not follow the call
    // into a lambda, so the lambda has to live on the annotated side.
    internal static Task<bool> IsMemberAsync(string identity, SecurityIdentifier groupSid, CancellationToken cancellationToken) =>
        Task.Run(() => IsMember(identity, groupSid), cancellationToken);

    /// <summary>
    /// Whether the account belongs to the pinned group, directly or through a group that does.
    ///
    /// The groups are compared by SID rather than by name. The lookup returns names, and a name is the
    /// part of an identity that can be made to look like another one; the SID is the part that cannot.
    /// </summary>
    internal static bool IsMember(string identity, SecurityIdentifier groupSid)
    {
        try
        {
            foreach (var name in WindowsAgentGroupInterop.GetLocalGroups(identity))
            {
                SecurityIdentifier candidate;
                try
                {
                    candidate = (SecurityIdentifier)new NTAccount(Environment.MachineName, name).Translate(typeof(SecurityIdentifier));
                }
                catch (Exception)
                {
                    // One unresolvable group is not an answer about the others.
                    continue;
                }

                if (candidate.Equals(groupSid)) return true;
            }

            return false;
        }
        catch (Exception)
        {
            // A question that could not be asked is answered no.
            return false;
        }
    }
}
