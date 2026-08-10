using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace NutManager.Infrastructure.Remote.Smb;

/// <summary>
/// Session-scoped Windows outbound identity. It deliberately models no global SMB
/// connection lifecycle: a successful logon token is not ownership of a redirector
/// connection and disposing it never disconnects another application.
/// </summary>
public interface IWindowsSmbSessionIdentity : IAsyncDisposable
{
    bool IsExplicitCredentialIdentity { get; }

    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}

public interface IWindowsSmbSessionIdentityFactory
{
    IWindowsSmbSessionIdentity CreateCurrentIdentity();

    Task<WindowsSmbIdentityCreationResult> CreateExplicitIdentityAsync(
        string sharePath,
        string username,
        ReadOnlyMemory<char> password,
        CancellationToken cancellationToken);
}

/// <summary>
/// Narrow native logon seam. It exists so the password buffer lifetime can be tested
/// without invoking Windows authentication.
/// </summary>
public interface IWindowsSmbNativeLogon
{
    bool TryLogon(string accountName, string? authority, char[] passwordBuffer, out SafeAccessTokenHandle token);
}

public sealed class WindowsSmbIdentityCreationResult
{
    public WindowsSmbIdentityCreationResult(IWindowsSmbSessionIdentity? identity, string? message = null)
    {
        Identity = identity;
        Message = message;
    }

    public IWindowsSmbSessionIdentity? Identity { get; }

    public string? Message { get; }

    public bool IsSuccess => Identity is not null;
}

/// <summary>
/// Uses LOGON32_LOGON_NEW_CREDENTIALS/LOGON32_PROVIDER_WINNT50 so explicit SMB
/// credentials remain scoped to the session's outbound Windows token.
/// </summary>
public sealed class WindowsSmbSessionIdentityFactory : IWindowsSmbSessionIdentityFactory
{
    private readonly IWindowsSmbNativeLogon _nativeLogon;

    public WindowsSmbSessionIdentityFactory(IWindowsSmbNativeLogon? nativeLogon = null) =>
        _nativeLogon = nativeLogon ?? new WindowsSmbNativeLogon();

    public IWindowsSmbSessionIdentity CreateCurrentIdentity() => new CurrentWindowsSmbSessionIdentity();

    public Task<WindowsSmbIdentityCreationResult> CreateExplicitIdentityAsync(
        string sharePath,
        string username,
        ReadOnlyMemory<char> password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new WindowsSmbIdentityCreationResult(null, "A identidade SMB explícita está disponível somente no Windows."));
        }

        var (accountName, authority) = SplitUsername(sharePath, username);
        var transientPassword = new char[password.Length + 1];
        password.Span.CopyTo(transientPassword);
        try
        {
            if (!_nativeLogon.TryLogon(accountName, authority, transientPassword, out var token))
            {
                return Task.FromResult(new WindowsSmbIdentityCreationResult(null, "Não foi possível criar uma identidade Windows isolada para as credenciais SMB informadas."));
            }

            return Task.FromResult(new WindowsSmbIdentityCreationResult(new ExplicitWindowsSmbSessionIdentity(token)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(transientPassword.AsSpan()));
        }
    }

    private static (string AccountName, string? Authority) SplitUsername(string sharePath, string username)
    {
        if (username.Contains('\\', StringComparison.Ordinal))
        {
            var separator = username.IndexOf('\\');
            return (username[(separator + 1)..], username[..separator]);
        }

        if (username.Contains('@', StringComparison.Ordinal))
        {
            return (username, null);
        }

        var normalizedShare = NutManager.Core.Models.SmbUncPath.NormalizeShareRoot(sharePath);
        var serverStart = 2;
        var serverEnd = normalizedShare.IndexOf('\\', serverStart);
        return (username, normalizedShare[serverStart..serverEnd]);
    }

}

public sealed class WindowsSmbNativeLogon : IWindowsSmbNativeLogon
{
    private const int Logon32LogonNewCredentials = 9;
    private const int Logon32ProviderWinnt50 = 3;

    public bool TryLogon(string accountName, string? authority, char[] passwordBuffer, out SafeAccessTokenHandle token)
    {
        ArgumentNullException.ThrowIfNull(passwordBuffer);
        GCHandle handle = default;
        try
        {
            handle = GCHandle.Alloc(passwordBuffer, GCHandleType.Pinned);
            return LogonUser(
                accountName,
                authority,
                handle.AddrOfPinnedObject(),
                Logon32LogonNewCredentials,
                Logon32ProviderWinnt50,
                out token);
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(
        string userName,
        string? domain,
        IntPtr password,
        int logonType,
        int logonProvider,
        out SafeAccessTokenHandle token);
}

internal sealed class CurrentWindowsSmbSessionIdentity : IWindowsSmbSessionIdentity
{
    public bool IsExplicitCredentialIdentity => false;

    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        operation(cancellationToken);

    public Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) => operation(cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ExplicitWindowsSmbSessionIdentity : IWindowsSmbSessionIdentity
{
    private readonly SafeAccessTokenHandle _token;
    private bool _disposed;

    public ExplicitWindowsSmbSessionIdentity(SafeAccessTokenHandle token) =>
        _token = token ?? throw new ArgumentNullException(nameof(token));

    public bool IsExplicitCredentialIdentity => true;

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Explicit SMB session identities require Windows.");
        }

        var task = System.Security.Principal.WindowsIdentity.RunImpersonated(_token, () => operation(cancellationToken));
        return await task.ConfigureAwait(false);
    }

    public async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Explicit SMB session identities require Windows.");
        }

        var task = System.Security.Principal.WindowsIdentity.RunImpersonated(_token, () => operation(cancellationToken));
        await task.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _token.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ExplicitWindowsSmbSessionIdentity));
        }
    }
}
