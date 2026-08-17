using System.Net;
using NutManager.Core.Agent;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Agent;

namespace NutManager.App.Services;

/// <summary>
/// Builds the agent client the profile asked for, and only that one.
///
/// There is deliberately no fallback of any kind here. A profile that selects HTTPS does not quietly
/// get a named pipe when the endpoint is wrong, and a profile that selects the named pipe never tries
/// HTTPS — an operator who cannot tell which transport answered cannot diagnose either. The same rule
/// that forbids falling back to the remote SCM forbids falling back between transports.
///
/// The alternate account's password is read here and handed straight to the client's handler. It is
/// never placed in the profile, never logged, and never travels in the agent protocol, which has no
/// field for it.
/// </summary>
public static class NutAgentClientFactory
{
    public static async Task<INutManagerAgentClient> CreateAsync(
        NutAgentProfileSettings settings,
        Guid profileId,
        IRemoteCredentialStore credentialStore,
        CancellationToken cancellationToken,
        INutAgentSessionCredentialStore? sessionCredentials = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credentialStore);

        if (settings.Transport == NutAgentTransportKind.NamedPipe)
        {
            return new WindowsNamedPipeNutAgentClient();
        }

        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(settings.HttpsEndpoint))
        {
            return new UnavailableNutAgentClient("The agent HTTPS endpoint is not configured.");
        }

        if (settings.Authentication != NutAgentAuthenticationMode.AlternateWindowsAccount)
        {
            return new WindowsHttpsNutAgentClient(settings.HttpsEndpoint);
        }

        if (string.IsNullOrWhiteSpace(settings.Username))
        {
            return new UnavailableNutAgentClient("The alternate Windows account has no user name configured.");
        }

        var (user, domain) = SplitAccount(settings.Username);

        // A credential the operator validated but asked not to save is still the credential they
        // chose for this session, so it is consulted before the stored one. Preferring the saved
        // copy would mean an account they just replaced kept being used until the app restarted.
        if (sessionCredentials is not null &&
            sessionCredentials.TryRead(profileId, settings.Username, out var session) &&
            !session.IsEmpty)
        {
            return new WindowsHttpsNutAgentClient(settings.HttpsEndpoint, BuildCredential(user, domain, session.Span));
        }

        var secret = await credentialStore
            .ReadAsync(profileId, RemoteCredentialKind.WindowsAgentPassword, cancellationToken)
            .ConfigureAwait(false);

        if (secret.Status != RemoteCredentialStoreStatus.Success || secret.Secret is not { } stored)
        {
            // The account was chosen but nothing was stored for it, so there is nothing to
            // authenticate with. Saying so beats connecting as the wrong identity.
            return new UnavailableNutAgentClient("No stored credential was found for the alternate Windows account.");
        }

        // The stored buffer is copied into the credential and then zeroed, so the secret does not
        // outlive this method as a managed string anyone could later find on the heap.
        using (stored)
        {
            return new WindowsHttpsNutAgentClient(settings.HttpsEndpoint, BuildCredential(user, domain, stored.Memory.Span));
        }
    }

    private static NetworkCredential BuildCredential(string user, string? domain, ReadOnlySpan<char> secret)
    {
        var password = new System.Security.SecureString();
        foreach (var character in secret) password.AppendChar(character);
        password.MakeReadOnly();

        return new NetworkCredential(user, password, domain);
    }

    /// <summary>
    /// Splits DOMAIN\user, which is the form Windows shows an operator, into what NetworkCredential
    /// expects. A bare name is left alone so the platform resolves it as it normally would.
    /// </summary>
    public static (string User, string? Domain) SplitAccount(string account)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);

        var trimmed = account.Trim();
        var separator = trimmed.IndexOf('\\', StringComparison.Ordinal);

        return separator > 0
            ? (trimmed[(separator + 1)..], trimmed[..separator])
            : (trimmed, null);
    }
}
