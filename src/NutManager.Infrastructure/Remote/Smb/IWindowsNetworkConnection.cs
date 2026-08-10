using System.Runtime.InteropServices;

namespace NutManager.Infrastructure.Remote.Smb;

public interface IWindowsNetworkConnection
{
    Task<WindowsNetworkConnectionResult> ConnectAsync(string sharePath, string username, ReadOnlyMemory<char> password, CancellationToken cancellationToken);

    Task<WindowsNetworkConnectionResult> DisconnectAsync(string sharePath, CancellationToken cancellationToken);
}

public sealed record WindowsNetworkConnectionResult(uint ErrorCode)
{
    public const uint Success = 0;
    public const uint CredentialConflict = 1219;

    public bool IsSuccess => ErrorCode == Success;
}

/// <summary>
/// Direct, non-persistent WNet access for a single UNC share. It never creates a mapped
/// drive and never uses CONNECT_UPDATE_PROFILE.
/// </summary>
public sealed class WindowsNetworkConnection : IWindowsNetworkConnection
{
    private const uint ResourceTypeDisk = 1;
    private const uint ConnectTemporary = 0;

    public Task<WindowsNetworkConnectionResult> ConnectAsync(string sharePath, string username, ReadOnlyMemory<char> password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new WindowsNetworkConnectionResult(50));
        }

        var transientPassword = new string(password.Span);
        try
        {
            var resource = new NetResource { ResourceType = ResourceTypeDisk, RemoteName = sharePath };
            return Task.FromResult(new WindowsNetworkConnectionResult(WNetAddConnection2(ref resource, transientPassword, username, ConnectTemporary)));
        }
        finally
        {
            transientPassword = string.Empty;
        }
    }

    public Task<WindowsNetworkConnectionResult> DisconnectAsync(string sharePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new WindowsNetworkConnectionResult(50));
        }

        return Task.FromResult(new WindowsNetworkConnectionResult(WNetCancelConnection2(sharePath, ConnectTemporary, false)));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public uint Scope;
        public uint ResourceType;
        public uint DisplayType;
        public uint Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint WNetAddConnection2(ref NetResource netResource, string password, string username, uint flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint WNetCancelConnection2(string name, uint flags, [MarshalAs(UnmanagedType.Bool)] bool force);
}
