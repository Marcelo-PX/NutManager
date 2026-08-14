using System.Text.Json;
using NutManager.App.Services;
using NutManager.App.ViewModels;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Persistence;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// Which NUT files a profile manages. The distinction the tests keep pinning is that this is
/// intent: enabling a file does not make it exist, detection does not silently change the profile,
/// and disabling one touches nothing on disk.
/// </summary>
public sealed class ManagedNutFileSelectionTests
{
    private static readonly NutConfigurationFileKind[] Five =
    [
        NutConfigurationFileKind.NutConf,
        NutConfigurationFileKind.UpsConf,
        NutConfigurationFileKind.UpsdConf,
        NutConfigurationFileKind.UpsdUsers,
        NutConfigurationFileKind.UpsmonConf
    ];

    private static ManagedNutServerProfile Profile(ManagedNutConfigurationFiles? files = null) => new(
        Guid.NewGuid(),
        "Server",
        new NutMonitoringProfile("monitor.example"),
        new NutManagementProfile(NutManagementMode.Local, managedFiles: files),
        ManagedNutServerAccessMode.Manage);

    // ==================== Model ====================

    [Fact]
    public void ANewProfileManagesEverySupportedFile()
    {
        var profile = Profile();

        Assert.True(profile.Management.ManagedFiles.IsAll);
        Assert.Equal(Five, profile.Management.ManagedFiles.Kinds);
    }

    [Fact]
    public void TheSupportedSetIsExactlyTheFilesWithEditors()
    {
        Assert.Equal(Five, ManagedNutConfigurationFiles.SupportedKinds);
        Assert.Equal(
            ["nut.conf", "ups.conf", "upsd.conf", "upsd.users", "upsmon.conf"],
            ManagedNutConfigurationFiles.SupportedKinds.Select(ManagedNutConfigurationFiles.FileNameFor));
    }

    [Fact]
    public void AnUnsupportedKindIsRejectedRatherThanQuietlyDropped()
    {
        // Dropping it would hide a real mistake in a caller or in a hand-edited document.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ManagedNutConfigurationFiles.Create([(NutConfigurationFileKind)99]));
    }

    [Fact]
    public void DuplicatesCollapseAndOrderIsCanonicalWhateverTheCallerPassed()
    {
        var scrambled = ManagedNutConfigurationFiles.Create(
        [
            NutConfigurationFileKind.UpsmonConf,
            NutConfigurationFileKind.NutConf,
            NutConfigurationFileKind.UpsmonConf
        ]);

        Assert.Equal([NutConfigurationFileKind.NutConf, NutConfigurationFileKind.UpsmonConf], scrambled.Kinds);
        Assert.Equal(2, scrambled.Count);
        // Two callers listing the same files in different orders produce the same value.
        Assert.Equal(
            ManagedNutConfigurationFiles.Create([NutConfigurationFileKind.NutConf, NutConfigurationFileKind.UpsmonConf]),
            scrambled);
    }

    [Fact]
    public void AnEmptySelectionIsRepresentableRatherThanForbidden()
    {
        // A remote profile used only for monitoring is legitimate, and Administration already has an
        // empty-list path, so the model does not invent a "at least one" rule.
        var none = ManagedNutConfigurationFiles.Create([]);

        Assert.True(none.IsEmpty);
        Assert.Equal(0, none.Count);
        Assert.Equal("(none)", none.ToString());
    }

    [Fact]
    public void TogglingProducesANewValueAndLeavesTheOriginalAlone()
    {
        var all = ManagedNutConfigurationFiles.All;

        var without = all.With(NutConfigurationFileKind.UpsdUsers, enabled: false);

        Assert.False(without.Contains(NutConfigurationFileKind.UpsdUsers));
        Assert.True(all.Contains(NutConfigurationFileKind.UpsdUsers));
        Assert.Same(all, all.With(NutConfigurationFileKind.UpsdUsers, enabled: true));
    }

