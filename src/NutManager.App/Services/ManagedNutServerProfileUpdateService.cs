using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.App.Services;

/// <summary>
/// Persists non-secret remote-management metadata without mutating the active runtime context.
/// </summary>
public sealed class ManagedNutServerProfileUpdateService
{
    private readonly IManagedNutServerProfileStore _store;

    public ManagedNutServerProfileUpdateService(IManagedNutServerProfileStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<ManagedNutServerProfile?> TrustHostKeyAsync(
        ManagedNutServerProfile expectedProfile,
        string algorithm,
        string fingerprint,
        CancellationToken cancellationToken = default) =>
        UpdateRemoteProfileAsync(
            expectedProfile,
            expectedProfile.Management.RemoteConfigurationDirectory,
            fingerprint,
            algorithm,
            cancellationToken);

    public Task<ManagedNutServerProfile?> SaveRemoteDirectoryAsync(
        ManagedNutServerProfile expectedProfile,
        string directory,
        CancellationToken cancellationToken = default) =>
        UpdateRemoteProfileAsync(
            expectedProfile,
            directory,
            expectedProfile.Management.TrustedHostKeyFingerprint,
            expectedProfile.Management.TrustedHostKeyAlgorithm,
            cancellationToken);

    private async Task<ManagedNutServerProfile?> UpdateRemoteProfileAsync(
        ManagedNutServerProfile expectedProfile,
        string? remoteDirectory,
        string? fingerprint,
        string? algorithm,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedProfile);
        var document = await _store.LoadAsync(cancellationToken);
        var current = document?.Profiles.SingleOrDefault(profile => profile.Id == expectedProfile.Id);
        if (document is null || current is null || !MatchesConnectionMetadata(current, expectedProfile))
        {
            return null;
        }

        var updated = new ManagedNutServerProfile(
            current.Id,
            current.Name,
            current.Monitoring,
            new NutManagementProfile(
                NutManagementMode.Remote,
                current.Management.ManagementHost,
                remoteDirectory,
                current.Management.SshPort,
                current.Management.SshUsername,
                fingerprint,
                algorithm),
            current.AccessMode);
        var profiles = document.Profiles.Select(profile => profile.Id == updated.Id ? updated : profile).ToArray();
        var saved = new ManagedNutServerProfiles(document.SchemaVersion, document.ActiveProfileId, profiles);
        await _store.SaveAsync(saved, cancellationToken);
        return updated;
    }

    private static bool MatchesConnectionMetadata(ManagedNutServerProfile current, ManagedNutServerProfile expected) =>
        current.Management.Mode == NutManagementMode.Remote &&
        expected.Management.Mode == NutManagementMode.Remote &&
        string.Equals(current.Management.ManagementHost, expected.Management.ManagementHost, StringComparison.Ordinal) &&
        current.Management.SshPort == expected.Management.SshPort &&
        string.Equals(current.Management.SshUsername, expected.Management.SshUsername, StringComparison.Ordinal);
}
