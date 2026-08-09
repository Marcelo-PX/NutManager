using NutManager.Core.Configuration;
using Xunit;

namespace NutManager.Tests.Configuration;

public sealed class NutConfigurationParserTests
{
    private readonly NutConfigurationParser _parser = new();

    public static IEnumerable<object[]> RoundTripCases()
    {
        yield return
        [
            NutConfigurationFileKind.NutConf,
            "# startup mode\r\n\r\nMODE=standalone\r\nCUSTOM=\"value # kept\"\r\n"
        ];
        yield return
        [
            NutConfigurationFileKind.UpsConf,
            "driverpath = C:\\drivers\n\n[NOBREAK]\n    driver   = nutdrv_qx\n    port = \"\\\\.\\COM4\"\n    protocol = q1\n\n[future]\n  new_option = keep me"
        ];
        yield return
        [
            NutConfigurationFileKind.UpsdConf,
            "# bind addresses\r\nLISTEN 127.0.0.1\r\nLISTEN ::1\r\nFUTURE \"a # b\""
        ];
        yield return
        [
            NutConfigurationFileKind.UpsdUsers,
            "[admin]\n    password = fake-admin-secret\n    actions = SET\n    actions = FSD\n    instcmds = all\n\n[observer]\n    password = fake-observer-secret\n    upsmon secondary\n    custom = preserved\n"
        ];
        yield return
        [
            NutConfigurationFileKind.UpsmonConf,
            "# monitoring\r\nMONITOR demo@server 1 observer fake-monitor-secret secondary\r\nMINSUPPLIES 1\r\nPOLLFREQ 5\r\nNOTIFYCMD \"C:\\Program Files\\notify.cmd\"\r\nFUTURE option\r\n"
        ];
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void EverySupportedFormatRoundTripsExactly(NutConfigurationFileKind fileKind, string original)
    {
        var document = _parser.Parse(fileKind, original);

        Assert.False(document.IsModified);
        Assert.Equal(original, document.Serialize());
    }

    [Fact]
    public void NutConfChangesOnlyTheSelectedAssignmentAndKeepsItsEqualsStyle()
    {
        const string original = "# keep this\r\nMODE=standalone\r\nMODE=none\r\nUNKNOWN=\"value # raw\"\r\n";
        var document = _parser.Parse(NutConfigurationFileKind.NutConf, original);
        var modes = document.FindAssignments("mode").ToArray();

        Assert.Equal(2, modes.Length);
        modes[0].SetValue("netserver");

        Assert.True(document.IsModified);
        Assert.True(modes[0].IsModified);
        Assert.False(modes[1].IsModified);
        Assert.Equal("# keep this\r\nMODE=netserver\r\nMODE=none\r\nUNKNOWN=\"value # raw\"\r\n", document.Serialize());
        Assert.DoesNotContain("MODE =", document.Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void NutConfPreservesUnknownAssignmentsQuotedValuesAndBlankLines()
    {
        const string original = "\nMODE=standalone\n\nCUSTOM=\"a # = []\"";

        var document = _parser.Parse(NutConfigurationFileKind.NutConf, original);
        var custom = Assert.Single(document.FindAssignments("CUSTOM"));

        Assert.Equal("a # = []", custom.Value);
        Assert.Equal("\"a # = []\"", custom.RawValue);
        Assert.Equal(original, document.Serialize());
    }

    [Fact]
    public void ReapplyingAQuotedLogicalValuePreservesTheOriginalQuotedToken()
    {
        const string original = "[demo]\n    desc = \"abc\"\n";
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, original);
        var description = Assert.Single(document.FindAssignments("desc", "demo"));

        Assert.Equal("abc", description.Value);
        Assert.Equal("\"abc\"", description.RawValue);

        description.SetValue(description.Value);

        Assert.Equal("\"abc\"", description.RawValue);
        Assert.Equal(original, document.Serialize());
    }

    [Fact]
    public void ReapplyingAQuotedWindowsPathDoesNotDuplicateEscaping()
    {
        const string original = "[NOBREAK]\n    port = \"\\\\\\\\.\\\\COM4\"\n";
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, original);
        var port = Assert.Single(document.FindAssignments("port", "nobreak"));

        Assert.DoesNotContain('"', port.Value);
        Assert.Contains('\\', port.Value);

        port.SetValue(port.Value);

        Assert.Equal("\"\\\\\\\\.\\\\COM4\"", port.RawValue);
        Assert.Equal(original, document.Serialize());
    }

    [Fact]
    public void SettingAQuotedValueEscapesOnlyItsQuoteDelimiterAndBackslashes()
    {
        const string original = "[demo]\n    desc = \"abc\"\n";
        const string value = "say \"hi\" \\ path";
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, original);
        var description = Assert.Single(document.FindAssignments("desc", "demo"));

        description.SetValue(value);

        Assert.Equal(value, description.Value);
        Assert.Equal("\"say \\\"hi\\\" \\\\ path\"", description.RawValue);
        Assert.Equal("[demo]\n    desc = \"say \\\"hi\\\" \\\\ path\"\n", document.Serialize());
    }

