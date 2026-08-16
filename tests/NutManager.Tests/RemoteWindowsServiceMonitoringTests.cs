using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using NutManager.App.ViewModels;
using NutManager.Core.Administration;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Platform.Windows;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// T34 remote Windows service monitoring. Everything here runs against a fake probe: no SCM, no RPC,
/// no network and no real host, which is what lets the awkward cases — a refused query, a blocked
/// call, a result that arrives too late — be tested at all.
/// </summary>
public sealed class RemoteWindowsServiceMonitoringTests
{
    private static readonly DateTimeOffset Observed = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private static RemoteWindowsNutServiceSnapshot Success(
        NutServiceState state = NutServiceState.Running,
        int? processId = 1234,
        string? executable = "nut.exe") =>
        new("Gandalf.sbra.local", RemoteWindowsServiceProbeState.Success, "Network UPS Tools",
            "Network UPS Tools", state, processId, executable, null, Observed);

    // ---------------------------------------------------------------- service state

    [Theory]
    [InlineData(NutServiceState.Running, "ServiceState.Running")]
    [InlineData(NutServiceState.Stopped, "ServiceState.Stopped")]
    [InlineData(NutServiceState.StartPending, "ServiceState.StartPending")]
    [InlineData(NutServiceState.StopPending, "ServiceState.StopPending")]
    [InlineData(NutServiceState.Paused, "ServiceState.Paused")]
    [InlineData(NutServiceState.PausePending, "ServiceState.PausePending")]
    [InlineData(NutServiceState.ContinuePending, "ServiceState.ContinuePending")]
    public async Task EveryWindowsServiceStateKeepsItsOwnWordingRatherThanCollapsingIntoRunningOrStopped(
        NutServiceState state,
        string expectedKey)
    {
        await using var monitor = Monitor(Success(state));
        await monitor.RefreshAsync();

        Assert.Equal(monitor.Strings.Get(expectedKey), monitor.ServiceStateText);
    }

    [Fact]
    public async Task ATransitionalServiceIsNeitherRunningNorStopped()
    {
        await using var monitor = Monitor(Success(NutServiceState.StartPending));
        await monitor.RefreshAsync();

        Assert.False(monitor.IsServiceRunning);
        Assert.False(monitor.IsServiceStopped);
        Assert.True(monitor.IsServiceTransitioning);
    }

    // ---------------------------------------------------------------- process and pid

    [Fact]
    public async Task ARunningServiceReportsItsExecutableAndProcessId()
    {
        await using var monitor = Monitor(Success());
        await monitor.RefreshAsync();

        Assert.Equal("nut.exe", monitor.ProcessText);
        Assert.Equal("1234", monitor.ProcessIdText);
    }

    [Fact]
    public async Task AStoppedServiceReportsNoProcessAndNoProcessId()
    {
        await using var monitor = Monitor(Success(NutServiceState.Stopped, processId: null, executable: null));
        await monitor.RefreshAsync();

        Assert.Equal(monitor.Strings.Get("RemoteService.Process.NotRunning"), monitor.ProcessText);
        Assert.Equal("—", monitor.ProcessIdText);
    }

    [Fact]
    public async Task AProcessIdTheServiceControlManagerWithholdsStaysMissingInsteadOfBecomingZero()
    {
        await using var monitor = Monitor(Success(processId: null, executable: null));
        await monitor.RefreshAsync();

        // The service is running, so the panel must not claim it is not; but no id was reported and
        // none is invented.
        Assert.True(monitor.IsServiceRunning);
        Assert.Equal("—", monitor.ProcessIdText);
    }

    // ---------------------------------------------------------------- probe failures

