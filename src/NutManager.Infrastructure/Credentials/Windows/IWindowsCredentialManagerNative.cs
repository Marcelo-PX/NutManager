namespace NutManager.Infrastructure.Credentials.Windows;

public sealed record WindowsCredentialNativeWriteRequest(
    string TargetName,
    uint Type,
    uint Persist,
    uint Flags,
    ReadOnlyMemory<byte> CredentialBlob);

public interface IWindowsCredentialNativeReadHandle : IDisposable
{
    /// <summary>
    /// Copies the native credential blob into one caller-owned buffer. The caller
    /// must clear that buffer after decoding it; disposing this handle releases
    /// only the native Credential Manager allocation.
    /// </summary>
    byte[] CopyCredentialBlob();
}

/// <summary>Small seam around the four Credential Manager Win32 calls.</summary>
public interface IWindowsCredentialManagerNative
{
    bool TryWrite(WindowsCredentialNativeWriteRequest request, out int errorCode);

    bool TryRead(string targetName, uint type, uint flags, out IWindowsCredentialNativeReadHandle? credential, out int errorCode);

    bool TryDelete(string targetName, uint type, uint flags, out int errorCode);
}
