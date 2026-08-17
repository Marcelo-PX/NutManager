using System.Runtime.Versioning;
using System.Security.Principal;
using NutManager.Core.Agent;

namespace NutManager.Infrastructure.Agent;

/// <summary>
/// Answers "may this caller control the service" by membership of one group.
///
/// The group is pinned by SID at startup, and it is resolved against <em>the server's own local
/// security database</em>: the SAM on a workstation or member server, and the directory a domain
/// controller uses as its local database. That distinction is the whole point of this class. The
/// earlier implementation qualified the name as <c>MachineName\group</c>, which reads correctly and
/// is wrong on a domain controller — there the group exists as <c>DOMAIN\group</c>, the
/// machine-qualified name resolves to nothing, and an agent installed on a DC refused every
/// operation while the group sat there plainly visible.
///
/// Resolution is deliberately two questions rather than one. First the local group database is asked
/// whether the name is a group it holds; only then is the name translated to a SID. Asking in that
/// order is what keeps a domain group of the same name from becoming the authority over a member
/// server's UPS: the translation starts at the local system, and the existence proof means a name
/// that is only a domain account never reaches it.
///
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
    private readonly IWindowsLocalSecurityDatabase? _database;

    public WindowsGroupAuthorization(string? groupName = null)
        : this(groupName, null)
    {
    }

    /// <summary>
    /// The database is injectable for one reason: the member-server and domain-controller cases
    /// differ in what Windows answers, not in what this class does with the answer, and one test
    /// machine can only ever be one of them.
    /// </summary>
    public WindowsGroupAuthorization(string? groupName, IWindowsLocalSecurityDatabase? database)
    {
        _groupName = string.IsNullOrWhiteSpace(groupName) ? DefaultGroupName : groupName;

        if (!OperatingSystem.IsWindows())
        {
            ConfigurationFailure = "The agent only runs on Windows.";
            return;
        }

        _database = database ?? new WindowsAgentGroupInterop();
        (_groupSid, ConfigurationFailure) = WindowsLocalGroupResolution.Resolve(_groupName, _database);
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
        if (_groupSid is null || _database is null || string.IsNullOrWhiteSpace(identity) || !OperatingSystem.IsWindows())
        {
            return Task.FromResult(false);
        }

        return WindowsLocalGroupResolution.IsMemberAsync(identity, _groupSid, _database, cancellationToken);
    }
}

/// <summary>
/// The resolution and membership rules, behind one platform annotation.
///
/// They live here rather than on <see cref="WindowsGroupAuthorization"/> because
/// <see cref="SecurityIdentifier"/> is Windows-typed and a platform guard does not follow a call into
/// a lambda — the <c>Task.Run</c> has to sit on the annotated side. Nothing here reaches Win32
/// directly: every question goes through <see cref="IWindowsLocalSecurityDatabase"/>, which is what
/// makes the domain-controller case provable without a domain controller.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsLocalGroupResolution
{
    /// <summary>
    /// Proves the group belongs to the local database, then translates it, then insists the result is
    /// actually a group. Reported as a failure rather than thrown, so the agent can start, refuse
    /// control and say why.
    /// </summary>
    internal static (SecurityIdentifier? Sid, string? Failure) Resolve(string groupName, IWindowsLocalSecurityDatabase database)
    {
        var (exists, existenceFailure) = database.FindLocalGroup(groupName);
        if (!exists)
        {
            return (null, existenceFailure ?? $"The local group '{groupName}' does not exist on this server.");
        }

        var (sid, kind, domain, lookupFailure) = database.LookupAccount(groupName);
        if (string.IsNullOrWhiteSpace(sid))
        {
            return (null, lookupFailure ?? $"The local group '{groupName}' could not be translated to a SID.");
        }

        if (!IsGroup(kind))
        {
            // The name resolved, but not to something that can hold members. Accepting it would pin a
            // user or a computer as the authority over service control.
            return (null, $"'{groupName}' resolved to a {kind} rather than a group{Qualify(domain)}.");
        }

        try
        {
            return (new SecurityIdentifier(sid), null);
        }
        catch (Exception exception)
        {
            return (null, $"The SID resolved for '{groupName}' is not usable ({exception.GetType().Name}).");
        }
    }

    /// <summary>
    /// Whether the account belongs to the pinned group, directly or through a group that does.
    ///
    /// The groups are compared by SID rather than by name. The lookup returns names, and a name is the
    /// part of an identity that can be made to look like another one; the SID is the part that cannot.
    /// The candidate names are resolved through the same local database that produced the pinned SID,
    /// because resolving them any other way is the same mistake in its second half.
    /// </summary>
    // Scheduled here rather than in the guarded caller: a platform guard does not follow the call
    // into a lambda, so the lambda has to live on the annotated side.
    internal static Task<bool> IsMemberAsync(
        string identity, SecurityIdentifier groupSid, IWindowsLocalSecurityDatabase database, CancellationToken cancellationToken) =>
        Task.Run(() => IsMember(identity, groupSid, database), cancellationToken);

    internal static bool IsMember(string identity, SecurityIdentifier groupSid, IWindowsLocalSecurityDatabase database)
    {
        try
        {
            foreach (var name in database.GetLocalGroupNames(identity))
            {
                var (sid, kind, _, _) = database.LookupAccount(name);

                // One unresolvable group is not an answer about the others.
                if (string.IsNullOrWhiteSpace(sid) || !IsGroup(kind)) continue;

                SecurityIdentifier candidate;
                try
                {
                    candidate = new SecurityIdentifier(sid);
                }
                catch (Exception)
                {
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

    /// <summary>
    /// The kinds a group authority may take. <c>Alias</c> covers both a member server's SAM group and
    /// a domain controller's domain-local group, which is what the installation instructions create in
    /// either place; <c>Group</c> covers the global and universal forms. Everything else — a user, a
    /// computer, a well-known SID, a deleted or unknown account — is refused, and the local-group
    /// existence proof has already run before this is consulted.
    /// </summary>
    private static bool IsGroup(WindowsAccountKind kind) =>
        kind is WindowsAccountKind.Alias or WindowsAccountKind.Group;

    private static string Qualify(string? domain) =>
        string.IsNullOrWhiteSpace(domain) ? string.Empty : $" in '{domain}'";
}
