using System.Reflection;
using NutManager.Core.Services;
using NutManager.Infrastructure.Credentials.Windows;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// The credential prompt as its callers see it. No test opens a real Windows dialog: the native
/// seam is faked, which is also what makes the buffer handling observable — a fake can hand over an
/// array and then check whether the wrapper erased it.
/// </summary>
public sealed class WindowsCredentialPromptTests
{
    private const string Password = "PROMPT_SECRET_SENTINEL_5B31C7";

    private sealed class FakeNative : IWindowsCredentialPromptNative
    {
        private readonly WindowsCredentialPromptNativeOutcome _outcome;
        private readonly Exception? _throws;

        public FakeNative(WindowsCredentialPromptNativeOutcome outcome) => _outcome = outcome;

        public FakeNative(Exception throws)
        {
            _throws = throws;
            _outcome = new WindowsCredentialPromptNativeOutcome();
        }

        public int Calls { get; private set; }

        public string? Caption { get; private set; }

        public string? PreferredUsername { get; private set; }

        public nint OwnerWindowHandle { get; private set; }

        public bool OfferedToRemember { get; private set; }

        /// <summary>The very array handed to the wrapper, so the test can inspect it afterwards.</summary>
        public char[]? IssuedPassword => _outcome.Password;

        public WindowsCredentialPromptNativeOutcome Prompt(
            string caption, string message, string? preferredUsername, nint ownerWindowHandle, bool offerToRemember)
        {
            Calls++;
            Caption = caption;
            PreferredUsername = preferredUsername;
            OwnerWindowHandle = ownerWindowHandle;
            OfferedToRemember = offerToRemember;
            if (_throws is not null) throw _throws;
            return _outcome;
        }
    }

    private static WindowsCredentialPromptNativeOutcome Succeeding(bool remember = false, string user = @"SBRA\pt90") =>
        new() { ResultCode = 0, Username = user, Password = Password.ToCharArray(), Remember = remember };

    private static WindowsCredentialPromptRequest Request(string? preferred = null, nint owner = 0) =>
        new("Credencial SMB", @"\\server\share", preferred, owner);

    [Fact]
    public async Task ASuccessfulPromptReturnsTheAccountAndADisposableSecret()
    {
        var native = new FakeNative(Succeeding());
        var prompt = new WindowsCredentialPrompt(native);

        using var result = await prompt.RequestAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(WindowsCredentialPromptStatus.Success, result.Status);
        Assert.Equal(@"SBRA\pt90", result.Username);
        Assert.Equal(Password, new string(result.Secret!.Memory.Span));
        Assert.False(result.Remember);
    }

    [Fact]
    public async Task TheRequestReachesTheDialogIncludingItsOwnerWindow()
    {
        var native = new FakeNative(Succeeding());
        var prompt = new WindowsCredentialPrompt(native);

        using var result = await prompt.RequestAsync(Request(preferred: @"SBRA\other", owner: 4242));

        Assert.Equal(1, native.Calls);
        Assert.Equal("Credencial SMB", native.Caption);
        Assert.Equal(@"SBRA\other", native.PreferredUsername);
        // Owner handle: the dialog has to belong to the application window, not float behind it.
        Assert.Equal(4242, native.OwnerWindowHandle);
        Assert.True(native.OfferedToRemember);
    }

    [Fact]
    public async Task CancellingReportsCancelledRatherThanAFailure()
    {
        // 1223 is ERROR_CANCELLED. It is an ordinary outcome, not an error to report as a fault.
        var native = new FakeNative(new WindowsCredentialPromptNativeOutcome { ResultCode = 1223 });
        var prompt = new WindowsCredentialPrompt(native);

        using var result = await prompt.RequestAsync(Request());

        Assert.Equal(WindowsCredentialPromptStatus.Cancelled, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Username);
        Assert.Null(result.Secret);
    }

    [Theory]
    [InlineData(5, @"SBRA\pt90", true)]
    [InlineData(0, null, true)]
    [InlineData(0, @"SBRA\pt90", false)]
    public async Task AnIncompleteOrFailedAnswerIsRefused(int code, string? username, bool withPassword)
    {
        var native = new FakeNative(new WindowsCredentialPromptNativeOutcome
        {
            ResultCode = code,
            Username = username,
            Password = withPassword ? Password.ToCharArray() : null
        });
        var prompt = new WindowsCredentialPrompt(native);

        using var result = await prompt.RequestAsync(Request());

        Assert.Equal(WindowsCredentialPromptStatus.Failed, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Secret);
    }

