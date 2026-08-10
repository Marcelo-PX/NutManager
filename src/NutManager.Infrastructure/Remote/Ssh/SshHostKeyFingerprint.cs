using System.Security.Cryptography;

namespace NutManager.Infrastructure.Remote.Ssh;

/// <summary>
/// Computes and compares OpenSSH-style SHA-256 host-key fingerprints without trust-on-first-use behavior.
/// </summary>
public static class SshHostKeyFingerprint
{
    public static string Create(ReadOnlySpan<byte> hostKey) => "SHA256:" + Convert.ToBase64String(SHA256.HashData(hostKey));

    public static bool Matches(string? trustedFingerprint, ReadOnlySpan<byte> presentedHostKey) =>
        !string.IsNullOrWhiteSpace(trustedFingerprint) &&
        string.Equals(trustedFingerprint, Create(presentedHostKey), StringComparison.Ordinal);
}
