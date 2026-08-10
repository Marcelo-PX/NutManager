using System.Runtime.InteropServices;

namespace NutManager.Infrastructure.Credentials.Windows;

public sealed class WindowsCredentialManagerNative : IWindowsCredentialManagerNative
{
    public bool TryWrite(WindowsCredentialNativeWriteRequest request, out int errorCode)
    {
        ArgumentNullException.ThrowIfNull(request);
        var blob = request.CredentialBlob.ToArray();
        var pinned = GCHandle.Alloc(blob, GCHandleType.Pinned);
        try
        {
            var credential = new NativeCredential
            {
                Flags = request.Flags,
                Type = request.Type,
                TargetName = request.TargetName,
                CredentialBlobSize = checked((uint)blob.Length),
                CredentialBlob = pinned.AddrOfPinnedObject(),
                Persist = request.Persist
            };
            var success = CredWriteW(ref credential, 0);
            errorCode = success ? 0 : Marshal.GetLastWin32Error();
            return success;
        }
        finally
        {
            pinned.Free();
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(blob);
        }
    }

    public bool TryRead(string targetName, uint type, uint flags, out IWindowsCredentialNativeReadHandle? credential, out int errorCode)
    {
        var success = CredReadW(targetName, type, flags, out var pointer);
        errorCode = success ? 0 : Marshal.GetLastWin32Error();
        credential = success ? new NativeReadHandle(pointer) : null;
        return success;
    }

    public bool TryDelete(string targetName, uint type, uint flags, out int errorCode)
    {
        var success = CredDeleteW(targetName, type, flags);
        errorCode = success ? 0 : Marshal.GetLastWin32Error();
        return success;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    private sealed class NativeReadHandle : IWindowsCredentialNativeReadHandle
    {
        private IntPtr _pointer;

        public NativeReadHandle(IntPtr pointer) => _pointer = pointer;

        public byte[] CopyCredentialBlob()
        {
            if (_pointer == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(NativeReadHandle));
            }

            var credential = Marshal.PtrToStructure<NativeCredential>(_pointer);
            var length = checked((int)credential.CredentialBlobSize);
            var bytes = new byte[length];
            if (length > 0)
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, length);
            }

            return bytes;
        }

        public void Dispose()
        {
            var pointer = Interlocked.Exchange(ref _pointer, IntPtr.Zero);
            if (pointer != IntPtr.Zero)
            {
                CredFree(pointer);
            }
        }
    }

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string targetName, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string targetName, uint type, uint flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
