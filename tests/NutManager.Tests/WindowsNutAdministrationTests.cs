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

    [Fact]
    public void HelperResponsePathIsDerivedFromTheRequestPathOnly()
    {
        Assert.Equal("C:\\Users\\test\\AppData\\Local\\NutManager\\AdminRequests\\abc.response.json", WindowsPrivilegeElevationBroker.GetResponsePath("C:\\Users\\test\\AppData\\Local\\NutManager\\AdminRequests\\abc.request.json"));
        Assert.Throws<ArgumentException>(() => WindowsPrivilegeElevationBroker.GetResponsePath("C:\\temp\\arbitrary.json"));
    }

    [Fact]
    public async Task InvalidRequestIsRejectedBeforeAnyElevationOrBackendAction()
    {
        var backend = new FakeBackend();
        var broker = new FakeBroker(PrivilegeState.StandardUser);
        var administration = new WindowsLocalNutAdministration(backend, broker);
        var request = new NutAdministrativeActionRequest(Guid.Empty, NutAdministrativeAction.StartService, "C:\\NUT", "C:\\NUT\\etc", "NetworkUpsTools");

        var result = await administration.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(NutAdministrativeActionStatus.InvalidRequest, result.Status);
        Assert.Equal(0, broker.ExecuteCalls);
        Assert.Equal(0, backend.ExecuteCalls);
    }

    [Fact]
    public void PermissionRepairRequestOnlyAcceptsModifyForTheExplicitUserSid()
    {
        var plan = new NutPermissionRepairPlan("C:\\NUT\\etc", "TEST\\user", "S-1-5-21-123", ["C:\\NUT\\etc", "C:\\NUT\\etc\\ups.conf"]);
        var request = new NutAdministrativeActionRequest(Guid.NewGuid(), NutAdministrativeAction.RepairConfigurationPermissions, "C:\\NUT", "C:\\NUT\\etc", PermissionRepairPlan: plan);

        Assert.True(WindowsNutAdministrativeRequestValidator.IsValid(request));
        Assert.False(WindowsNutAdministrativeRequestValidator.IsValid(request with { PermissionRepairPlan = plan with { Right = "FullControl" } }));
        Assert.False(WindowsNutAdministrativeRequestValidator.IsValid(request with { PermissionRepairPlan = plan with { AffectedPaths = ["C:\\Other\\ups.conf"] } }));
    }

    [Fact]
    public async Task StandardUserUsesTheElevationBrokerForAConfirmedAction()
    {
        var backend = new FakeBackend();
        var broker = new FakeBroker(PrivilegeState.StandardUser);
        var administration = new WindowsLocalNutAdministration(backend, broker);
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
        var administration = new WindowsLocalNutAdministration(backend, broker);
        var request = new NutAdministrativeActionRequest(Guid.NewGuid(), NutAdministrativeAction.StopService, "C:\\NUT", "C:\\NUT\\etc", "NetworkUpsTools");

        var result = await administration.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(NutAdministrativeActionStatus.Success, result.Status);
        Assert.Equal(0, broker.ExecuteCalls);
        Assert.Equal(1, backend.ExecuteCalls);
    }

    [Fact]
    public async Task InspectionIsReadOnlyAndReturnsTheBackendSnapshot()
    {
        var backend = new FakeBackend();
        var administration = new WindowsLocalNutAdministration(backend, new FakeBroker(PrivilegeState.Elevated));
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
}