    [Theory]
    [InlineData("upsd.users", NutConfigurationFileKind.UpsdUsers)]
    [InlineData("UPSMON.CONF", NutConfigurationFileKind.UpsmonConf)]
    public void AKnownFileNameResolvesToItsKind(string fileName, NutConfigurationFileKind expected)
    {
        Assert.True(ManagedNutConfigurationFiles.TryParseFileName(fileName, out var kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData("upssched.conf")]
    [InlineData("upsset.conf")]
    [InlineData("hosts.conf")]
    [InlineData("ups.conf.sample")]
    [InlineData("nut.conf.bak")]
    [InlineData(null)]
    public void AnythingOutsideTheClosedSetIsNotAConfigurationFile(string? fileName)
    {
        Assert.False(ManagedNutConfigurationFiles.TryParseFileName(fileName, out _));
    }

    // ==================== Persistence and migration ====================

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nutmanager-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task TheSelectionSurvivesARoundTripThroughTheStore()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonManagedNutServerProfileStore(directory.Path);
        var selection = ManagedNutConfigurationFiles.Create(
            [NutConfigurationFileKind.NutConf, NutConfigurationFileKind.UpsmonConf]);
        var profile = Profile(selection);

        await store.SaveAsync(
            new ManagedNutServerProfiles(ManagedNutServerProfiles.CurrentSchemaVersion, profile.Id, [profile]),
            CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(selection, loaded!.ActiveProfile!.Management.ManagedFiles);
    }

    [Fact]
    public async Task AProfileSavedBeforeThisSettingExistedManagesEveryFile()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "managed-servers.json");
        var id = Guid.NewGuid();

        // A schema 4 document: no managedFiles anywhere.
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 4,
            activeProfileId = id,
            profiles = new[]
            {
                new
                {
                    id,
                    name = "Legacy",
                    monitoringHost = "monitor.example",
                    monitoringPort = 3493,
                    managementMode = 0,
                    accessMode = 0
                }
            }
        }));

        var loaded = await new JsonManagedNutServerProfileStore(directory.Path).LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.True(loaded!.ActiveProfile!.Management.ManagedFiles.IsAll);
    }

    // ==================== Draft ====================

    [Fact]
    public void TheDraftExposesOneTogglePerFileInTheCanonicalOrder()
    {
        var draft = new ManagedNutServerProfileDraftViewModel(Profile());

        Assert.Equal(
            ["nut.conf", "ups.conf", "upsd.conf", "upsd.users", "upsmon.conf"],
            draft.ManagedFileToggles.Select(toggle => toggle.FileName));
        Assert.All(draft.ManagedFileToggles, toggle => Assert.True(toggle.IsEnabled));
    }

    [Fact]
    public void TogglingAFileMakesTheDraftDifferFromItsProfile()
    {
        var profile = Profile();
        var draft = new ManagedNutServerProfileDraftViewModel(profile);
        Assert.True(draft.Matches(profile));

        draft.ManagedFileToggles.Single(toggle => toggle.Kind == NutConfigurationFileKind.UpsdUsers).IsEnabled = false;

        Assert.False(draft.Matches(profile));
        Assert.False(draft.ManagedFiles.Contains(NutConfigurationFileKind.UpsdUsers));
        Assert.Equal(4, draft.ManagedFiles.Count);
    }

    [Fact]
    public void ReapplyingTheProfileRestoresTheToggles()
    {
        var profile = Profile();
        var draft = new ManagedNutServerProfileDraftViewModel(profile);
        draft.ManagedFileToggles[0].IsEnabled = false;

        draft.Apply(profile);

        Assert.True(draft.Matches(profile));
        Assert.All(draft.ManagedFileToggles, toggle => Assert.True(toggle.IsEnabled));
    }

    [Fact]
    public void ClearingEveryToggleIsAllowedAndAnnounced()
    {
        var draft = new ManagedNutServerProfileDraftViewModel(Profile());

        foreach (var toggle in draft.ManagedFileToggles)
        {
            toggle.IsEnabled = false;
        }

        Assert.True(draft.HasNoManagedFiles);
        Assert.True(draft.Validate([]).CanSave);
    }

    [Fact]
    public void TheSelectionReachesTheMaterializedProfile()
    {
        var draft = new ManagedNutServerProfileDraftViewModel(Profile());
        draft.ManagedFileToggles.Single(toggle => toggle.Kind == NutConfigurationFileKind.UpsdConf).IsEnabled = false;

        var result = draft.Validate([]);

        Assert.True(result.CanSave);
        Assert.False(result.Profile!.Management.ManagedFiles.Contains(NutConfigurationFileKind.UpsdConf));
        Assert.Equal(4, result.Profile.Management.ManagedFiles.Count);
    }

    // ==================== Detection ====================

    private sealed class FakeInstallationDetector : ILocalNutInstallationDetector
    {
        private readonly NutInstallationInfo _info;

        public FakeInstallationDetector(params string[] present) =>
            _info = new NutInstallationInfo(
                true, "/nut", "/nut/etc", "2.8.5", new Dictionary<string, string>(),
                [.. new[] { "nut.conf", "ups.conf", "upsd.conf", "upsd.users", "upsmon.conf", "upssched.conf", "ups.conf.sample" }
                    .Select(name => new NutConfigurationFileInfo(name, "/nut/etc/" + name, present.Contains(name), true))],
                "Test");

        public Task<NutInstallationInfo> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_info);

        public Task<NutInstallationInfo> InspectDirectoryAsync(string directory, CancellationToken cancellationToken = default) =>
            Task.FromResult(_info);
    }

    [Fact]
    public async Task LocalDetectionReportsEveryPresentSupportedFile()
    {
        var detector = new LocalNutManagedFileDetector(
            new FakeInstallationDetector("nut.conf", "ups.conf", "upsd.conf", "upsd.users", "upsmon.conf"));

        var result = await detector.DetectAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(Five, result.Found);
    }

    [Fact]
    public async Task LocalDetectionReportsOnlyTheSubsetThatExists()
    {
        var detector = new LocalNutManagedFileDetector(new FakeInstallationDetector("nut.conf", "upsmon.conf"));

        var result = await detector.DetectAsync();

        Assert.Equal([NutConfigurationFileKind.NutConf, NutConfigurationFileKind.UpsmonConf], result.Found);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task LocalDetectionIgnoresFilesOutsideTheClosedSet()
    {
        // upssched.conf and the .sample files are present but are not supported configuration files.
        var detector = new LocalNutManagedFileDetector(
            new FakeInstallationDetector("upssched.conf", "ups.conf.sample"));

        var result = await detector.DetectAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Found);
    }

    [Fact]
    public async Task ADetectionThatFindsNothingIsStillASuccessfulAnswer()
    {
        var result = await new LocalNutManagedFileDetector(new FakeInstallationDetector()).DetectAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Count);
        Assert.True(result.ToManagedFiles().IsEmpty);
    }

    [Fact]
    public async Task RemoteDetectionReadsWhatDirectoryValidationAlreadyEstablished()
    {
        var validation = new RemoteNutDirectoryValidationResult(
            RemoteNutTransportStatus.Success, "/etc/nut", ["nut.conf", "upsd.users"]);
        var detector = new RemoteNutManagedFileDetector(() => validation);

        Assert.True(detector.CanDetect);
        var result = await detector.DetectAsync();

        Assert.Equal([NutConfigurationFileKind.NutConf, NutConfigurationFileKind.UpsdUsers], result.Found);
    }

    [Fact]
    public async Task RemoteDetectionIsUnavailableWithoutAValidatedDirectory()
    {
        var detector = new RemoteNutManagedFileDetector(() => null);

        Assert.False(detector.CanDetect);
        var result = await detector.DetectAsync();

        // It reports that it cannot look, rather than connecting on the administrator's behalf.
        Assert.Equal(NutManagedFileDetectionStatus.Unavailable, result.Status);
        Assert.Empty(result.Found);
    }

    [Fact]
    public async Task ACancelledDetectionIsNotReportedAsAFailure()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await new LocalNutManagedFileDetector(new FakeInstallationDetector()).DetectAsync(cancellation.Token);

        Assert.Equal(NutManagedFileDetectionStatus.Cancelled, result.Status);
    }

    [Fact]
    public void DetectionProducesAProposalRatherThanChangingAnything()
    {
        var result = NutManagedFileDetectionResult.Success([NutConfigurationFileKind.UpsConf]);

        // The detector hands back a value; applying it to a profile is a separate, explicit step.
        Assert.Equal(ManagedNutConfigurationFiles.Create([NutConfigurationFileKind.UpsConf]), result.ToManagedFiles());
        Assert.Equal(
            [NutConfigurationFileKind.UpsConf],
            NutManagedFileDetectionResult.Success(
                [NutConfigurationFileKind.UpsConf, NutConfigurationFileKind.UpsConf]).Found);
    }
}
