using System.Text.Json;
using System.Text.Json.Serialization;
using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.Persistence;

public sealed class JsonManagedNutServerProfileStore : IManagedNutServerProfileStore
{
    private const string FileName = "managed-servers.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly string _profilesPath;

    public JsonManagedNutServerProfileStore(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NutManager");
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ManagedNutServerProfilePersistenceException("The managed server profile directory is unavailable.");
        }

        _profilesPath = Path.Combine(root, FileName);
    }

    public string ProfilesPath => _profilesPath;

    public async Task<ManagedNutServerProfiles?> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_profilesPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(_profilesPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var document = await JsonSerializer.DeserializeAsync<ProfileDocument>(stream, SerializerOptions, cancellationToken)
                ?? throw new ManagedNutServerProfilePersistenceException("The managed server profiles JSON is empty.");
            return document.ToProfiles();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ManagedNutServerProfilePersistenceException("The managed server profiles JSON is malformed.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new ManagedNutServerProfilePersistenceException("The managed server profile values are invalid.", exception);
        }
        catch (IOException exception)
        {
            throw new ManagedNutServerProfilePersistenceException("The managed server profiles could not be read.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ManagedNutServerProfilePersistenceException("The managed server profiles could not be accessed.", exception);
        }
    }

    public async Task SaveAsync(ManagedNutServerProfiles profiles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        _ = new ManagedNutServerProfiles(profiles.SchemaVersion, profiles.ActiveProfileId, profiles.Profiles);
        cancellationToken.ThrowIfCancellationRequested();

        await _saveLock.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        var temporaryCreated = false;
        try
        {
            var directory = Path.GetDirectoryName(_profilesPath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                temporaryCreated = true;
                await JsonSerializer.SerializeAsync(stream, ProfileDocument.FromProfiles(profiles), SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _profilesPath, true);
            temporaryCreated = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new ManagedNutServerProfilePersistenceException("The managed server profiles could not be saved.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ManagedNutServerProfilePersistenceException("The managed server profiles could not be saved.", exception);
        }
        finally
        {
            if (temporaryCreated && temporaryPath is not null && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            _saveLock.Release();
        }
    }

    private sealed class ProfileDocument
    {
        public int SchemaVersion { get; set; }

        public Guid ActiveProfileId { get; set; }

        public List<ProfileEntry?>? Profiles { get; set; }

        public ManagedNutServerProfiles ToProfiles()
        {
            if (Profiles is null || Profiles.Any(profile => profile is null))
            {
                throw new ArgumentException("Profiles are required.");
            }

            if (SchemaVersion is < 1 or > ManagedNutServerProfiles.CurrentSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(SchemaVersion), "Unsupported managed server profile schema version.");
            }

            return new ManagedNutServerProfiles(
                ManagedNutServerProfiles.CurrentSchemaVersion,
                ActiveProfileId,
                Profiles.Select(profile => profile!.ToProfile(SchemaVersion)).ToArray());
        }

        public static ProfileDocument FromProfiles(ManagedNutServerProfiles profiles) => new()
        {
            SchemaVersion = profiles.SchemaVersion,
            ActiveProfileId = profiles.ActiveProfileId,
            Profiles = profiles.Profiles.Select(ProfileEntry.FromProfile).Cast<ProfileEntry?>().ToList()
        };
    }

    private sealed class ProfileEntry
    {
        public Guid Id { get; set; }

        public string? Name { get; set; }

        public string? MonitoringHost { get; set; }

        public int MonitoringPort { get; set; }

        public string? PreferredUpsName { get; set; }

        public NutManagementMode ManagementMode { get; set; }

        public string? ManagementHost { get; set; }

        public string? RemoteConfigurationDirectory { get; set; }

        public int SshPort { get; set; }

        public string? SshUsername { get; set; }

        public SshAuthenticationMode? SshAuthenticationMode { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SshPrivateKeyPath { get; set; }

        public string? TrustedHostKeyFingerprint { get; set; }

        public string? TrustedHostKeyAlgorithm { get; set; }

        public RemoteConfigurationTransportKind? ConfigurationTransport { get; set; }

        public string? SmbSharePath { get; set; }

        public string? SmbConfigurationDirectory { get; set; }

        public SmbAuthenticationMode? SmbAuthenticationMode { get; set; }

        public string? SmbUsername { get; set; }

        public ManagedNutServerAccessMode AccessMode { get; set; }

        public ManagedNutServerProfile ToProfile(int schemaVersion) => new(
            Id,
            Name!,
            new NutMonitoringProfile(MonitoringHost!, MonitoringPort, PreferredUpsName),
            new NutManagementProfile(
                ManagementMode,
                ManagementHost,
                RemoteConfigurationDirectory,
                schemaVersion == 1 ? NutManagementProfile.DefaultSshPort : SshPort,
                schemaVersion == 1 ? null : SshUsername,
                schemaVersion == 1 ? null : TrustedHostKeyFingerprint,
                schemaVersion == 1 ? null : TrustedHostKeyAlgorithm,
                schemaVersion < 3 ? RemoteConfigurationTransportKind.SshSftp : ConfigurationTransport ?? RemoteConfigurationTransportKind.SshSftp,
                schemaVersion < 3 ? null : SmbSharePath,
                schemaVersion < 3 ? null : SmbConfigurationDirectory,
                schemaVersion < 3 ? global::NutManager.Core.Models.SmbAuthenticationMode.CurrentWindowsIdentity : SmbAuthenticationMode ?? global::NutManager.Core.Models.SmbAuthenticationMode.CurrentWindowsIdentity,
                schemaVersion < 3 ? null : SmbUsername,
                schemaVersion < 4 ? global::NutManager.Core.Models.SshAuthenticationMode.Password : SshAuthenticationMode ?? global::NutManager.Core.Models.SshAuthenticationMode.Password,
                schemaVersion < 4 ? null : SshPrivateKeyPath),
            AccessMode);

        public static ProfileEntry FromProfile(ManagedNutServerProfile profile) => new()
        {
            Id = profile.Id,
            Name = profile.Name,
            MonitoringHost = profile.Monitoring.Host,
            MonitoringPort = profile.Monitoring.Port,
            PreferredUpsName = profile.Monitoring.PreferredUpsName,
            ManagementMode = profile.Management.Mode,
            ManagementHost = profile.Management.ManagementHost,
            RemoteConfigurationDirectory = profile.Management.RemoteConfigurationDirectory,
            SshPort = profile.Management.SshPort,
            SshUsername = profile.Management.SshUsername,
            SshAuthenticationMode = profile.Management.Mode == NutManagementMode.Remote && profile.Management.ConfigurationTransport == RemoteConfigurationTransportKind.SshSftp
                ? profile.Management.SshAuthenticationMode
                : null,
            SshPrivateKeyPath = profile.Management.SshPrivateKeyPath,
            TrustedHostKeyFingerprint = profile.Management.TrustedHostKeyFingerprint,
            TrustedHostKeyAlgorithm = profile.Management.TrustedHostKeyAlgorithm,
            ConfigurationTransport = profile.Management.Mode == NutManagementMode.Remote ? profile.Management.ConfigurationTransport : null,
            SmbSharePath = profile.Management.SmbSharePath,
            SmbConfigurationDirectory = profile.Management.SmbConfigurationDirectory,
            SmbAuthenticationMode = profile.Management.ConfigurationTransport == RemoteConfigurationTransportKind.Smb ? profile.Management.SmbAuthenticationMode : null,
            SmbUsername = profile.Management.SmbUsername,
            AccessMode = profile.AccessMode
        };
    }
}