    [Fact]
    public void UpsConfChangesOneSectionDirectiveWithoutTouchingNeighboringContent()
    {
        const string original = "# global\nstatepath = /var/run/nut\n\n[NOBREAK]\n    driver   = nutdrv_qx\n    port = \"\\\\.\\COM4\"\n    protocol = q1\n\n[unmanaged]\n    driver = other\n    future_option = preserve\n";
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, original);
        var port = Assert.Single(document.FindAssignments("port", "nobreak"));

        port.SetValue("novo-valor");

        Assert.Equal(
            "# global\nstatepath = /var/run/nut\n\n[NOBREAK]\n    driver   = nutdrv_qx\n    port = \"novo-valor\"\n    protocol = q1\n\n[unmanaged]\n    driver = other\n    future_option = preserve\n",
            document.Serialize());
        Assert.Single(document.FindSections("UNMANAGED"));
        Assert.Single(document.FindAssignments("future_option", "unmanaged"));
    }

    [Fact]
    public void UpsConfPreservesRepeatedDirectivesAndGlobalContent()
    {
        const string original = "driverpath = D:\\fixtures\\nut\\bin\r\n\r\n[first]\r\ndriver = a\r\ndriver = b\r\n";
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, original);

        var drivers = document.FindAssignments("driver", "first").ToArray();

        Assert.Equal(2, drivers.Length);
        Assert.Equal("driverpath", Assert.Single(document.FindAssignments("driverpath")).Name);
        Assert.Equal(original, document.Serialize());
    }

    [Fact]
    public void UpsdConfPreservesRepeatedDirectivesAndChangesOnlyTheSelectedOccurrence()
    {
        const string original = "# server\r\nLISTEN 127.0.0.1\r\nLISTEN ::1\r\nMAXCONN 16\r\nFUTURE \"quoted value\"";
        var document = _parser.Parse(NutConfigurationFileKind.UpsdConf, original);
        var listeners = document.FindDirectives("listen").ToArray();

        Assert.Equal(2, listeners.Length);
        listeners[1].SetArguments("::1 3493");

        Assert.Equal("# server\r\nLISTEN 127.0.0.1\r\nLISTEN ::1 3493\r\nMAXCONN 16\r\nFUTURE \"quoted value\"", document.Serialize());
        Assert.Single(document.FindDirectives("MAXCONN"));
        Assert.Single(document.FindDirectives("future"));
    }

    [Fact]
    public void UpsdConfCertidentIsSensitiveAndDoesNotExposeItsPassword()
    {
        const string secret = "fictional-private-key-password";
        var original = $"CERTIDENT \"server cert\" {secret}\nLISTEN 127.0.0.1\n";
        var document = _parser.Parse(NutConfigurationFileKind.UpsdConf, original);
        var certident = Assert.Single(document.FindDirectives("certident"));

        Assert.True(certident.IsSensitive);
        Assert.DoesNotContain(secret, document.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, certident.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, string.Join(" ", document.Diagnostics.Select(diagnostic => diagnostic.Message)), StringComparison.Ordinal);
        Assert.Equal(original, document.Serialize());
    }

    [Fact]
    public void UpsdUsersPreservesUsersRepeatedFieldsAndUpsmonRole()
    {
        const string secret = "fictional-user-password";
        var original = $"[admin]\n    password = {secret}\n    actions = SET\n    actions = FSD\n    instcmds = all\n\n[watcher]\n    password = another-fictional-password\n    upsmon secondary\n    future_field = keep\n";
        var document = _parser.Parse(NutConfigurationFileKind.UpsdUsers, original);
        var actions = document.FindAssignments("actions", "admin").ToArray();
        var password = Assert.Single(document.FindAssignments("password", "admin"));

        actions[1].SetValue("FSD-UPDATED");

        Assert.True(password.IsSensitive);
        Assert.Single(document.FindDirectives("upsmon", "watcher"));
        Assert.DoesNotContain(secret, document.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, password.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, string.Join(" ", document.Diagnostics.Select(diagnostic => diagnostic.Message)), StringComparison.Ordinal);
        Assert.Equal(
            $"[admin]\n    password = {secret}\n    actions = SET\n    actions = FSD-UPDATED\n    instcmds = all\n\n[watcher]\n    password = another-fictional-password\n    upsmon secondary\n    future_field = keep\n",
            document.Serialize());
    }

    [Fact]
    public void UpsmonConfPreservesMonitorAndDoesNotExposeItsSecretInObjectTextOrDiagnostics()
    {
        const string secret = "fictional-monitor-password";
        var original = $"MONITOR demo@host 1 observer {secret} secondary\nMINSUPPLIES 1\nPOLLFREQ 5\nPOLLFREQALERT 2\nFUTURE \"# remains argument\"";
        var document = _parser.Parse(NutConfigurationFileKind.UpsmonConf, original);
        var monitor = Assert.Single(document.FindDirectives("monitor"));

        document.FindDirectives("pollfreq").Single().SetArguments("10");

        Assert.True(monitor.IsSensitive);
        Assert.DoesNotContain(secret, document.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, monitor.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, string.Join(" ", document.Diagnostics.Select(diagnostic => diagnostic.Message)), StringComparison.Ordinal);
        Assert.Equal($"MONITOR demo@host 1 observer {secret} secondary\nMINSUPPLIES 1\nPOLLFREQ 10\nPOLLFREQALERT 2\nFUTURE \"# remains argument\"", document.Serialize());
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("# comment\r\n\r\nMODE=standalone")]
    [InlineData("# comment\n\nMODE=standalone\n")]
    [InlineData("MODE=standalone\r\nCUSTOM=value\nTAIL=final\r")]
    [InlineData("UNKNOWN \"# = [ ] \\\\ path\"")]
    public void RoundTripPreservesLineEndingsEofAndDifficultRawContent(string original)
    {
        var document = _parser.Parse(NutConfigurationFileKind.NutConf, original);

        Assert.Equal(original, document.Serialize());
    }

    [Theory]
    [InlineData(NutConfigurationFileKind.NutConf)]
    [InlineData(NutConfigurationFileKind.UpsConf)]
    [InlineData(NutConfigurationFileKind.UpsdConf)]
    [InlineData(NutConfigurationFileKind.UpsdUsers)]
    [InlineData(NutConfigurationFileKind.UpsmonConf)]
    public void EverySupportedFormatAcceptsAnEmptyDocument(NutConfigurationFileKind fileKind)
    {
        var document = _parser.Parse(fileKind, string.Empty);

        Assert.Empty(document.Nodes);
        Assert.Equal(string.Empty, document.Serialize());
    }

    [Fact]
    public void UnrecognizedSyntaxRemainsRawAndDoesNotCreateSensitiveDiagnostics()
    {
        const string original = "[valid]\nfield = value\n[not closed\n# secret-like raw content stays raw\n";
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, original);

        Assert.Contains(document.Nodes, node => node is NutRawNode raw && raw.RawText == "[not closed");
        Assert.Empty(document.Diagnostics);
        Assert.Equal(original, document.Serialize());
    }

    [Fact]
    public void QueriesAreCaseInsensitiveByDefaultAndNeverModifyTheDocument()
    {
        const string original = "[MiXeD]\n    Driver = nutdrv_qx\n";
        var document = _parser.Parse(NutConfigurationFileKind.UpsConf, original);

        Assert.Single(document.FindSections("mixed"));
        Assert.Single(document.FindAssignments("driver", "MIXED"));
        Assert.False(document.IsModified);
        Assert.Equal(original, document.Serialize());
    }
}
