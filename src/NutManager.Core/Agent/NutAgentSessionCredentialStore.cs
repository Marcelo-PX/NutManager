using NutManager.Core.Models;

namespace NutManager.Core.Agent;

/// <summary>
/// Holds an agent credential that was validated but deliberately not saved.
///
/// It exists because "remember this credential" and "use this credential now" are different
/// decisions. An operator who declines to store a password still expects the connection they just
/// authenticated to keep working for the rest of the session, and the only honest way to offer that
/// is to keep the secret in memory and nowhere else.
///
/// Memory only, by construction. There is no path here that writes to a file, a registry key or the
/// Credential Manager, so a secret entrusted to this store cannot outlive the process that was told
/// not to persist it.
/// </summary>
public interface INutAgentSessionCredentialStore
{
    /// <summary>
    /// Keeps a copy of the secret for this profile and account, replacing and disposing whatever was
    /// held before. The caller keeps ownership of what it passed in.
    /// </summary>
    void Store(Guid profileId, string username, ReadOnlySpan<char> secret);

    /// <summary>
    /// Lends the secret for the given profile and account.
    ///
    /// The account has to match. A credential validated for one account says nothing about another,
    /// and returning it for a profile that has since been pointed at a different user would be
    /// authenticating as someone the operator did not choose.
    /// </summary>
    bool TryRead(Guid profileId, string username, out ReadOnlyMemory<char> secret);

    /// <summary>Whether a secret is held for this profile, without revealing it.</summary>
    bool Contains(Guid profileId, out string? username);

    /// <summary>Drops and zeroes the secret for one profile.</summary>
    void Forget(Guid profileId);
}

/// <summary>
/// The in-memory implementation. Every replacement and every removal disposes the buffer it
/// displaced, so a superseded password does not sit in the heap waiting to be found.
/// </summary>
public sealed class NutAgentSessionCredentialStore : INutAgentSessionCredentialStore, IDisposable
{
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly Lock _gate = new();
    private bool _disposed;

    public void Store(Guid profileId, string username, ReadOnlySpan<char> secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        if (secret.IsEmpty) throw new ArgumentException("A credential secret is required.", nameof(secret));

        var entry = new Entry(username, new RemoteCredentialSecret(secret));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_entries.Remove(profileId, out var previous)) previous.Secret.Dispose();
            _entries[profileId] = entry;
        }
    }

    public bool TryRead(Guid profileId, string username, out ReadOnlyMemory<char> secret)
    {
        secret = ReadOnlyMemory<char>.Empty;
        if (string.IsNullOrWhiteSpace(username)) return false;

        lock (_gate)
        {
            if (_disposed || !_entries.TryGetValue(profileId, out var entry)) return false;
            if (!string.Equals(entry.Username, username, StringComparison.OrdinalIgnoreCase)) return false;

            secret = entry.Secret.Memory;
            return !secret.IsEmpty;
        }
    }

    public bool Contains(Guid profileId, out string? username)
    {
        lock (_gate)
        {
            if (!_disposed && _entries.TryGetValue(profileId, out var entry))
            {
                username = entry.Username;
                return true;
            }

            username = null;
            return false;
        }
    }

    public void Forget(Guid profileId)
    {
        lock (_gate)
        {
            if (_entries.Remove(profileId, out var entry)) entry.Secret.Dispose();
        }
    }

    /// <summary>Clears everything, which is what shutting the application down must do.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var entry in _entries.Values) entry.Secret.Dispose();
            _entries.Clear();
        }
    }

    private sealed record Entry(string Username, RemoteCredentialSecret Secret);
}
