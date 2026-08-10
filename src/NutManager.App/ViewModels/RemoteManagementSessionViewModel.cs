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
    private readonly IRemoteNutManagementTransport _transport;
    private readonly ManagedNutServerProfileUpdateService? _profileUpdater;
    private IRemoteNutManagementSession? _session;
    private RemoteNutDirectoryValidationResult? _directoryValidation;

    public RemoteManagementSessionViewModel(
        ManagedNutServerProfile profile,
        IRemoteNutManagementTransport transport,
        ManagedNutServerProfileUpdateService? profileUpdater = null)
    {
        if (profile.Management.Mode != NutManagementMode.Remote)
        {
            throw new ArgumentException("A remote profile is required.", nameof(profile));
        }

        _profile = profile;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _profileUpdater = profileUpdater;
        DirectoryEntries = new ObservableCollection<RemoteNutDirectoryEntry>();
        CurrentDirectory = profile.Management.RemoteConfigurationDirectory ?? string.Empty;
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

    public string ManagementHost => _profile.Management.ManagementHost!;

    public int SshPort => _profile.Management.SshPort;

    public string SshUsername => _profile.Management.SshUsername ?? "Não configurado";

    public string TrustedHostKeyFingerprint => _profile.Management.TrustedHostKeyFingerprint ?? "Não configurada";

    public string TrustedHostKeyAlgorithm => _profile.Management.TrustedHostKeyAlgorithm ?? "Indisponível";

    public bool IsConnected => _session is not null;

    public bool IsDirectoryValidated => _directoryValidation?.IsValid == true;

    public bool CanConnect => !IsBusy && !IsConnected && !string.IsNullOrWhiteSpace(_profile.Management.SshUsername);

    public bool CanDisconnect => !IsBusy && IsConnected;

    public bool CanTrustHostKey => !IsBusy && ConnectionState == RemoteNutConnectionState.HostKeyTrustRequired && PresentedHostKey is not null && _profileUpdater is not null;

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
        WriteCapability is { IsSupported: true, Platform: RemoteNutPlatform.Windows };

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
        ? "Verificada para Windows/OpenSSH"
        : WriteCapability?.Message ?? "A escrita remota requer verificação explícita de capacidade em um servidor Windows/OpenSSH.";

    public bool IsWriteCapabilityCritical => !string.IsNullOrWhiteSpace(WriteCapability?.CleanupPath);

    public string WriteCapabilityCriticalText => "CRÍTICO — o arquivo temporário remoto pode necessitar de limpeza manual antes de tentar novamente.";

    public async Task ConnectWithPasswordAsync(ReadOnlyMemory<char> password, CancellationToken cancellationToken = default)
    {
        if (password.IsEmpty)
        {
            StatusMessage = "Informe uma credencial de sessão para conectar.";
            return;
        }

        await ConnectAsync(new RemoteNutPasswordAuthentication(password), cancellationToken);
    }

    public async Task ConnectWithPrivateKeyAsync(string keyPath, ReadOnlyMemory<char> passphrase = default, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            StatusMessage = "Selecione uma chave privada para conectar.";
            return;
        }

        await ConnectAsync(new RemoteNutPrivateKeyAuthentication(keyPath, passphrase), cancellationToken);
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
        if (string.IsNullOrWhiteSpace(CurrentDirectory))
        {
            return;
        }

        var slash = CurrentDirectory.TrimEnd('/').LastIndexOf('/');
        if (slash < 0)
        {
            return;
        }

        await BrowseDirectoryAsync(slash == 0 ? "/" : CurrentDirectory[..slash], cancellationToken);
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

    private async Task ConnectAsync(RemoteNutAuthentication authentication, CancellationToken cancellationToken)
    {
        if (!CanConnect)
        {
            StatusMessage = "Configure um usuário SSH antes de conectar.";
            return;
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
            ConnectionState = result.State;
            PresentedHostKey = result.HostKey;
            StatusMessage = result.Message;
            if (result.Session is null)
            {
                return;
            }

            _session = result.Session;
            CurrentDirectory = _profile.Management.RemoteConfigurationDirectory ?? result.Session.HomeDirectory;
            Platform = result.Session.Platform;
            WriteCapability = null;
            DirectoryEntries.Clear();
            _directoryValidation = null;
            NotifyConfigurationContextChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ConnectionState = RemoteNutConnectionState.Disconnected;
            StatusMessage = "A conexão remota foi cancelada.";
        }
        catch
        {
            ConnectionState = RemoteNutConnectionState.ConnectionFailed;
            StatusMessage = "Não foi possível estabelecer a conexão remota.";
        }
        finally
        {
            IsBusy = false;
        }
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
        OnPropertyChanged(nameof(TrustedHostKeyFingerprint));
        OnPropertyChanged(nameof(TrustedHostKeyAlgorithm));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanTrustHostKey));
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
    }

    partial void OnWriteCapabilityChanged(RemoteNutWriteCapabilityResult? value)
    {
        OnPropertyChanged(nameof(CanProbeWriteCapability));
        OnPropertyChanged(nameof(CanEditConfiguration));
        OnPropertyChanged(nameof(WriteCapabilityText));
        OnPropertyChanged(nameof(IsWriteCapabilityCritical));
    }
}
