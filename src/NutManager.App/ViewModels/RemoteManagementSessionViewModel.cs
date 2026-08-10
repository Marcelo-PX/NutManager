using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NutManager.App.Services;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Configuration;

namespace NutManager.App.ViewModels;

public sealed partial class RemoteManagementSessionViewModel : ObservableObject, IAsyncDisposable
{
    private ManagedNutServerProfile _profile;
    private readonly IRemoteNutConfigurationTransport _transport;
    private readonly ManagedNutServerProfileUpdateService? _profileUpdater;
    private readonly IRemoteCredentialStore? _credentialStore;
    private IRemoteNutConfigurationSession? _session;
    private RemoteNutDirectoryValidationResult? _directoryValidation;

    public RemoteManagementSessionViewModel(
        ManagedNutServerProfile profile,
        IRemoteNutConfigurationTransport transport,
        ManagedNutServerProfileUpdateService? profileUpdater = null,
        IRemoteCredentialStore? credentialStore = null)
    {
        if (profile.Management.Mode != NutManagementMode.Remote)
        {
            throw new ArgumentException("A remote profile is required.", nameof(profile));
        }

        _profile = profile;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _profileUpdater = profileUpdater;
        _credentialStore = credentialStore;
        DirectoryEntries = new ObservableCollection<RemoteNutDirectoryEntry>();
        CurrentDirectory = profile.Management.ConfigurationTransport == RemoteConfigurationTransportKind.Smb
            ? profile.Management.SmbConfigurationDirectory ?? profile.Management.SmbSharePath ?? string.Empty
            : profile.Management.RemoteConfigurationDirectory ?? string.Empty;
    }

    public event Action<INutConfigurationFilePipeline?, RemoteNutDirectoryValidationResult?, bool>? ConfigurationContextChanged;

    public ObservableCollection<RemoteNutDirectoryEntry> DirectoryEntries { get; }

    [ObservableProperty]
    private RemoteNutConnectionState _connectionState = RemoteNutConnectionState.Disconnected;

    [ObservableProperty]
    private string _currentDirectory;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private RemoteNutHostKeyInfo? _presentedHostKey;

    [ObservableProperty]
    private RemoteNutPlatform _platform = RemoteNutPlatform.Unknown;

    [ObservableProperty]
    private RemoteNutWriteCapabilityResult? _writeCapability;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private RemoteCredentialStoreStatus _storedCredentialStatus = RemoteCredentialStoreStatus.NotFound;

    public bool IsSshSftp => _profile.Management.ConfigurationTransport == RemoteConfigurationTransportKind.SshSftp;

    public bool IsSmb => _profile.Management.ConfigurationTransport == RemoteConfigurationTransportKind.Smb;

    public bool UsesSmbExplicitCredentials => IsSmb && _profile.Management.SmbAuthenticationMode == SmbAuthenticationMode.ExplicitCredentials;

    public bool UsesSmbCurrentWindowsIdentity => IsSmb && !UsesSmbExplicitCredentials;

    public bool UsesSshPassword => IsSshSftp && _profile.Management.SshAuthenticationMode == SshAuthenticationMode.Password;

    public bool UsesSshPrivateKey => IsSshSftp && _profile.Management.SshAuthenticationMode == SshAuthenticationMode.PrivateKey;

    public string ConfigurationTransportText => IsSmb ? "SMB" : "SSH/SFTP";

    public string ManagementHost => _profile.Management.ManagementHost ?? "Não aplicável";

    public int SshPort => _profile.Management.SshPort;

    public string SshUsername => _profile.Management.SshUsername ?? "Não configurado";

    public string SshAuthenticationModeText => _profile.Management.SshAuthenticationMode == SshAuthenticationMode.PrivateKey ? "Chave privada" : "Senha";

    public string SshPrivateKeyPath => _profile.Management.SshPrivateKeyPath ?? "Não configurada";

    public string? ConfiguredSshPrivateKeyPath => _profile.Management.SshPrivateKeyPath;

