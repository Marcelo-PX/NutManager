using NutManager.Core.Administration;
using NutManager.Core.Models;
using NutManager.Infrastructure.Platform.Windows;
using Xunit;

namespace NutManager.Tests;

public sealed class WindowsNutAdministrationTests
{
    [Fact]
    public void ValidServiceRequestIsRestrictedToAnAbsoluteConfigurationContext()
    {
        var valid = new NutAdministrativeActionRequest(Guid.NewGuid(), NutAdministrativeAction.RestartService, "C:\\NUT", "C:\\NUT\\etc", "NetworkUpsTools");
        var relative = valid with { InstallationDirectory = "NUT" };
        var outside = valid with { ConfigurationDirectory = "C:\\Other" };

        Assert.True(WindowsNutAdministrativeRequestValidator.IsValid(valid));
        Assert.False(WindowsNutAdministrativeRequestValidator.IsValid(relative));
        Assert.False(WindowsNutAdministrativeRequestValidator.IsValid(outside));
    }

    [Theory]
    [InlineData("C:\\NUT\\bin\\upsd.exe", true)]
    [InlineData("C:\\NUT-malicious\\upsd.exe", false)]
    public void CanonicalDirectoryContainmentDoesNotAcceptPrefixCollisions(string path, bool expected) =>
        Assert.Equal(expected, WindowsNutAdministrativeRequestValidator.IsPathInsideDirectory(path, "C:\\NUT"));

    [Theory]
    [InlineData("\"C:\\NUT\\bin\\nut.exe\" --service", NutAssociationConfidence.BinaryPath)]
    [InlineData("\"C:\\NUT-malicious\\bin\\nut.exe\" --service", NutAssociationConfidence.None)]
    [InlineData(null, NutAssociationConfidence.NameFallback)]
    public void ServiceAssociationUsesExecutableContainmentAndExactFallback(string? imagePath, NutAssociationConfidence expected)
    {
        var (_, confidence) = WindowsNutServiceAssociation.Determine("NetworkUpsTools", "Network UPS Tools", imagePath, "C:\\NUT");

        Assert.Equal(expected, confidence);
    }

    [Fact]
    public void ServiceAssociationRejectsSubstringOnlyFallback()
    {
        var (_, confidence) = WindowsNutServiceAssociation.Determine("DonutService", "Nutcracker updater", null, "C:\\NUT");

        Assert.Equal(NutAssociationConfidence.None, confidence);
    }

    [Fact]
    public void ServiceImagePathParserExtractsQuotedExecutableWithoutArguments()
    {
        Assert.Equal("C:\\NUT\\bin\\nut.exe", WindowsNutServiceAssociation.TryExtractExecutablePath("\"C:\\NUT\\bin\\nut.exe\" --service"));
    }

    [Fact]
    public void AclEvaluationAcceptsModifyGrantedThroughAGroup()
    {
        var result = WindowsAclPermissionEvaluation.Assess(
            [new WindowsAclRule("S-1-5-32-555", WindowsAclAccessControlType.Allow, WindowsAclRights.Modify)],
            new HashSet<string>(["S-1-5-21-user", "S-1-5-32-555"], StringComparer.OrdinalIgnoreCase));

        Assert.Equal(NutPermissionState.Modifiable, result);
    }

    [Fact]
    public void AclEvaluationAggregatesAllowRulesButRejectsPartialRights()
    {
        var identities = new HashSet<string>(["S-1-5-21-user"], StringComparer.OrdinalIgnoreCase);
        var combined = WindowsAclPermissionEvaluation.Assess(
            [
                new WindowsAclRule("S-1-5-21-user", WindowsAclAccessControlType.Allow, WindowsAclRights.ReadData),
                new WindowsAclRule("S-1-5-21-user", WindowsAclAccessControlType.Allow, WindowsAclRights.Modify & ~WindowsAclRights.ReadData)
            ], identities);
        var partial = WindowsAclPermissionEvaluation.Assess(
            [new WindowsAclRule("S-1-5-21-user", WindowsAclAccessControlType.Allow, WindowsAclRights.WriteData)], identities);

        Assert.Equal(NutPermissionState.Modifiable, combined);
        Assert.Equal(NutPermissionState.Insufficient, partial);
    }

