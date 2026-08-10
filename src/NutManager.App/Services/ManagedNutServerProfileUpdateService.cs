using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.App.Services;

/// <summary>
/// Persists non-secret remote-management metadata without mutating the active runtime context.
/// </summary>
public sealed class ManagedNutServerProfileUpdateService
{
    private static readonly SemaphoreSlim UpdateLock = new(1, 1);
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
            current => new NutManagementProfile(
                NutManagementMode.Remote,
                current.Management.ManagementHost,
                current.Management.RemoteConfigurationDirectory,
                current.Management.SshPort,
                current.Management.SshUsername,
                fingerprint,
                algorithm),
            cancellationToken);

    public Task<ManagedNutServerProfile?> SaveRemoteDirectoryAsync(
        ManagedNutServerProfile expectedProfile,
        string directory,
        CancellationToken cancellationToken = default) =>
        UpdateRemoteProfileAsync(
            expectedProfile,
            current => new NutManagementProfile(
                NutManagementMode.Remote,
                current.Management.ManagementHost,
                directory,
                current.Management.SshPort,
                current.Management.SshUsername,
                current.Management.TrustedHostKeyFingerprint,
                current.Management.TrustedHostKeyAlgorithm),
            cancellationToken);

    public Task<ManagedNutServerProfile?> ForgetTrustedHostKeyAsync(
        ManagedNutServerProfile expectedProfile,
        CancellationToken cancellationToken = default) =>
        UpdateRemoteProfileAsync(
            expectedProfile,
            current => new NutManagementProfile(
                NutManagementMode.Remote,
                current.Management.ManagementHost,
                current.Management.RemoteConfigurationDirectory,
                current.Management.SshPort,
                current.Management.SshUsername),
            cancellationToken);

    private async Task<ManagedNutServerProfile?> UpdateRemoteProfileAsync(
        ManagedNutServerProfile expectedProfile,
        Func<ManagedNutServerProfile, NutManagementProfile> updateManagement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedProfile);
        ArgumentNullException.ThrowIfNull(updateManagement);
        await UpdateLock.WaitAsync(cancellationToken);
        try
        {
            var document = await _store.LoadAsync(cancellationToken);
            var current = document?.Profiles.SingleOrDefault(profile => profile.Id == expectedProfile.Id);
            if (document is null || current is null || !MatchesSessionIdentity(current, expectedProfile))
            {
                return null;
            }

            var updated = new ManagedNutServerProfile(
                current.Id,
                current.Name,
                current.Monitoring,
                updateManagement(current),
                current.AccessMode);
            var profiles = document.Profiles.Select(profile => profile.Id == updated.Id ? updated : profile).ToArray();
            var saved = new ManagedNutServerProfiles(document.SchemaVersion, document.ActiveProfileId, profiles);
            await _store.SaveAsync(saved, cancellationToken);
            return updated;
        }
        finally
        {
            UpdateLock.Release();
        }
    }

    private static bool MatchesSessionIdentity(ManagedNutServerProfile current, ManagedNutServerProfile expected) =>
        current.Management.Mode == NutManagementMode.Remote &&
        expected.Management.Mode == NutManagementMode.Remote &&
        string.Equals(current.Management.ManagementHost, expected.Management.ManagementHost, StringComparison.Ordinal) &&
        current.Management.SshPort == expected.Management.SshPort &&
        string.Equals(current.Management.SshUsername, expected.Management.SshUsername, StringComparison.Ordinal) &&
        string.Equals(current.Management.TrustedHostKeyFingerprint, expected.Management.TrustedHostKeyFingerprint, StringComparison.Ordinal) &&
        string.Equals(current.Management.TrustedHostKeyAlgorithm, expected.Management.TrustedHostKeyAlgorithm, StringComparison.Ordinal);
}
