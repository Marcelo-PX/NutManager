using System.Globalization;
using System.Net;
using System.Net.Sockets;
using NutManager.Core.Agent;
using NutManager.Core.Models;

namespace NutManager.Core.Validation;

public static class ManagedProfileFields
{
    public const string Name = "Name";
    public const string MonitoringHost = "MonitoringHost";
    public const string MonitoringPort = "MonitoringPort";
    public const string PreferredUpsName = "PreferredUpsName";
    public const string ManagementMode = "ManagementMode";
    public const string AccessMode = "AccessMode";
    public const string ManagementHost = "ManagementHost";
    public const string SshPort = "SshPort";
    public const string SshUsername = "SshUsername";
    public const string SshAuthenticationMode = "SshAuthenticationMode";
    public const string SshPrivateKeyPath = "SshPrivateKeyPath";
    public const string RemoteConfigurationDirectory = "RemoteConfigurationDirectory";
    public const string ConfigurationTransport = "ConfigurationTransport";
    public const string SmbSharePath = "SmbSharePath";
    public const string SmbConfigurationDirectory = "SmbConfigurationDirectory";
    public const string SmbAuthenticationMode = "SmbAuthenticationMode";
    public const string SmbUsername = "SmbUsername";
    public const string AgentHttpsEndpoint = "AgentHttpsEndpoint";
}

public sealed record ManagedNutServerProfileInput(
    Guid Id,
    string? Name,
    string? MonitoringHost,
    string? MonitoringPort,
    string? PreferredUpsName,
    NutManagementMode ManagementMode,
    ManagedNutServerAccessMode AccessMode,
    string? ManagementHost,
    string? RemoteConfigurationDirectory,
    string? SshPort,
    string? SshUsername,
    SshAuthenticationMode SshAuthenticationMode,
    string? SshPrivateKeyPath,
    string? TrustedHostKeyFingerprint,
    string? TrustedHostKeyAlgorithm,
    RemoteConfigurationTransportKind ConfigurationTransport,
    string? SmbSharePath,
    string? SmbConfigurationDirectory,
    SmbAuthenticationMode SmbAuthenticationMode,
    string? SmbUsername,
    ManagedNutConfigurationFiles? ManagedFiles = null,
    NutAgentTransportKind AgentTransport = NutAgentTransportKind.NamedPipe,
    string? AgentHttpsEndpoint = null,
    NutAgentAuthenticationMode AgentAuthentication = NutAgentAuthenticationMode.CurrentWindowsIdentity,
    string? AgentUsername = null);

public sealed record ManagedNutServerProfileValidationResult(
    ManagedNutServerProfile? Profile,
    IReadOnlyList<FieldValidationIssue> Issues)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public bool CanSave => !HasErrors && Profile is not null;
}

public static class ManagedNutServerProfileValidator
{
    public static FieldValidationResult<string> ValidateHost(string? value, string field = ManagedProfileFields.MonitoringHost)
    {
        var issues = new List<FieldValidationIssue>();
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(Error(field, "Host.Required", "Validation.Host.Required"));
            return new FieldValidationResult<string>(null, issues);
        }

        var normalized = value.Trim();
        if (normalized.Length > 253 || normalized.Any(char.IsControl) || normalized.Any(char.IsWhiteSpace) ||
            normalized.Contains('@') || normalized.Contains('/') || normalized.Contains('\\') || normalized.Contains('%'))
        {
            issues.Add(Error(field, "Host.Invalid", "Validation.Host.Invalid"));
            return new FieldValidationResult<string>(null, issues);
        }

        if (IPAddress.TryParse(normalized, out var address) &&
            address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
        {
            return new FieldValidationResult<string>(normalized, issues);
        }

        if (normalized.Contains(':') || !IsHostname(normalized))
        {
            issues.Add(Error(field, "Host.Invalid", "Validation.Host.Invalid"));
            return new FieldValidationResult<string>(null, issues);
        }

        return new FieldValidationResult<string>(normalized, issues);
    }

    public static FieldValidationResult<int> ValidatePort(string? value, string field = ManagedProfileFields.MonitoringPort)
    {
        var issues = new List<FieldValidationIssue>();
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(Error(field, "Port.Required", "Validation.Port.Required"));
            return new FieldValidationResult<int>(default, issues);
        }

        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
        {
            issues.Add(Error(field, "Port.Invalid", "Validation.Port.Invalid"));
            return new FieldValidationResult<int>(default, issues);
        }

