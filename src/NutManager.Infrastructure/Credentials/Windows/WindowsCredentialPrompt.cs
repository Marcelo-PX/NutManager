using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.Credentials.Windows;

/// <summary>
/// What the native dialog handed back, before any of it is turned into a managed result. The
/// password arrives as a mutable array precisely so the caller can zero it; nothing here is a
/// string, because a string could not be erased afterwards.
/// </summary>
public sealed class WindowsCredentialPromptNativeOutcome
{
    public int ResultCode { get; init; }

    public string? Username { get; init; }

    public char[]? Password { get; init; }

    public bool Remember { get; init; }
}

/// <summary>
/// The narrow native seam. It exists for the same reason <c>IWindowsSmbNativeLogon</c> does: the
/// buffer lifetime around a credential can then be tested without opening a real Windows dialog.
/// </summary>
public interface IWindowsCredentialPromptNative
{
    WindowsCredentialPromptNativeOutcome Prompt(
        string caption,
        string message,
        string? preferredUsername,
        nint ownerWindowHandle,
        bool offerToRemember);
}

/// <summary>
/// Shows the Windows credential dialog and converts its answer into a disposable secret.
///
/// The dialog is modal and is shown on the calling thread so it stays owned by the window that
/// asked for it. That is the documented use of the API and it is what makes the prompt appear in
/// front of NutManager rather than behind it.
/// </summary>
public sealed class WindowsCredentialPrompt : IWindowsCredentialPrompt
{
    private const int ErrorSuccess = 0;
    private const int ErrorCancelled = 1223;

    private readonly IWindowsCredentialPromptNative _native;

    /// <param name="native">
    /// Overridden only by tests, so the buffer lifetime around a credential can be exercised
    /// without opening a real Windows dialog.
    /// </param>
    public WindowsCredentialPrompt(IWindowsCredentialPromptNative? native = null) =>
        _native = native ?? new WindowsCredentialPromptNative();

    public Task<WindowsCredentialPromptResult> RequestAsync(
        WindowsCredentialPromptRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(WindowsCredentialPromptResult.Unsupported("Credential.Prompt.WindowsOnly"));
        }

        WindowsCredentialPromptNativeOutcome outcome;
        try
        {
            outcome = _native.Prompt(
                request.Caption,
                request.Message,
                request.PreferredUsername,
                request.OwnerWindowHandle,
                request.OfferToRemember);
        }
        catch (DllNotFoundException)
        {
            return Task.FromResult(WindowsCredentialPromptResult.Unsupported("Credential.Prompt.WindowsOnly"));
        }
        catch (EntryPointNotFoundException)
        {
            return Task.FromResult(WindowsCredentialPromptResult.Unsupported("Credential.Prompt.WindowsOnly"));
        }

        try
        {
            if (outcome.ResultCode == ErrorCancelled)
            {
                return Task.FromResult(WindowsCredentialPromptResult.Cancelled());
            }

            if (outcome.ResultCode != ErrorSuccess || outcome.Password is null || string.IsNullOrWhiteSpace(outcome.Username))
            {
                return Task.FromResult(WindowsCredentialPromptResult.Failed("Credential.Prompt.Failed"));
            }

            // An empty password is a valid thing for the dialog to return and a useless thing to
            // store, so it is refused here rather than saved and then rejected by the share.
            if (outcome.Password.Length == 0)
            {
                return Task.FromResult(WindowsCredentialPromptResult.Failed("Credential.Prompt.Failed"));
            }

            return Task.FromResult(WindowsCredentialPromptResult.Success(
                outcome.Username,
                outcome.Password,
                outcome.Remember));
        }
        finally
        {
            // The managed copy is erased whatever happened above. Success already took its own
            // copy into the disposable secret, so nothing is lost by clearing this one.
            if (outcome.Password is { Length: > 0 })
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(outcome.Password.AsSpan()));
            }
        }
    }
}

/// <summary>
/// The real P/Invoke layer. Every buffer it allocates is zeroed and released before it returns,
/// including on the failure paths.
/// </summary>
public sealed class WindowsCredentialPromptNative : IWindowsCredentialPromptNative
{
    private const uint CredUiWinGeneric = 0x00000001;
    private const uint CredUiWinCheckbox = 0x00000002;
    private const uint CredPackGenericCredentials = 0x00000004;
    private const int ErrorInsufficientBuffer = 122;

    // Documented Windows limits; the unpack call still reports what it needs if these fall short.
    private const int MaxUsername = 513;
    private const int MaxDomain = 256;
    private const int MaxPassword = 256;

    public WindowsCredentialPromptNativeOutcome Prompt(
        string caption,
        string message,
        string? preferredUsername,
        nint ownerWindowHandle,
        bool offerToRemember)
    {
        var info = new CredUiInfo
        {
            cbSize = Marshal.SizeOf<CredUiInfo>(),
            hwndParent = ownerWindowHandle,
            pszCaptionText = caption,
            pszMessageText = message,
            hbmBanner = nint.Zero
        };

        var inBuffer = nint.Zero;
        uint inBufferSize = 0;
        if (!string.IsNullOrWhiteSpace(preferredUsername))
        {
            // Pre-selecting the account is a convenience only. If packing fails the dialog simply
            // opens empty, which is still a working prompt.
            TryPackPreferredUsername(preferredUsername, out inBuffer, out inBufferSize);
        }

        uint authPackage = 0;
        var save = false;
        var flags = CredUiWinGeneric | (offerToRemember ? CredUiWinCheckbox : 0u);

        var code = CredUIPromptForWindowsCredentials(
            ref info,
            0,
            ref authPackage,
            inBuffer,
            inBufferSize,
            out var outBuffer,
            out var outBufferSize,
            ref save,
            flags);

        try
        {
            if (code != 0 || outBuffer == nint.Zero)
            {
                return new WindowsCredentialPromptNativeOutcome { ResultCode = code };
            }

            return Unpack(outBuffer, outBufferSize, save, code);
        }
        finally
        {
            ReleaseNativeBuffer(ref outBuffer, outBufferSize);
            ReleaseNativeBuffer(ref inBuffer, inBufferSize);
        }
    }

