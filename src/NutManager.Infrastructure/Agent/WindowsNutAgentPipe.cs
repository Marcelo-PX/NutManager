using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace NutManager.Infrastructure.Agent;

/// <summary>
/// Who is allowed to reach the agent's pipe.
///
/// The name itself lives in Core, with the rest of the protocol, because both halves of the
/// transport have to agree on it and a name held twice is a name that will one day be changed once.
/// What belongs here is the part that is genuinely Windows: the access control.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsNutAgentPipe
{
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
