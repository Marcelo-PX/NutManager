using NutManager.Core.Agent;

namespace NutManager.Core.Models;

/// <summary>
/// Selects the transport used to reach the NutManager agent on the managed server.
///
/// Deliberately not <see cref="RemoteConfigurationTransportKind"/>. That one chooses how NUT's
/// configuration files are read and written — SFTP or SMB — and this one chooses how the agent that
/// controls the Windows service is reached. A profile can perfectly well edit configuration over SMB
/// while talking to the agent over a named pipe, and folding the two into one setting would make one
/// of those choices impossible to express.
/// </summary>
public enum NutAgentTransportKind
{
    /// <summary>The default. Authenticated by Windows, and the only transport enabled out of the box.</summary>
    NamedPipe,

    /// <summary>Optional, and off unless the server was deliberately configured for it.</summary>
    Https
}

/// <summary>
/// How this profile reaches its agent.
///
/// The host is not here on purpose: it comes from the profile's own NUT endpoint, the machine whose
/// NUT is being managed, rather than from the SMB share path or the SSH management host, which may
/// point somewhere else entirely. Only the choices that have nowhere else to live are stored.
/// </summary>
public sealed record NutAgentProfileSettings
{
    public static readonly NutAgentProfileSettings NamedPipeDefault = new(NutAgentTransportKind.NamedPipe);

    public NutAgentProfileSettings(
        NutAgentTransportKind transport,
        string? httpsEndpoint = null,
        NutAgentAuthenticationMode authentication = NutAgentAuthenticationMode.CurrentWindowsIdentity,
        string? username = null)
    {
        if (!Enum.IsDefined(transport))
        {
            throw new ArgumentOutOfRangeException(nameof(transport), "The agent transport is invalid.");
        }

        if (!Enum.IsDefined(authentication))
        {
            throw new ArgumentOutOfRangeException(nameof(authentication), "The agent authentication mode is invalid.");
        }

        Transport = transport;

        // The endpoint is kept only for the transport that uses it, so a profile switched back to
        // the named pipe cannot carry a stale HTTPS address that nothing validates any more.
        HttpsEndpoint = transport == NutAgentTransportKind.Https ? ValidateHttpsEndpoint(httpsEndpoint) : null;

        // An alternate account is only meaningful where a credential can be handed to Negotiate.
        // Over the named pipe the caller is whoever Windows already authenticated, so the setting
        // is normalised away rather than kept as a promise the transport cannot keep.
        Authentication = transport == NutAgentTransportKind.Https
            ? authentication
            : NutAgentAuthenticationMode.CurrentWindowsIdentity;

        Username = Authentication == NutAgentAuthenticationMode.AlternateWindowsAccount
            ? NormalizeUsername(username)
            : null;
    }

    public NutAgentTransportKind Transport { get; }

    public string? HttpsEndpoint { get; }

    public NutAgentAuthenticationMode Authentication { get; }

    /// <summary>
    /// The account name only. The password lives in the Windows Credential Manager under the
    /// agent's own target and never touches this record, the profile document, or a log.
    /// </summary>
    public string? Username { get; }

    private static string? NormalizeUsername(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();
        return trimmed.Length > 255
            ? throw new ArgumentException("The agent account name is too long.", nameof(value))
            : trimmed;
    }

    /// <summary>
    /// Accepts one scheme and refuses everything else.
    ///
    /// A plain-text scheme is not a degraded option here, it is a different product: the agent
    /// controls a service, and an unauthenticated or unencrypted path to it is not something a
    /// setting should be able to select. Credentials embedded in the authority are refused too — a
    /// URI is not a place to keep a password.
    /// </summary>
    public static string ValidateHttpsEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An HTTPS endpoint is required when the agent transport is HTTPS.", nameof(value));
        }

        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("The agent HTTPS endpoint must be an absolute URI.", nameof(value));
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The agent HTTPS endpoint must use https.", nameof(value));
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("The agent HTTPS endpoint must not embed credentials.", nameof(value));
        }

        if (uri.IsUnc || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("The agent HTTPS endpoint must name a host.", nameof(value));
        }

        return uri.ToString();
    }

    /// <summary>Whether a value would be accepted, for a settings editor that must not throw to ask.</summary>
    public static bool IsValidHttpsEndpoint(string? value)
    {
        try
        {
            ValidateHttpsEndpoint(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
