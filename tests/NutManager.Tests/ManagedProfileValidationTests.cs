using NutManager.Core.Models;
using NutManager.Core.Validation;
using Xunit;

namespace NutManager.Tests;

public sealed class ManagedProfileValidationTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.21")]
    [InlineData("::1")]
    [InlineData("fd50:1:1::105")]
    [InlineData("nutserver")]
    [InlineData("nutserver.sbra.local")]
    [InlineData("ups.example.com")]
    public void HostValidatorAcceptsOnlyNetworkHostSyntaxWithoutOperationalIo(string input)
    {
        var result = ManagedNutServerProfileValidator.ValidateHost(input);

        Assert.True(result.IsValid);
        Assert.Equal(input, result.Value);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("NOBREAK@127.0.0.1")]
    [InlineData("user@server")]
    [InlineData("http://server")]
    [InlineData("https://server")]
    [InlineData("server:3493")]
    [InlineData("\\\\server\\share")]
    [InlineData("C:\\NUT")]
    [InlineData("/NUT/etc")]
    [InlineData("server/path")]
    [InlineData("host name")]
    [InlineData("host\nname")]
    [InlineData("fe80::1%12")]
    public void HostValidatorRejectsMixedIdentityUriPathWhitespaceAndUnsupportedScope(string input)
    {
        var result = ManagedNutServerProfileValidator.ValidateHost(input);

        Assert.True(result.HasErrors);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("65535", 65535)]
    public void PortValidatorAcceptsBoundaries(string text, int expected)
    {
        var result = ManagedNutServerProfileValidator.ValidatePort(text);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("text")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    public void PortValidatorSupportsPartialTypingWithoutThrowing(string text)
    {
        var exception = Record.Exception(() => ManagedNutServerProfileValidator.ValidatePort(text));

        Assert.Null(exception);
        Assert.True(ManagedNutServerProfileValidator.ValidatePort(text).HasErrors);
    }

    [Fact]
    public void ProfileNameValidationEnforcesRequiredLengthControlsAndUniquenessButAllowsSelfEdit()
    {
        var current = Profile("Server A");
        var other = Profile("Server B");

        Assert.True(ManagedNutServerProfileValidator.ValidateProfileName(" ", [current, other], current.Id).HasErrors);
        Assert.True(ManagedNutServerProfileValidator.ValidateProfileName(new string('a', 81), [current], current.Id).HasErrors);
        Assert.True(ManagedNutServerProfileValidator.ValidateProfileName("bad\u0001name", [current], current.Id).HasErrors);
        Assert.True(ManagedNutServerProfileValidator.ValidateProfileName("server b", [current, other], current.Id).HasErrors);
        Assert.True(ManagedNutServerProfileValidator.ValidateProfileName(" Server A ", [current, other], current.Id).IsValid);
    }

    [Theory]
    [InlineData("\\\\server\\share")]
    [InlineData("  \\\\server\\share  ")]
    public void UncValidatorAcceptsExactShareRoot(string input)
    {
        var result = ManagedNutServerProfileValidator.ValidateUncShareRoot(input);

        Assert.True(result.IsValid);
        Assert.Equal("\\\\server\\share", result.Value);
    }

    [Theory]
    [InlineData("C:\\folder")]
    [InlineData("server\\share")]
    [InlineData("//server/share")]
    [InlineData("http://server/share")]
    [InlineData("\\\\server")]
    [InlineData("\\\\")]
    [InlineData("\\\\server\\share\\folder")]
    public void UncValidatorRejectsNonRootOrNonUncInput(string input) =>
        Assert.True(ManagedNutServerProfileValidator.ValidateUncShareRoot(input).HasErrors);

    [Fact]
    public void LocalProfileIgnoresAndDoesNotMaterializeRemoteOnlyMetadata()
    {
        var input = ValidInput() with
        {
            ManagementMode = NutManagementMode.Local,
            ManagementHost = "remote.example",
            SmbSharePath = "\\\\server\\share",
            SshPrivateKeyPath = @"C:\\secret.key"
        };

        var result = ManagedNutServerProfileValidator.Validate(input, []);

        Assert.True(result.CanSave);
        Assert.Equal(NutManagementMode.Local, result.Profile!.Management.Mode);
        Assert.Null(result.Profile.Management.ManagementHost);
        Assert.Null(result.Profile.Management.Smb);
        Assert.Null(result.Profile.Management.SshPrivateKeyPath);
    }

    [Fact]
    public void SftpValidationRequiresHostPortAndPrivateKeyPathOnlyWhenApplicable()
    {
        var invalid = ValidInput() with
        {
            ManagementMode = NutManagementMode.Remote,
            ConfigurationTransport = RemoteConfigurationTransportKind.SshSftp,
            ManagementHost = "",
            SshPort = "0",
            SshAuthenticationMode = SshAuthenticationMode.PrivateKey,
            SshPrivateKeyPath = null
        };

        var result = ManagedNutServerProfileValidator.Validate(invalid, []);

        Assert.Contains(result.Issues, issue => issue.Field == ManagedProfileFields.ManagementHost);
        Assert.Contains(result.Issues, issue => issue.Field == ManagedProfileFields.SshPort);
        Assert.Contains(result.Issues, issue => issue.Field == ManagedProfileFields.SshPrivateKeyPath);
        Assert.False(result.CanSave);
    }

    [Fact]
    public void SmbValidationStillRequiresAnExactShareRoot()
    {
        var invalid = ValidInput() with
        {
            ManagementMode = NutManagementMode.Remote,
            ConfigurationTransport = RemoteConfigurationTransportKind.Smb,
            SmbSharePath = "server/share",
            SmbAuthenticationMode = SmbAuthenticationMode.ExplicitCredentials,
            SmbUsername = null
        };

        var result = ManagedNutServerProfileValidator.Validate(invalid, []);

        Assert.Contains(result.Issues, issue => issue.Field == ManagedProfileFields.SmbSharePath);
        Assert.False(result.CanSave);
    }

    [Fact]
    public void AnExplicitCredentialSmbProfileSavesBeforeAnAccountHasBeenChosen()
    {
        // The account comes from the Windows credential dialog, not from a typed field, so a
        // profile that has been switched to "another Windows account" but not yet signed in is a
        // missing credential — an operational state — rather than an invalid profile.
        var input = ValidInput() with
        {
            ManagementMode = NutManagementMode.Remote,
            ConfigurationTransport = RemoteConfigurationTransportKind.Smb,
            SmbSharePath = @"\\server\share",
            SmbAuthenticationMode = SmbAuthenticationMode.ExplicitCredentials,
            SmbUsername = null
        };

        var result = ManagedNutServerProfileValidator.Validate(input, []);

        Assert.DoesNotContain(result.Issues, issue => issue.Field == ManagedProfileFields.SmbUsername);
        Assert.True(result.CanSave);
        Assert.Equal(SmbAuthenticationMode.ExplicitCredentials, result.Profile!.Management.SmbAuthenticationMode);
        Assert.Null(result.Profile.Management.SmbUsername);
    }

    [Fact]
    public void AMalformedSmbUsernameIsStillRejectedWhenOneIsPresent()
    {
        var input = ValidInput() with
        {
            ManagementMode = NutManagementMode.Remote,
            ConfigurationTransport = RemoteConfigurationTransportKind.Smb,
            SmbSharePath = @"\\server\share",
            SmbAuthenticationMode = SmbAuthenticationMode.ExplicitCredentials,
            SmbUsername = new string('u', 300)
        };

        var result = ManagedNutServerProfileValidator.Validate(input, []);

        Assert.Contains(result.Issues, issue => issue.Field == ManagedProfileFields.SmbUsername);
        Assert.False(result.CanSave);
    }

    [Fact]
    public void WarningDoesNotBlockSaveButErrorDoes()
    {
        var warning = ValidInput() with
        {
            ManagementMode = NutManagementMode.Remote,
            AccessMode = ManagedNutServerAccessMode.Manage,
            ManagementHost = "remote.example",
            RemoteConfigurationDirectory = null
        };
        var warningResult = ManagedNutServerProfileValidator.Validate(warning, []);
        var errorResult = ManagedNutServerProfileValidator.Validate(warning with { MonitoringHost = "user@host" }, []);

        Assert.Contains(warningResult.Issues, issue => issue.Severity == ValidationSeverity.Warning);
        Assert.True(warningResult.CanSave);
        Assert.Contains(errorResult.Issues, issue => issue.Severity == ValidationSeverity.Error);
        Assert.False(errorResult.CanSave);
    }

    [Fact]
    public void SmbConfigurationDirectoryMustRemainInsideShare()
    {
        var input = ValidInput() with
        {
            ManagementMode = NutManagementMode.Remote,
            ConfigurationTransport = RemoteConfigurationTransportKind.Smb,
            SmbSharePath = "\\\\server\\share",
            SmbConfigurationDirectory = "\\\\other\\share\\etc"
        };

        var result = ManagedNutServerProfileValidator.Validate(input, []);

        Assert.Contains(result.Issues, issue => issue.Code == "Smb.ConfigurationDirectoryOutsideShare");
        Assert.False(result.CanSave);
    }

    private static ManagedNutServerProfileInput ValidInput() => new(
        Guid.NewGuid(),
        "Server",
        "monitor.example",
        "3493",
        null,
        NutManagementMode.Local,
        ManagedNutServerAccessMode.ReadOnly,
        null,
        null,
        "22",
        null,
        SshAuthenticationMode.Password,
        null,
        null,
        null,
        RemoteConfigurationTransportKind.SshSftp,
        null,
        null,
        SmbAuthenticationMode.CurrentWindowsIdentity,
        null);

    private static ManagedNutServerProfile Profile(string name) => new(
        Guid.NewGuid(),
        name,
        new NutMonitoringProfile("monitor.example"),
        new NutManagementProfile(NutManagementMode.Local),
        ManagedNutServerAccessMode.ReadOnly);
}