    [Fact]
    public void AclEvaluationTreatsGroupDenyAsManualIntervention()
    {
        var result = WindowsAclPermissionEvaluation.Assess(
            [
                new WindowsAclRule("S-1-5-21-user", WindowsAclAccessControlType.Allow, WindowsAclRights.Modify),
                new WindowsAclRule("S-1-5-32-555", WindowsAclAccessControlType.Deny, WindowsAclRights.Modify)
            ],
            new HashSet<string>(["S-1-5-21-user", "S-1-5-32-555"], StringComparer.OrdinalIgnoreCase));

        Assert.Equal(NutPermissionState.ManualInterventionRequired, result);
    }

    [Fact]
    public void HelperResponsePathIsDerivedFromTheRequestPathOnly()
    {
        Assert.Equal("C:\\Users\\test\\AppData\\Local\\NutManager\\AdminRequests\\abc.response.json", WindowsPrivilegeElevationBroker.GetResponsePath("C:\\Users\\test\\AppData\\Local\\NutManager\\AdminRequests\\abc.request.json"));
        Assert.Throws<ArgumentException>(() => WindowsPrivilegeElevationBroker.GetResponsePath("C:\\temp\\arbitrary.json"));
    }

    [Fact]
    public void RequestPathValidationRejectsExternalPathsWithoutDerivingAnOutputPath()
    {
        var expected = "C:\\Users\\test\\AppData\\Local\\NutManager\\AdminRequests";
        Assert.True(WindowsPrivilegeElevationBroker.TryValidateRequestPath(expected + "\\0123456789abcdef0123456789abcdef.request.json", expected, out var requestId, out var requestPath, out var responsePath));
        Assert.Equal(Guid.ParseExact("0123456789abcdef0123456789abcdef", "N"), requestId);
        Assert.Equal(expected + "\\0123456789abcdef0123456789abcdef.request.json", requestPath);
        Assert.Equal(expected + "\\0123456789abcdef0123456789abcdef.response.json", responsePath);
        Assert.False(WindowsPrivilegeElevationBroker.TryValidateRequestPath("C:\\temp\\0123456789abcdef0123456789abcdef.request.json", expected, out _, out _, out _));
        Assert.False(WindowsPrivilegeElevationBroker.TryValidateRequestPath(expected + "\\not-a-guid.request.json", expected, out _, out _, out _));
        Assert.False(WindowsPrivilegeElevationBroker.TryValidateRequestPath(expected + "\\subdir\\0123456789abcdef0123456789abcdef.request.json", expected, out _, out _, out _));
    }

