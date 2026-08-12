using NutManager.Core.Administration;
using NutManager.Infrastructure.Platform.Windows;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// Windows NUT service discovery and its presentation mapping. Everything here works on the pure
/// association/state functions and on constructed snapshots: no Service Control Manager, no
/// installed NUT and no elevation is required.
/// </summary>
public sealed class WindowsNutServiceDiscoveryTests
{
    private const string InstallationDirectory = "C:\\NUT";

    [Theory]
    [InlineData("Network UPS Tools", "Network UPS Tools")]
    [InlineData("NetworkUpsTools", "Network UPS Tools")]
    [InlineData("NUT", "Network UPS Tools")]
    [InlineData("network ups tools", "network ups tools")]
    public void KnownServiceIdentityIsRecognizedWithoutAResolvableBinary(string serviceName, string displayName)
    {
        var (binaryPath, confidence) = WindowsNutServiceAssociation.Determine(serviceName, displayName, null, InstallationDirectory);

        Assert.Null(binaryPath);
        Assert.Equal(NutAssociationConfidence.NameFallback, confidence);
    }

    [Fact]
    public void KnownIdentityIsReportedWhenThereIsNoInstallationToVerifyAgainst()
    {
        // Regression: with no detected installation the service was dropped entirely, so a running
        // NUT service looked absent. It is now reported by identity with the weaker confidence.
        var (binaryPath, confidence) = WindowsNutServiceAssociation.Determine(
            "Network UPS Tools", "Network UPS Tools", "\"C:\\Program Files\\NUT\\sbin\\nutsrv.exe\" -service", string.Empty);

        Assert.Equal("C:\\Program Files\\NUT\\sbin\\nutsrv.exe", binaryPath);
        Assert.Equal(NutAssociationConfidence.NameFallback, confidence);
        Assert.True(new NutServiceInfo("Network UPS Tools", "Network UPS Tools", NutServiceState.Running,
            NutServiceStartMode.Automatic, binaryPath, confidence).IsAssociated);
    }

    [Fact]
    public void ContainmentStaysAuthoritativeWhenAnInstallationIsKnown()
    {
        // Anti name-squatting: a service borrowing a known NUT name but pointing outside the
        // detected installation must not be associated at all.
        var (_, confidence) = WindowsNutServiceAssociation.Determine(
            "Network UPS Tools", "Network UPS Tools", "\"C:\\NUT-malicious\\bin\\nut.exe\" --service", InstallationDirectory);

        Assert.Equal(NutAssociationConfidence.None, confidence);
    }

    [Fact]
    public void VerifiedInstallationBinaryStillProducesTheStrongestConfidence()
    {
        var (binaryPath, confidence) = WindowsNutServiceAssociation.Determine(
            "Network UPS Tools", "Network UPS Tools", "\"C:\\NUT\\bin\\upsd.exe\"", InstallationDirectory);

        Assert.Equal("C:\\NUT\\bin\\upsd.exe", binaryPath);
        Assert.Equal(NutAssociationConfidence.BinaryPath, confidence);
    }

    [Fact]
    public void UnrelatedServicesAreNotAssociatedEvenWhenTheyMentionUps()
    {
        var (_, named) = WindowsNutServiceAssociation.Determine("DonutService", "Nutcracker updater", null, InstallationDirectory);
        var (_, upsish) = WindowsNutServiceAssociation.Determine("ContosoUpsAgent", "Contoso UPS Agent", null, InstallationDirectory);
        var (_, foreign) = WindowsNutServiceAssociation.Determine("Other", "Other", "\"C:\\Other\\svc.exe\"", InstallationDirectory);

        Assert.Equal(NutAssociationConfidence.None, named);
        Assert.Equal(NutAssociationConfidence.None, upsish);
        Assert.Equal(NutAssociationConfidence.None, foreign);
    }

    [Theory]
    [InlineData("Network UPS Tools", "Network UPS Tools")]
    [InlineData("network ups tools", "irrelevant")]
    [InlineData("irrelevant", "Network UPS Tools")]
    [InlineData("NUT", "irrelevant")]
    public void ExactKnownIdentityLookupMatchesCaseInsensitively(string serviceName, string displayName) =>
        Assert.True(WindowsNutServiceAssociation.IsKnownIdentity(serviceName, displayName));

    [Theory]
    [InlineData("DonutService", "Nutcracker updater")]
    [InlineData("ContosoUpsAgent", "Contoso UPS Agent")]
    [InlineData("Network UPS Tools Helper", "Network UPS Tools Helper")]
    public void UnrelatedIdentitiesAreNotMatchedBySubstring(string serviceName, string displayName) =>
        Assert.False(WindowsNutServiceAssociation.IsKnownIdentity(serviceName, displayName));

