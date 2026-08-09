using System.Diagnostics;
using NutManager.Core.Administration;
using NutManager.Core.Configuration;
using NutManager.Infrastructure.Platform.Windows;
using Xunit;

namespace NutManager.Tests;

public sealed class WindowsNutDriverDiagnosticsTests
{
    private const string RealLikeUpsConf = """
        driverpath = "C:/NUT/patched-driver"
        maxretry = 3
        retrydelay = 5

        [NOBREAK]
            driver = nutdrv_qx
            port = "\\\\.\\COM4"
            protocol = q1

            battery_voltage_reports_one_pack
            override.battery.packs = 48
            default.battery.voltage.nominal = 96

            desc = "UPSBrasil 3 kVA"
        """;

    [Theory]
    [InlineData("COM4", "COM4")]
    [InlineData("com4", "COM4")]
    [InlineData("\\\\.\\COM4", "COM4")]
    public void NormalizesOnlySupportedComDeviceSyntax(string value, string expected)
    {
        Assert.True(WindowsComPortNormalizer.TryNormalize(value, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("COM0")]
    [InlineData("\\\\.\\PhysicalDrive0")]
    [InlineData("C:\\NUT\\COM4")]
    [InlineData("")]
    public void RejectsNonComDeviceSyntax(string value) =>
        Assert.False(WindowsComPortNormalizer.TryNormalize(value, out _));

    [Fact]
    public void InterpretsRealLikeUpsConfWithoutChangingItsRawContent()
    {
        var document = new NutConfigurationParser().Parse(NutConfigurationFileKind.UpsConf, RealLikeUpsConf);
        var ports = new[] { new NutComPortInfo("COM4", "UGREEN serial adapter", "Prolific", "fictional-pnp", "OK", 0, true) };

        var drivers = WindowsUpsConfigurationInterpreter.Interpret(
            document,
            "C:\\NUT",
            ports,
            (installation, path, driver) => WindowsNutDriverResolver.Resolve(installation, path, driver, candidate => candidate == "C:\\NUT\\patched-driver\\nutdrv_qx.exe"),
            _ => false);

        var configured = Assert.Single(drivers);
        Assert.Equal("NOBREAK", configured.UpsName);
        Assert.Equal("UPSBrasil 3 kVA", configured.Description);
        Assert.Equal("nutdrv_qx", configured.DriverName);
        Assert.Equal("COM4", configured.NormalizedComPort);
        Assert.Equal("q1", configured.Protocol);
        Assert.Equal("C:/NUT/patched-driver", configured.DriverPath);
        Assert.True(configured.Executable.IsAvailable);
        Assert.True(configured.Executable.IsTrusted);
        Assert.True(configured.IsConfiguredComPortPresent);
        Assert.Equal(RealLikeUpsConf, document.Serialize());
    }

    [Fact]
    public void KeepsMultipleUpsSectionsDistinctAndDoesNotTreatNonComPortsAsErrors()
    {
        var document = new NutConfigurationParser().Parse(
            NutConfigurationFileKind.UpsConf,
            "driverpath = C:/NUT/bin\n[first]\ndriver = nutdrv_qx\nport = COM4\n[second]\ndriver = blazer_usb\nport = auto\n");

        var drivers = WindowsUpsConfigurationInterpreter.Interpret(
            document,
            "C:\\NUT",
            [new NutComPortInfo("COM4", null, null, null, null, null, true)],
            (installation, path, driver) => WindowsNutDriverResolver.Resolve(installation, path, driver, _ => true),
            _ => false);

        Assert.Equal(["first", "second"], drivers.Select(driver => driver.UpsName));
        Assert.Equal("COM4", drivers[0].NormalizedComPort);
        Assert.Null(drivers[1].NormalizedComPort);
        Assert.True(drivers[1].IsConfiguredComPortPresent);
    }

    [Theory]
    [InlineData("C:\\NUT\\patched-driver", "nutdrv_qx", "C:\\NUT\\patched-driver\\nutdrv_qx.exe", NutDriverExecutableState.Available, true)]
    [InlineData("C:\\NUT-malicious\\drivers", "nutdrv_qx", "C:\\NUT-malicious\\drivers\\nutdrv_qx.exe", NutDriverExecutableState.Untrusted, false)]
    public void ResolvesOnlyDriversInsideTheDetectedInstallation(string driverPath, string driver, string expectedPath, NutDriverExecutableState expectedState, bool trusted)
    {
        var result = WindowsNutDriverResolver.Resolve("C:\\NUT", driverPath, driver, candidate => candidate == expectedPath);

        Assert.Equal(expectedPath, result.Path);
        Assert.Equal(expectedState, result.State);
        Assert.Equal(trusted, result.IsTrusted);
    }

    [Theory]
    [InlineData("..\\driver")]
    [InlineData("nutdrv/qx")]
    [InlineData("nutdrv_qx.exe")]
    public void RejectsDriverNamesThatCouldBecomePaths(string name)
    {
        var result = WindowsNutDriverResolver.Resolve("C:\\NUT", "C:\\NUT\\bin", name, _ => true);

        Assert.Equal(NutDriverExecutableState.InvalidName, result.State);
    }

    [Fact]
    public void BuildsOnlyTheExplicitlyAllowlistedNUTDiagnosticArguments()
    {
        var driver = Driver();
        var requests = new Dictionary<NutDriverDiagnosticKind, string[]>
        {
            [NutDriverDiagnosticKind.UpsdrvctlHelp] = ["-h"],
            [NutDriverDiagnosticKind.UpsdrvctlList] = ["list", "NOBREAK"],
            [NutDriverDiagnosticKind.UpsdrvctlStatus] = ["status", "NOBREAK"],
            [NutDriverDiagnosticKind.UpsdrvctlDryRunStart] = ["-t", "start", "NOBREAK"],
            [NutDriverDiagnosticKind.DriverHelp] = ["-h"],
            [NutDriverDiagnosticKind.DriverVersion] = ["-V"],
            [NutDriverDiagnosticKind.DriverVariableList] = ["-L"],
            [NutDriverDiagnosticKind.DriverDataDump] = ["-a", "NOBREAK", "-d"]
        };

        foreach (var (kind, arguments) in requests)
        {
            var specification = WindowsNutDiagnosticCommandBuilder.Create(
                new NutDriverDiagnosticRequest(kind, "C:\\NUT", "C:\\NUT\\etc", driver),
                "C:\\NUT\\bin\\upsdrvctl.exe");

            Assert.NotNull(specification);
            Assert.Equal(arguments, specification!.Arguments);
            Assert.DoesNotContain("shutdown", specification.Arguments, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("-k", specification.Arguments, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DryRunAlwaysIncludesTheTestFlag()
    {
        var specification = WindowsNutDiagnosticCommandBuilder.Create(
            new NutDriverDiagnosticRequest(NutDriverDiagnosticKind.UpsdrvctlDryRunStart, "C:\\NUT", "C:\\NUT\\etc", Driver()),
            "C:\\NUT\\bin\\upsdrvctl.exe");

        Assert.Equal(["-t", "start", "NOBREAK"], specification!.Arguments);
    }

    [Fact]
    public void ProcessStartInfoUsesExactPathArgumentListAndChildOnlyEnvironment()
    {
        var startInfo = WindowsNutDiagnosticProcessRunner.CreateStartInfo(
            new NutDiagnosticProcessSpec("C:\\NUT\\bin\\upsdrvctl.exe", ["-t", "start", "NOBREAK"], "C:\\NUT\\etc", TimeSpan.FromSeconds(15), true));

        Assert.False(startInfo.UseShellExecute);
        Assert.Equal("C:\\NUT\\bin\\upsdrvctl.exe", startInfo.FileName);
        Assert.Equal(["-t", "start", "NOBREAK"], startInfo.ArgumentList);
        Assert.Equal("C:\\NUT\\etc", startInfo.Environment["NUT_CONFPATH"]);
        Assert.Equal("true", startInfo.Environment["NUT_QUIET_INIT_BANNER"]);
    }

    [Fact]
    public void DriverDataDumpIsExplicitlyMarkedAsHardwareContacting()
    {
        var result = new NutDriverDiagnosticResult(
            NutDriverDiagnosticKind.DriverDataDump,
            NutDriverDiagnosticStatus.Success,
            "nutdrv_qx.exe",
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(1),
            0,
            string.Empty,
            string.Empty,
            false,
            true,
            "ok");

        Assert.True(result.ContactsHardware);
    }

    [Fact]
    public void RedactsSensitiveDiagnosticOutputBeforeItCanReachTheSessionResult()
    {
        var output = WindowsNutDiagnosticOutput.Redact("normal line\npassword = fictional-password\nTOKEN: fictional-token\nnext line");

        Assert.DoesNotContain("fictional-password", output);
        Assert.DoesNotContain("fictional-token", output);
        Assert.Equal(["normal line", "<redacted>", "<redacted>", "next line"], output.Split(Environment.NewLine));
    }

    private static NutConfiguredDriver Driver() => new(
        "NOBREAK",
        "Fictitious UPS",
        "nutdrv_qx",
        "\\\\.\\COM4",
        "COM4",
        "q1",
        "C:\\NUT\\patched-driver",
        new NutDriverExecutableInfo("C:\\NUT\\patched-driver\\nutdrv_qx.exe", NutDriverExecutableState.Available, true),
        true,
        NutDriverRuntimeState.NotRunning);
}
