using System.Net;
using NutManager.Core.Agent;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Agent;

namespace NutManager.App.Services;

/// <summary>How an attempt to establish an agent credential ended.</summary>
public enum NutAgentCredentialOutcome
{
    /// <summary>The account authenticated against the agent itself, not merely against the dialog.</summary>
    Validated,

    /// <summary>The operator closed the dialog. Not a failure, and nothing changes.</summary>
    Cancelled,

    /// <summary>Windows could not show the credential dialog.</summary>
    PromptUnavailable,

    AccessDenied,
    AgentUnavailable,
    HostUnreachable,
    TimedOut,
    ProtocolFailure,
    Failed
}

/// <summary>
/// The result, carrying the account when there is one. Never the secret.
/// </summary>
public sealed record NutAgentCredentialResult(NutAgentCredentialOutcome Outcome, string? Username = null)
{
    public bool IsValidated => Outcome == NutAgentCredentialOutcome.Validated && !string.IsNullOrWhiteSpace(Username);
}

/// <summary>
/// Establishes an alternate Windows account for the agent, and is the only place that ever holds the
/// password.
///
/// The rule this class exists to enforce is that the Windows dialog returning OK is not
/// authentication. It collected a credential; whether that credential is any good is a question only
/// the agent can answer, and it is answered here by performing a real handshake before anything is
/// remembered, stored, or shown to the operator as valid. A password that was typed correctly for an
/// account with no rights on that server is a password that must not be saved.
///
/// The secret's lifetime is bounded and explicit. It exists as a disposable buffer for the length of
/// one validation, is copied into the session store only on success, and is disposed on every path —
/// including the failing ones, which are the paths where a forgotten buffer would otherwise linger.
/// </summary>
public sealed class NutAgentCredentialCoordinator
{
    private readonly IWindowsCredentialPrompt _prompt;
    private readonly INutAgentSessionCredentialStore _session;
    private readonly Func<string, NetworkCredential, INutManagerAgentClient> _clientFactory;

    public NutAgentCredentialCoordinator(
        IWindowsCredentialPrompt prompt,
        INutAgentSessionCredentialStore session,
        Func<string, NetworkCredential, INutManagerAgentClient>? clientFactory = null)
    {
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _session = session ?? throw new ArgumentNullException(nameof(session));

        // Injectable so the validation can be tested without a server; the default is the real
        // HTTPS client, which is the one the application will actually use afterwards.
        _clientFactory = clientFactory ?? CreateHttpsClient;
    }

    /// <summary>
    /// Prompts, then proves the credential against the agent at the given endpoint.
    ///
    /// On success the secret goes to the session store and the account name comes back. Nothing is
    /// written to the Credential Manager here: whether to persist is a decision that belongs to
    /// saving the profile, because a credential stored for a profile that was never saved is an
    /// orphan nobody will think to remove.
    /// </summary>
    public async Task<NutAgentCredentialResult> AuthenticateAsync(
        Guid profileId,
        string endpoint,
        string? preferredUsername,
        string caption,
        string message,
        nint ownerWindowHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !NutAgentProfileSettings.IsValidHttpsEndpoint(endpoint))
        {
            return new NutAgentCredentialResult(NutAgentCredentialOutcome.Failed);
        }

        using var prompted = await _prompt.RequestAsync(
            new WindowsCredentialPromptRequest(caption, message, preferredUsername, ownerWindowHandle, OfferToRemember: false),
            cancellationToken).ConfigureAwait(true);

        switch (prompted.Status)
        {
            case WindowsCredentialPromptStatus.Cancelled:
                return new NutAgentCredentialResult(NutAgentCredentialOutcome.Cancelled);

            case WindowsCredentialPromptStatus.Unsupported:
                return new NutAgentCredentialResult(NutAgentCredentialOutcome.PromptUnavailable);

            case WindowsCredentialPromptStatus.Failed:
                return new NutAgentCredentialResult(NutAgentCredentialOutcome.Failed);
        }

        if (!prompted.IsSuccess || prompted.Secret is not { } secret || prompted.Username is not { } username)
        {
            return new NutAgentCredentialResult(NutAgentCredentialOutcome.Failed);
        }

        var (user, domain) = NutAgentClientFactory.SplitAccount(username);
        var credential = BuildCredential(user, domain, secret.Memory.Span);

        var client = _clientFactory(endpoint, credential);
        try
        {
            // The handshake is the validation. It proves the account authenticated to this agent and
            // that the agent speaks a protocol this build understands; a status read afterwards would
            // add a round trip and prove nothing further.
            var handshake = await client.HandshakeAsync(endpoint, cancellationToken).ConfigureAwait(true);

            if (handshake.Status != NutAgentClientStatus.Success)
            {
                return new NutAgentCredentialResult(Map(handshake.Status));
            }

            _session.Store(profileId, username, secret.Memory.Span);
            return new NutAgentCredentialResult(NutAgentCredentialOutcome.Validated, username);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new NutAgentCredentialResult(NutAgentCredentialOutcome.Failed);
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Copies the session secret into the Credential Manager. Called when a profile that asked to
    /// remember its credential has actually been saved.
    /// </summary>
    public async Task<bool> PersistAsync(
        Guid profileId,
        string username,
        IRemoteCredentialStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (!_session.TryRead(profileId, username, out var secret)) return false;

        var result = await store
            .WriteAsync(profileId, RemoteCredentialKind.WindowsAgentPassword, secret, cancellationToken)
            .ConfigureAwait(true);

        return result.IsSuccess;
    }

    /// <summary>Drops the session copy. The persistent one is removed by the caller, deliberately.</summary>
    public void ForgetSession(Guid profileId) => _session.Forget(profileId);

    public bool HasSessionCredential(Guid profileId, out string? username) => _session.Contains(profileId, out username);

    private static NutAgentCredentialOutcome Map(NutAgentClientStatus status) => status switch
    {
        NutAgentClientStatus.AccessDenied => NutAgentCredentialOutcome.AccessDenied,
        NutAgentClientStatus.AgentUnavailable => NutAgentCredentialOutcome.AgentUnavailable,
        NutAgentClientStatus.HostUnreachable => NutAgentCredentialOutcome.HostUnreachable,
        NutAgentClientStatus.TimedOut => NutAgentCredentialOutcome.TimedOut,
        NutAgentClientStatus.ProtocolFailure => NutAgentCredentialOutcome.ProtocolFailure,
        _ => NutAgentCredentialOutcome.Failed
    };

    private static INutManagerAgentClient CreateHttpsClient(string endpoint, NetworkCredential credential) =>
        OperatingSystem.IsWindows()
            ? new WindowsHttpsNutAgentClient(endpoint, credential)
            : new UnavailableNutAgentClient("The HTTPS agent transport requires Windows.");

    private static NetworkCredential BuildCredential(string user, string? domain, ReadOnlySpan<char> secret)
    {
        var password = new System.Security.SecureString();
        foreach (var character in secret) password.AppendChar(character);
        password.MakeReadOnly();

        return new NetworkCredential(user, password, domain);
    }
}