    public string TrustedHostKeyFingerprint => _profile.Management.TrustedHostKeyFingerprint ?? "Não configurada";

    public string TrustedHostKeyAlgorithm => _profile.Management.TrustedHostKeyAlgorithm ?? "Indisponível";

    public string SmbSharePath => _profile.Management.SmbSharePath ?? "Não configurado";

    public string SmbAuthenticationModeText => _profile.Management.SmbAuthenticationMode == SmbAuthenticationMode.ExplicitCredentials
        ? "Credenciais explícitas da sessão"
        : "Usuário Windows atual";

    public string SmbUsername => _profile.Management.SmbUsername ?? "Não aplicável";

    public bool IsConnected => _session is not null;

    public bool IsDirectoryValidated => _directoryValidation?.IsValid == true;

    public bool CanConnect => !IsBusy && !IsConnected && (IsSmb || (!string.IsNullOrWhiteSpace(_profile.Management.SshUsername) && (!UsesSshPrivateKey || !string.IsNullOrWhiteSpace(_profile.Management.SshPrivateKeyPath))));

    public bool HasStoredCredential => StoredCredentialStatus == RemoteCredentialStoreStatus.Success;

    public bool CanConnectWithStoredCredential => CanConnect && HasStoredCredential && GetCredentialKind() is not null;

    public bool CanForgetStoredCredential => !IsBusy && GetCredentialKind() is not null && _profileUpdater is not null;

    public string StoredCredentialText => GetCredentialKind() is null
        ? UsesSmbCurrentWindowsIdentity ? "Nenhuma credencial protegida é necessária." : "Não aplicável"
        : StoredCredentialStatus switch
        {
            RemoteCredentialStoreStatus.Success => "Credencial salva: Sim",
            RemoteCredentialStoreStatus.NotFound => "Credencial salva: Não",
            RemoteCredentialStoreStatus.Unsupported or RemoteCredentialStoreStatus.CredentialStoreUnavailable => "Credencial salva: Indisponível",
            _ => "Não foi possível consultar a credencial protegida."
        };

    public bool CanDisconnect => !IsBusy && IsConnected;

    public bool CanTrustHostKey => IsSshSftp && !IsBusy && ConnectionState == RemoteNutConnectionState.HostKeyTrustRequired && PresentedHostKey is not null && _profileUpdater is not null;

    public bool CanBrowse => !IsBusy && IsConnected;

    public bool CanValidateDirectory => CanBrowse && !string.IsNullOrWhiteSpace(CurrentDirectory);

    public bool CanUseCurrentDirectory => CanBrowse && IsDirectoryValidated && _profileUpdater is not null;

    public bool CanProbeWriteCapability =>
        CanBrowse &&
        IsDirectoryValidated &&
        _profile.AccessMode == ManagedNutServerAccessMode.Manage &&
        WriteCapability is null;

    public bool CanReadConfiguration => IsDirectoryValidated;

    public bool CanEditConfiguration =>
        _profile.AccessMode == ManagedNutServerAccessMode.Manage &&
        WriteCapability is { IsSupported: true } && (IsSmb || Platform == RemoteNutPlatform.Windows);

    public string ConnectionStateText => ConnectionState switch
    {
        RemoteNutConnectionState.Disconnected => "Não conectado",
        RemoteNutConnectionState.Connecting => "Conectando",
        RemoteNutConnectionState.HostKeyTrustRequired => "Chave do host precisa ser confiada",
        RemoteNutConnectionState.Connected => "Conectado",
        RemoteNutConnectionState.Validating => "Validando diretório",
        RemoteNutConnectionState.Ready => "Diretório validado",
        RemoteNutConnectionState.AuthenticationFailed => "Falha de autenticação",
        RemoteNutConnectionState.HostKeyMismatch => "Chave do host não corresponde",
        RemoteNutConnectionState.AccessDenied => "Acesso negado",
        RemoteNutConnectionState.Timeout => "Tempo limite excedido",
        _ => "Falha de conexão"
    };

