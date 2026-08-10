using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using NutManager.Core.Administration;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Core.Services;
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
            [NutDriverDiagnosticKind.DriverDataDump] = ["-a", "NOBREAK", "-d", "1"]
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

    [Fact]
    public void UsesOnlyTheGlobalDriverpathAndPreservesSectionLocalAndUnknownContent()
    {
        const string text = "driverpath = C:/NUT/bin\n[UPS1]\ndriver = nutdrv_qx\ndriverpath = C:/NUT/should-not-be-global\nfuture.option = preserve\n";
        var document = new NutConfigurationParser().Parse(NutConfigurationFileKind.UpsConf, text);

        var driver = Assert.Single(WindowsUpsConfigurationInterpreter.Interpret(
            document,
            "C:\\NUT",
            Array.Empty<NutComPortInfo>(),
            (installation, path, name) => WindowsNutDriverResolver.Resolve(installation, path, name, candidate => candidate == "C:\\NUT\\bin\\nutdrv_qx.exe"),
            _ => false));

        Assert.Equal("C:/NUT/bin", driver.DriverPath);
        Assert.Equal("C:\\NUT\\bin\\nutdrv_qx.exe", driver.Executable.Path);
        Assert.Equal(text, document.Serialize());
    }

    [Fact]
    public void UsesTheFirstGlobalDriverpathDeterministicallyWhenItIsDuplicated()
    {
        var document = new NutConfigurationParser().Parse(
            NutConfigurationFileKind.UpsConf,
            "driverpath = C:/NUT/first\ndriverpath = C:/NUT/second\n[UPS1]\ndriver = nutdrv_qx\n");

        var driver = Assert.Single(WindowsUpsConfigurationInterpreter.Interpret(
            document,
            "C:\\NUT",
            Array.Empty<NutComPortInfo>(),
            (installation, path, name) => WindowsNutDriverResolver.Resolve(installation, path, name, candidate => candidate == "C:\\NUT\\first\\nutdrv_qx.exe"),
            _ => false));

        Assert.Equal("C:/NUT/first", driver.DriverPath);
        Assert.Equal("C:\\NUT\\first\\nutdrv_qx.exe", driver.Executable.Path);
    }

    [Fact]
    public async Task ChangedUpsConfFingerprintRejectsPortChangesWithoutLaunchingAProcess()
    {
        var pipeline = new DriverPipeline("[NOBREAK]\ndriver = nutdrv_qx\nport = COM4\nprotocol = q1\n");
        var runner = new RecordingRunner();
        var diagnostics = CreateDiagnostics(pipeline, runner);
        var snapshot = await diagnostics.InspectAsync(Installation(), CancellationToken.None);
        var request = Request(snapshot);
        pipeline.Text = "[NOBREAK]\ndriver = nutdrv_qx\nport = COM5\nprotocol = q1\n";

        var result = await diagnostics.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(NutDriverDiagnosticStatus.InvalidConfiguration, result.Status);
        Assert.Contains("Atualize os dispositivos", result.Message);
        Assert.Equal(0, runner.Calls);
    }

    [Theory]
    [InlineData("port = COM5")]
    [InlineData("protocol = qx")]
    [InlineData("driverpath = C:/NUT/other\n")]
    public async Task ReviewedDriverMetadataIsAlsoCheckedWhenAPathologicalPipelineReturnsTheSameFingerprint(string replacement)
    {
        const string original = "driverpath = C:/NUT/patched-driver\n[NOBREAK]\ndriver = nutdrv_qx\nport = COM4\nprotocol = q1\n";
        var pipeline = new DriverPipeline(original);
        var runner = new RecordingRunner();
        var diagnostics = CreateDiagnostics(pipeline, runner);
        var snapshot = await diagnostics.InspectAsync(Installation(), CancellationToken.None);
        var request = Request(snapshot);
        pipeline.FingerprintOverride = snapshot.UpsConfFingerprint;
        pipeline.Text = replacement.StartsWith("driverpath", StringComparison.Ordinal)
            ? replacement + "[NOBREAK]\ndriver = nutdrv_qx\nport = COM4\nprotocol = q1\n"
            : "driverpath = C:/NUT/patched-driver\n[NOBREAK]\ndriver = nutdrv_qx\n" + replacement + "\nprotocol = q1\n";

        var result = await diagnostics.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(NutDriverDiagnosticStatus.InvalidConfiguration, result.Status);
        Assert.Equal(0, runner.Calls);
    }

    [Theory]
    [InlineData("protocol = qx")]
    [InlineData("driverpath = C:/NUT/other\n")]
    public async Task ChangedReviewedDriverMetadataRejectsExecutionWithoutLaunchingAProcess(string replacement)
    {
        var pipeline = new DriverPipeline("driverpath = C:/NUT/patched-driver\n[NOBREAK]\ndriver = nutdrv_qx\nport = COM4\nprotocol = q1\n");
        var runner = new RecordingRunner();
        var diagnostics = CreateDiagnostics(pipeline, runner);
        var snapshot = await diagnostics.InspectAsync(Installation(), CancellationToken.None);
        var request = Request(snapshot);
        pipeline.Text = replacement.StartsWith("driverpath", StringComparison.Ordinal)
            ? replacement + "[NOBREAK]\ndriver = nutdrv_qx\nport = COM4\nprotocol = q1\n"
            : "driverpath = C:/NUT/patched-driver\n[NOBREAK]\ndriver = nutdrv_qx\nport = COM4\n" + replacement + "\n";

        var result = await diagnostics.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(NutDriverDiagnosticStatus.InvalidConfiguration, result.Status);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task MatchingFingerprintAndDriverMetadataCanProceed()
    {
        var pipeline = new DriverPipeline("[NOBREAK]\ndriver = nutdrv_qx\nport = COM4\nprotocol = q1\n");
        var runner = new RecordingRunner();
        var diagnostics = CreateDiagnostics(pipeline, runner);
        var snapshot = await diagnostics.InspectAsync(Installation(), CancellationToken.None);

        var result = await diagnostics.ExecuteAsync(Request(snapshot), CancellationToken.None);

        Assert.Equal(NutDriverDiagnosticStatus.Success, result.Status);
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public async Task UpsdrvctlHelpDoesNotRequireUpsConf()
    {
        var pipeline = new DriverPipeline(null);
        var runner = new RecordingRunner();
        var diagnostics = CreateDiagnostics(pipeline, runner);

        var result = await diagnostics.ExecuteAsync(
            new NutDriverDiagnosticRequest(NutDriverDiagnosticKind.UpsdrvctlHelp, "C:\\NUT", "C:\\NUT\\etc"),
            CancellationToken.None);

        Assert.Equal(NutDriverDiagnosticStatus.Success, result.Status);
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public async Task InspectionPrefersTheValidatedUpsdrvctlPathReportedByTheInstallationDetector()
    {
        var pipeline = new DriverPipeline("[NOBREAK]\ndriver = nutdrv_qx\n");
        var diagnostics = CreateDiagnostics(pipeline, new RecordingRunner());
        var installation = Installation() with
        {
            Executables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["upsdrvctl.exe"] = "C:\\NUT\\tools\\upsdrvctl.exe"
            }
        };

        var snapshot = await diagnostics.InspectAsync(installation, CancellationToken.None);

        Assert.Equal("C:\\NUT\\tools\\upsdrvctl.exe", snapshot.UpsdrvctlPath);
    }

    [Theory]
    [InlineData(NutServiceState.Running, false, true, NutDriverDiagnosticStatus.Conflict)]
    [InlineData(NutServiceState.Unknown, false, true, NutDriverDiagnosticStatus.Conflict)]
    [InlineData(NutServiceState.Stopped, true, true, NutDriverDiagnosticStatus.Success)]
    [InlineData(NutServiceState.Stopped, false, true, NutDriverDiagnosticStatus.Conflict)]
    public async Task HardwareInterlocksAreEvaluatedThroughFakes(NutServiceState serviceState, bool portPresent, bool trustedFile, NutDriverDiagnosticStatus expected)
    {
        var pipeline = new DriverPipeline("[NOBREAK]\ndriver = nutdrv_qx\nport = COM4\n");
        var runner = new RecordingRunner();
        var fileSystem = new DriverFileSystem { DriverExists = trustedFile };
        var services = new ServiceStateSource([new NutServiceInfo("NetworkUpsTools", "Network UPS Tools", serviceState, NutServiceStartMode.Automatic, "C:\\NUT\\bin\\nut.exe", NutAssociationConfidence.BinaryPath)]);
        var diagnostics = CreateDiagnostics(pipeline, runner, fileSystem, services, new DriverProcessInspector(), portPresent ? [Port()] : Array.Empty<NutComPortInfo>());
        var snapshot = await diagnostics.InspectAsync(Installation(), CancellationToken.None);

        var result = await diagnostics.ExecuteAsync(Request(snapshot, NutDriverDiagnosticKind.DriverDataDump), CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Equal(expected == NutDriverDiagnosticStatus.Success ? 1 : 0, runner.Calls);
    }

    [Fact]
    public async Task ActiveDriverProcessBlocksHardwareDiagnosticWithoutLaunching()
    {
        var pipeline = new DriverPipeline("[NOBREAK]\ndriver = nutdrv_qx\nport = auto\n");
        var runner = new RecordingRunner();
        var diagnostics = CreateDiagnostics(
            pipeline,
            runner,
            serviceStateSource: new ServiceStateSource([StoppedService()]),
            processInspector: new DriverProcessInspector { IsRunning = true });
        var snapshot = await diagnostics.InspectAsync(Installation(), CancellationToken.None);

        var result = await diagnostics.ExecuteAsync(Request(snapshot, NutDriverDiagnosticKind.DriverDataDump), CancellationToken.None);

        Assert.Equal(NutDriverDiagnosticStatus.Conflict, result.Status);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task UntrustedDriverExecutableBlocksHardwareDiagnosticWithoutLaunching()
    {
        var pipeline = new DriverPipeline("[NOBREAK]\ndriver = nutdrv_qx\nport = auto\n");
        var runner = new RecordingRunner();
        var diagnostics = CreateDiagnostics(pipeline, runner, new DriverFileSystem { DriverExists = false }, new ServiceStateSource([StoppedService()]));
        var snapshot = await diagnostics.InspectAsync(Installation(), CancellationToken.None);

        var result = await diagnostics.ExecuteAsync(Request(snapshot, NutDriverDiagnosticKind.DriverDataDump), CancellationToken.None);

        Assert.Equal(NutDriverDiagnosticStatus.InvalidExecutable, result.Status);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task RunnerReturnsCancelledBeforeLaunchWithoutCreatingAProcess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new ProcessFactory(new FakeProcess());
        var runner = new WindowsNutDiagnosticProcessRunner(factory);

        var result = await runner.RunAsync(Specification(), cancellation.Token);

        Assert.Equal(NutDriverDiagnosticStatus.CancelledBeforeLaunch, result.Status);
        Assert.Equal(0, factory.CreateCalls);
    }

    [Theory]
    [InlineData(0, NutDriverDiagnosticStatus.Success)]
    [InlineData(7, NutDriverDiagnosticStatus.NonZeroExit)]
    [InlineData(-1073741515, NutDriverDiagnosticStatus.MissingDependency)]
    public async Task RunnerMapsCompletedExitCodesWithoutARealProcess(int exitCode, NutDriverDiagnosticStatus expected)
    {
        var runner = new WindowsNutDiagnosticProcessRunner(new ProcessFactory(new FakeProcess { HasExitedValue = true, ExitCodeValue = exitCode }));

        var result = await runner.RunAsync(Specification(), CancellationToken.None);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task RunnerCancelsAfterLaunchAndTerminatesOnlyItsCreatedProcess()
    {
        using var cancellation = new CancellationTokenSource();
        var process = new FakeProcess { WaitUntilCancellation = true };
        var factory = new ProcessFactory(process);
        var runner = new WindowsNutDiagnosticProcessRunner(factory);
        var running = runner.RunAsync(Specification(), cancellation.Token);
        await process.Started.Task;
        cancellation.Cancel();

        var result = await running;

        Assert.Equal(NutDriverDiagnosticStatus.CancelledAfterLaunch, result.Status);
        Assert.Equal(1, process.KillCalls);
        Assert.Equal(1, factory.CreateCalls);
    }

    [Fact]
    public async Task RunnerTimeoutKillsOnlyItsCreatedProcessAndReturnsBoundedTimeout()
    {
        var process = new FakeProcess { WaitUntilCancellation = true };
        var runner = new WindowsNutDiagnosticProcessRunner(new ProcessFactory(process));

        var result = await runner.RunAsync(Specification(TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(NutDriverDiagnosticStatus.Timeout, result.Status);
        Assert.Equal(1, process.KillCalls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RunnerReportsCleanupFailureForKillOrBoundedCleanupFailure(bool killFails, bool cleanupWaitFails)
    {
        var process = new FakeProcess { WaitUntilCancellation = true, ThrowOnKill = killFails, ThrowOnCleanupWait = killFails || cleanupWaitFails };
        var runner = new WindowsNutDiagnosticProcessRunner(new ProcessFactory(process));

        var result = await runner.RunAsync(Specification(TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(NutDriverDiagnosticStatus.CleanupFailed, result.Status);
    }

    [Theory]
    [InlineData(1100000, 0)]
    [InlineData(0, 1100000)]
    [InlineData(700000, 700000)]
    public async Task RunnerEnforcesOneCombinedOutputCaptureBudget(int standardOutputLength, int standardErrorLength)
    {
        var process = new FakeProcess
        {
            HasExitedValue = true,
            StandardOutputText = new string('o', standardOutputLength),
            StandardErrorText = new string('e', standardErrorLength)
        };
        var runner = new WindowsNutDiagnosticProcessRunner(new ProcessFactory(process));

        var result = await runner.RunAsync(Specification(), CancellationToken.None);

        Assert.Equal(NutDriverDiagnosticStatus.OutputTruncated, result.Status);
        Assert.True(result.OutputTruncated);
        Assert.True(result.StandardOutput.Length + result.StandardError.Length <= 1024 * 1024);
    }

    private static WindowsNutDriverDiagnostics CreateDiagnostics(
        DriverPipeline pipeline,
        RecordingRunner runner,
        DriverFileSystem? fileSystem = null,
        IWindowsNutServiceStateSource? serviceStateSource = null,
        DriverProcessInspector? processInspector = null,
        IReadOnlyList<NutComPortInfo>? ports = null) =>
        new(
            pipeline,
            new ComPortSource(ports ?? [Port()]),
            fileSystem ?? new DriverFileSystem(),
            processInspector ?? new DriverProcessInspector(),
            serviceStateSource ?? new ServiceStateSource([StoppedService()]),
            runner,
            new TestPlatform());

    private static NutInstallationInfo Installation() => new(
        true,
        "C:\\NUT",
        "C:\\NUT\\etc",
        "2.8.5",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["upsdrvctl.exe"] = "C:\\NUT\\bin\\upsdrvctl.exe" },
        [new NutConfigurationFileInfo("ups.conf", "C:\\NUT\\etc\\ups.conf", true, true)],
        "test");

    private static NutDriverDiagnosticRequest Request(NutDriverDiagnosticsSnapshot snapshot, NutDriverDiagnosticKind kind = NutDriverDiagnosticKind.DriverHelp) =>
        new(kind, "C:\\NUT", "C:\\NUT\\etc", Assert.Single(snapshot.ConfiguredDrivers), snapshot.UpsConfFingerprint);

    private static NutComPortInfo Port() => new("COM4", "Fictitious port", null, null, "OK", 0, true);

    private static NutServiceInfo StoppedService() => new("NetworkUpsTools", "Network UPS Tools", NutServiceState.Stopped, NutServiceStartMode.Automatic, "C:\\NUT\\bin\\nut.exe", NutAssociationConfidence.BinaryPath);

    private static NutDiagnosticProcessSpec Specification(TimeSpan? timeout = null) =>
        new("C:\\NUT\\bin\\upsdrvctl.exe", ["-h"], "C:\\NUT\\etc", timeout ?? TimeSpan.FromSeconds(1), false);

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

    private sealed class DriverPipeline : INutConfigurationFilePipeline
    {
        private readonly NutConfigurationParser _parser = new();

        public DriverPipeline(string? text)
        {
            Text = text;
        }

        public string? Text { get; set; }

        public string? FingerprintOverride { get; set; }

        public Task<NutConfigurationLoadResult> LoadAsync(string targetPath, NutConfigurationFileKind fileKind, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Text is null)
            {
                return Task.FromResult(new NutConfigurationLoadResult(NutConfigurationLoadStatus.TargetNotFound));
            }

            var bytes = Encoding.UTF8.GetBytes(Text);
            var snapshot = new NutConfigurationFileSnapshot(
                targetPath,
                fileKind,
                _parser.Parse(fileKind, Text),
                NutConfigurationTextEncoding.Utf8,
                FingerprintOverride ?? Convert.ToHexString(SHA256.HashData(bytes)),
                bytes.LongLength);
            return Task.FromResult(new NutConfigurationLoadResult(NutConfigurationLoadStatus.Success, snapshot));
        }

        public NutConfigurationPreparedChange Prepare(NutConfigurationFileSnapshot snapshot) => throw new NotSupportedException();

        public Task<NutConfigurationApplyResult> ApplyAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ComPortSource : IWindowsComPortSource
    {
        private readonly IReadOnlyList<NutComPortInfo> _ports;

        public ComPortSource(IReadOnlyList<NutComPortInfo> ports)
        {
            _ports = ports;
        }

        public IReadOnlyList<NutComPortInfo> GetPorts() => _ports;
    }

    private sealed class DriverFileSystem : IWindowsDriverFileSystem
    {
        public bool DriverExists { get; set; } = true;

        public bool FileExists(string path) => path.EndsWith("upsdrvctl.exe", StringComparison.OrdinalIgnoreCase) || DriverExists;
    }

    private sealed class DriverProcessInspector : IWindowsDriverProcessInspector
    {
        public bool IsRunning { get; set; }

        public bool IsProcessRunning(string executablePath) => IsRunning;
    }

    private sealed class ServiceStateSource : IWindowsNutServiceStateSource
    {
        private readonly IReadOnlyList<NutServiceInfo> _services;

        public ServiceStateSource(IReadOnlyList<NutServiceInfo> services)
        {
            _services = services;
        }

        public Task<IReadOnlyList<NutServiceInfo>> GetServicesAsync(string installationDirectory, CancellationToken cancellationToken) => Task.FromResult(_services);
    }

    private sealed class TestPlatform : IWindowsDriverDiagnosticsPlatform
    {
        public bool IsWindows => true;
    }

    private sealed class RecordingRunner : INutDiagnosticProcessRunner
    {
        public int Calls { get; private set; }

        public Task<NutDiagnosticProcessResult> RunAsync(NutDiagnosticProcessSpec specification, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new NutDiagnosticProcessResult(NutDriverDiagnosticStatus.Success, 0, string.Empty, string.Empty, false, TimeSpan.Zero, "ok"));
        }
    }

    private sealed class ProcessFactory : INutDiagnosticProcessFactory
    {
        private readonly INutDiagnosticProcess _process;

        public ProcessFactory(INutDiagnosticProcess process)
        {
            _process = process;
        }

        public int CreateCalls { get; private set; }

        public INutDiagnosticProcess Create(NutDiagnosticProcessSpec specification)
        {
            CreateCalls++;
            return _process;
        }
    }

    private sealed class FakeProcess : INutDiagnosticProcess
    {
        private bool _hasExited;

        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool HasExitedValue
        {
            get => _hasExited;
            set => _hasExited = value;
        }

        public int ExitCodeValue { get; set; }

        public string StandardOutputText { get; set; } = string.Empty;

        public string StandardErrorText { get; set; } = string.Empty;

        public bool WaitUntilCancellation { get; set; }

        public bool ThrowOnKill { get; set; }

        public bool ThrowOnCleanupWait { get; set; }

        public int KillCalls { get; private set; }

        public bool Start()
        {
            Started.TrySetResult(true);
            return true;
        }

        public bool HasExited => _hasExited;

        public int ExitCode => ExitCodeValue;

        public TextReader StandardOutput => new StringReader(StandardOutputText);

        public TextReader StandardError => new StringReader(StandardErrorText);

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            if (ThrowOnCleanupWait && KillCalls > 0)
            {
                return Task.FromCanceled(new CancellationToken(canceled: true));
            }

            if (_hasExited)
            {
                return Task.CompletedTask;
            }

            return WaitUntilCancellation ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken) : Task.CompletedTask;
        }

        public void KillCreatedProcessTree()
        {
            KillCalls++;
            if (ThrowOnKill)
            {
                throw new InvalidOperationException("fake kill failure");
            }

            _hasExited = true;
        }

        public void Dispose()
        {
        }
    }
}
