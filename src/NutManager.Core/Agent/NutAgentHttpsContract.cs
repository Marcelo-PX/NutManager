namespace NutManager.Core.Agent;

/// <summary>
/// The single route the HTTPS transport exposes, and the limits it enforces.
///
/// One path and one method. There is no <c>/start</c>, no <c>/service/{name}</c> and no GET health
/// endpoint: a URL that names an operation is a URL that can be guessed, and an unauthenticated
/// health probe on a privileged agent is a way to enumerate servers. Everything goes through the
/// same envelope the named pipe already uses, so both transports are parsed by the same code.
/// </summary>
public static class NutAgentHttpsProtocol
{
    /// <summary>Versioned in the path, matching the pipe's versioned name.</summary>
    public const string Path = "/v1/agent";

    public const string Method = "POST";

    public const string ContentType = "application/json";

    public const string TransportName = "Https";

    /// <summary>Deliberately the same ceiling as the pipe: the payload is identical.</summary>
    public const int MaxRequestBytes = NutAgentFraming.MaxRequestBytes;

    public const int MaxResponseBytes = NutAgentFraming.MaxResponseBytes;

    /// <summary>
    /// Whether a request line is the one this agent answers. Pure, so the routing decision can be
    /// asserted without a listener.
    /// </summary>
    public static bool IsAgentRoute(string? method, string? absolutePath) =>
        string.Equals(method, Method, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(absolutePath?.TrimEnd('/'), Path, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Which Windows identity the client presents to the agent.
///
/// Separate from the SMB and SSH authentication modes on purpose. Those choose how configuration
/// files are reached; this chooses who is asking the agent to control a service, and the same
/// operator may legitimately be a different principal for each.
/// </summary>
public enum NutAgentAuthenticationMode
{
    /// <summary>The account NutManager is already running as. No secret is stored or prompted for.</summary>
    CurrentWindowsIdentity,

    /// <summary>
    /// A different Windows account, supplied to Negotiate as an explicit credential. This is the
    /// mode that makes a non-domain client usable against a domain server without anyone having to
    /// establish a session outside the product.
    /// </summary>
    AlternateWindowsAccount
}