    [Fact]
    public async Task AMissingCredentialLibraryIsUnsupportedRatherThanAnException()
    {
        var prompt = new WindowsCredentialPrompt(new FakeNative(new DllNotFoundException("credui.dll")));

        using var result = await prompt.RequestAsync(Request());

        Assert.Equal(WindowsCredentialPromptStatus.Unsupported, result.Status);
    }

    [Fact]
    public async Task TheRememberChoiceIsCarriedThroughUntouched()
    {
        using var remembered = await new WindowsCredentialPrompt(new FakeNative(Succeeding(remember: true)))
            .RequestAsync(Request());
        using var session = await new WindowsCredentialPrompt(new FakeNative(Succeeding(remember: false)))
            .RequestAsync(Request());

        // The dialog decides; persisting remains NutManager's own step.
        Assert.True(remembered.Remember);
        Assert.False(session.Remember);
    }

    // ==================== Secret hygiene ====================

    [Fact]
    public async Task TheArrayHandedOverByTheDialogIsErasedOnceItHasBeenCopied()
    {
        var native = new FakeNative(Succeeding());
        var issued = native.IssuedPassword!;

        using var result = await new WindowsCredentialPrompt(native).RequestAsync(Request());

        // The result carries its own copy; the transient array the native layer produced must not
        // still hold the password once the call has returned.
        Assert.Equal(Password, new string(result.Secret!.Memory.Span));
        Assert.All(issued, character => Assert.Equal('\0', character));
    }

    [Fact]
    public async Task AFailedAnswerAlsoErasesWhateverWasHandedOver()
    {
        var native = new FakeNative(new WindowsCredentialPromptNativeOutcome
        {
            ResultCode = 0,
            Username = null,
            Password = Password.ToCharArray()
        });
        var issued = native.IssuedPassword!;

        using var result = await new WindowsCredentialPrompt(native).RequestAsync(Request());

        Assert.False(result.IsSuccess);
        Assert.All(issued, character => Assert.Equal('\0', character));
    }

    [Fact]
    public async Task DisposingTheResultZeroesItsOwnCopy()
    {
        var result = await new WindowsCredentialPrompt(new FakeNative(Succeeding())).RequestAsync(Request());
        var secret = result.Secret!;

        result.Dispose();

        Assert.True(secret.Memory.IsEmpty);
    }

    [Fact]
    public void NoPropertyOnTheResultCanExposeThePassword()
    {
        var readable = typeof(WindowsCredentialPromptResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Password", readable);
        Assert.DoesNotContain("Secret", readable.Where(name => name != "Secret").ToArray());
        // The secret is reachable only as a disposable buffer, never as text.
        Assert.Equal(typeof(NutManager.Core.Models.RemoteCredentialSecret),
            typeof(WindowsCredentialPromptResult).GetProperty("Secret")!.PropertyType);
    }

    [Fact]
    public async Task NeitherTheResultNorTheSecretPrintsThePasswordWhenLogged()
    {
        using var result = await new WindowsCredentialPrompt(new FakeNative(Succeeding())).RequestAsync(Request());

        Assert.DoesNotContain(Password, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, result.Secret!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheNativeLayerZeroesAndFreesEveryBufferItAllocates()
    {
        // The real P/Invoke path cannot be exercised without showing a dialog, so the guarantees
        // that matter are pinned at the source level instead of being left undocumented.
        var source = Repository.Read(Path.Combine(
            "src", "NutManager.Infrastructure", "Credentials", "Windows", "WindowsCredentialPrompt.cs"));

        Assert.Contains("Marshal.FreeCoTaskMem", source, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.ZeroMemory", source, StringComparison.Ordinal);
        // The output buffer is wiped before it is released, not merely released.
        Assert.Contains("Marshal.Copy(new byte[size], 0, buffer, (int)size);", source, StringComparison.Ordinal);
        // Nothing shells out to resolve a credential.
        foreach (var forbidden in new[] { "Process.Start", "cmdkey", "net use", "powershell", "rundll32" })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }
}
