using System.Security.Cryptography;
using System.Text;
using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.Credentials.Windows;

public sealed class WindowsCredentialManagerRemoteCredentialStore : IRemoteCredentialStore
{
    internal const uint CredentialTypeGeneric = 1;
    internal const uint CredentialPersistLocalMachine = 2;
    internal const int ErrorNotFound = 1168;
    internal const int ErrorAccessDenied = 5;
    private const int MaximumSecretCharacters = 1024;

    private readonly IWindowsCredentialManagerNative _native;
    private readonly Func<bool> _isWindows;

    public WindowsCredentialManagerRemoteCredentialStore()
        : this(new WindowsCredentialManagerNative(), OperatingSystem.IsWindows)
    {
    }

    public WindowsCredentialManagerRemoteCredentialStore(IWindowsCredentialManagerNative native, Func<bool> isWindows)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _isWindows = isWindows ?? throw new ArgumentNullException(nameof(isWindows));
    }

    public async Task<RemoteCredentialStoreResult> ContainsAsync(Guid profileId, RemoteCredentialKind kind, CancellationToken cancellationToken = default)
    {
        using var result = await ReadAsync(profileId, kind, cancellationToken).ConfigureAwait(false);
        return new RemoteCredentialStoreResult(result.Status, result.Message);
    }

    public Task<RemoteCredentialReadResult> ReadAsync(Guid profileId, RemoteCredentialKind kind, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isWindows())
        {
            return Task.FromResult(new RemoteCredentialReadResult(RemoteCredentialStoreStatus.Unsupported, message: "O armazenamento protegido de credenciais está disponível somente no Windows."));
        }

        var targetName = WindowsCredentialTargetNames.GetTargetName(profileId, kind);
        try
        {
            if (!_native.TryRead(targetName, CredentialTypeGeneric, 0, out var handle, out var errorCode))
            {
                return Task.FromResult(new RemoteCredentialReadResult(MapError(errorCode), message: GetSafeMessage(errorCode)));
            }

            using (handle)
            {
                var blob = handle!.GetCredentialBlob();
                if (blob.IsEmpty || blob.Length > MaximumSecretCharacters * sizeof(char) || blob.Length % sizeof(char) != 0)
                {
                    return Task.FromResult(new RemoteCredentialReadResult(RemoteCredentialStoreStatus.Failed, message: "A credencial protegida possui um formato inválido."));
                }

                var blobCopy = blob.ToArray();
                char[] chars;
                try
                {
                    chars = Encoding.Unicode.GetChars(blobCopy);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(blobCopy);
                }
                try
                {
                    return Task.FromResult(new RemoteCredentialReadResult(RemoteCredentialStoreStatus.Success, new RemoteCredentialSecret(chars)));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(chars.AsSpan()));
                }
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new RemoteCredentialReadResult(RemoteCredentialStoreStatus.CredentialStoreUnavailable, message: "O Gerenciador de Credenciais do Windows não está disponível."));
        }
    }

    public Task<RemoteCredentialStoreResult> WriteAsync(Guid profileId, RemoteCredentialKind kind, ReadOnlyMemory<char> secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isWindows())
        {
            return Task.FromResult(Unsupported());
        }

        if (secret.IsEmpty || secret.Length > MaximumSecretCharacters)
        {
            return Task.FromResult(new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.Failed, "O tamanho da credencial protegida é inválido."));
        }

        var chars = secret.ToArray();
        byte[] bytes;
        try
        {
            bytes = Encoding.Unicode.GetBytes(chars);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(chars.AsSpan()));
        }
        try
        {
            var request = new WindowsCredentialNativeWriteRequest(
                WindowsCredentialTargetNames.GetTargetName(profileId, kind),
                CredentialTypeGeneric,
                CredentialPersistLocalMachine,
                0,
                bytes);
            var success = _native.TryWrite(request, out var errorCode);
            return Task.FromResult(success
                ? new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.Success)
                : new RemoteCredentialStoreResult(MapError(errorCode), GetSafeMessage(errorCode)));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.CredentialStoreUnavailable, "O Gerenciador de Credenciais do Windows não está disponível."));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public Task<RemoteCredentialStoreResult> DeleteAsync(Guid profileId, RemoteCredentialKind kind, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isWindows())
        {
            return Task.FromResult(Unsupported());
        }

        try
        {
            var success = _native.TryDelete(WindowsCredentialTargetNames.GetTargetName(profileId, kind), CredentialTypeGeneric, 0, out var errorCode);
            return Task.FromResult(success || errorCode == ErrorNotFound
                ? new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.Success)
                : new RemoteCredentialStoreResult(MapError(errorCode), GetSafeMessage(errorCode)));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.CredentialStoreUnavailable, "O Gerenciador de Credenciais do Windows não está disponível."));
        }
    }

    public async Task<RemoteCredentialStoreResult> DeleteAllForProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        foreach (var kind in Enum.GetValues<RemoteCredentialKind>())
        {
            var result = await DeleteAsync(profileId, kind, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return result;
            }
        }

        return new RemoteCredentialStoreResult(RemoteCredentialStoreStatus.Success);
    }

    private static RemoteCredentialStoreResult Unsupported() => new(RemoteCredentialStoreStatus.Unsupported, "O armazenamento protegido de credenciais está disponível somente no Windows.");

    private static RemoteCredentialStoreStatus MapError(int errorCode) => errorCode switch
    {
        ErrorNotFound => RemoteCredentialStoreStatus.NotFound,
        ErrorAccessDenied => RemoteCredentialStoreStatus.AccessDenied,
        _ => RemoteCredentialStoreStatus.Failed
    };

    private static string GetSafeMessage(int errorCode) => errorCode switch
    {
        ErrorNotFound => "A credencial protegida não foi encontrada.",
        ErrorAccessDenied => "O acesso ao Gerenciador de Credenciais foi negado.",
        _ => "Não foi possível acessar o Gerenciador de Credenciais do Windows."
    };
}

public static class WindowsCredentialTargetNames
{
    public static string GetTargetName(Guid profileId, RemoteCredentialKind kind)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("A profile identifier is required.", nameof(profileId));
        }

        var suffix = kind switch
        {
            RemoteCredentialKind.SshPassword => "ssh-password",
            RemoteCredentialKind.SshPrivateKeyPassphrase => "ssh-key-passphrase",
            RemoteCredentialKind.SmbPassword => "smb-password",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return $"NutManager:RemoteCredential:v1:{profileId:N}:{suffix}";
    }
}