    public string PlatformText => Platform switch
    {
        RemoteNutPlatform.Windows => "Windows",
        RemoteNutPlatform.NonWindows => "Não Windows",
        _ => "Indisponível"
    };

    public string ReadCapabilityText => CanReadConfiguration ? "Disponível" : "Valide um diretório remoto para habilitar a leitura.";

    public string WriteCapabilityText => CanEditConfiguration
        ? IsSmb ? "Verificada para este diretório SMB" : "Verificada para Windows/OpenSSH"
        : WriteCapability?.Message ?? (IsSmb
            ? "A escrita SMB requer verificação explícita de File.Replace neste diretório."
            : "A escrita remota requer verificação explícita de capacidade em um servidor Windows/OpenSSH.");

    public bool IsWriteCapabilityCritical => !string.IsNullOrWhiteSpace(WriteCapability?.CleanupPath);

    public string WriteCapabilityCriticalText => "CRÍTICO — o arquivo temporário remoto pode necessitar de limpeza manual antes de tentar novamente.";

    public async Task ConnectWithPasswordAsync(ReadOnlyMemory<char> password, bool rememberCredential = false, CancellationToken cancellationToken = default)
    {
        if (IsSmb)
        {
            if (_profile.Management.SmbAuthenticationMode != SmbAuthenticationMode.ExplicitCredentials)
            {
                await ConnectWithCurrentWindowsIdentityAsync(cancellationToken);
                return;
            }

            if (password.IsEmpty)
            {
                StatusMessage = "Informe a senha da sessão SMB.";
                return;
            }

            var connected = await ConnectSmbAsync(password, cancellationToken);
            if (connected && rememberCredential)
            {
                await SaveCredentialAfterSuccessfulConnectionAsync(RemoteCredentialKind.SmbPassword, password, cancellationToken);
            }
            return;
        }

        if (password.IsEmpty)
        {
            StatusMessage = "Informe uma credencial de sessão para conectar.";
            return;
        }

        var sshConnected = await ConnectSshAsync(new RemoteNutPasswordAuthentication(password), cancellationToken);
        if (sshConnected && rememberCredential)
        {
            await SaveCredentialAfterSuccessfulConnectionAsync(RemoteCredentialKind.SshPassword, password, cancellationToken);
        }
    }

    public async Task ConnectWithPrivateKeyAsync(string keyPath, ReadOnlyMemory<char> passphrase = default, bool rememberPassphrase = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            StatusMessage = "Selecione uma chave privada para conectar.";
            return;
        }

