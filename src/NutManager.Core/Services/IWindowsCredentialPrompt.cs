using NutManager.Core.Models;

namespace NutManager.Core.Services;

/// <summary>
/// Asks Windows itself for a user name and password. NutManager deliberately owns no credential
/// input control for this: the operating system already has the trusted dialog, it knows how to
/// present smart cards, Hello and domain accounts, and routing through it means a typed password
/// never passes through an application text box.
///
/// The contract is platform-neutral so callers and tests never touch Win32. An implementation that
/// cannot show the dialog reports <see cref="WindowsCredentialPromptStatus.Unsupported"/> rather
/// than throwing.
/// </summary>
public interface IWindowsCredentialPrompt
{
    /// <param name="request">What to show, and which window the dialog belongs to.</param>
    Task<WindowsCredentialPromptResult> RequestAsync(
        WindowsCredentialPromptRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What the dialog should say and where it should appear. The owner handle is an opaque value the
/// App supplies; Core never interprets it.
/// </summary>
public sealed record WindowsCredentialPromptRequest(
    string Caption,
    string Message,
    string? PreferredUsername = null,
    nint OwnerWindowHandle = 0,
    bool OfferToRemember = true);

public enum WindowsCredentialPromptStatus
{
    Success,
    Cancelled,
    Unsupported,
    Failed
}

/// <summary>
/// The dialog's answer. The password is a disposable buffer, never a string: on success the caller
/// owns it and must dispose it, which zeroes the copy. There is deliberately no property that could
/// expose the value, and <see cref="ToString"/> is redacted.
/// </summary>
public sealed class WindowsCredentialPromptResult : IDisposable
{
    private WindowsCredentialPromptResult(
        WindowsCredentialPromptStatus status,
        string? username,
        RemoteCredentialSecret? secret,
        bool remember,
        string? message)
    {
        Status = status;
        Username = username;
        Secret = secret;
        Remember = remember;
        Message = message;
    }

    public WindowsCredentialPromptStatus Status { get; }

    /// <summary>The account Windows returned, such as <c>DOMAIN\user</c>. Non-secret metadata.</summary>
    public string? Username { get; }

    public RemoteCredentialSecret? Secret { get; }

    /// <summary>The dialog's own "remember" choice. Persisting it remains NutManager's decision.</summary>
    public bool Remember { get; }

    public string? Message { get; }

    public bool IsSuccess => Status == WindowsCredentialPromptStatus.Success && Secret is not null &&
        !string.IsNullOrWhiteSpace(Username);

    public static WindowsCredentialPromptResult Success(string username, ReadOnlySpan<char> password, bool remember) =>
        new(WindowsCredentialPromptStatus.Success, username, new RemoteCredentialSecret(password), remember, null);

    public static WindowsCredentialPromptResult Cancelled() =>
        new(WindowsCredentialPromptStatus.Cancelled, null, null, false, null);

    public static WindowsCredentialPromptResult Unsupported(string? message = null) =>
        new(WindowsCredentialPromptStatus.Unsupported, null, null, false, message);

    public static WindowsCredentialPromptResult Failed(string? message = null) =>
        new(WindowsCredentialPromptStatus.Failed, null, null, false, message);

    public void Dispose() => Secret?.Dispose();

    public override string ToString() => $"WindowsCredentialPromptResult({Status})";
}