        if (port is < 1 or > 65535)
        {
            issues.Add(Error(field, "Port.Range", "Validation.Port.Range"));
            return new FieldValidationResult<int>(default, issues);
        }

        return new FieldValidationResult<int>(port, issues);
    }

    public static FieldValidationResult<string> ValidateProfileName(
        string? value,
        IEnumerable<ManagedNutServerProfile> existingProfiles,
        Guid currentProfileId)
    {
        ArgumentNullException.ThrowIfNull(existingProfiles);
        var issues = new List<FieldValidationIssue>();
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(Error(ManagedProfileFields.Name, "Profile.NameRequired", "Validation.Profile.NameRequired"));
            return new FieldValidationResult<string>(null, issues);
        }

        var normalized = value.Trim();
        if (normalized.Length > 80)
        {
            issues.Add(Error(ManagedProfileFields.Name, "Profile.NameTooLong", "Validation.Profile.NameTooLong"));
        }

        if (normalized.Any(char.IsControl))
        {
            issues.Add(Error(ManagedProfileFields.Name, "Profile.NameInvalid", "Validation.Profile.NameInvalid"));
        }

        if (existingProfiles.Any(profile => profile.Id != currentProfileId && string.Equals(profile.Name, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(Error(ManagedProfileFields.Name, "Profile.NameDuplicate", "Validation.Profile.NameDuplicate"));
        }

        return new FieldValidationResult<string>(issues.Count == 0 ? normalized : null, issues);
    }

    public static FieldValidationResult<string> ValidateUncShareRoot(string? value)
    {
        var issues = new List<FieldValidationIssue>();
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(Error(ManagedProfileFields.SmbSharePath, "Smb.ShareRequired", "Validation.Smb.ShareRootRequired"));
            return new FieldValidationResult<string>(null, issues);
        }

        var normalized = value.Trim();
        if (normalized.Any(char.IsControl) || normalized.Contains('/') || !normalized.StartsWith("\\\\", StringComparison.Ordinal) ||
            normalized.StartsWith("\\\\\\", StringComparison.Ordinal))
        {
            issues.Add(Error(ManagedProfileFields.SmbSharePath, "Smb.ShareInvalid", "Validation.Smb.ShareRootInvalid"));
            return new FieldValidationResult<string>(null, issues);
        }

        var parts = normalized[2..].Split('\\', StringSplitOptions.None);
        if (parts.Length != 2 || parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".." ||
            part.Any(char.IsControl) || part.Any(char.IsWhiteSpace) || part.Contains(':')))
        {
            issues.Add(Error(ManagedProfileFields.SmbSharePath, "Smb.ShareInvalid", "Validation.Smb.ShareRootInvalid"));
            return new FieldValidationResult<string>(null, issues);
        }

        return new FieldValidationResult<string>($"\\\\{parts[0]}\\{parts[1]}", issues);
    }

    public static ManagedNutServerProfileValidationResult Validate(
        ManagedNutServerProfileInput input,
        IEnumerable<ManagedNutServerProfile> existingProfiles)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(existingProfiles);
        var issues = new List<FieldValidationIssue>();
        var name = ValidateProfileName(input.Name, existingProfiles, input.Id);
        var monitoringHost = ValidateHost(input.MonitoringHost);
        var monitoringPort = ValidatePort(input.MonitoringPort);
        var preferredUpsName = ValidateOptionalText(
            input.PreferredUpsName,
            ManagedProfileFields.PreferredUpsName,
            255,
            "Profile.PreferredUpsInvalid",
            "Validation.Profile.PreferredUpsInvalid");
        issues.AddRange(name.Issues);
        issues.AddRange(monitoringHost.Issues);
        issues.AddRange(monitoringPort.Issues);
        issues.AddRange(preferredUpsName.Issues);

        if (!Enum.IsDefined(input.ManagementMode))
        {
            issues.Add(Error(ManagedProfileFields.ManagementMode, "Management.ModeInvalid", "Validation.Profile.ManagementModeInvalid"));
        }

        if (!Enum.IsDefined(input.AccessMode))
        {
            issues.Add(Error(ManagedProfileFields.AccessMode, "Access.ModeInvalid", "Validation.Profile.AccessModeInvalid"));
        }

        // Validated once and shared by both configuration transports: editing over SMB while
        // controlling over a named pipe is an ordinary combination, so one setting must never be
        // reachable only through the other.
        var agent = ValidateAgent(input, issues);

        NutManagementProfile? management = null;
        if (input.ManagementMode == NutManagementMode.Local)
        {
            management = new NutManagementProfile(NutManagementMode.Local, managedFiles: input.ManagedFiles);
        }
        else if (input.ManagementMode == NutManagementMode.Remote && input.ConfigurationTransport == RemoteConfigurationTransportKind.SshSftp)
        {
            var managementHost = ValidateHost(input.ManagementHost, ManagedProfileFields.ManagementHost);
            var sshPort = ValidatePort(input.SshPort, ManagedProfileFields.SshPort);
            var sshUsername = ValidateOptionalText(
                input.SshUsername,
                ManagedProfileFields.SshUsername,
                255,
                "Ssh.UsernameInvalid",
                "Validation.Ssh.UsernameInvalid");
            var privateKeyPath = ValidateOptionalText(
                input.SshPrivateKeyPath,
                ManagedProfileFields.SshPrivateKeyPath,
                1024,
                "Ssh.PrivateKeyInvalid",
                "Validation.Ssh.PrivateKeyInvalid");
            var remoteDirectory = ValidateOptionalText(
                input.RemoteConfigurationDirectory,
                ManagedProfileFields.RemoteConfigurationDirectory,
                1024,
                "Remote.ConfigurationDirectoryInvalid",
                "Validation.Remote.ConfigurationDirectoryInvalid");
            issues.AddRange(managementHost.Issues);
            issues.AddRange(sshPort.Issues);
            issues.AddRange(sshUsername.Issues);
            issues.AddRange(privateKeyPath.Issues);
            issues.AddRange(remoteDirectory.Issues);
            if (!Enum.IsDefined(input.SshAuthenticationMode))
            {
                issues.Add(Error(ManagedProfileFields.SshAuthenticationMode, "Ssh.AuthenticationInvalid", "Validation.Ssh.AuthenticationInvalid"));
            }

            if (input.SshAuthenticationMode == SshAuthenticationMode.PrivateKey && privateKeyPath.Value is null)
            {
                issues.Add(Error(ManagedProfileFields.SshPrivateKeyPath, "Ssh.PrivateKeyRequired", "Validation.Ssh.PrivateKeyRequired"));
            }

            if (input.AccessMode == ManagedNutServerAccessMode.Manage && remoteDirectory.Value is null)
            {
                issues.Add(new FieldValidationIssue(
                    ManagedProfileFields.RemoteConfigurationDirectory,
                    "Remote.ConfigurationDirectoryRecommended",
                    ValidationSeverity.Warning,
                    "Validation.Remote.ConfigurationDirectoryRecommended"));
            }

            if (!issues.Any(issue => issue.Severity == ValidationSeverity.Error && IsSshField(issue.Field)))
            {
                management = new NutManagementProfile(
                    NutManagementMode.Remote,
                    managementHost.Value,
                    remoteDirectory.Value,
                    sshPort.Value,
                    sshUsername.Value,
                    input.TrustedHostKeyFingerprint,
                    input.TrustedHostKeyAlgorithm,
                    RemoteConfigurationTransportKind.SshSftp,
                    sshAuthenticationMode: input.SshAuthenticationMode,
                    sshPrivateKeyPath: privateKeyPath.Value,
                    managedFiles: input.ManagedFiles,
                    agent: agent);
            }
        }
        else if (input.ManagementMode == NutManagementMode.Remote && input.ConfigurationTransport == RemoteConfigurationTransportKind.Smb)
        {
            var share = ValidateUncShareRoot(input.SmbSharePath);
            var smbUsername = ValidateOptionalText(
                input.SmbUsername,
                ManagedProfileFields.SmbUsername,
                255,
                "Smb.UsernameInvalid",
                "Validation.Smb.UsernameInvalid");
            issues.AddRange(share.Issues);
            issues.AddRange(smbUsername.Issues);
            if (!Enum.IsDefined(input.SmbAuthenticationMode))
            {
                issues.Add(Error(ManagedProfileFields.SmbAuthenticationMode, "Smb.AuthenticationInvalid", "Validation.Smb.AuthenticationInvalid"));
            }

            // An explicit-credential profile no longer carries a typed username. The account comes
            // from the Windows credential dialog, so before the administrator has signed in once
            // there is legitimately nothing here. That is a missing credential, which is an
            // operational state resolved at connection time, not a syntactically invalid profile.

            var configurationDirectory = NormalizeOptional(input.SmbConfigurationDirectory);
            if (share.Value is not null && configurationDirectory is not null)
            {
                try
                {
                    configurationDirectory = SmbUncPath.NormalizeConfigurationDirectory(share.Value, configurationDirectory);
                }
                catch (ArgumentException)
                {
                    issues.Add(Error(ManagedProfileFields.SmbConfigurationDirectory, "Smb.ConfigurationDirectoryOutsideShare", "Validation.Smb.ConfigurationDirectoryOutsideShare"));
                }
            }

            if (!issues.Any(issue => issue.Severity == ValidationSeverity.Error && IsSmbField(issue.Field)))
            {
                management = new NutManagementProfile(
                    NutManagementMode.Remote,
                    configurationTransport: RemoteConfigurationTransportKind.Smb,
                    smbSharePath: share.Value,
                    smbConfigurationDirectory: configurationDirectory,
                    smbAuthenticationMode: input.SmbAuthenticationMode,
                    smbUsername: smbUsername.Value,
                    managedFiles: input.ManagedFiles,
                    agent: agent);
            }
        }
        else if (input.ManagementMode == NutManagementMode.Remote)
        {
            issues.Add(Error(ManagedProfileFields.ConfigurationTransport, "Remote.TransportInvalid", "Validation.Remote.TransportInvalid"));
        }

        var hasErrors = issues.Any(issue => issue.Severity == ValidationSeverity.Error);
        var profile = !hasErrors && name.Value is not null && monitoringHost.Value is not null && management is not null
            ? new ManagedNutServerProfile(
                input.Id,
                name.Value,
                new NutMonitoringProfile(monitoringHost.Value, monitoringPort.Value, preferredUpsName.Value),
                management,
                input.AccessMode)
            : null;
        return new ManagedNutServerProfileValidationResult(profile, issues.AsReadOnly());
    }

    /// <summary>
    /// Turns the agent fields into settings, or records why they cannot be used.
    ///
    /// The endpoint is only required by the transport that needs one, and the model already
    /// normalises the rest — a named pipe drops an endpoint and an alternate account, because the
    /// caller there is whoever Windows already authenticated. What this adds is the message an
    /// operator reads instead of an exception.
    /// </summary>
    private static NutAgentProfileSettings? ValidateAgent(ManagedNutServerProfileInput input, List<FieldValidationIssue> issues)
    {
        if (input.ManagementMode != NutManagementMode.Remote) return null;

        if (!Enum.IsDefined(input.AgentTransport) || !Enum.IsDefined(input.AgentAuthentication))
        {
            issues.Add(Error(ManagedProfileFields.AgentHttpsEndpoint, "Agent.TransportInvalid", "Validation.Agent.TransportInvalid"));
            return null;
        }

        if (input.AgentTransport != NutAgentTransportKind.Https)
        {
            return new NutAgentProfileSettings(NutAgentTransportKind.NamedPipe);
        }

        if (!NutAgentProfileSettings.IsValidHttpsEndpoint(input.AgentHttpsEndpoint))
        {
            issues.Add(Error(ManagedProfileFields.AgentHttpsEndpoint, "Agent.EndpointInvalid", "Validation.Agent.EndpointInvalid"));
            return null;
        }

        return new NutAgentProfileSettings(
            NutAgentTransportKind.Https, input.AgentHttpsEndpoint, input.AgentAuthentication, input.AgentUsername);
    }

    private static bool IsHostname(string value)
    {
        var labels = value.Split('.', StringSplitOptions.None);
        return labels.Length > 0 && labels.All(label =>
            label.Length is >= 1 and <= 63 &&
            char.IsLetterOrDigit(label[0]) &&
            char.IsLetterOrDigit(label[^1]) &&
            label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'));
    }

    private static bool IsSshField(string field) => field is
        ManagedProfileFields.ManagementHost or ManagedProfileFields.SshPort or
        ManagedProfileFields.SshUsername or ManagedProfileFields.SshAuthenticationMode or
        ManagedProfileFields.SshPrivateKeyPath or ManagedProfileFields.RemoteConfigurationDirectory;

    private static bool IsSmbField(string field) => field is
        ManagedProfileFields.SmbSharePath or ManagedProfileFields.SmbConfigurationDirectory or
        ManagedProfileFields.SmbAuthenticationMode or ManagedProfileFields.SmbUsername;

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static FieldValidationResult<string> ValidateOptionalText(
        string? value,
        string field,
        int maximumLength,
        string code,
        string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new FieldValidationResult<string>(null, []);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength || normalized.Any(char.IsControl))
        {
            return new FieldValidationResult<string>(null, [Error(field, code, resourceKey)]);
        }

        return new FieldValidationResult<string>(normalized, []);
    }

    private static FieldValidationIssue Error(string field, string code, string resourceKey) =>
        new(field, code, ValidationSeverity.Error, resourceKey);
}