        var connected = await ConnectSshAsync(new RemoteNutPrivateKeyAuthentication(keyPath, passphrase), cancellationToken);
        if (connected && rememberPassphrase && !passphrase.IsEmpty && string.Equals(keyPath, _profile.Management.SshPrivateKeyPath, StringComparison.Ordinal))
        {
            await SaveCredentialAfterSuccessfulConnectionAsync(RemoteCredentialKind.SshPrivateKeyPassphrase, passphrase, cancellationToken);
        }
    }

    public async Task ConnectWithCurrentWindowsIdentityAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSmb)
        {
            return;
        }

        await ConnectSmbAsync(default, cancellationToken);
    }

    public async Task RefreshStoredCredentialStatusAsync(CancellationToken cancellationToken = default)
    {
        var kind = GetCredentialKind();
        if (kind is null || _credentialStore is null)
        {
            StoredCredentialStatus = kind is null ? RemoteCredentialStoreStatus.NotFound : RemoteCredentialStoreStatus.Unsupported;
            return;
        }

        var result = await _credentialStore.ContainsAsync(_profile.Id, kind.Value, cancellationToken);
        StoredCredentialStatus = result.Status;
    }

    public async Task ConnectWithStoredCredentialAsync(CancellationToken cancellationToken = default)
    {
        var kind = GetCredentialKind();
        if (!CanConnectWithStoredCredential || kind is null || _credentialStore is null)
        {
            return;
        }

        using var read = await _credentialStore.ReadAsync(_profile.Id, kind.Value, cancellationToken);
        StoredCredentialStatus = read.Status;
        if (!read.IsSuccess || read.Secret is null)
        {
            StatusMessage = read.Message ?? "A credencial protegida não está disponível.";
            return;
        }

        if (kind == RemoteCredentialKind.SshPrivateKeyPassphrase)
        {
            var keyPath = _profile.Management.SshPrivateKeyPath;
            if (string.IsNullOrWhiteSpace(keyPath))
            {
                StatusMessage = "Configure a chave privada no perfil antes de usar a passphrase salva.";
                return;
            }

            await ConnectSshAsync(new RemoteNutPrivateKeyAuthentication(keyPath, read.Secret.Memory), cancellationToken);
            return;
        }

        if (kind == RemoteCredentialKind.SmbPassword)
        {
            await ConnectSmbAsync(read.Secret.Memory, cancellationToken);
            return;
        }

        await ConnectSshAsync(new RemoteNutPasswordAuthentication(read.Secret.Memory), cancellationToken);
    }

    public async Task ForgetStoredCredentialAsync(CancellationToken cancellationToken = default)
    {
        var kind = GetCredentialKind();
        if (!CanForgetStoredCredential || kind is null || _profileUpdater is null)
        {
            return;
        }

        var result = await _profileUpdater.ForgetCredentialAsync(_profile.Id, kind.Value, cancellationToken);
        StoredCredentialStatus = result.IsSuccess ? RemoteCredentialStoreStatus.NotFound : result.Status;
        StatusMessage = result.IsSuccess ? "A credencial protegida foi removida." : result.Message ?? "Não foi possível remover a credencial protegida.";
    }

    public async Task TrustPresentedHostKeyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanTrustHostKey || PresentedHostKey is null || _profileUpdater is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var updated = await _profileUpdater.TrustHostKeyAsync(_profile, PresentedHostKey.Algorithm, PresentedHostKey.Fingerprint, cancellationToken);
            StatusMessage = updated is null
                ? "A chave não foi salva porque os metadados do perfil foram alterados. Revise o perfil e conecte novamente."
                : "A chave do host foi confiada. Conecte novamente para iniciar a sessão SSH.";
            if (updated is not null)
            {
                _profile = updated;
                ConnectionState = RemoteNutConnectionState.Disconnected;
                PresentedHostKey = null;
                NotifyProfileMetadataChanged();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "A confiança da chave do host foi cancelada.";
        }
        catch
        {
            StatusMessage = "Não foi possível salvar a chave confiável do host.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task BrowseDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!CanBrowse || _session is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var listing = await _session.BrowseDirectoryAsync(directory, cancellationToken);
            CurrentDirectory = listing.CurrentPath;
            DirectoryEntries.Clear();
            foreach (var entry in listing.Entries)
            {
                DirectoryEntries.Add(entry);
            }

            InvalidateDirectoryValidation();
            StatusMessage = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "A navegação remota foi cancelada.";
        }
        catch
        {
            StatusMessage = "Não foi possível listar o diretório remoto selecionado.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task BrowseParentAsync(CancellationToken cancellationToken = default)
    {
        if (_session is null || string.IsNullOrWhiteSpace(CurrentDirectory))
        {
            return;
        }

        var parent = _session.PathPolicy.GetParentDirectory(CurrentDirectory);
        if (parent is not null)
        {
            await BrowseDirectoryAsync(parent, cancellationToken);
        }
    }

    public string CombineConfigurationFilePath(string directory, string fileName)
    {
        if (_session is null)
        {
            throw new InvalidOperationException("A remote configuration session is required to compose a configuration path.");
        }

        return _session.PathPolicy.CombineDirectChild(directory, fileName);
    }

    public Task BrowseChildAsync(RemoteNutDirectoryEntry? entry, CancellationToken cancellationToken = default) =>
        entry is { IsDirectory: true, IsSymbolicLink: false }
            ? BrowseDirectoryAsync(entry.FullPath, cancellationToken)
            : Task.CompletedTask;

    public async Task ValidateCurrentDirectoryAsync(CancellationToken cancellationToken = default)
    {
        if (!CanValidateDirectory || _session is null)
        {
            return;
        }

        IsBusy = true;
        ConnectionState = RemoteNutConnectionState.Validating;
        try
        {
            var validation = await _session.ValidateConfigurationDirectoryAsync(CurrentDirectory, cancellationToken);
            _directoryValidation = validation;
            CurrentDirectory = validation.Directory;
            if (!validation.IsValid)
            {
                ConnectionState = validation.Status switch
                {
                    RemoteNutTransportStatus.AccessDenied => RemoteNutConnectionState.AccessDenied,
                    RemoteNutTransportStatus.Timeout => RemoteNutConnectionState.Timeout,
                    _ => RemoteNutConnectionState.Failed
                };
                StatusMessage = validation.Message;
                NotifyConfigurationContextChanged();
                return;
            }

            ConnectionState = RemoteNutConnectionState.Ready;
            StatusMessage = validation.Message;
            NotifyConfigurationContextChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ConnectionState = RemoteNutConnectionState.Connected;
            StatusMessage = "A validação do diretório remoto foi cancelada.";
        }
        catch
        {
            ConnectionState = RemoteNutConnectionState.Failed;
            StatusMessage = "Não foi possível validar o diretório remoto.";
            InvalidateDirectoryValidation();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UseCurrentDirectoryAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUseCurrentDirectory || _profileUpdater is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var updated = await _profileUpdater.SaveRemoteDirectoryAsync(_profile, CurrentDirectory, cancellationToken);
            StatusMessage = updated is null
                ? "O diretório não foi salvo porque os metadados do perfil foram alterados."
                : "O diretório remoto foi salvo no perfil. A conexão de monitoring não foi alterada.";
            if (updated is not null)
            {
                _profile = updated;
                NotifyProfileMetadataChanged();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "O salvamento do diretório remoto foi cancelado.";
        }
        catch
        {
            StatusMessage = "Não foi possível salvar o diretório remoto no perfil.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ProbeWriteCapabilityAsync(CancellationToken cancellationToken = default)
    {
        if (!CanProbeWriteCapability || _session is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            WriteCapability = await _session.ProbeSafeWriteCapabilityAsync(CurrentDirectory, cancellationToken);
            Platform = WriteCapability.Platform;
            StatusMessage = WriteCapability.Message;
            NotifyConfigurationContextChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "A verificação de capacidade de gravação foi cancelada.";
        }
        catch
        {
            WriteCapability = new RemoteNutWriteCapabilityResult(false, Platform, message: "Não foi possível verificar a capacidade de gravação remota.");
            NotifyConfigurationContextChanged();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DisconnectAsync()
    {
        var session = _session;
        _session = null;
        if (session is not null)
        {
            await session.DisposeAsync();
        }

        DirectoryEntries.Clear();
        InvalidateDirectoryValidation();
        ConnectionState = RemoteNutConnectionState.Disconnected;
        StatusMessage = "Sessão remota desconectada.";
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();

    public void InvalidateWriteCapabilityAfterUncertainOutcome()
    {
        WriteCapability = new RemoteNutWriteCapabilityResult(
            false,
            Platform,
            message: "A operação remota teve resultado indeterminado. Desconecte, conecte novamente e refaça a validação de capacidade antes de gravar.");
        OnPropertyChanged(nameof(CanEditConfiguration));
        OnPropertyChanged(nameof(WriteCapabilityText));
    }

    private async Task<bool> ConnectSshAsync(RemoteNutAuthentication authentication, CancellationToken cancellationToken)
    {
        if (!CanConnect || !IsSshSftp)
        {
            StatusMessage = "Configure um usuário SSH antes de conectar.";
            return false;
        }

        IsBusy = true;
        ConnectionState = RemoteNutConnectionState.Connecting;
        StatusMessage = null;
        try
        {
            var result = await _transport.ConnectAsync(
                new RemoteNutConnectionRequest(
                    _profile.Id,
                    ManagementHost,
                    SshPort,
                    _profile.Management.SshUsername!,
                    _profile.Management.TrustedHostKeyFingerprint,
                    authentication),
                cancellationToken);
            AcceptConnectionResult(result, _profile.Management.RemoteConfigurationDirectory);
            return result.State == RemoteNutConnectionState.Connected && result.Session is not null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ConnectionState = RemoteNutConnectionState.Disconnected;
            StatusMessage = "A conexão remota foi cancelada.";
            return false;
        }
        catch
        {
            ConnectionState = RemoteNutConnectionState.ConnectionFailed;
            StatusMessage = "Não foi possível estabelecer a conexão remota.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> ConnectSmbAsync(ReadOnlyMemory<char> password, CancellationToken cancellationToken)
    {
        if (!CanConnect || !IsSmb)
        {
            return false;
        }

        IsBusy = true;
        ConnectionState = RemoteNutConnectionState.Connecting;
        StatusMessage = null;
        try
        {
            var management = _profile.Management;
            var result = await _transport.ConnectAsync(
                new SmbRemoteNutConnectionRequest(
                    _profile.Id,
                    management.SmbSharePath!,
                    management.SmbAuthenticationMode,
                    management.SmbUsername,
                    password,
                    _profile.AccessMode == ManagedNutServerAccessMode.Manage),
                cancellationToken);
            AcceptConnectionResult(result, management.SmbConfigurationDirectory ?? management.SmbSharePath);
            return result.State == RemoteNutConnectionState.Connected && result.Session is not null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ConnectionState = RemoteNutConnectionState.Disconnected;
            StatusMessage = "A conexão SMB foi cancelada.";
            return false;
        }
        catch
        {
            ConnectionState = RemoteNutConnectionState.ConnectionFailed;
            StatusMessage = "Não foi possível estabelecer a conexão SMB.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveCredentialAfterSuccessfulConnectionAsync(RemoteCredentialKind kind, ReadOnlyMemory<char> secret, CancellationToken cancellationToken)
    {
        if (_profileUpdater is null)
        {
            StatusMessage = "Conectado, mas o armazenamento protegido de credenciais não está disponível.";
            return;
        }

        var result = await _profileUpdater.SaveCredentialForCurrentSessionAsync(_profile, kind, secret, cancellationToken);
        StoredCredentialStatus = result.Status;
        StatusMessage = result.IsSuccess
            ? "Conectado. A credencial foi salva no Windows."
            : result.Message ?? "Conectado, mas a credencial não pôde ser salva.";
    }

    private RemoteCredentialKind? GetCredentialKind()
    {
        if (UsesSshPassword)
        {
            return RemoteCredentialKind.SshPassword;
        }

        if (UsesSshPrivateKey && !string.IsNullOrWhiteSpace(_profile.Management.SshPrivateKeyPath))
        {
            return RemoteCredentialKind.SshPrivateKeyPassphrase;
        }

        return UsesSmbExplicitCredentials ? RemoteCredentialKind.SmbPassword : null;
    }

    private void AcceptConnectionResult(RemoteNutConnectionResult result, string? initialDirectory)
    {
        ConnectionState = result.State;
        PresentedHostKey = IsSshSftp ? result.HostKey : null;
        StatusMessage = result.Message;
        if (result.Session is null)
        {
            return;
        }

        _session = result.Session;
        CurrentDirectory = initialDirectory ?? result.Session.HomeDirectory;
        Platform = result.Session.Platform;
        WriteCapability = null;
        DirectoryEntries.Clear();
        _directoryValidation = null;
        NotifyConfigurationContextChanged();
    }

    private void InvalidateDirectoryValidation()
    {
        _directoryValidation = null;
        WriteCapability = null;
        NotifyConfigurationContextChanged();
    }

    private void NotifyConfigurationContextChanged()
    {
        var pipeline = _session is not null && _directoryValidation?.IsValid == true
            ? new RemoteNutConfigurationFilePipeline(_session, _directoryValidation.Directory, CanEditConfiguration)
            : null;
        ConfigurationContextChanged?.Invoke(pipeline, _directoryValidation, CanEditConfiguration);
        OnPropertyChanged(nameof(IsDirectoryValidated));
        OnPropertyChanged(nameof(CanReadConfiguration));
        OnPropertyChanged(nameof(CanEditConfiguration));
        OnPropertyChanged(nameof(ReadCapabilityText));
        OnPropertyChanged(nameof(WriteCapabilityText));
    }

    private void NotifyProfileMetadataChanged()
    {
        OnPropertyChanged(nameof(ManagementHost));
        OnPropertyChanged(nameof(SshPort));
        OnPropertyChanged(nameof(SshUsername));
        OnPropertyChanged(nameof(SshAuthenticationModeText));
        OnPropertyChanged(nameof(SshPrivateKeyPath));
        OnPropertyChanged(nameof(ConfiguredSshPrivateKeyPath));
        OnPropertyChanged(nameof(TrustedHostKeyFingerprint));
        OnPropertyChanged(nameof(TrustedHostKeyAlgorithm));
        OnPropertyChanged(nameof(SmbSharePath));
        OnPropertyChanged(nameof(SmbAuthenticationModeText));
        OnPropertyChanged(nameof(SmbUsername));
        OnPropertyChanged(nameof(IsSshSftp));
        OnPropertyChanged(nameof(IsSmb));
        OnPropertyChanged(nameof(UsesSmbExplicitCredentials));
        OnPropertyChanged(nameof(UsesSmbCurrentWindowsIdentity));
        OnPropertyChanged(nameof(UsesSshPassword));
        OnPropertyChanged(nameof(UsesSshPrivateKey));
        OnPropertyChanged(nameof(ConfigurationTransportText));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanTrustHostKey));
        OnPropertyChanged(nameof(CanConnectWithStoredCredential));
        OnPropertyChanged(nameof(CanForgetStoredCredential));
        OnPropertyChanged(nameof(StoredCredentialText));
    }

    partial void OnConnectionStateChanged(RemoteNutConnectionState value)
    {
        OnPropertyChanged(nameof(ConnectionStateText));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanDisconnect));
        OnPropertyChanged(nameof(CanTrustHostKey));
    }

    partial void OnCurrentDirectoryChanged(string value)
    {
        if (_directoryValidation is not null && !string.Equals(value, _directoryValidation.Directory, StringComparison.Ordinal))
        {
            InvalidateDirectoryValidation();
        }

        OnPropertyChanged(nameof(CanValidateDirectory));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanDisconnect));
        OnPropertyChanged(nameof(CanTrustHostKey));
        OnPropertyChanged(nameof(CanBrowse));
        OnPropertyChanged(nameof(CanValidateDirectory));
        OnPropertyChanged(nameof(CanUseCurrentDirectory));
        OnPropertyChanged(nameof(CanProbeWriteCapability));
        OnPropertyChanged(nameof(CanConnectWithStoredCredential));
        OnPropertyChanged(nameof(CanForgetStoredCredential));
    }

    partial void OnWriteCapabilityChanged(RemoteNutWriteCapabilityResult? value)
    {
        OnPropertyChanged(nameof(CanProbeWriteCapability));
        OnPropertyChanged(nameof(CanEditConfiguration));
        OnPropertyChanged(nameof(WriteCapabilityText));
        OnPropertyChanged(nameof(IsWriteCapabilityCritical));
    }

    partial void OnStoredCredentialStatusChanged(RemoteCredentialStoreStatus value)
    {
        OnPropertyChanged(nameof(HasStoredCredential));
        OnPropertyChanged(nameof(CanConnectWithStoredCredential));
        OnPropertyChanged(nameof(StoredCredentialText));
    }
}