    [Fact]
    public void ExistingResponsePathIsDetectedBeforeAnyCreateNewWrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"NutManager.T16.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var responsePath = Path.Combine(directory, "response.json");
        try
        {
            File.WriteAllText(responsePath, "existing-response");

            Assert.True(WindowsPrivilegeElevationBroker.PathAlreadyExistsOrIsReparsePoint(responsePath));
            Assert.Equal("existing-response", File.ReadAllText(responsePath));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch { }
        }
    }

    [Theory]
    [InlineData("C:\\NUT\\etc", true)]
    [InlineData("C:\\NUT2\\etc", false)]
    [InlineData("C:\\NUT\\..\\Other", false)]
    [InlineData("..\\NUT\\etc", false)]
    public void WindowsPathValidationIsHostIndependent(string path, bool expected) =>
        Assert.Equal(expected, WindowsNutAdministrativeRequestValidator.IsPathInsideDirectory(path, "C:\\NUT"));

    [Fact]
    public async Task InvalidRequestIsRejectedBeforeAnyElevationOrBackendAction()
    {
        var backend = new FakeBackend();
        var broker = new FakeBroker(PrivilegeState.StandardUser);
        var administration = new WindowsLocalNutAdministration(backend, broker, new FakeRevalidator());
        var request = new NutAdministrativeActionRequest(Guid.Empty, NutAdministrativeAction.StartService, "C:\\NUT", "C:\\NUT\\etc", "NetworkUpsTools");

        var result = await administration.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(NutAdministrativeActionStatus.InvalidRequest, result.Status);
        Assert.Equal(0, broker.ExecuteCalls);
        Assert.Equal(0, backend.ExecuteCalls);
    }

    [Fact]
    public void PermissionRepairRequestOnlyAcceptsModifyForTheExplicitUserSid()
    {
        var plan = new NutPermissionRepairPlan("C:\\NUT\\etc", "TEST\\user", "S-1-5-21-123", ["C:\\NUT\\etc", "C:\\NUT\\etc\\ups.conf"], EffectiveIdentitySids: ["S-1-5-21-123"]);
        var request = new NutAdministrativeActionRequest(Guid.NewGuid(), NutAdministrativeAction.RepairConfigurationPermissions, "C:\\NUT", "C:\\NUT\\etc", PermissionRepairPlan: plan);

        Assert.True(WindowsNutAdministrativeRequestValidator.IsValid(request));
        Assert.False(WindowsNutAdministrativeRequestValidator.IsValid(request with { PermissionRepairPlan = plan with { Right = "FullControl" } }));
        Assert.False(WindowsNutAdministrativeRequestValidator.IsValid(request with { PermissionRepairPlan = plan with { AffectedPaths = ["C:\\Other\\ups.conf"] } }));
        Assert.False(WindowsNutAdministrativeRequestValidator.IsValid(request with { PermissionRepairPlan = plan with { AffectedPaths = ["C:\\NUT\\etc\\arbitrary.txt"] } }));
        Assert.False(WindowsNutAdministrativeRequestValidator.IsValid(request with { PermissionRepairPlan = plan with { AffectedPaths = ["C:\\NUT\\etc\\..\\outside.txt"] } }));
        Assert.False(WindowsNutAdministrativeRequestValidator.IsValid(request with { PermissionRepairPlan = plan with { EffectiveIdentitySids = ["S-1-5-32-555"] } }));
        Assert.False(WindowsNutAdministrativeRequestValidator.IsValid(request with { PermissionRepairPlan = plan with { EffectiveIdentitySids = ["not-a-sid", "S-1-5-21-123"] } }));
    }

    [Fact]
    public void PermissionRepairRequestAcceptsOnlyRecognizedFilesAndConfigurationDirectory()
    {
        var plan = new NutPermissionRepairPlan("C:\\NUT\\etc", "TEST\\user", "S-1-5-21-123", ["C:\\NUT\\etc", "C:\\NUT\\etc\\upsd.users"], EffectiveIdentitySids: ["S-1-5-21-123"]);
        var request = new NutAdministrativeActionRequest(Guid.NewGuid(), NutAdministrativeAction.RepairConfigurationPermissions, "C:\\NUT", "C:\\NUT\\etc", PermissionRepairPlan: plan);

        Assert.True(WindowsNutAdministrativeRequestValidator.IsValid(request));
    }

    [Fact]
    public void EventLogDiagnosticIsDistinctFromAnEmptySuccessfulList()
    {
        var noEvents = new NutWindowsAdministrationSnapshot(true, PrivilegeState.StandardUser, Array.Empty<NutServiceInfo>(), NutPermissionAssessment.Unsupported(), Array.Empty<NutProcessInfo>(), Array.Empty<NutEventLogEntry>());
        var denied = noEvents with { EventLogStatus = NutEventLogStatus.AccessDenied, EventLogDiagnosticMessage = "Acesso negado" };

        Assert.Equal(NutEventLogStatus.Success, noEvents.EventLogStatus);
        Assert.Empty(noEvents.Events);
        Assert.Equal(NutEventLogStatus.AccessDenied, denied.EventLogStatus);
        Assert.Equal("Acesso negado", denied.EventLogDiagnosticMessage);
    }

    [Fact]
    public async Task StandardUserUsesTheElevationBrokerForAConfirmedAction()
    {
        var backend = new FakeBackend();
        var broker = new FakeBroker(PrivilegeState.StandardUser);
        var administration = new WindowsLocalNutAdministration(backend, broker, new FakeRevalidator());
        var request = new NutAdministrativeActionRequest(Guid.NewGuid(), NutAdministrativeAction.StartService, "C:\\NUT", "C:\\NUT\\etc", "NetworkUpsTools");

        var result = await administration.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(NutAdministrativeActionStatus.ElevationCancelled, result.Status);
        Assert.Equal(1, broker.ExecuteCalls);
        Assert.Equal(0, backend.ExecuteCalls);
    }

    [Fact]
    public async Task ElevatedProcessUsesTheValidatedBackendWithoutASecondElevation()
    {
        var backend = new FakeBackend();
        var broker = new FakeBroker(PrivilegeState.Elevated);
        var administration = new WindowsLocalNutAdministration(backend, broker, new FakeRevalidator());
        var request = new NutAdministrativeActionRequest(Guid.NewGuid(), NutAdministrativeAction.StopService, "C:\\NUT", "C:\\NUT\\etc", "NetworkUpsTools");

        var result = await administration.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(NutAdministrativeActionStatus.Success, result.Status);
        Assert.Equal(0, broker.ExecuteCalls);
        Assert.Equal(1, backend.ExecuteCalls);
    }

    [Fact]
    public async Task ElevatedProcessRejectsAnInstallationThatFailsFinalRedetection()
    {
        var backend = new FakeBackend();
        var broker = new FakeBroker(PrivilegeState.Elevated);
        var revalidator = new FakeRevalidator { IsDetected = false };
        var administration = new WindowsLocalNutAdministration(backend, broker, revalidator);
        var request = new NutAdministrativeActionRequest(Guid.NewGuid(), NutAdministrativeAction.RestartService, "C:\\NUT", "C:\\NUT\\etc", "NetworkUpsTools");

        var result = await administration.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(NutAdministrativeActionStatus.InvalidRequest, result.Status);
        Assert.Equal(0, backend.ExecuteCalls);
        Assert.Equal(0, broker.ExecuteCalls);
    }

    [Fact]
    public void AclTransactionRestoresTheDistinctOriginalDescriptorAfterSecondWriteFailure()
    {
        var accessor = new FakeAclAccessor { FailCandidateWriteAt = 2 };
        var targets = CreateAclTargets();

        var result = WindowsAclRepairTransaction.Apply(accessor, targets, "S-1-user", new HashSet<string>(["S-1-user"]), NutAdministrativeAction.RepairConfigurationPermissions);

        Assert.Equal(NutAdministrativeActionStatus.Failed, result.Status);
        Assert.Equal("A0", accessor.Current["A"].Value);
        Assert.Equal("B0", accessor.Current["B"].Value);
        Assert.DoesNotContain("modify:S-1-user", accessor.Current["A"].Value);
    }

    [Fact]
    public void AclTransactionDoesNotLeaveTheFirstTargetChangedWhenItsWriteFails()
    {
        var accessor = new FakeAclAccessor { FailCandidateWriteAt = 1 };

        var result = WindowsAclRepairTransaction.Apply(accessor, CreateAclTargets(), "S-1-user", new HashSet<string>(["S-1-user"]), NutAdministrativeAction.RepairConfigurationPermissions);

        Assert.Equal(NutAdministrativeActionStatus.Failed, result.Status);
        Assert.Equal("A0", accessor.Current["A"].Value);
    }

    [Fact]
    public void AclTransactionRollsBackAfterUnauthorizedWriteFailure()
    {
        var accessor = new FakeAclAccessor { FailCandidateWriteAt = 2, ThrowUnauthorized = true };

        var result = WindowsAclRepairTransaction.Apply(accessor, CreateAclTargets(), "S-1-user", new HashSet<string>(["S-1-user"]), NutAdministrativeAction.RepairConfigurationPermissions);

        Assert.Equal(NutAdministrativeActionStatus.AccessDenied, result.Status);
        Assert.Equal("A0", accessor.Current["A"].Value);
    }

    [Fact]
    public void AclTransactionReportsManualInterventionWhenRollbackFails()
    {
        var accessor = new FakeAclAccessor { FailCandidateWriteAt = 2, FailRollbackForA = true };

        var result = WindowsAclRepairTransaction.Apply(accessor, CreateAclTargets(), "S-1-user", new HashSet<string>(["S-1-user"]), NutAdministrativeAction.RepairConfigurationPermissions);

        Assert.Equal(NutAdministrativeActionStatus.ManualInterventionRequired, result.Status);
    }

    [Fact]
    public async Task InspectionIsReadOnlyAndReturnsTheBackendSnapshot()
    {
        var backend = new FakeBackend();
        var administration = new WindowsLocalNutAdministration(backend, new FakeBroker(PrivilegeState.Elevated), new FakeRevalidator());
        var installation = new NutInstallationInfo(true, "C:\\NUT", "C:\\NUT\\etc", null, new Dictionary<string, string>(), Array.Empty<NutConfigurationFileInfo>(), "test");

        var snapshot = await administration.InspectAsync(installation, CancellationToken.None);

        Assert.Single(snapshot.Services);
        Assert.Equal(1, backend.InspectCalls);
        Assert.Equal(0, backend.ExecuteCalls);
    }

    private sealed class FakeBackend : IWindowsNutAdministrationBackend
    {
        public int InspectCalls { get; private set; }
        public int ExecuteCalls { get; private set; }
        public Task<NutWindowsAdministrationSnapshot> InspectAsync(NutInstallationInfo installation, CancellationToken cancellationToken)
        {
            InspectCalls++;
            return Task.FromResult(new NutWindowsAdministrationSnapshot(true, PrivilegeState.StandardUser, [new NutServiceInfo("NetworkUpsTools", "Network UPS Tools", NutServiceState.Running, NutServiceStartMode.Automatic, "C:\\NUT\\bin\\upsd.exe", NutAssociationConfidence.BinaryPath)], new NutPermissionAssessment(NutPermissionState.Modifiable, "TEST\\user", "S-1-5-21-123", false, null, ["C:\\NUT\\etc"]), Array.Empty<NutProcessInfo>(), Array.Empty<NutEventLogEntry>()));
        }
        public Task<NutAdministrativeActionResult> ExecuteAsync(NutAdministrativeActionRequest request, CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            return Task.FromResult(new NutAdministrativeActionResult(NutAdministrativeActionStatus.Success, request.Action, "ok", request.ServiceName));
        }
    }

    private sealed class FakeBroker(PrivilegeState state) : IWindowsPrivilegeElevationBroker
    {
        public int ExecuteCalls { get; private set; }
        public PrivilegeState GetPrivilegeState() => state;
        public Task<NutAdministrativeActionResult> ExecuteElevatedAsync(NutAdministrativeActionRequest request, CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            return Task.FromResult(new NutAdministrativeActionResult(NutAdministrativeActionStatus.ElevationCancelled, request.Action, "cancelled", request.ServiceName));
        }
    }

    private sealed class FakeRevalidator : IWindowsNutInstallationRevalidator
    {
        public bool IsDetected { get; set; } = true;

        public Task<NutInstallationInfo> InspectAsync(string installationDirectory, CancellationToken cancellationToken) =>
            Task.FromResult(IsDetected
                ? new NutInstallationInfo(true, "C:\\NUT", "C:\\NUT\\etc", null, new Dictionary<string, string>(), Array.Empty<NutConfigurationFileInfo>(), "test")
                : NutInstallationInfo.NotDetected());
    }

    private static IReadOnlyList<WindowsAclTarget> CreateAclTargets() => [new WindowsAclTarget("A", true), new WindowsAclTarget("B", false)];

    private sealed class FakeAclAccessor : IWindowsAclAccessor
    {
        private int _candidateWriteCount;

        public Dictionary<string, FakeSecurity> Current { get; } = new(StringComparer.Ordinal)
        {
            ["A"] = new("A0"),
            ["B"] = new("B0")
        };

        public int? FailCandidateWriteAt { get; init; }

        public bool ThrowUnauthorized { get; init; }

        public bool FailRollbackForA { get; init; }

        public object CaptureSecurity(WindowsAclTarget target) => Current[target.Path];

        public object CloneSecurity(object security, bool isDirectory) => ((FakeSecurity)security).Clone();

        public IReadOnlyList<WindowsAclRule> GetRules(object security) => ((FakeSecurity)security).Rules;

        public void AddModify(object candidateSecurity, string userSid, bool isDirectory) => ((FakeSecurity)candidateSecurity).Value += $"|modify:{userSid}";

        public void WriteSecurity(WindowsAclTarget target, object security)
        {
            var candidate = (FakeSecurity)security;
            if (candidate.Value.Contains("modify:", StringComparison.Ordinal))
            {
                _candidateWriteCount++;
                if (_candidateWriteCount == FailCandidateWriteAt)
                {
                    if (ThrowUnauthorized) throw new UnauthorizedAccessException();
                    throw new IOException();
                }
            }
            else if (FailRollbackForA && target.Path == "A")
            {
                throw new IOException();
            }

            Current[target.Path] = candidate;
        }
    }

    private sealed class FakeSecurity(string value)
    {
        public string Value { get; set; } = value;

        public IReadOnlyList<WindowsAclRule> Rules { get; } = Array.Empty<WindowsAclRule>();

        public FakeSecurity Clone() => new(Value);
    }
}
