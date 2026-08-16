using NutManager.Core.Administration;
using NutManager.Core.Agent;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// T35 agent security core. The agent runs as LocalSystem, so the rules below are the difference
/// between a NUT control surface and a privileged remote executor. Everything here runs against
/// fakes: no SCM, no Windows service, no Event Log, no network.
/// </summary>
public sealed class NutAgentApplicationServiceTests
{
    private static readonly NutServiceTarget Target =
        new("Network UPS Tools", "Network UPS Tools", @"C:\NUT\sbin\nut.exe", NutAssociationConfidence.BinaryPath);

    private static readonly NutAgentCallerContext Operator = new(@"SBRA\operador", true, "pipe");
    private static readonly NutAgentCallerContext Stranger = new(@"SBRA\ninguem", false, "pipe");

    // ---------------------------------------------------------------- authorization

    [Fact]
    public async Task AMemberOfTheOperatorsGroupMayControlTheService()
    {
        var (service, controller, _, _) = await BuildAsync(state: NutServiceState.Stopped);

        var result = await service.StartAsync(Guid.NewGuid(), Operator, default);

        Assert.Equal(NutAgentResultCode.Success, result.Code);
        Assert.Equal(1, controller.StartCalls);
    }

    [Fact]
    public async Task ANonMemberIsRefusedAndTheServiceIsNeverTouched()
    {
        var (service, controller, audit, _) = await BuildAsync(state: NutServiceState.Stopped);

        var result = await service.StopAsync(Guid.NewGuid(), Stranger, default);

        Assert.Equal(NutAgentResultCode.Unauthorized, result.Code);
        Assert.Equal(0, controller.StopCalls);
        Assert.Contains(audit.Entries, entry => entry.Kind == NutAgentAuditKind.UnauthorizedAttempt);
    }

    [Fact]
    public async Task AClientClaimingAuthorizationItDoesNotHaveIsStillRefused()
    {
        // The transport says "authorized" but the group says otherwise. The service asks the group.
        var (service, controller, _, _) = await BuildAsync(state: NutServiceState.Stopped, members: []);

        var result = await service.StartAsync(Guid.NewGuid(), Operator, default);

        Assert.Equal(NutAgentResultCode.Unauthorized, result.Code);
        Assert.Equal(0, controller.StartCalls);
    }

    [Fact]
    public async Task AMissingOperatorsGroupDisablesControlEntirelyRatherThanFallingBackToAdministrators()
    {
        var authorization = new FakeAuthorization { IsConfigured = false, ConfigurationFailure = "group not found" };
        var (service, controller, _, _) = await BuildAsync(state: NutServiceState.Stopped, authorization: authorization);

        var result = await service.RestartAsync(Guid.NewGuid(), Operator, default);
        var handshake = await service.HandshakeAsync(default);

        Assert.Equal(NutAgentResultCode.Unauthorized, result.Code);
        Assert.Equal(0, controller.StopCalls);
        Assert.False(handshake.ControlAvailable);
        Assert.DoesNotContain(NutAgentOperation.Start, handshake.Capabilities);
    }

    [Fact]
    public async Task AnAuthorizationLookupThatFailsDeniesRatherThanAllows()
    {
        var authorization = new FakeAuthorization { Throws = true };
        var (service, controller, _, _) = await BuildAsync(state: NutServiceState.Stopped, authorization: authorization);

        var result = await service.StartAsync(Guid.NewGuid(), Operator, default);

        Assert.Equal(NutAgentResultCode.Unauthorized, result.Code);
        Assert.Equal(0, controller.StartCalls);
    }

    // ---------------------------------------------------------------- audit readiness

    [Fact]
    public async Task MutationIsRefusedWhenTheAuditSinkIsNotReady()
    {
        var audit = new FakeAudit { Ready = false };
        var (service, controller, _, _) = await BuildAsync(state: NutServiceState.Stopped, audit: audit);

        var result = await service.StartAsync(Guid.NewGuid(), Operator, default);

        // Acting first and finding out afterwards that nothing was recorded is the failure this
        // ordering exists to prevent.
        Assert.Equal(NutAgentResultCode.AuditUnavailable, result.Code);
        Assert.Equal(0, controller.StartCalls);
    }

