using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace NutManager.Infrastructure.Agent;

/// <summary>
/// The named pipe both ends agree on: its name, and who is allowed to reach it.
///
/// It lives here rather than in the agent executable because the client has to name the same pipe,
/// and a transport whose two halves each hold their own copy of the name is a transport that will one
/// day be renamed in one of them.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsNutAgentPipe
{
    /// <summary>Versioned in the name, so a future incompatible protocol is a different pipe.</summary>
    public const string PipeName = "NutManager.Agent.v1";

    public const string TransportName = "NamedPipe";

    /// <summary>
    /// The pipe's access control.
    ///
    /// Two allow entries: LocalSystem, which is the agent itself, and the operators group. Everyone,
    /// Authenticated Users and Users are simply absent, and absence is what refuses them — a pipe
    /// grants nothing it was not told to grant.
    ///
    /// There is deliberately no explicit deny for Everyone. A deny entry outranks every allow entry,
    /// and every operator is also a member of Everyone, so that "hardening" rule would refuse exactly
    /// the people the pipe exists for. ANONYMOUS LOGON is the case where a deny is both safe and
    /// meaningful: no authenticated caller carries it, so denying it closes a null-session path
    /// without touching anyone legitimate.
    /// </summary>
    public static PipeSecurity CreateSecurity(SecurityIdentifier operatorsGroup)
    {
        ArgumentNullException.ThrowIfNull(operatorsGroup);

        var security = new PipeSecurity();

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            operatorsGroup,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AnonymousSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        return security;
    }
}