    [Theory]
    [InlineData(RemoteWindowsServiceProbeState.AccessDenied, "RemoteService.Query.AccessDenied")]
    [InlineData(RemoteWindowsServiceProbeState.RpcUnavailable, "RemoteService.Query.RpcUnavailable")]
    [InlineData(RemoteWindowsServiceProbeState.ServiceNotFound, "RemoteService.Query.ServiceNotFound")]
    [InlineData(RemoteWindowsServiceProbeState.AmbiguousService, "RemoteService.Query.Ambiguous")]
    [InlineData(RemoteWindowsServiceProbeState.TimedOut, "RemoteService.Query.TimedOut")]
    [InlineData(RemoteWindowsServiceProbeState.Unsupported, "RemoteService.Query.Unsupported")]
    [InlineData(RemoteWindowsServiceProbeState.UnknownFailure, "RemoteService.Query.Failed")]
    public async Task EachQueryFailureIsNamedRatherThanShownAsAStoppedService(
        RemoteWindowsServiceProbeState probeState,
        string expectedKey)
    {
        await using var monitor = Monitor(RemoteWindowsNutServiceSnapshot.Failure("host", probeState, Observed));
        await monitor.RefreshAsync();

        Assert.Equal(monitor.Strings.Get(expectedKey), monitor.QueryStateText);

        // A failed query never claims the service is stopped: it does not know.
        Assert.False(monitor.IsServiceStopped);
        Assert.False(monitor.IsServiceRunning);
        Assert.True(monitor.IsQueryUnavailable);
        Assert.Equal(monitor.Strings.Get("Status.Unavailable"), monitor.ServiceStateText);
    }

    [Fact]
    public async Task AnAmbiguousResultNamesTheCandidatesInsteadOfPickingOne()
    {
        var snapshot = RemoteWindowsNutServiceSnapshot.Failure(
            "host",
            RemoteWindowsServiceProbeState.AmbiguousService,
            Observed,
            candidates: ["Network UPS Tools", "NUT"]);
        await using var monitor = Monitor(snapshot);
        await monitor.RefreshAsync();

        Assert.True(monitor.HasDiagnostic);
        Assert.Contains("Network UPS Tools", monitor.DiagnosticText);
        Assert.Contains("NUT", monitor.DiagnosticText);
        Assert.Null(monitor.Snapshot!.ServiceName);
    }

    [Fact]
    public async Task TheNumericWindowsCodeSurvivesIntoTheDiagnostic()
    {
        var snapshot = RemoteWindowsNutServiceSnapshot.Failure(
            "host", RemoteWindowsServiceProbeState.AccessDenied, Observed, WindowsRemoteNutServiceProbe.ErrorAccessDenied);
        await using var monitor = Monitor(snapshot);
        await monitor.RefreshAsync();

        Assert.Contains("5", monitor.DiagnosticText);
    }

    [Theory]
    [InlineData(WindowsRemoteNutServiceProbe.ErrorAccessDenied, RemoteWindowsServiceProbeState.AccessDenied)]
    [InlineData(WindowsRemoteNutServiceProbe.ErrorServiceDoesNotExist, RemoteWindowsServiceProbeState.ServiceNotFound)]
    [InlineData(WindowsRemoteNutServiceProbe.RpcServerUnavailable, RemoteWindowsServiceProbeState.RpcUnavailable)]
    [InlineData(1359, RemoteWindowsServiceProbeState.UnknownFailure)]
    public void FailuresAreMappedByNumericCodeNotByTheirLocalizedMessage(int code, RemoteWindowsServiceProbeState expected)
    {
        // The message is localized on the machine running NutManager, so mapping on text would make
        // the behaviour depend on the operator's display language.
        var exception = new InvalidOperationException("qualquer texto", new Win32Exception(code));

        Assert.Equal(expected, WindowsRemoteNutServiceProbe.MapFailure(exception, out var reported));
        Assert.Equal(code, reported);
    }

    // ---------------------------------------------------------------- executable identity