    [Fact]
    public async Task ReadingStatusStillWorksWhileAuditIsUnavailable()
    {
        var audit = new FakeAudit { Ready = false };
        var (service, _, _, _) = await BuildAsync(state: NutServiceState.Running, audit: audit);

        var status = await service.GetStatusAsync(default);

        Assert.Equal(NutServiceState.Running, status.ServiceState);
    }

    [Fact]
    public async Task AnActionThatSucceedsButCannotBeRecordedReportsBothFacts()
    {
        var audit = new FakeAudit { FailResultWrites = true };
        var (service, controller, _, _) = await BuildAsync(state: NutServiceState.Stopped, audit: audit);

        var result = await service.StartAsync(Guid.NewGuid(), Operator, default);

        // The service really did start. Saying so, and saying the record failed, is the honest report.
        Assert.Equal(NutAgentResultCode.CompletedWithAuditFailure, result.Code);
        Assert.Equal(NutServiceState.Running, result.FinalState);
        Assert.Equal(1, controller.StartCalls);
    }

    [Fact]
    public async Task PollingStatusWritesNoAuditEntries()
    {
        var (service, _, audit, _) = await BuildAsync(state: NutServiceState.Running);

        for (var i = 0; i < 20; i++) await service.GetStatusAsync(default);

        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task NoAuditFieldCanCarryASecret()
    {
        var (service, _, audit, _) = await BuildAsync(state: NutServiceState.Stopped);
        await service.StartAsync(Guid.NewGuid(), Operator, default);

        var written = string.Join("\n", audit.Entries.Select(entry =>
            string.Join("|", entry.CallerIdentity, entry.Transport, entry.MachineName, entry.ServiceName, entry.Detail)));

        foreach (var forbidden in new[] { "password", "senha", "passphrase", "secret", "token" })
        {
            Assert.DoesNotContain(forbidden, written, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------------------------------------------------------------- target boundary

    [Fact]
    public async Task NoRequestCanNameAServiceBecauseNoRequestCarriesOne()
    {
        // The defence is structural: every mutation entry point takes an operation id and a caller,
        // and nothing else. There is no parameter a service name could arrive through.
        foreach (var name in new[] { "StartAsync", "StopAsync", "RestartAsync" })
        {
            var method = typeof(NutAgentApplicationService).GetMethod(name)!;
            var parameters = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

            Assert.Equal([typeof(Guid), typeof(NutAgentCallerContext), typeof(CancellationToken)], parameters);
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task AServiceThatStopsValidatingAsNutIsNotControlled()
    {
        var resolver = new FakeResolver(Target);
        var (service, controller, audit, _) = await BuildAsync(state: NutServiceState.Running, resolver: resolver);

        // An administrator repoints the binary between two requests.
        resolver.RevalidationResult = new NutServiceTargetResolution(
            NutServiceTargetStatus.ValidationFailed, null, "binary is outside the NUT installation");

        var result = await service.StopAsync(Guid.NewGuid(), Operator, default);

        Assert.Equal(NutAgentResultCode.TargetRevalidationFailed, result.Code);
        Assert.Equal(0, controller.StopCalls);
        Assert.Contains(audit.Entries, entry => entry.Kind == NutAgentAuditKind.TargetRevalidationFailure);
    }

    [Fact]
    public async Task ARevalidationThatNamesADifferentServiceIsRefused()
    {
        var resolver = new FakeResolver(Target);
        var (service, controller, _, _) = await BuildAsync(state: NutServiceState.Running, resolver: resolver);

        resolver.RevalidationResult = new NutServiceTargetResolution(
            NutServiceTargetStatus.Resolved,
            new NutServiceTarget("SomethingElse", "Something Else", @"C:\Other\other.exe", NutAssociationConfidence.NameFallback));

        var result = await service.StopAsync(Guid.NewGuid(), Operator, default);

        Assert.Equal(NutAgentResultCode.TargetRevalidationFailed, result.Code);
        Assert.Equal(0, controller.StopCalls);
    }

    [Fact]
    public async Task AnAgentWithoutAnIdentifiedServiceHoldsNoAuthority()
    {
        var resolver = new FakeResolver(null)
        {
            InitialResult = new NutServiceTargetResolution(
                NutServiceTargetStatus.Ambiguous, null, null, ["Network UPS Tools", "NUT"])
        };
        var (service, controller, _, _) = await BuildAsync(state: NutServiceState.Running, resolver: resolver);

        var result = await service.StartAsync(Guid.NewGuid(), Operator, default);
        var handshake = await service.HandshakeAsync(default);
        var status = await service.GetStatusAsync(default);

        Assert.Equal(NutAgentResultCode.TargetUnavailable, result.Code);
        Assert.Equal(0, controller.StartCalls);
        Assert.False(handshake.ControlAvailable);
        Assert.False(status.TargetValidated);
    }

    // ---------------------------------------------------------------- start and stop

    [Fact]
    public async Task StartingAnAlreadyRunningServiceDoesNotIssueASecondStart()
    {
        var (service, controller, _, _) = await BuildAsync(state: NutServiceState.Running);

        var result = await service.StartAsync(Guid.NewGuid(), Operator, default);

        Assert.Equal(NutAgentResultCode.AlreadyInRequestedState, result.Code);
        Assert.Equal(0, controller.StartCalls);
    }

    [Fact]
    public async Task StoppingAnAlreadyStoppedServiceDoesNotIssueASecondStop()
    {
        var (service, controller, _, _) = await BuildAsync(state: NutServiceState.Stopped);

        var result = await service.StopAsync(Guid.NewGuid(), Operator, default);

        Assert.Equal(NutAgentResultCode.AlreadyInRequestedState, result.Code);
        Assert.Equal(0, controller.StopCalls);
    }

    [Fact]
    public async Task AServiceThatWillNotStopIsReportedAsFailedRatherThanKilled()
    {
        var controller = new FakeController(NutServiceState.Running)
        {
            StopOutcome = new NutServiceControlOutcome(NutAgentResultCode.TimedOut, NutServiceState.StopPending, 1053)
        };
        var (service, _, _, _) = await BuildAsync(controller: controller);

        var result = await service.StopAsync(Guid.NewGuid(), Operator, default);

        Assert.Equal(NutAgentResultCode.TimedOut, result.Code);
        Assert.Equal(NutServiceState.StopPending, result.FinalState);
        Assert.Equal(1053, result.Win32ErrorCode);
    }

    // ---------------------------------------------------------------- restart

    [Fact]
    public async Task RestartStopsThenStartsAndReportsBothPhases()
    {
        var (service, controller, _, _) = await BuildAsync(state: NutServiceState.Running);

        var result = await service.RestartAsync(Guid.NewGuid(), Operator, default);

        Assert.Equal(NutAgentResultCode.Success, result.Code);
        Assert.Equal(NutServiceState.Running, result.FinalState);
        Assert.Equal(1, controller.StopCalls);
        Assert.Equal(1, controller.StartCalls);
        Assert.Equal(NutAgentRestartPhase.None, result.FailedPhase);
    }

    [Fact]
    public async Task RestartDoesNotAttemptTheStartWhenTheStopFailed()
    {
        var controller = new FakeController(NutServiceState.Running)
        {
            StopOutcome = new NutServiceControlOutcome(NutAgentResultCode.ServiceControlFailed, NutServiceState.Running, 5)
        };
        var (service, _, _, _) = await BuildAsync(controller: controller);

        var result = await service.RestartAsync(Guid.NewGuid(), Operator, default);

        // Starting a service we could not stop would either duplicate it or report a success that
        // never happened.
        Assert.Equal(NutAgentRestartPhase.Stop, result.FailedPhase);
        Assert.Equal(0, controller.StartCalls);
        Assert.Equal(NutServiceState.Running, result.FinalState);
    }

    [Fact]
    public async Task ARestartWhoseStartFailsReportsTheServiceLeftStopped()
    {
        var controller = new FakeController(NutServiceState.Running)
        {
            StartOutcome = new NutServiceControlOutcome(NutAgentResultCode.ServiceControlFailed, NutServiceState.Stopped, 1069)
        };
        var (service, _, _, _) = await BuildAsync(controller: controller);

        var result = await service.RestartAsync(Guid.NewGuid(), Operator, default);

        // "Restart failed" alone would hide the fact that matters most: the UPS monitor is down.
        Assert.False(result.Succeeded);
        Assert.Equal(NutAgentRestartPhase.Start, result.FailedPhase);
        Assert.Equal(NutServiceState.Stopped, result.FinalState);
        // The stop half really did run and really did work; only the start failed.
        Assert.Equal(NutAgentResultCode.Success, result.StopPhase);
        Assert.Equal(1069, result.Win32ErrorCode);
    }

    [Fact]
    public async Task RestartIsOneAuditedOperationWithOneIdentifier()
    {
        var (service, _, audit, _) = await BuildAsync(state: NutServiceState.Running);
        var operationId = Guid.NewGuid();

        await service.RestartAsync(operationId, Operator, default);

        var results = audit.Entries
            .Where(entry => entry.Kind is NutAgentAuditKind.OperationSucceeded or NutAgentAuditKind.OperationFailed)
            .ToArray();

        Assert.Single(results);
        Assert.Equal(operationId, results[0].OperationId);
        Assert.Equal(NutAgentOperation.Restart, results[0].Operation);
        Assert.All(audit.Entries, entry => Assert.Equal(operationId, entry.OperationId));
    }

    // ---------------------------------------------------------------- concurrency and retries

    [Fact]
    public async Task TwoMutationsNeverInterleave()
    {
        var controller = new FakeController(NutServiceState.Running) { BlockStop = true };
        var (service, _, _, _) = await BuildAsync(controller: controller);

        var restart = service.RestartAsync(Guid.NewGuid(), Operator, default);
        await controller.StopEntered.Task;

        // A second mutation arrives while the restart holds the gate, between its stop and its start.
        var start = service.StartAsync(Guid.NewGuid(), Operator, default);
        var startResult = await start;

        Assert.Equal(NutAgentResultCode.Busy, startResult.Code);

        controller.ReleaseStop();
        var restartResult = await restart;
        Assert.Equal(NutAgentResultCode.Success, restartResult.Code);
    }

    [Fact]
    public async Task StatusKeepsAnsweringWhileAMutationHoldsTheGate()
    {
        var controller = new FakeController(NutServiceState.Running) { BlockStop = true };
        var (service, _, _, _) = await BuildAsync(controller: controller);

        var restart = service.RestartAsync(Guid.NewGuid(), Operator, default);
        await controller.StopEntered.Task;

        var status = await service.GetStatusAsync(default).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(status);
        controller.ReleaseStop();
        await restart;
    }

    [Fact]
    public async Task ARetriedRequestWithTheSameIdentifierIsNotPerformedTwice()
    {
        var (service, controller, _, _) = await BuildAsync(state: NutServiceState.Running);
        var operationId = Guid.NewGuid();

        var first = await service.StopAsync(operationId, Operator, default);
        var second = await service.StopAsync(operationId, Operator, default);

        Assert.Equal(1, controller.StopCalls);
        Assert.Equal(first.Code, second.Code);
        Assert.Equal(first.OperationId, second.OperationId);
    }

    [Fact]
    public async Task ADifferentIdentifierIsADifferentIntentionAndRuns()
    {
        var (service, controller, _, _) = await BuildAsync(state: NutServiceState.Running);

        await service.StopAsync(Guid.NewGuid(), Operator, default);
        await service.StartAsync(Guid.NewGuid(), Operator, default);

        Assert.Equal(1, controller.StopCalls);
        Assert.Equal(1, controller.StartCalls);
    }

    // ---------------------------------------------------------------- handshake

    [Fact]
    public async Task TheHandshakeReportsTheAgentsOwnMachineNotOneTheClientSupplied()
    {
        var (service, _, _, _) = await BuildAsync(state: NutServiceState.Running);

        var handshake = await service.HandshakeAsync(default);

        Assert.Equal(service.MachineName, handshake.MachineName);
        Assert.Equal(NutAgentOptions.ProtocolVersion, handshake.ProtocolVersion);
        Assert.True(handshake.ControlAvailable);
        Assert.Contains(NutAgentOperation.Restart, handshake.Capabilities);
    }

    [Fact]
    public async Task StatusAndHandshakeStayAvailableWhenControlIsNot()
    {
        var authorization = new FakeAuthorization { IsConfigured = false, ConfigurationFailure = "group not found" };
        var (service, _, _, _) = await BuildAsync(state: NutServiceState.Running, authorization: authorization);

        var handshake = await service.HandshakeAsync(default);

        Assert.Contains(NutAgentOperation.GetStatus, handshake.Capabilities);
        Assert.Contains(NutAgentOperation.Handshake, handshake.Capabilities);
        Assert.False(handshake.ControlAvailable);
        Assert.Equal("group not found", handshake.ControlUnavailableReason);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<(NutAgentApplicationService Service, FakeController Controller, FakeAudit Audit, FakeAuthorization Authorization)> BuildAsync(
        NutServiceState state = NutServiceState.Running,
        FakeController? controller = null,
        FakeResolver? resolver = null,
        FakeAudit? audit = null,
        FakeAuthorization? authorization = null,
        string[]? members = null)
    {
        controller ??= new FakeController(state);
        resolver ??= new FakeResolver(Target);
        audit ??= new FakeAudit();
        authorization ??= new FakeAuthorization();
        if (members is not null) authorization.Members = [.. members];

        var service = new NutAgentApplicationService(
            resolver, controller, audit, authorization, TimeProvider.System,
            new NutAgentOptions { MachineName = "GANDALF", GateWaitTimeout = TimeSpan.FromMilliseconds(150) });

        await service.InitializeAsync(default);
        audit.Entries.Clear();
        return (service, controller, audit, authorization);
    }

    private sealed class FakeResolver(NutServiceTarget? target) : INutServiceTargetResolver
    {
        public NutServiceTargetResolution? InitialResult { get; set; }
        public NutServiceTargetResolution? RevalidationResult { get; set; }

        public Task<NutServiceTargetResolution> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(InitialResult ?? Resolved());

        public Task<NutServiceTargetResolution> RevalidateAsync(NutServiceTarget candidate, CancellationToken cancellationToken) =>
            Task.FromResult(RevalidationResult ?? Resolved());

        private NutServiceTargetResolution Resolved() => target is null
            ? new NutServiceTargetResolution(NutServiceTargetStatus.NotFound, null)
            : new NutServiceTargetResolution(NutServiceTargetStatus.Resolved, target);
    }

    private sealed class FakeController(NutServiceState initial) : INutServiceController
    {
        private NutServiceState _state = initial;

        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public NutServiceControlOutcome? StartOutcome { get; set; }
        public NutServiceControlOutcome? StopOutcome { get; set; }
        public bool BlockStop { get; set; }

        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseStop() => _stopGate.TrySetResult();

        public Task<NutAgentServiceStatus> GetStatusAsync(NutServiceTarget target, CancellationToken cancellationToken) =>
            Task.FromResult(new NutAgentServiceStatus(
                "GANDALF", target.ServiceName, target.DisplayName, _state,
                _state == NutServiceState.Running ? 4242 : null,
                _state == NutServiceState.Running ? "nut.exe" : null,
                true, DateTimeOffset.UtcNow));

        public Task<NutServiceControlOutcome> StartAsync(NutServiceTarget target, TimeSpan timeout, CancellationToken cancellationToken)
        {
            StartCalls++;
            if (StartOutcome is { } outcome)
            {
                _state = outcome.FinalState;
                return Task.FromResult(outcome);
            }

            _state = NutServiceState.Running;
            return Task.FromResult(new NutServiceControlOutcome(NutAgentResultCode.Success, _state));
        }

        public async Task<NutServiceControlOutcome> StopAsync(NutServiceTarget target, TimeSpan timeout, CancellationToken cancellationToken)
        {
            StopCalls++;
            if (BlockStop)
            {
                StopEntered.TrySetResult();
                await _stopGate.Task;
            }

            if (StopOutcome is { } outcome)
            {
                _state = outcome.FinalState;
                return outcome;
            }

            _state = NutServiceState.Stopped;
            return new NutServiceControlOutcome(NutAgentResultCode.Success, _state);
        }
    }

    private sealed class FakeAudit : INutAgentAuditSink
    {
        public bool Ready { get; set; } = true;
        public bool FailResultWrites { get; set; }
        public List<NutAgentAuditEntry> Entries { get; } = [];

        public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(Ready);

        public Task<bool> WriteAsync(NutAgentAuditEntry entry, CancellationToken cancellationToken)
        {
            if (FailResultWrites && entry.Kind is NutAgentAuditKind.OperationSucceeded or NutAgentAuditKind.OperationFailed)
            {
                return Task.FromResult(false);
            }

            Entries.Add(entry);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeAuthorization : INutAgentAuthorization
    {
        public bool IsConfigured { get; set; } = true;
        public string? ConfigurationFailure { get; set; }
        public bool Throws { get; set; }
        public HashSet<string> Members { get; set; } = new(StringComparer.OrdinalIgnoreCase) { @"SBRA\operador" };

        public Task<bool> IsAuthorizedAsync(string identity, CancellationToken cancellationToken)
        {
            if (Throws) throw new InvalidOperationException("lookup failed");
            return Task.FromResult(Members.Contains(identity));
        }
    }
}
