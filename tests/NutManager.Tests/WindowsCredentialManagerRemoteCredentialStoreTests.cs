using System.Text;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Credentials.Windows;
using Xunit;

namespace NutManager.Tests;

public sealed class WindowsCredentialManagerRemoteCredentialStoreTests
{
    [Theory]
    [InlineData(RemoteCredentialKind.SshPassword, "ssh-password")]
    [InlineData(RemoteCredentialKind.SshPrivateKeyPassphrase, "ssh-key-passphrase")]
    [InlineData(RemoteCredentialKind.SmbPassword, "smb-password")]
    public void TargetNamesAreDeterministicAndContainOnlyProfileIdentityAndFixedKind(RemoteCredentialKind kind, string suffix)
    {
        var profileId = Guid.Parse("b065ddaf-e0de-40a8-9cbe-b488503d7c2a");

        var target = WindowsCredentialTargetNames.GetTargetName(profileId, kind);

        Assert.Equal($"NutManager:RemoteCredential:v1:{profileId:N}:{suffix}", target);
        Assert.DoesNotContain("host", target, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user", target, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("share", target, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProfileRenameCannotAffectTheCredentialTargetAndCallersCannotSupplyOne()
    {
        var profileId = Guid.NewGuid();

        var beforeRename = WindowsCredentialTargetNames.GetTargetName(profileId, RemoteCredentialKind.SshPassword);
        var afterRename = WindowsCredentialTargetNames.GetTargetName(profileId, RemoteCredentialKind.SshPassword);

        Assert.Equal(beforeRename, afterRename);
        Assert.DoesNotContain(
            typeof(IRemoteCredentialStore).GetMethods().SelectMany(method => method.GetParameters()),
            parameter => string.Equals(parameter.Name, "targetName", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WriteUsesOnlyGenericLocalMachineCredentialAndZeroesItsTemporaryBlob()
    {
        var native = new FakeNative();
        var store = new WindowsCredentialManagerRemoteCredentialStore(native, () => true);
        var profileId = Guid.NewGuid();

        var result = await store.WriteAsync(profileId, RemoteCredentialKind.SshPassword, "fictional-password".AsMemory());

        Assert.True(result.IsSuccess);
        Assert.NotNull(native.LastWrite);
        Assert.Equal(1u, native.LastWrite!.Type);
        Assert.Equal(2u, native.LastWrite.Persist);
        Assert.Equal(0u, native.LastWrite.Flags);
        Assert.Equal(Encoding.Unicode.GetBytes("fictional-password"), native.Stored[native.LastWrite.TargetName]);
        Assert.All(native.LastWrite.CredentialBlob.Span.ToArray(), value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public async Task OversizedSecretIsRejectedBeforeTheNativeBoundary()
    {
        var native = new FakeNative();
        var store = new WindowsCredentialManagerRemoteCredentialStore(native, () => true);

        var result = await store.WriteAsync(Guid.NewGuid(), RemoteCredentialKind.SshPassword, new string('x', 1025).AsMemory());

        Assert.Equal(RemoteCredentialStoreStatus.Failed, result.Status);
        Assert.Equal(0, native.WriteCalls);
    }

    [Fact]
    public async Task ReadCopiesTheSecretAndAlwaysFreesTheNativeCredential()
    {
        var native = new FakeNative();
        var profileId = Guid.NewGuid();
        var target = WindowsCredentialTargetNames.GetTargetName(profileId, RemoteCredentialKind.SmbPassword);
        native.Stored[target] = Encoding.Unicode.GetBytes("fictional-password");
        var store = new WindowsCredentialManagerRemoteCredentialStore(native, () => true);

        using var result = await store.ReadAsync(profileId, RemoteCredentialKind.SmbPassword);

        Assert.True(result.IsSuccess);
        Assert.Equal("fictional-password", new string(result.Secret!.Memory.Span));
        Assert.Equal(1, native.LastReadHandle!.DisposeCalls);
        Assert.DoesNotContain("fictional-password", result.Secret.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadProcessingFailureStillFreesTheNativeCredential()
    {
        var native = new FakeNative { ThrowWhenReadingBlob = true };
        var store = new WindowsCredentialManagerRemoteCredentialStore(native, () => true);

        using var result = await store.ReadAsync(Guid.NewGuid(), RemoteCredentialKind.SshPassword);

        Assert.Equal(RemoteCredentialStoreStatus.CredentialStoreUnavailable, result.Status);
        Assert.Equal(1, native.LastReadHandle!.DisposeCalls);
    }

    [Fact]
    public async Task MalformedCredentialBlobIsRejectedAndStillFreed()
    {
        var native = new FakeNative { ForcedReadBlob = [0x61] };
        var store = new WindowsCredentialManagerRemoteCredentialStore(native, () => true);

        using var result = await store.ReadAsync(Guid.NewGuid(), RemoteCredentialKind.SshPassword);

        Assert.Equal(RemoteCredentialStoreStatus.Failed, result.Status);
        Assert.Equal(1, native.LastReadHandle!.DisposeCalls);
    }

    [Fact]
    public async Task NotFoundReadAndDeleteAreControlledAndIdempotent()
    {
        var native = new FakeNative();
        var store = new WindowsCredentialManagerRemoteCredentialStore(native, () => true);

        using var read = await store.ReadAsync(Guid.NewGuid(), RemoteCredentialKind.SshPassword);
        var delete = await store.DeleteAsync(Guid.NewGuid(), RemoteCredentialKind.SshPassword);

        Assert.Equal(RemoteCredentialStoreStatus.NotFound, read.Status);
        Assert.True(delete.IsSuccess);
    }

    [Fact]
    public async Task DeleteAllTouchesOnlyTheThreeKnownAppOwnedTargets()
    {
        var native = new FakeNative();
        var profileId = Guid.NewGuid();
        var store = new WindowsCredentialManagerRemoteCredentialStore(native, () => true);

        var result = await store.DeleteAllForProfileAsync(profileId);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, native.DeleteTargets.Count);
        Assert.All(native.DeleteTargets, target => Assert.StartsWith($"NutManager:RemoteCredential:v1:{profileId:N}:", target, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnsupportedPlatformFailsClosedWithoutCallingNativeCredentialManager()
    {
        var native = new FakeNative();
        var store = new WindowsCredentialManagerRemoteCredentialStore(native, () => false);

        var result = await store.WriteAsync(Guid.NewGuid(), RemoteCredentialKind.SshPassword, "fictional-password".AsMemory());

        Assert.Equal(RemoteCredentialStoreStatus.Unsupported, result.Status);
        Assert.Equal(0, native.WriteCalls);
    }

    [Theory]
    [InlineData(5, RemoteCredentialStoreStatus.AccessDenied)]
    [InlineData(87, RemoteCredentialStoreStatus.Failed)]
    public async Task NativeWriteErrorsAreMappedWithoutReturningSecretContent(int errorCode, RemoteCredentialStoreStatus expected)
    {
        var native = new FakeNative { WriteErrorCode = errorCode };
        var store = new WindowsCredentialManagerRemoteCredentialStore(native, () => true);

        var result = await store.WriteAsync(Guid.NewGuid(), RemoteCredentialKind.SshPassword, "fictional-password".AsMemory());

        Assert.Equal(expected, result.Status);
        Assert.DoesNotContain("fictional-password", result.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void DisposableSecretZerosItsBufferAndDoesNotExposeItsValueInToString()
    {
        using var secret = new RemoteCredentialSecret("fictional-password".AsSpan());
        var memory = secret.Memory;

        secret.Dispose();
        secret.Dispose();

        Assert.All(memory.Span.ToArray(), value => Assert.Equal('\0', value));
        Assert.DoesNotContain("fictional-password", secret.ToString(), StringComparison.Ordinal);
    }

    private sealed class FakeNative : IWindowsCredentialManagerNative
    {
        public Dictionary<string, byte[]> Stored { get; } = new(StringComparer.Ordinal);
        public WindowsCredentialNativeWriteRequest? LastWrite { get; private set; }
        public List<string> DeleteTargets { get; } = [];
        public FakeReadHandle? LastReadHandle { get; private set; }
        public int WriteCalls { get; private set; }
        public bool ThrowWhenReadingBlob { get; set; }
        public byte[]? ForcedReadBlob { get; set; }
        public int? WriteErrorCode { get; set; }

        public bool TryWrite(WindowsCredentialNativeWriteRequest request, out int errorCode)
        {
            WriteCalls++;
            LastWrite = request;
            if (WriteErrorCode is { } writeErrorCode)
            {
                errorCode = writeErrorCode;
                return false;
            }

            Stored[request.TargetName] = request.CredentialBlob.ToArray();
            errorCode = 0;
            return true;
        }

        public bool TryRead(string targetName, uint type, uint flags, out IWindowsCredentialNativeReadHandle? credential, out int errorCode)
        {
            if (ForcedReadBlob is { } forcedReadBlob)
            {
                LastReadHandle = new FakeReadHandle(forcedReadBlob, false);
                credential = LastReadHandle;
                errorCode = 0;
                return true;
            }

            if (ThrowWhenReadingBlob)
            {
                LastReadHandle = new FakeReadHandle(Encoding.Unicode.GetBytes("fictional-password"), true);
                credential = LastReadHandle;
                errorCode = 0;
                return true;
            }

            if (!Stored.TryGetValue(targetName, out var blob))
            {
                credential = null;
                errorCode = 1168;
                return false;
            }

            LastReadHandle = new FakeReadHandle(blob, false);
            credential = LastReadHandle;
            errorCode = 0;
            return true;
        }

        public bool TryDelete(string targetName, uint type, uint flags, out int errorCode)
        {
            DeleteTargets.Add(targetName);
            if (!Stored.Remove(targetName))
            {
                errorCode = 1168;
                return false;
            }

            errorCode = 0;
            return true;
        }
    }

    private sealed class FakeReadHandle : IWindowsCredentialNativeReadHandle
    {
        private readonly byte[] _blob;
        private readonly bool _throws;

        public FakeReadHandle(byte[] blob, bool throws)
        {
            _blob = blob.ToArray();
            _throws = throws;
        }

        public int DisposeCalls { get; private set; }

        public ReadOnlyMemory<byte> GetCredentialBlob() => _throws
            ? throw new InvalidOperationException()
            : _blob;

        public void Dispose() => DisposeCalls++;
    }
}
