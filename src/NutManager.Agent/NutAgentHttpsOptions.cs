using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NutManager.Agent;

/// <summary>
/// The agent's server-side configuration, read once at startup.
///
/// It holds no secret and cannot be made to. There is no password, no PFX, no private key and no
/// client credential here — the certificate is named by thumbprint and lives in the Windows
/// certificate store, where the private key is protected by the operating system rather than by a
/// file this process could be tricked into reading.
///
/// A missing file means HTTPS is off, which is the default an installation gets by doing nothing.
/// </summary>
internal sealed record NutAgentHttpsOptions
{
    public const string DirectoryName = "NutManager";
    public const string FileName = "agent.json";

    /// <summary>Off unless a deployment deliberately turned it on.</summary>
    public bool HttpsEnabled { get; init; }

    /// <summary>The HTTP.sys prefix, for example <c>https://gandalf.sbra.local:5199/</c>.</summary>
    public string? HttpsPrefix { get; init; }

    /// <summary>Identifies the certificate in LocalMachine\My. Never the certificate itself.</summary>
    public string? CertificateThumbprint { get; init; }

    public static NutAgentHttpsOptions Disabled => new();

    /// <summary>
    /// Where the configuration lives. Under ProgramData because it belongs to the machine rather
    /// than to whoever installed it, and its ACL is a deployment concern documented for the
    /// administrator.
    /// </summary>
    internal static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        DirectoryName,
        "Agent",
        FileName);

    /// <summary>
    /// Reads the file, or returns the disabled default. Unreadable and malformed both mean disabled:
    /// a configuration this process cannot understand must not become a listener it did not intend.
    /// </summary>
    internal static NutAgentHttpsOptions Load(string? path = null)
    {
        var target = path ?? DefaultPath;

        try
        {
            if (!File.Exists(target)) return Disabled;

            var parsed = JsonSerializer.Deserialize<NutAgentHttpsOptions>(
                File.ReadAllText(target),
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    Converters = { new JsonStringEnumConverter() }
                });

            return parsed ?? Disabled;
        }
        catch (Exception)
        {
            return Disabled;
        }
    }

    /// <summary>
    /// Whether this configuration is usable as written. Pure, so every rejection reason can be
    /// asserted without a certificate store or a listener.
    ///
    /// A prefix that is not HTTPS is refused rather than corrected: the agent must never end up
    /// listening in plain text because a character was wrong in a file.
    /// </summary>
    internal static bool Validate(NutAgentHttpsOptions options, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.HttpsEnabled)
        {
            failure = null;
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.HttpsPrefix))
        {
            failure = "HTTPS is enabled but no prefix is configured.";
            return false;
        }

        var prefix = options.HttpsPrefix.Trim();
        if (!prefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            failure = "The agent HTTPS prefix must use https.";
            return false;
        }

        if (!prefix.EndsWith('/'))
        {
            // HTTP.sys requires the trailing slash, and silently appending one would mean the
            // listener binds to something the administrator did not write down.
            failure = "The agent HTTPS prefix must end with a forward slash.";
            return false;
        }

        // Checked before parsing, because Uri cannot represent these at all and the failure would
        // otherwise be reported as a malformed URI — true, but not the reason that matters. A
        // wildcard binding on a privileged agent accepts requests aimed at any name that resolves to
        // this machine, which is the ambiguity the HTTP.sys documentation warns about.
        var authority = prefix["https://".Length..];
        if (authority.StartsWith('*') || authority.StartsWith('+'))
        {
            failure = "The agent HTTPS prefix must name an explicit host rather than a wildcard.";
            return false;
        }

        if (!Uri.TryCreate(prefix, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            failure = "The agent HTTPS prefix must be an absolute URI naming a host.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.CertificateThumbprint))
        {
            failure = "HTTPS is enabled but no certificate thumbprint is configured.";
            return false;
        }

        if (!IsPlausibleThumbprint(options.CertificateThumbprint))
        {
            failure = "The certificate thumbprint is not a hexadecimal value.";
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>Thumbprints are hex. Anything else is a typo or an attempt to smuggle a path.</summary>
    internal static bool IsPlausibleThumbprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = Normalize(value);
        return trimmed.Length >= 40 && trimmed.All(Uri.IsHexDigit);
    }

    internal static string Normalize(string thumbprint) =>
        thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
}

/// <summary>
/// Looks the certificate up in the machine store. Windows-typed, so it lives behind one annotation.
///
/// The agent does not install, generate or trust a certificate. It checks that the one the
/// administrator named exists and has a usable private key, and refuses to start HTTPS otherwise —
/// the alternative is a listener that accepts connections and fails every handshake, which looks
/// like a network problem and is not one.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NutAgentCertificateCheck
{
    internal static bool Exists(string thumbprint, out string? failure)
    {
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);

            var normalized = NutAgentHttpsOptions.Normalize(thumbprint);
            var match = store.Certificates.FirstOrDefault(certificate =>
                string.Equals(certificate.Thumbprint, normalized, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                failure = $"No certificate with thumbprint {normalized} was found in LocalMachine\\My.";
                return false;
            }

            using (match)
            {
                if (!match.HasPrivateKey)
                {
                    failure = "The configured certificate has no private key on this machine.";
                    return false;
                }
            }

            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = $"The certificate store could not be read ({exception.GetType().Name}).";
            return false;
        }
    }
}
