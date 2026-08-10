using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.App.Services;

/// <summary>
/// Serializes every managed-profile read-modify-write operation without mutating the active runtime context.
/// </summary>
public sealed class ManagedNutServerProfileUpdateService
{
    private static readonly SemaphoreSlim MutationLock = new(1, 1);
    private readonly IManagedNutServerProfileStore _store;

    public ManagedNutServerProfileUpdateService(IManagedNutServerProfileStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ManagedNutServerProfiles?> LoadCurrentAsync(CancellationToken cancellationToken = default)
    {
        await MutationLock.WaitAsync(cancellationToken);
        try
        {
            return await _store.LoadAsync(cancellationToken);
        }
        finally
        {
            MutationLock.Release();
        }
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

    public async Task<ManagedNutServerProfiles?> SaveExistingProfileAsync(
        ManagedNutServerProfile baseProfile,
        ManagedNutServerProfile updatedProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseProfile);
        ArgumentNullException.ThrowIfNull(updatedProfile);
        if (baseProfile.Id != updatedProfile.Id)
        {
            throw new ArgumentException("The updated profile must retain the original identifier.", nameof(updatedProfile));
        }

        return await MutateAsync(document =>
        {
            var current = document.Profiles.SingleOrDefault(profile => profile.Id == baseProfile.Id);
            if (current is null || !Equals(current, baseProfile))
            {
                return null;
            }

            var safeguarded = PreserveCurrentTrustMetadata(current, updatedProfile);
            return ReplaceProfile(document, safeguarded);
        }, cancellationToken);
    }

    public async Task<ManagedNutServerProfiles?> CreateProfileAsync(ManagedNutServerProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return await MutateAsync(document =>
        {
            if (document.Profiles.Any(current => current.Id == profile.Id || string.Equals(current.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            return new ManagedNutServerProfiles(
                document.SchemaVersion,
                document.ActiveProfileId,
                document.Profiles.Append(profile).OrderBy(current => current.Name, StringComparer.OrdinalIgnoreCase).ToArray());
        }, cancellationToken);
    }

    public async Task<ManagedNutServerProfiles?> DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("A profile identifier is required.", nameof(profileId));
        }

        return await MutateAsync(document =>
        {
            if (document.Profiles.Count <= 1 || document.ActiveProfileId == profileId || document.Profiles.All(profile => profile.Id != profileId))
            {
                return null;
            }

            return new ManagedNutServerProfiles(
                document.SchemaVersion,
                document.ActiveProfileId,
                document.Profiles.Where(profile => profile.Id != profileId).ToArray());
        }, cancellationToken);
    }

    public async Task<ManagedNutServerProfiles?> ActivateProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("A profile identifier is required.", nameof(profileId));
        }

        return await MutateAsync(document => document.Profiles.Any(profile => profile.Id == profileId)
            ? new ManagedNutServerProfiles(document.SchemaVersion, profileId, document.Profiles)
            : null, cancellationToken);
    }

    private async Task<ManagedNutServerProfile?> UpdateRemoteProfileAsync(
        ManagedNutServerProfile expectedProfile,
        Func<ManagedNutServerProfile, NutManagementProfile> updateManagement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedProfile);
        ArgumentNullException.ThrowIfNull(updateManagement);
        var mutation = await MutateAsync(document =>
        {
            var current = document.Profiles.SingleOrDefault(profile => profile.Id == expectedProfile.Id);
            if (current is null || !MatchesSessionIdentity(current, expectedProfile))
            {
                return null;
            }

            var updated = new ManagedNutServerProfile(
                current.Id,
                current.Name,
                current.Monitoring,
                updateManagement(current),
                current.AccessMode);
            return new ProfileMutationResult(ReplaceProfile(document, updated), updated);
        }, cancellationToken);
        return mutation?.Profile;
    }

    private async Task<T?> MutateAsync<T>(Func<ManagedNutServerProfiles, T?> mutation, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await MutationLock.WaitAsync(cancellationToken);
        try
        {
            var document = await _store.LoadAsync(cancellationToken);
            if (document is null)
            {
                return null;
            }

            var result = mutation(document);
            if (result is null)
            {
                return null;
            }

            var saved = result is ProfileMutationResult profileMutation
                ? profileMutation.Document
                : result as ManagedNutServerProfiles;
            if (saved is null)
            {
                throw new InvalidOperationException("The profile mutation returned an unsupported result.");
            }

            await _store.SaveAsync(saved, cancellationToken);
            return result;
        }
        finally
        {
            MutationLock.Release();
        }
    }

    private static ManagedNutServerProfiles ReplaceProfile(ManagedNutServerProfiles document, ManagedNutServerProfile updated) =>
        new(document.SchemaVersion, document.ActiveProfileId, document.Profiles.Select(profile => profile.Id == updated.Id ? updated : profile).ToArray());

    private static ManagedNutServerProfile PreserveCurrentTrustMetadata(ManagedNutServerProfile current, ManagedNutServerProfile updated)
    {
        if (updated.Management.Mode != NutManagementMode.Remote)
        {
            return updated;
        }

        var management = new NutManagementProfile(
            NutManagementMode.Remote,
            updated.Management.ManagementHost,
            updated.Management.RemoteConfigurationDirectory,
            updated.Management.SshPort,
            updated.Management.SshUsername,
            current.Management.TrustedHostKeyFingerprint,
            current.Management.TrustedHostKeyAlgorithm);
        return new ManagedNutServerProfile(updated.Id, updated.Name, updated.Monitoring, management, updated.AccessMode);
    }

    private sealed record ProfileMutationResult(ManagedNutServerProfiles Document, ManagedNutServerProfile Profile);

    private static bool MatchesSessionIdentity(ManagedNutServerProfile current, ManagedNutServerProfile expected) =>
        current.Management.Mode == NutManagementMode.Remote &&
        expected.Management.Mode == NutManagementMode.Remote &&
        string.Equals(current.Management.ManagementHost, expected.Management.ManagementHost, StringComparison.Ordinal) &&
        current.Management.SshPort == expected.Management.SshPort &&
        string.Equals(current.Management.SshUsername, expected.Management.SshUsername, StringComparison.Ordinal) &&
        string.Equals(current.Management.TrustedHostKeyFingerprint, expected.Management.TrustedHostKeyFingerprint, StringComparison.Ordinal) &&
        string.Equals(current.Management.TrustedHostKeyAlgorithm, expected.Management.TrustedHostKeyAlgorithm, StringComparison.Ordinal);
}
