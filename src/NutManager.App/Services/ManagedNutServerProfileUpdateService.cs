using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.App.Services;

/// <summary>
/// Serializes profile metadata and app-owned credential mutations. A secret is only
/// written after the persisted profile still matches the session identity that connected.
/// </summary>
public sealed class ManagedNutServerProfileUpdateService
{
    private static readonly SemaphoreSlim MutationLock = new(1, 1);
    private readonly IManagedNutServerProfileStore _store;
    private readonly IRemoteCredentialStore? _credentialStore;

    public ManagedNutServerProfileUpdateService(IManagedNutServerProfileStore store, IRemoteCredentialStore? credentialStore = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _credentialStore = credentialStore;
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

    public Task<ManagedNutServerProfile?> TrustHostKeyAsync(ManagedNutServerProfile expectedProfile, string algorithm, string fingerprint, CancellationToken cancellationToken = default) =>
        UpdateRemoteProfileAsync(
            expectedProfile,
            current => CreateSshManagement(
                current.Management,
                trustedHostKeyFingerprint: fingerprint,
                trustedHostKeyAlgorithm: algorithm,
                preserveTrust: false),
            cancellationToken);

    public Task<ManagedNutServerProfile?> SaveRemoteDirectoryAsync(ManagedNutServerProfile expectedProfile, string directory, CancellationToken cancellationToken = default) =>
        UpdateRemoteProfileAsync(
            expectedProfile,
            current => current.Management.ConfigurationTransport == RemoteConfigurationTransportKind.Smb
                ? new NutManagementProfile(
                    NutManagementMode.Remote,
                    configurationTransport: RemoteConfigurationTransportKind.Smb,
                    smbSharePath: current.Management.SmbSharePath,
                    smbConfigurationDirectory: directory,
                    smbAuthenticationMode: current.Management.SmbAuthenticationMode,
                    smbUsername: current.Management.SmbUsername)
                : CreateSshManagement(current.Management, remoteConfigurationDirectory: directory),
            cancellationToken);

    public Task<ManagedNutServerProfile?> ForgetTrustedHostKeyAsync(ManagedNutServerProfile expectedProfile, CancellationToken cancellationToken = default) =>
        UpdateRemoteProfileAsync(
            expectedProfile,
            current => CreateSshManagement(current.Management, trustedHostKeyFingerprint: null, trustedHostKeyAlgorithm: null, preserveTrust: false),
            cancellationToken);

    public async Task<ManagedNutServerProfiles?> SaveExistingProfileAsync(ManagedNutServerProfile baseProfile, ManagedNutServerProfile updatedProfile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseProfile);
        ArgumentNullException.ThrowIfNull(updatedProfile);
        if (baseProfile.Id != updatedProfile.Id)
        {
            throw new ArgumentException("The updated profile must retain the original identifier.", nameof(updatedProfile));
        }

        await MutationLock.WaitAsync(cancellationToken);
        try
        {
            var document = await _store.LoadAsync(cancellationToken);
            var current = document?.Profiles.SingleOrDefault(profile => profile.Id == baseProfile.Id);
            if (document is null || current is null || !Equals(current, baseProfile))
            {
                return null;
            }

            var safeguarded = PreserveCurrentTrustMetadata(current, updatedProfile);
            var invalidation = await InvalidateChangedCredentialsAsync(current, safeguarded, cancellationToken);
            if (!invalidation.IsSuccess)
            {
                throw new InvalidOperationException("A credencial protegida não pôde ser removida antes de salvar a nova identidade do perfil.");
            }

            var saved = ReplaceProfile(document, safeguarded);
            await _store.SaveAsync(saved, cancellationToken);
            return saved;
        }
        finally
        {
            MutationLock.Release();
        }
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

            return new ManagedNutServerProfiles(document.SchemaVersion, document.ActiveProfileId, document.Profiles.Append(profile).OrderBy(current => current.Name, StringComparer.OrdinalIgnoreCase).ToArray());
        }, cancellationToken);
    }

    public async Task<ManagedNutServerProfiles?> DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("A profile identifier is required.", nameof(profileId));
        }

        await MutationLock.WaitAsync(cancellationToken);
        try
        {
            var document = await _store.LoadAsync(cancellationToken);
            var profile = document?.Profiles.SingleOrDefault(current => current.Id == profileId);
            if (document is null || profile is null || document.Profiles.Count <= 1 || document.ActiveProfileId == profileId)
            {
                return null;
            }

            if (_credentialStore is not null)
            {
                var cleanup = await _credentialStore.DeleteAllForProfileAsync(profileId, cancellationToken);
                if (!cleanup.IsSuccess)
                {
                    throw new InvalidOperationException("As credenciais protegidas não puderam ser removidas antes de excluir o perfil.");
                }
            }

            var saved = new ManagedNutServerProfiles(document.SchemaVersion, document.ActiveProfileId, document.Profiles.Where(current => current.Id != profileId).ToArray());
            await _store.SaveAsync(saved, cancellationToken);
            return saved;
        }
        finally
        {
            MutationLock.Release();
        }
    }

    public Task<ManagedNutServerProfiles?> ActivateProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("A profile identifier is required.", nameof(profileId));
        }

        return MutateAsync(document => document.Profiles.Any(profile => profile.Id == profileId)
            ? new ManagedNutServerProfiles(document.SchemaVersion, profileId, document.Profiles)
            : null, cancellationToken);
    }

    public async Task<RemoteCredentialStoreResult> SaveCredentialForCurrentSessionAsync(ManagedNutServerProfile expectedProfile, RemoteCredentialKind kind, ReadOnlyMemory<char> secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedProfile);
        if (_credentialStore is null)
        {
            return StoreUnavailable();
        }

        await MutationLock.WaitAsync(cancellationToken);
        try
        {
            var document = await _store.LoadAsync(cancellationToken);
            var current = document?.Profiles.SingleOrDefault(profile => profile.Id == expectedProfile.Id);
            if (current is null || !MatchesSessionIdentity(current, expectedProfile) || !IsCredentialKindAllowed(current, kind))
            {
                return new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.Failed, "O perfil foi alterado; a credencial não foi salva.");
            }

            return await _credentialStore.WriteAsync(current.Id, kind, secret, cancellationToken);
        }
        finally
        {
            MutationLock.Release();
        }
    }

    public async Task<RemoteCredentialStoreResult> ForgetCredentialAsync(Guid profileId, RemoteCredentialKind kind, CancellationToken cancellationToken = default)
    {
        if (_credentialStore is null)
        {
            return StoreUnavailable();
        }

        await MutationLock.WaitAsync(cancellationToken);
        try
        {
            var document = await _store.LoadAsync(cancellationToken);
            if (document?.Profiles.Any(profile => profile.Id == profileId) != true)
            {
                return new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.NotFound);
            }

            return await _credentialStore.DeleteAsync(profileId, kind, cancellationToken);
        }
        finally
        {
            MutationLock.Release();
        }
    }

    private async Task<ManagedNutServerProfile?> UpdateRemoteProfileAsync(ManagedNutServerProfile expectedProfile, Func<ManagedNutServerProfile, NutManagementProfile> updateManagement, CancellationToken cancellationToken)
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

            var updated = new ManagedNutServerProfile(current.Id, current.Name, current.Monitoring, updateManagement(current), current.AccessMode);
            return new ProfileMutationResult(ReplaceProfile(document, updated), updated);
        }, cancellationToken);
        return mutation?.Profile;
    }

    private async Task<T?> MutateAsync<T>(Func<ManagedNutServerProfiles, T?> mutation, CancellationToken cancellationToken)
        where T : class
    {
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

            var saved = result is ProfileMutationResult profileMutation ? profileMutation.Document : result as ManagedNutServerProfiles;
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

    private async Task<RemoteCredentialStoreResult> InvalidateChangedCredentialsAsync(ManagedNutServerProfile current, ManagedNutServerProfile updated, CancellationToken cancellationToken)
    {
        if (_credentialStore is null)
        {
            return new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.Success);
        }

        if (HasSshIdentityChanged(current, updated))
        {
            foreach (var kind in new[] { RemoteCredentialKind.SshPassword, RemoteCredentialKind.SshPrivateKeyPassphrase })
            {
                var result = await _credentialStore.DeleteAsync(current.Id, kind, cancellationToken);
                if (!result.IsSuccess)
                {
                    return result;
                }
            }
        }

        return HasSmbIdentityChanged(current, updated)
            ? await _credentialStore.DeleteAsync(current.Id, RemoteCredentialKind.SmbPassword, cancellationToken)
            : new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.Success);
    }

    private static ManagedNutServerProfiles ReplaceProfile(ManagedNutServerProfiles document, ManagedNutServerProfile updated) =>
        new(document.SchemaVersion, document.ActiveProfileId, document.Profiles.Select(profile => profile.Id == updated.Id ? updated : profile).ToArray());

    private static ManagedNutServerProfile PreserveCurrentTrustMetadata(ManagedNutServerProfile current, ManagedNutServerProfile updated)
    {
        if (updated.Management.Mode != NutManagementMode.Remote || updated.Management.ConfigurationTransport != RemoteConfigurationTransportKind.SshSftp)
        {
            return updated;
        }

        var management = CreateSshManagement(
            updated.Management,
            trustedHostKeyFingerprint: current.Management.TrustedHostKeyFingerprint,
            trustedHostKeyAlgorithm: current.Management.TrustedHostKeyAlgorithm,
            preserveTrust: false);
        return new ManagedNutServerProfile(updated.Id, updated.Name, updated.Monitoring, management, updated.AccessMode);
    }

    private static NutManagementProfile CreateSshManagement(
        NutManagementProfile source,
        string? remoteConfigurationDirectory = null,
        string? trustedHostKeyFingerprint = null,
        string? trustedHostKeyAlgorithm = null,
        bool preserveTrust = true) =>
        new(
            NutManagementMode.Remote,
            source.ManagementHost,
            remoteConfigurationDirectory ?? source.RemoteConfigurationDirectory,
            source.SshPort,
            source.SshUsername,
            preserveTrust ? source.TrustedHostKeyFingerprint : trustedHostKeyFingerprint,
            preserveTrust ? source.TrustedHostKeyAlgorithm : trustedHostKeyAlgorithm,
            RemoteConfigurationTransportKind.SshSftp,
            sshAuthenticationMode: source.SshAuthenticationMode,
            sshPrivateKeyPath: source.SshPrivateKeyPath);

    private sealed record ProfileMutationResult(ManagedNutServerProfiles Document, ManagedNutServerProfile Profile);

    private static bool MatchesSessionIdentity(ManagedNutServerProfile current, ManagedNutServerProfile expected) =>
        current.Management.Mode == NutManagementMode.Remote &&
        expected.Management.Mode == NutManagementMode.Remote &&
        current.Management.ConfigurationTransport == expected.Management.ConfigurationTransport &&
        (current.Management.ConfigurationTransport == RemoteConfigurationTransportKind.Smb
            ? string.Equals(current.Management.SmbSharePath, expected.Management.SmbSharePath, StringComparison.OrdinalIgnoreCase) &&
              current.Management.SmbAuthenticationMode == expected.Management.SmbAuthenticationMode &&
              string.Equals(current.Management.SmbUsername, expected.Management.SmbUsername, StringComparison.Ordinal)
            : string.Equals(current.Management.ManagementHost, expected.Management.ManagementHost, StringComparison.Ordinal) &&
              current.Management.SshPort == expected.Management.SshPort &&
              string.Equals(current.Management.SshUsername, expected.Management.SshUsername, StringComparison.Ordinal) &&
              current.Management.SshAuthenticationMode == expected.Management.SshAuthenticationMode &&
              string.Equals(current.Management.SshPrivateKeyPath, expected.Management.SshPrivateKeyPath, StringComparison.Ordinal) &&
              string.Equals(current.Management.TrustedHostKeyFingerprint, expected.Management.TrustedHostKeyFingerprint, StringComparison.Ordinal) &&
              string.Equals(current.Management.TrustedHostKeyAlgorithm, expected.Management.TrustedHostKeyAlgorithm, StringComparison.Ordinal));

    private static bool HasSshIdentityChanged(ManagedNutServerProfile current, ManagedNutServerProfile updated)
    {
        var wasSsh = current.Management.Mode == NutManagementMode.Remote && current.Management.ConfigurationTransport == RemoteConfigurationTransportKind.SshSftp;
        var remainsSsh = updated.Management.Mode == NutManagementMode.Remote && updated.Management.ConfigurationTransport == RemoteConfigurationTransportKind.SshSftp;
        return wasSsh && (!remainsSsh ||
            !string.Equals(current.Management.ManagementHost, updated.Management.ManagementHost, StringComparison.Ordinal) ||
            current.Management.SshPort != updated.Management.SshPort ||
            !string.Equals(current.Management.SshUsername, updated.Management.SshUsername, StringComparison.Ordinal) ||
            current.Management.SshAuthenticationMode != updated.Management.SshAuthenticationMode ||
            !string.Equals(current.Management.SshPrivateKeyPath, updated.Management.SshPrivateKeyPath, StringComparison.Ordinal));
    }

    private static bool HasSmbIdentityChanged(ManagedNutServerProfile current, ManagedNutServerProfile updated)
    {
        var wasSmb = current.Management.Mode == NutManagementMode.Remote && current.Management.ConfigurationTransport == RemoteConfigurationTransportKind.Smb;
        var remainsSmb = updated.Management.Mode == NutManagementMode.Remote && updated.Management.ConfigurationTransport == RemoteConfigurationTransportKind.Smb;
        return wasSmb && (!remainsSmb ||
            !string.Equals(current.Management.SmbSharePath, updated.Management.SmbSharePath, StringComparison.OrdinalIgnoreCase) ||
            current.Management.SmbAuthenticationMode != updated.Management.SmbAuthenticationMode ||
            !string.Equals(current.Management.SmbUsername, updated.Management.SmbUsername, StringComparison.Ordinal));
    }

    private static bool IsCredentialKindAllowed(ManagedNutServerProfile profile, RemoteCredentialKind kind) => kind switch
    {
        RemoteCredentialKind.SshPassword => profile.Management.Mode == NutManagementMode.Remote && profile.Management.ConfigurationTransport == RemoteConfigurationTransportKind.SshSftp && profile.Management.SshAuthenticationMode == SshAuthenticationMode.Password,
        RemoteCredentialKind.SshPrivateKeyPassphrase => profile.Management.Mode == NutManagementMode.Remote && profile.Management.ConfigurationTransport == RemoteConfigurationTransportKind.SshSftp && profile.Management.SshAuthenticationMode == SshAuthenticationMode.PrivateKey && !string.IsNullOrWhiteSpace(profile.Management.SshPrivateKeyPath),
        RemoteCredentialKind.SmbPassword => profile.Management.Mode == NutManagementMode.Remote && profile.Management.ConfigurationTransport == RemoteConfigurationTransportKind.Smb && profile.Management.SmbAuthenticationMode == SmbAuthenticationMode.ExplicitCredentials,
        _ => false
    };

    private static RemoteCredentialStoreResult StoreUnavailable() => new(RemoteCredentialStoreStatus.Unsupported, "O armazenamento protegido de credenciais não está disponível.");
}