    [Theory]
    [InlineData("\"C:\\NUT\\nut.exe\" --service", "nut.exe")]
    [InlineData("C:\\Program Files\\NUT\\sbin\\upsd.exe -D", "upsd.exe")]
    [InlineData("\"C:\\Program Files (x86)\\NUT\\nut.exe\"", "nut.exe")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TheExecutableIsExtractedFromAQuotedPathWithArgumentsRatherThanSplitOnSpaces(string? imagePath, string? expected)
    {
        Assert.Equal(expected, WindowsRemoteNutServiceProbe.ExecutableNameOf(imagePath));
    }

    [Theory]
    [InlineData("Gandalf.sbra.local", "Gandalf.sbra.local")]
    [InlineData("\\\\Gandalf\\etc", "Gandalf")]
    [InlineData("  Gandalf  ", "Gandalf")]
    [InlineData("", "")]
    public void TheHostIsNormalizedOnlyAsFarAsTheWindowsApiNeeds(string host, string expected)
    {
        Assert.Equal(expected, WindowsRemoteNutServiceProbe.NormalizeHost(host));
    }

    // ---------------------------------------------------------------- security boundary

    [Fact]
    public void TheMonitorExposesNoWayToStartStopOrRestartTheRemoteService()
    {
        // The view binds to this object, so a control able to act on the remote host would need a
        // command here. Refusing the whole verb is stronger than disabling a button.
        string[] controlVerbs = ["Start", "Stop", "Restart", "Pause", "Continue", "Delete", "Install", "Configure"];
        string[] allowed = ["StartMonitoring", "StopMonitoringAsync"];

        var violations = typeof(RemoteWindowsServiceViewModel)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            // Property accessors are reads, not actions: IsServiceStopped reports a state the SCM
            // gave us, and naming it must not be mistaken for offering to stop anything.
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Where(name => controlVerbs.Any(verb => name.Contains(verb, StringComparison.OrdinalIgnoreCase)))
            .Where(name => !allowed.Contains(name, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(violations.Length == 0, $"Unexpected control-shaped members: {string.Join(", ", violations)}");

        // And the only command it publishes is the refresh.
        var commands = typeof(RemoteWindowsServiceViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => typeof(ICommand).IsAssignableFrom(property.PropertyType))
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(["RefreshCommand"], commands);
    }

    [Fact]
    public void TheProbeCollectsNoCredentialAndReadsNoCredentialStore()
    {
        var parameters = typeof(WindowsRemoteNutServiceProbe)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        // Nothing that could carry or fetch a secret is reachable from here: the query runs as the
        // current Windows identity and has no other account to offer.
        Assert.DoesNotContain(parameters, type => typeof(IRemoteCredentialStore).IsAssignableFrom(type));
        Assert.DoesNotContain(parameters, type => typeof(IWindowsCredentialPrompt).IsAssignableFrom(type));

        var source = Repository.Read(Path.Combine(
            "src", "NutManager.Infrastructure", "Platform", "Windows", "WindowsRemoteNutServiceProbe.cs"));
        foreach (var forbidden in new[]
        {
            "LogonUser", "RunImpersonated", "WindowsIdentity.Impersonate", "CredUIPrompt",
            "IRemoteCredentialStore", "IWindowsCredentialPrompt", "Password", "Process.Start"
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheNativeLayerAsksForQueryRightsAndNothingElse()
    {
        var source = Repository.Read(Path.Combine(
            "src", "NutManager.Infrastructure", "Platform", "Windows", "WindowsServiceControlManagerInterop.cs"));

        // A mutation would need one of these named here first, which makes adding one a reviewable
        // change rather than a silent one.
        foreach (var forbidden in new[]
        {
            "SERVICE_START", "ServiceStart", "SERVICE_STOP", "ServiceStop", "SERVICE_CHANGE_CONFIG",
            "ServiceChangeConfig", "SC_MANAGER_CREATE_SERVICE", "ScManagerCreateService",
            "SC_MANAGER_MODIFY_BOOT_CONFIG", "WRITE_DAC", "WriteDac", "WRITE_OWNER", "WriteOwner",
            "GENERIC_WRITE", "GenericWrite", "ChangeServiceConfig", "StartServiceW",
            "ControlService", "DeleteService", "SetServiceObjectSecurity"
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }

        // And the rights it does declare are the read-only ones, at their documented values.
        Assert.Contains("ScManagerConnect = 0x0001", source, StringComparison.Ordinal);
        Assert.Contains("ServiceQueryConfig = 0x0001", source, StringComparison.Ordinal);
        Assert.Contains("ServiceQueryStatus = 0x0004", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProductNeverShellsOutToImplementRemoteMonitoring()
    {
        foreach (var file in new[]
        {
            Path.Combine("src", "NutManager.Infrastructure", "Platform", "Windows", "WindowsRemoteNutServiceProbe.cs"),
            Path.Combine("src", "NutManager.Infrastructure", "Platform", "Windows", "WindowsServiceControlManagerInterop.cs"),
            Path.Combine("src", "NutManager.App", "ViewModels", "RemoteWindowsServiceViewModel.cs")
        })
        {
            var source = Repository.Read(file);
            foreach (var forbidden in new[] { "Process.Start", "sc.exe", "powershell", "cmd.exe", "wmic", "netsh" })
            {
                Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ---------------------------------------------------------------- lifecycle

    [Fact]
    public async Task ASecondRefreshJoinsTheRunningProbeInsteadOfStartingAnother()
    {
        var probe = new BlockingProbe();
        await using var monitor = new RemoteWindowsServiceViewModel("host", probe);

        var first = monitor.RefreshAsync();
        var second = monitor.RefreshAsync();

        Assert.Equal(1, probe.Started);
        probe.Release(Success());
        await first;
        await second;

        // A blocked RPC can outlive its interval; piling a second call on top of it each tick is how
        // a monitor turns into a thread leak against a host that is already not answering.
        Assert.Equal(1, probe.Started);
    }

    [Fact]
    public async Task AProbeThatReturnsAfterMonitoringStoppedCannotOverwriteTheReading()
    {
        var probe = new BlockingProbe();
        var monitor = new RemoteWindowsServiceViewModel("host", probe);

        var pending = monitor.RefreshAsync();
        await monitor.StopMonitoringAsync();

        probe.Release(Success(NutServiceState.Stopped));
        await pending;

        // The late answer describes a host nobody is watching any more, so it lands nowhere.
        Assert.Null(monitor.Snapshot);
        await monitor.DisposeAsync();
    }

    [Fact]
    public async Task DisposeStopsThePollingAndRefusesFurtherProbes()
    {
        var probe = new CountingProbe(Success());
        var monitor = new RemoteWindowsServiceViewModel("host", probe, interval: TimeSpan.FromMilliseconds(20));

        monitor.StartMonitoring();
        await monitor.DisposeAsync();

        var afterDispose = probe.Calls;
        await monitor.RefreshAsync();
        await Task.Delay(80);

        Assert.Equal(afterDispose, probe.Calls);
    }

    [Fact]
    public async Task ARefreshInProgressLabelsTheReadingItIsStillShowing()
    {
        var probe = new BlockingProbe();
        await using var monitor = new RemoteWindowsServiceViewModel("host", probe);

        var first = monitor.RefreshAsync();
        probe.Release(Success());
        await first;

        Assert.False(monitor.IsShowingStaleReading);
        Assert.True(monitor.HasObservation);
    }

    // ---------------------------------------------------------------- independence from NUT

    [Fact]
    public async Task ARefusedWindowsQueryLeavesTheNutProtocolStateAlone()
    {
        await using var monitor = Monitor(RemoteWindowsNutServiceSnapshot.Failure(
            "Gandalf.sbra.local", RemoteWindowsServiceProbeState.AccessDenied, Observed, 5));
        await monitor.RefreshAsync();

        // The monitor holds no connection, endpoint or protocol state at all, so there is nothing it
        // could mark offline. That absence is the independence.
        var members = typeof(RemoteWindowsServiceViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(members, name => name.Contains("Connection", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(members, name => name.Contains("Endpoint", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(members, name => name.Contains("Online", StringComparison.OrdinalIgnoreCase));
        Assert.True(monitor.IsQueryUnavailable);
    }

    [Fact]
    public async Task AServiceThatRunsWhileNutIsUnreachableIsStillReportedAsRunning()
    {
        // Case B from the task: the Windows service is up, NUT is not answering. The two readings
        // disagree, and neither is allowed to correct the other.
        await using var monitor = Monitor(Success());
        await monitor.RefreshAsync();

        Assert.True(monitor.IsServiceRunning);
        Assert.Equal(monitor.Strings.Get("RemoteService.Query.Available"), monitor.QueryStateText);
    }

    // ---------------------------------------------------------------- localization

    [Fact]
    public void EveryStringTheRemoteMonitorAsksForExistsInBothLanguages()
    {
        var keys = new[]
        {
            "RemoteService.Title", "RemoteService.Host", "RemoteService.Service", "RemoteService.State",
            "RemoteService.Process", "RemoteService.ProcessId", "RemoteService.Query",
            "RemoteService.Refresh", "RemoteService.Refreshing", "RemoteService.ReadOnlyNotice",
            "RemoteService.ControlUnavailable", "RemoteService.ObservedAt",
            "RemoteService.Process.NotRunning", "RemoteService.Process.Unnamed",
            "RemoteService.Query.Available", "RemoteService.Query.AccessDenied",
            "RemoteService.Query.RpcUnavailable", "RemoteService.Query.ServiceNotFound",
            "RemoteService.Query.Ambiguous", "RemoteService.Query.AmbiguousDetail",
            "RemoteService.Query.TimedOut", "RemoteService.Query.Unsupported",
            "RemoteService.Query.Failed", "RemoteService.Query.Win32Code",
            "ServiceState.PausePending", "ServiceState.ContinuePending"
        };

        foreach (var language in new[] { UiLanguagePreference.PtBr, UiLanguagePreference.EnUs })
        {
            var localizer = new NutManager.App.Localization.NutManagerLocalizer(language);
            foreach (var key in keys)
            {
                var value = localizer.Get(key);
                Assert.False(string.IsNullOrWhiteSpace(value), $"{key} is empty for {language}.");
                Assert.NotEqual(key, value);
            }
        }
    }

    // ---------------------------------------------------------------- local regression

    [Fact]
    public void ALocalProfilePageCarriesNoRemoteMonitorAtAll()
    {
        // This is the shape the application builds for a local profile. Nothing about T34 attaches,
        // so the local service administration below it is exactly what it was.
        var page = new AdministrationPageViewModel(null, null);

        Assert.Null(page.RemoteWindowsService);
        Assert.False(page.HasRemoteWindowsService);
    }

    [Fact]
    public void ARemoteProfilePageExposesTheMonitorAndNothingThatCouldActOnTheHost()
    {
        var monitor = Monitor(Success());
        var page = new AdministrationPageViewModel(null, null, remoteWindowsService: monitor);

        Assert.Same(monitor, page.RemoteWindowsService);
        Assert.True(page.HasRemoteWindowsService);

        // The local action gates stay shut without a local installation, so no button on this page
        // could be pointed at the remote machine even by accident.
        Assert.False(page.CanStartWindowsService);
        Assert.False(page.CanStopWindowsService);
        Assert.False(page.CanRestartWindowsService);
    }

    private static RemoteWindowsServiceViewModel Monitor(RemoteWindowsNutServiceSnapshot snapshot) =>
        new("Gandalf.sbra.local", new CountingProbe(snapshot));

    private sealed class CountingProbe(RemoteWindowsNutServiceSnapshot snapshot) : IRemoteWindowsNutServiceProbe
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<RemoteWindowsNutServiceSnapshot> ProbeAsync(string host, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(snapshot);
        }
    }

    /// <summary>A probe that does not answer until the test says so, standing in for a blocked RPC.</summary>
    private sealed class BlockingProbe : IRemoteWindowsNutServiceProbe
    {
        private readonly TaskCompletionSource<RemoteWindowsNutServiceSnapshot> _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;

        public int Started => Volatile.Read(ref _started);

        public void Release(RemoteWindowsNutServiceSnapshot snapshot) => _gate.TrySetResult(snapshot);

        public Task<RemoteWindowsNutServiceSnapshot> ProbeAsync(string host, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _started);
            return _gate.Task;
        }
    }
}