    [Theory]
    // The real installation: plain path, quoted path, and quoted path followed by arguments.
    [InlineData(@"C:\NUT\bin\nut.exe")]
    [InlineData(@"""C:\NUT\bin\nut.exe""")]
    [InlineData(@"""C:\NUT\bin\nut.exe"" -service")]
    public void RealInstallationServiceIsTrustedRegardlessOfHowThePathNameIsQuoted(string imagePath)
    {
        var (binaryPath, confidence) = WindowsNutServiceAssociation.Determine(
            "Network UPS Tools", "Network UPS Tools", imagePath, InstallationDirectory);

        Assert.Equal(@"C:\NUT\bin\nut.exe", binaryPath);
        Assert.Equal(NutAssociationConfidence.BinaryPath, confidence);
    }

    [Fact]
    public void ExecutablePathWithSpacesSurvivesQuotedParsing()
    {
        Assert.Equal(@"C:\Program Files\NUT\bin\nut.exe",
            WindowsNutServiceAssociation.TryExtractExecutablePath(@"""C:\Program Files\NUT\bin\nut.exe"" --service"));
    }

    [Fact]
    public void DiscoveryTraceCarriesTheStageWithoutLeakingTheServiceList()
    {
        var trace = new NutServiceDiscoveryTrace(true, true, 187, false, null, null, null, null,
            @"C:\NUT", null, NutAssociationConfidence.None, "reason");

        Assert.True(trace.PlatformSupported);
        Assert.Equal(187, trace.EnumeratedServiceCount);
        Assert.Equal(@"C:\NUT", trace.InstallationRoot);
        Assert.Null(trace.CandidateServiceName);
    }

    [Fact]
    public void PlatformUnsupportedRemainsDistinctFromAnUndeterminedAssessment()
    {
        var unsupported = NutPermissionAssessment.Unsupported();
        var undetermined = NutPermissionAssessment.NotDetermined("No local NUT installation is selected.");

        Assert.NotEqual(unsupported.Message, undetermined.Message);
        Assert.Equal("No local NUT installation is selected.", undetermined.Message);
    }

    [Fact]
    public void DiscoveryStatusSeparatesNotFoundFromAccessAndQueryFailures()
    {
        var notFound = new NutServiceDiscoveryResult([], NutServiceDiscoveryStatus.Completed);
        var denied = new NutServiceDiscoveryResult([], NutServiceDiscoveryStatus.AccessDenied, "denied");
        var failed = new NutServiceDiscoveryResult([], NutServiceDiscoveryStatus.QueryFailed, "failed");

        Assert.Empty(notFound.Services);
        Assert.Equal(NutServiceDiscoveryStatus.Completed, notFound.Status);
        Assert.NotEqual(notFound.Status, denied.Status);
        Assert.NotEqual(denied.Status, failed.Status);
    }

    [Fact]
    public void SnapshotDefaultsToCompletedDiscoveryAndUnsupportedReportsThePlatform()
    {
        var snapshot = new NutWindowsAdministrationSnapshot(true, PrivilegeState.StandardUser, [],
            NutPermissionAssessment.NotDetermined("x"), [], []);

        Assert.Equal(NutServiceDiscoveryStatus.Completed, snapshot.ServiceDiscoveryStatus);
        Assert.Equal(PrivilegeState.PlatformUnsupported, NutWindowsAdministrationSnapshot.Unsupported().PrivilegeState);
    }

    [Theory]
    [InlineData(NutServiceState.Running, NutAssociationConfidence.BinaryPath, false, true, true)]
    [InlineData(NutServiceState.Stopped, NutAssociationConfidence.BinaryPath, true, false, true)]
    // A service recognised only by name cannot be mutated, so no action is offered.
    [InlineData(NutServiceState.Running, NutAssociationConfidence.NameFallback, false, false, false)]
    [InlineData(NutServiceState.Stopped, NutAssociationConfidence.NameFallback, false, false, false)]
    public void ServiceCommandsFollowStateAndControllability(
        NutServiceState state,
        NutAssociationConfidence confidence,
        bool canStart,
        bool canStop,
        bool canRestart)
    {
        var viewModel = new AdministrationPageViewModelHarness(
            new NutServiceInfo("Network UPS Tools", "Network UPS Tools", state, NutServiceStartMode.Automatic, "C:\\NUT\\bin\\upsd.exe", confidence));

        Assert.Equal(canStart, viewModel.WouldAllowStart);
        Assert.Equal(canStop, viewModel.WouldAllowStop);
        Assert.Equal(canRestart, viewModel.WouldAllowRestart);
    }

    /// <summary>
    /// Mirrors the view-model gating rules over a constructed service so the expectations stay
    /// verifiable without a live administration capability.
    /// </summary>
    private sealed class AdministrationPageViewModelHarness(NutServiceInfo service)
    {
        private bool IsControllable => service.AssociationConfidence == NutAssociationConfidence.BinaryPath;

        public bool WouldAllowStart => IsControllable &&
            service is { State: NutServiceState.Stopped, StartMode: not NutServiceStartMode.Disabled };

        public bool WouldAllowStop => IsControllable && service.State == NutServiceState.Running;

        public bool WouldAllowRestart => IsControllable &&
            service.StartMode != NutServiceStartMode.Disabled &&
            service.State is NutServiceState.Running or NutServiceState.Stopped;
    }
}
