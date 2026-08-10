namespace NutManager.Infrastructure.Credentials.Windows;

public sealed record WindowsCredentialNativeWriteRequest(
    string TargetName,
    uint Type,
    uint Persist,
    uint Flags,
    ReadOnlyMemory<byte> CredentialBlob);

public interface IWindowsCredentialNativeReadHandle : IDisposable
{
    ReadOnlyMemory<byte> GetCredentialBlob();
}

/// <summary>Small seam around the four Credential Manager Win32 calls.</summary>
public interface IWindowsCredentialManagerNative
{
    bool TryWrite(WindowsCredentialNativeWriteRequest request, out int errorCode);

    bool TryRead(string targetName, uint type, uint flags, out IWindowsCredentialNativeReadHandle? credential, out int errorCode);

    bool TryDelete(string targetName, uint type, uint flags, out int errorCode);
}