    private static WindowsCredentialPromptNativeOutcome Unpack(nint buffer, uint bufferSize, bool save, int code)
    {
        var usernameLength = (uint)MaxUsername;
        var domainLength = (uint)MaxDomain;
        var passwordLength = (uint)MaxPassword;
        var username = new char[usernameLength];
        var domain = new char[domainLength];
        var password = new char[passwordLength];

        var unpacked = CredUnPackAuthenticationBuffer(
            0, buffer, bufferSize,
            username, ref usernameLength,
            domain, ref domainLength,
            password, ref passwordLength);

        if (!unpacked && Marshal.GetLastWin32Error() == ErrorInsufficientBuffer)
        {
            // Windows has told us exactly how much it needs; the first, too-small buffers are
            // erased before being replaced so no fragment survives in a discarded array.
            Erase(username, domain, password);
            username = new char[usernameLength];
            domain = new char[domainLength];
            password = new char[passwordLength];
            unpacked = CredUnPackAuthenticationBuffer(
                0, buffer, bufferSize,
                username, ref usernameLength,
                domain, ref domainLength,
                password, ref passwordLength);
        }

        if (!unpacked)
        {
            Erase(username, domain, password);
            return new WindowsCredentialPromptNativeOutcome { ResultCode = Marshal.GetLastWin32Error() };
        }

        try
        {
            // The lengths Windows reports include the terminator, and the dialog may return the
            // domain separately from the account name.
            var account = TrimToText(username);
            var authority = TrimToText(domain);
            var qualified = authority.Length > 0 && !account.Contains('\\', StringComparison.Ordinal) &&
                !account.Contains('@', StringComparison.Ordinal)
                ? $"{authority}\\{account}"
                : account;

            var secretLength = LengthOfText(password);
            var secret = new char[secretLength];
            password.AsSpan(0, secretLength).CopyTo(secret);

            return new WindowsCredentialPromptNativeOutcome
            {
                ResultCode = code,
                Username = qualified,
                Password = secret,
                Remember = save
            };
        }
        finally
        {
            Erase(username, domain, password);
        }
    }

    private static void TryPackPreferredUsername(string preferredUsername, out nint buffer, out uint bufferSize)
    {
        buffer = nint.Zero;
        bufferSize = 0;
        uint required = 0;

        // The first call only measures; it is expected to fail.
        CredPackAuthenticationBuffer(CredPackGenericCredentials, preferredUsername, string.Empty, nint.Zero, ref required);
        if (required == 0)
        {
            return;
        }

        var candidate = Marshal.AllocCoTaskMem((int)required);
        if (CredPackAuthenticationBuffer(CredPackGenericCredentials, preferredUsername, string.Empty, candidate, ref required))
        {
            buffer = candidate;
            bufferSize = required;
            return;
        }

        ReleaseNativeBuffer(ref candidate, required);
    }

    private static void ReleaseNativeBuffer(ref nint buffer, uint size)
    {
        if (buffer == nint.Zero)
        {
            return;
        }

        // Wipe before freeing: the authentication buffer holds the password in whatever encoding
        // the authentication package chose, and freed memory is reused. Copying a zeroed array
        // over it does the same job as a pointer clear without turning on unsafe compilation for
        // the whole project.
        if (size > 0)
        {
            Marshal.Copy(new byte[size], 0, buffer, (int)size);
        }

        Marshal.FreeCoTaskMem(buffer);
        buffer = nint.Zero;
    }

    private static void Erase(params char[][] buffers)
    {
        foreach (var buffer in buffers)
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(buffer.AsSpan()));
        }
    }

    private static int LengthOfText(char[] buffer)
    {
        var terminator = Array.IndexOf(buffer, '\0');
        return terminator < 0 ? buffer.Length : terminator;
    }

    private static string TrimToText(char[] buffer) => new(buffer, 0, LengthOfText(buffer));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CredUiInfo
    {
        public int cbSize;
        public nint hwndParent;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszMessageText;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszCaptionText;
        public nint hbmBanner;
    }

    [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int CredUIPromptForWindowsCredentials(
        ref CredUiInfo uiInfo,
        uint authError,
        ref uint authPackage,
        nint inAuthBuffer,
        uint inAuthBufferSize,
        out nint outAuthBuffer,
        out uint outAuthBufferSize,
        [MarshalAs(UnmanagedType.Bool)] ref bool save,
        uint flags);

    [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredUnPackAuthenticationBuffer(
        uint flags,
        nint authBuffer,
        uint authBufferSize,
        [Out] char[] userName,
        ref uint maxUserName,
        [Out] char[] domainName,
        ref uint maxDomainName,
        [Out] char[] password,
        ref uint maxPassword);

    [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredPackAuthenticationBuffer(
        uint flags,
        string userName,
        string password,
        nint packedCredentials,
        ref uint packedCredentialsSize);
}
