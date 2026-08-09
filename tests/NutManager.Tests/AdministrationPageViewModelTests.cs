using System.Security.Cryptography;
using System.Text;
using NutManager.App.ViewModels;
using NutManager.Core.Administration;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Configuration;
using Xunit;

namespace NutManager.Tests;

public sealed class AdministrationPageViewModelTests
{
    [Fact]
    public async Task InitializesInstallationAndPresentsTheFiveFilesInProductOrder()
    {
        var detector = new TestInstallationDetector(CreateInstallation("/session/nut", "/session/nut/etc", "nut.conf"));
        var pipeline = new TestPipeline();
        var viewModel = new AdministrationPageViewModel(detector, pipeline);

        await viewModel.InitializeAsync();

        Assert.Equal("Instalação NUT encontrada", viewModel.InstallationStatusText);
        Assert.Equal("/session/nut", viewModel.InstallationDirectoryText);
        Assert.Equal("/session/nut/etc", viewModel.ConfigurationDirectoryText);
        Assert.Equal(
            ["Geral", "UPS e drivers", "Servidor", "Usuários", "Monitoramento"],
            viewModel.ConfigurationFiles.Select(file => file.Category));
        Assert.Equal(
            ["nut.conf", "ups.conf", "upsd.conf", "upsd.users", "upsmon.conf"],
            viewModel.ConfigurationFiles.Select(file => file.FileName));
        Assert.Equal("Disponível", viewModel.ConfigurationFiles[0].StatusText);
        Assert.All(viewModel.ConfigurationFiles.Skip(1), file => Assert.Equal("Ausente", file.StatusText));

        await viewModel.InspectInstallationDirectoryAsync("/manual/partial");

        Assert.Equal("/manual/partial", detector.LastManualDirectory);
        Assert.Equal("Instalação NUT encontrada", viewModel.InstallationStatusText);
    }

    [Fact]
    public async Task MissingFileCannotBeLoaded()
    {
        var pipeline = new TestPipeline();
        var viewModel = await CreateInitializedViewModelAsync(pipeline, "nut.conf");
        var missing = viewModel.ConfigurationFiles.Single(file => file.FileName == "upsd.users");

        await viewModel.SelectFileAsync(missing);

        Assert.False(viewModel.HasLoadedFile);
        Assert.Equal(0, pipeline.LoadCalls);
        Assert.Equal("O arquivo não existe neste diretório.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LoadsSectionsAndRepeatedEntriesInOriginalOrderWithoutMakingRawContentEditable()
    {
        const string text = "# keep this\nLISTEN 127.0.0.1\nLISTEN ::1\nunknown future value\n";
        var pipeline = new TestPipeline();
        pipeline.SetFile("/session/nut/etc/upsd.conf", NutConfigurationFileKind.UpsdConf, text);
        var viewModel = await CreateInitializedViewModelAsync(pipeline, "upsd.conf");

        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == "upsd.conf"));

        var entries = viewModel.Sections.Single().Entries;
        Assert.Equal(["LISTEN", "LISTEN", "unknown"], entries.Select(entry => entry.Name));
        Assert.Equal([2, 3, 4], entries.Select(entry => entry.LineNumber));
        Assert.Contains("preservad", viewModel.Sections.Single().RawContentSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("127.0.0.1", entries[0].DraftValue);
        Assert.False(viewModel.HasDraftChanges);

        entries[0].DraftValue = "127.0.0.2";

        Assert.True(viewModel.HasDraftChanges);
        entries[0].DraftValue = "127.0.0.1";
        Assert.False(viewModel.HasDraftChanges);
        entries[0].DraftValue = "127.0.0.2";
        Assert.Equal(text, pipeline.LastLoadedDocument!.Serialize());
    }

    [Fact]
    public async Task GroupsAssignmentsByTheirOriginalSections()
    {
        var pipeline = new TestPipeline();
        pipeline.SetFile(
            "/session/nut/etc/ups.conf",
            NutConfigurationFileKind.UpsConf,
            "driverpath = /drivers\n\n[first]\n    driver = blazer_usb\n\n[second]\n    driver = nutdrv_qx\n");
        var viewModel = await CreateInitializedViewModelAsync(pipeline, "ups.conf");

        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == "ups.conf"));

        Assert.Equal(["Geral", "first", "second"], viewModel.Sections.Select(section => section.Name));
        Assert.Equal("driverpath", viewModel.Sections[0].Entries.Single().Name);
        Assert.Equal("blazer_usb", viewModel.Sections[1].Entries.Single().DraftValue);
        Assert.Equal("nutdrv_qx", viewModel.Sections[2].Entries.Single().DraftValue);
    }

    [Fact]
    public void DoesNotDependOnANutClientOrPollingCoordinator()
    {
        var constructorParameters = typeof(AdministrationPageViewModel)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(constructorParameters, type => typeof(INutClient).IsAssignableFrom(type));
        Assert.DoesNotContain(constructorParameters, type => typeof(IUpsPollingCoordinator).IsAssignableFrom(type));
    }

    [Fact]
    public async Task ReviewConfirmationAndApplyUseTheExactPreparedChangeThenReload()
    {
        var pipeline = new TestPipeline();
        pipeline.SetFile("/session/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        var viewModel = await CreateInitializedViewModelAsync(pipeline, "nut.conf");
        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == "nut.conf"));
        var mode = GetEntry(viewModel, "MODE");

        mode.DraftValue = "netserver";

        Assert.True(viewModel.CanReview);
        Assert.False(viewModel.CanApply);
        await viewModel.ReviewChangesAsync();

        Assert.True(viewModel.HasPreview);
        Assert.Single(viewModel.PreviewLines);
        Assert.Contains("netserver", viewModel.PreviewLines.Single().CandidateText);
        Assert.False(viewModel.CanApply);

        viewModel.IsPreviewConfirmed = true;
        Assert.True(viewModel.CanApply);

        await viewModel.ApplyChangesAsync();

        Assert.Equal(1, pipeline.ApplyCalls);
        Assert.Same(pipeline.LastPreparedChange, pipeline.LastAppliedChange);
        Assert.Equal("Configuração aplicada com sucesso.", viewModel.StatusMessage);
        Assert.False(viewModel.IsCriticalResult);
        Assert.Equal("/session/backup.bak", viewModel.BackupPath);
        Assert.False(viewModel.HasPreview);
        Assert.False(viewModel.HasDraftChanges);
        Assert.Equal("netserver", GetEntry(viewModel, "MODE").DraftValue);
    }

    [Fact]
    public async Task EditingAfterReviewInvalidatesPreviewAndConfirmation()
    {
        var pipeline = new TestPipeline();
        pipeline.SetFile("/session/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        var viewModel = await CreateLoadedNutConfAsync(pipeline);
        var mode = GetEntry(viewModel, "MODE");
        mode.DraftValue = "netserver";
        await viewModel.ReviewChangesAsync();
        viewModel.IsPreviewConfirmed = true;

        mode.DraftValue = "none";

        Assert.False(viewModel.HasPreview);
        Assert.False(viewModel.IsPreviewConfirmed);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public async Task DiscardIsRejectedWhileReviewIsInProgressWithoutStartingAnotherLoad()
    {
        var pipeline = new TestPipeline();
        pipeline.SetFile("/session/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        var viewModel = await CreateLoadedNutConfAsync(pipeline);
        var mode = GetEntry(viewModel, "MODE");
        mode.DraftValue = "netserver";
        var reviewLoadCompletion = new TaskCompletionSource<NutConfigurationLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reviewLoadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.NextLoadCompletion = reviewLoadCompletion;
        pipeline.LoadStarted = reviewLoadStarted;
        var loadCallsBeforeReview = pipeline.LoadCalls;

        var review = viewModel.ReviewChangesAsync();
        await reviewLoadStarted.Task;
        await viewModel.DiscardChangesAsync();

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.CanEditEntries);
        Assert.False(viewModel.CanDiscard);
        Assert.True(viewModel.HasDraftChanges);
        Assert.Equal("netserver", mode.DraftValue);
        Assert.Equal(loadCallsBeforeReview + 1, pipeline.LoadCalls);
        Assert.False(viewModel.HasPreview);

        reviewLoadCompletion.SetResult(pipeline.CreateSuccessLoadResult("/session/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n"));
        await review;

        Assert.True(viewModel.HasDraftChanges);
        Assert.True(viewModel.HasPreview);
        Assert.True(viewModel.CanEditEntries);
    }

    [Fact]
    public async Task DiscardIsRejectedWhileApplyIsInProgressWithoutStartingAReload()
    {
        var pipeline = new TestPipeline();
        pipeline.SetFile("/session/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        var viewModel = await CreateLoadedNutConfAsync(pipeline);
        var mode = GetEntry(viewModel, "MODE");
        mode.DraftValue = "netserver";
        await viewModel.ReviewChangesAsync();
        viewModel.IsPreviewConfirmed = true;
        var applyCompletion = new TaskCompletionSource<NutConfigurationApplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applyStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.NextApplyCompletion = applyCompletion;
        pipeline.ApplyStarted = applyStarted;
        var loadCallsBeforeApply = pipeline.LoadCalls;

        var apply = viewModel.ApplyChangesAsync();
        await applyStarted.Task;
        await viewModel.DiscardChangesAsync();

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.CanEditEntries);
        Assert.False(viewModel.CanDiscard);
        Assert.True(viewModel.HasDraftChanges);
        Assert.True(viewModel.HasPreview);
        Assert.Equal("netserver", mode.DraftValue);
        Assert.Equal(loadCallsBeforeApply, pipeline.LoadCalls);
        Assert.Equal(1, pipeline.ApplyCalls);

        applyCompletion.SetResult(new NutConfigurationApplyResult(NutConfigurationApplyStatus.Success, "/session/backup.bak"));
        await apply;

        Assert.False(viewModel.HasDraftChanges);
        Assert.False(viewModel.HasPreview);
        Assert.Equal("netserver", GetEntry(viewModel, "MODE").DraftValue);
        Assert.True(viewModel.CanEditEntries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingInstallationOperationFreezesEditingAndPreservesDraftCreatedDuringTheWait(bool inspectDirectory)
    {
        var installationA = CreateInstallation("/context-a/nut", "/context-a/nut/etc", "nut.conf");
        var installationB = CreateInstallation("/context-b/nut", "/context-b/nut/etc", "nut.conf");
        var detector = new TestInstallationDetector(installationA);
        var pipeline = new TestPipeline();
        pipeline.SetFile("/context-a/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        var viewModel = new AdministrationPageViewModel(detector, pipeline);
        await viewModel.InitializeAsync();
        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == "nut.conf"));
        var mode = GetEntry(viewModel, "MODE");
        var operationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operationCompletion = new TaskCompletionSource<NutInstallationInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (inspectDirectory)
        {
            detector.InspectStarted = operationStarted;
            detector.NextInspectCompletion = operationCompletion;
        }
        else
        {
            detector.DetectStarted = operationStarted;
            detector.NextDetectCompletion = operationCompletion;
        }

        var loadCallsBeforeOperation = pipeline.LoadCalls;
        var operation = inspectDirectory
            ? viewModel.InspectInstallationDirectoryAsync("/context-b/nut")
            : viewModel.RefreshInstallationAsync();
        await operationStarted.Task;

        Assert.True(viewModel.IsDetectingInstallation);
        Assert.False(viewModel.CanEditEntries);
        Assert.False(viewModel.CanReview);
        Assert.False(viewModel.CanReload);
        Assert.False(viewModel.CanDiscard);
        Assert.False(viewModel.CanSelectConfigurationFile);
        Assert.False(viewModel.CanChangeInstallation);

        await viewModel.ReviewChangesAsync();
        await viewModel.ReloadSelectedFileAsync();
        Assert.Equal(loadCallsBeforeOperation, pipeline.LoadCalls);

        mode.DraftValue = "netserver";
        operationCompletion.SetResult(installationB);
        await operation;

        Assert.False(viewModel.IsDetectingInstallation);
        Assert.True(viewModel.CanEditEntries);
        Assert.Equal("/context-a/nut/etc", viewModel.ConfigurationDirectoryText);
        Assert.Equal("/context-a/nut/etc/nut.conf", viewModel.SelectedFile!.FullPath);
        Assert.Equal("netserver", mode.DraftValue);
        Assert.True(viewModel.HasDraftChanges);
        Assert.Equal("A instalação não foi atualizada porque surgiram alterações locais durante a operação.", viewModel.StatusMessage);
        Assert.Equal(loadCallsBeforeOperation, pipeline.LoadCalls);
        Assert.Equal(0, pipeline.ApplyCalls);
    }

    [Fact]
    public async Task SensitiveAssignmentNeverExposesCurrentSecretAndItsPreviewIsRedacted()
    {
        const string originalSecret = "fictional-password";
        var pipeline = new TestPipeline();
        pipeline.SetFile("/session/nut/etc/upsd.users", NutConfigurationFileKind.UpsdUsers, $"[admin]\npassword = {originalSecret}\nactions = SET\n");
        var viewModel = await CreateInitializedViewModelAsync(pipeline, "upsd.users");
        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == "upsd.users"));
        var password = GetEntry(viewModel, "password");

        Assert.True(password.IsSensitive);
        Assert.Equal(string.Empty, password.DraftValue);
        Assert.False(password.IsChanged);
        Assert.DoesNotContain(originalSecret, GetPublicStringValues(password));
        Assert.DoesNotContain(originalSecret, GetPublicStringValues(viewModel));

        password.DraftValue = "new-fictional-password";
        await viewModel.ReviewChangesAsync();

        Assert.Single(viewModel.PreviewLines);
        Assert.True(viewModel.PreviewLines.Single().IsRedacted);
        Assert.Equal("<redacted>", viewModel.PreviewLines.Single().OriginalText);
        Assert.Equal("<redacted>", viewModel.PreviewLines.Single().CandidateText);
        Assert.DoesNotContain(originalSecret, GetPublicStringValues(viewModel));
        Assert.DoesNotContain("new-fictional-password", GetPublicStringValues(viewModel));
    }

    [Fact]
    public Task MonitorDirectiveUsesAnEmptyReplacementFieldAndRedactedPreview() =>
        AssertSensitiveDirectiveUsesAnEmptyReplacementFieldAndRedactedPreview(
            NutConfigurationFileKind.UpsmonConf,
            "upsmon.conf",
            "MONITOR ups@localhost 1 user fictional-monitor-secret master");

    [Fact]
    public Task CertidentDirectiveUsesAnEmptyReplacementFieldAndRedactedPreview() =>
        AssertSensitiveDirectiveUsesAnEmptyReplacementFieldAndRedactedPreview(
            NutConfigurationFileKind.UpsdConf,
            "upsd.conf",
            "CERTIDENT \"server cert\" fictional-private-key-password");

    private static async Task AssertSensitiveDirectiveUsesAnEmptyReplacementFieldAndRedactedPreview(
        NutConfigurationFileKind kind,
        string fileName,
        string line)
    {
        var pipeline = new TestPipeline();
        pipeline.SetFile($"/session/nut/etc/{fileName}", kind, line + "\n");
        var viewModel = await CreateInitializedViewModelAsync(pipeline, fileName);
        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == fileName));
        var entry = viewModel.Sections.Single().Entries.Single();

        Assert.True(entry.IsSensitive);
        Assert.Equal(string.Empty, entry.DraftValue);
        Assert.DoesNotContain("fictional", GetPublicStringValues(entry), StringComparison.OrdinalIgnoreCase);

        entry.DraftValue = "new complete protected arguments";
        await viewModel.ReviewChangesAsync();

        Assert.True(viewModel.PreviewLines.Single().IsRedacted);
        Assert.Equal("<redacted>", viewModel.PreviewLines.Single().CandidateText);
    }

    [Fact]
    public async Task ChangedExternallyDuringReviewPreservesDraftAndDoesNotApplyOrRetry()
    {
        var pipeline = new TestPipeline();
        pipeline.SetFile("/session/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        var viewModel = await CreateLoadedNutConfAsync(pipeline);
        GetEntry(viewModel, "MODE").DraftValue = "netserver";
        pipeline.SetFile("/session/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=external\n");

        await viewModel.ReviewChangesAsync();

        Assert.Equal("O arquivo foi alterado externamente desde que foi carregado.", viewModel.StatusMessage);
        Assert.False(viewModel.IsCriticalResult);
        Assert.True(viewModel.HasDraftChanges);
        Assert.False(viewModel.HasPreview);
        Assert.Equal(0, pipeline.ApplyCalls);
    }

    [Fact]
    public async Task ChangedExternallyDuringApplyPreservesDraftAndInvalidatesTheReviewedChange()
    {
        var pipeline = new TestPipeline
        {
            NextApplyResult = new NutConfigurationApplyResult(NutConfigurationApplyStatus.ChangedExternally)
        };
        pipeline.SetFile("/session/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        var viewModel = await CreateLoadedNutConfAsync(pipeline);
        GetEntry(viewModel, "MODE").DraftValue = "netserver";
        await viewModel.ReviewChangesAsync();
        viewModel.IsPreviewConfirmed = true;

        await viewModel.ApplyChangesAsync();

        Assert.Equal(1, pipeline.ApplyCalls);
        Assert.Equal("O arquivo foi alterado externamente desde que foi carregado.", viewModel.StatusMessage);
        Assert.True(viewModel.HasDraftChanges);
        Assert.False(viewModel.HasPreview);
        Assert.False(viewModel.IsPreviewConfirmed);
    }

    [Fact]
    public async Task ChangingFilesAndReloadingAreBlockedUntilDraftIsDiscarded()
    {
        var pipeline = new TestPipeline();
        pipeline.SetFile("/session/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        pipeline.SetFile("/session/nut/etc/upsd.conf", NutConfigurationFileKind.UpsdConf, "LISTEN 127.0.0.1\n");
        var viewModel = await CreateInitializedViewModelAsync(pipeline, "nut.conf", "upsd.conf");
        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == "nut.conf"));
        GetEntry(viewModel, "MODE").DraftValue = "netserver";

        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == "upsd.conf"));
        Assert.Equal("nut.conf", viewModel.SelectedFile!.FileName);
        Assert.Equal("Aplique ou descarte as alterações antes de trocar de arquivo.", viewModel.StatusMessage);

        await viewModel.ReloadSelectedFileAsync();
        Assert.Equal("Há alterações locais. Descarte-as antes de recarregar o arquivo.", viewModel.StatusMessage);

        await viewModel.DiscardChangesAsync();

        Assert.False(viewModel.HasDraftChanges);
        Assert.Equal("standalone", GetEntry(viewModel, "MODE").DraftValue);
    }

    [Fact]
    public async Task InstallationChangeIsBlockedWhileDraftChangesKeepTheLoadedContext()
    {
        var installationA = CreateInstallation("/context-a/nut", "/context-a/nut/etc", "nut.conf");
        var installationB = CreateInstallation("/context-b/nut", "/context-b/nut/etc", "nut.conf");
        var detector = new TestInstallationDetector(installationA) { InspectionResult = installationB };
        var pipeline = new TestPipeline();
        pipeline.SetFile("/context-a/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        var viewModel = new AdministrationPageViewModel(detector, pipeline);
        await viewModel.InitializeAsync();
        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == "nut.conf"));
        GetEntry(viewModel, "MODE").DraftValue = "netserver";

        await viewModel.InspectInstallationDirectoryAsync("/context-b/nut");

        Assert.Equal(0, detector.InspectCalls);
        Assert.Equal("Descarte ou aplique as alterações antes de trocar a instalação.", viewModel.StatusMessage);
        Assert.Equal("/context-a/nut/etc", viewModel.ConfigurationDirectoryText);
        Assert.Equal("/context-a/nut/etc/nut.conf", viewModel.SelectedFile!.FullPath);
        Assert.Equal("netserver", GetEntry(viewModel, "MODE").DraftValue);
        Assert.True(viewModel.HasLoadedFile);
    }

    [Fact]
    public async Task InstallationChangeIsBlockedWhilePreviewKeepsTheCurrentContext()
    {
        var installationA = CreateInstallation("/context-a/nut", "/context-a/nut/etc", "nut.conf");
        var installationB = CreateInstallation("/context-b/nut", "/context-b/nut/etc", "nut.conf");
        var detector = new TestInstallationDetector(installationA);
        var pipeline = new TestPipeline();
        pipeline.SetFile("/context-a/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        var viewModel = new AdministrationPageViewModel(detector, pipeline);
        await viewModel.InitializeAsync();
        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == "nut.conf"));
        GetEntry(viewModel, "MODE").DraftValue = "netserver";
        await viewModel.ReviewChangesAsync();
        detector.DetectResult = installationB;
        var detectCallsBeforeRefresh = detector.DetectCalls;

        await viewModel.RefreshInstallationAsync();

        Assert.Equal(detectCallsBeforeRefresh, detector.DetectCalls);
        Assert.True(viewModel.HasPreview);
        Assert.Equal("Descarte ou aplique as alterações antes de trocar a instalação.", viewModel.StatusMessage);
        Assert.Equal("/context-a/nut/etc", viewModel.ConfigurationDirectoryText);
        Assert.Equal("/context-a/nut/etc/nut.conf", viewModel.SelectedFile!.FullPath);
    }

    [Fact]
    public async Task AcceptedInstallationChangeClearsTheLoadedDocumentAndRequiresExplicitNewSelection()
    {
        var installationA = CreateInstallation("/context-a/nut", "/context-a/nut/etc", "nut.conf");
        var installationB = CreateInstallation("/context-b/nut", "/context-b/nut/etc", "nut.conf");
        var detector = new TestInstallationDetector(installationA) { InspectionResult = installationB };
        var pipeline = new TestPipeline();
        pipeline.SetFile("/context-a/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        pipeline.SetFile("/context-b/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=netserver\n");
        var viewModel = new AdministrationPageViewModel(detector, pipeline);
        await viewModel.InitializeAsync();
        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == "nut.conf"));
        var loadCallsBeforeChange = pipeline.LoadCalls;

        await viewModel.InspectInstallationDirectoryAsync("/context-b/nut");
        await viewModel.ReviewChangesAsync();
        await viewModel.ApplyChangesAsync();

        Assert.Equal(1, detector.InspectCalls);
        Assert.False(viewModel.HasLoadedFile);
        Assert.Null(viewModel.SelectedFile);
        Assert.Empty(viewModel.Sections);
        Assert.False(viewModel.HasPreview);
        Assert.False(viewModel.IsPreviewConfirmed);
        Assert.Equal("/context-b/nut/etc", viewModel.ConfigurationDirectoryText);
        Assert.All(viewModel.ConfigurationFiles, file => Assert.StartsWith("/context-b/nut/etc/", file.FullPath));
        Assert.Equal(loadCallsBeforeChange, pipeline.LoadCalls);
        Assert.Equal(0, pipeline.ApplyCalls);
    }

    [Fact]
    public async Task ConcurrentFileSelectionIsRejectedWhileTheCurrentLoadIsInProgress()
    {
        var pipeline = new TestPipeline();
        pipeline.SetFile("/session/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        pipeline.SetFile("/session/nut/etc/upsd.conf", NutConfigurationFileKind.UpsdConf, "LISTEN 127.0.0.1\n");
        var viewModel = await CreateInitializedViewModelAsync(pipeline, "nut.conf", "upsd.conf");
        var completion = new TaskCompletionSource<NutConfigurationLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.NextLoadCompletion = completion;
        pipeline.LoadStarted = started;
        var nutConf = viewModel.ConfigurationFiles.Single(file => file.FileName == "nut.conf");
        var upsdConf = viewModel.ConfigurationFiles.Single(file => file.FileName == "upsd.conf");

        var firstSelection = viewModel.SelectFileAsync(nutConf);
        await started.Task;
        await viewModel.SelectFileAsync(upsdConf);

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.CanSelectConfigurationFile);
        Assert.Equal(1, pipeline.LoadCalls);
        Assert.Same(nutConf, viewModel.SelectedFile);
        completion.SetResult(pipeline.CreateSuccessLoadResult("/session/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n"));
        await firstSelection;

        Assert.Equal(1, pipeline.LoadCalls);
        Assert.Equal("nut.conf", viewModel.SelectedFile!.FileName);
        Assert.True(viewModel.HasLoadedFile);
    }

    [Fact]
    public async Task StaleLoadResultCannotReplaceTheCurrentSelectedFileContext()
    {
        var pipeline = new TestPipeline();
        pipeline.SetFile("/session/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        pipeline.SetFile("/session/nut/etc/upsd.conf", NutConfigurationFileKind.UpsdConf, "LISTEN 127.0.0.1\n");
        var viewModel = await CreateInitializedViewModelAsync(pipeline, "nut.conf", "upsd.conf");
        var completion = new TaskCompletionSource<NutConfigurationLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.NextLoadCompletion = completion;
        pipeline.LoadStarted = started;
        var nutConf = viewModel.ConfigurationFiles.Single(file => file.FileName == "nut.conf");
        var upsdConf = viewModel.ConfigurationFiles.Single(file => file.FileName == "upsd.conf");

        var selection = viewModel.SelectFileAsync(nutConf);
        await started.Task;
        viewModel.SelectedFile = upsdConf;
        completion.SetResult(pipeline.CreateSuccessLoadResult("/session/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n"));
        await selection;

        Assert.Same(upsdConf, viewModel.SelectedFile);
        Assert.False(viewModel.HasLoadedFile);
        Assert.Empty(viewModel.Sections);
    }

    [Theory]
    [InlineData(NutConfigurationApplyStatus.PostApplyValidationFailedRolledBack, false, "A validação falhou e a configuração original foi restaurada.")]
    [InlineData(NutConfigurationApplyStatus.PostApplyValidationFailedRollbackFailed, true, "A validação falhou e a configuração pode necessitar recuperação manual.")]
    [InlineData(NutConfigurationApplyStatus.ChangedExternallyRollbackFailed, true, "O arquivo foi alterado externamente e a recuperação exige atenção manual.")]
    [InlineData(NutConfigurationApplyStatus.VerificationFailedRollbackFailed, true, "A verificação falhou e a configuração pode necessitar recuperação manual.")]
    [InlineData(NutConfigurationApplyStatus.Failed, true, "Não foi possível aplicar a configuração.")]
    [InlineData(NutConfigurationApplyStatus.Cancelled, false, "A aplicação das alterações foi cancelada.")]
    public async Task ApplyStatusesAreMappedWithoutRetry(
        NutConfigurationApplyStatus status,
        bool critical,
        string expectedMessage)
    {
        var pipeline = new TestPipeline
        {
            NextApplyResult = new NutConfigurationApplyResult(status, "/session/backup.bak", recoveryPath: "/session/recovery.bak")
        };
        pipeline.SetFile("/session/nut/etc/nut.conf", NutConfigurationFileKind.NutConf, "MODE=standalone\n");
        var viewModel = await CreateLoadedNutConfAsync(pipeline);
        GetEntry(viewModel, "MODE").DraftValue = "netserver";
        await viewModel.ReviewChangesAsync();
        viewModel.IsPreviewConfirmed = true;

        await viewModel.ApplyChangesAsync();

        Assert.Equal(1, pipeline.ApplyCalls);
        Assert.Equal(expectedMessage, viewModel.StatusMessage);
        Assert.Equal(critical, viewModel.IsCriticalResult);
        Assert.Equal("CRÍTICO — a configuração pode necessitar recuperação manual.", viewModel.CriticalResultText);
        Assert.Equal("/session/backup.bak", viewModel.BackupPath);
        Assert.Equal("/session/recovery.bak", viewModel.RecoveryPath);
        Assert.False(viewModel.HasPreview);
        Assert.False(viewModel.IsPreviewConfirmed);
    }

    [Theory]
    [InlineData(NutConfigurationLoadStatus.AccessDenied, "Permissão insuficiente. A elevação administrativa será tratada pela etapa de administração do Windows.")]
    [InlineData(NutConfigurationLoadStatus.UnsupportedEncoding, "A codificação do arquivo não é suportada.")]
    [InlineData(NutConfigurationLoadStatus.TargetNotFound, "O arquivo não existe neste diretório.")]
    public async Task LoadFailuresArePresentedWithoutTechnicalDetails(NutConfigurationLoadStatus status, string message)
    {
        var pipeline = new TestPipeline { ForcedLoadStatus = status };
        var viewModel = await CreateInitializedViewModelAsync(pipeline, "nut.conf");

        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == "nut.conf"));

        Assert.Equal(message, viewModel.StatusMessage);
        Assert.False(viewModel.HasLoadedFile);
    }

    [Fact]
    public async Task RealPipelineAppliesNutConfInATemporaryDirectoryAndShowsBackup()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = Path.Combine(directory.Path, "nut.conf");
        await File.WriteAllTextAsync(targetPath, "MODE=standalone\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var pipeline = new NutConfigurationFilePipeline();
        var viewModel = new AdministrationPageViewModel(
            new TestInstallationDetector(CreateInstallation(directory.Path, directory.Path, "nut.conf")),
            pipeline);
        await viewModel.InitializeAsync();
        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == "nut.conf"));
        GetEntry(viewModel, "MODE").DraftValue = "netserver";
        await viewModel.ReviewChangesAsync();
        viewModel.IsPreviewConfirmed = true;

        await viewModel.ApplyChangesAsync();

        Assert.Equal("MODE=netserver\n", await File.ReadAllTextAsync(targetPath));
        Assert.NotNull(viewModel.BackupPath);
        Assert.True(File.Exists(viewModel.BackupPath));
    }

    [Fact]
    public async Task RealPipelineAppliesFictitiousSensitivePasswordWithoutExposingTheOldValue()
    {
        const string originalSecret = "fictional-password";
        const string replacement = "new-fictional-password";
        using var directory = new TemporaryDirectory();
        var targetPath = Path.Combine(directory.Path, "upsd.users");
        await File.WriteAllTextAsync(targetPath, $"[admin]\npassword = {originalSecret}\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var viewModel = new AdministrationPageViewModel(
            new TestInstallationDetector(CreateInstallation(directory.Path, directory.Path, "upsd.users")),
            new NutConfigurationFilePipeline());
        await viewModel.InitializeAsync();
        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == "upsd.users"));
        var password = GetEntry(viewModel, "password");

        Assert.Equal(string.Empty, password.DraftValue);
        Assert.DoesNotContain(originalSecret, GetPublicStringValues(viewModel));
        password.DraftValue = replacement;
        await viewModel.ReviewChangesAsync();

        Assert.All(viewModel.PreviewLines, line => Assert.Equal("<redacted>", line.CandidateText));
        viewModel.IsPreviewConfirmed = true;
        await viewModel.ApplyChangesAsync();

        Assert.Contains(replacement, await File.ReadAllTextAsync(targetPath));
        Assert.True(File.Exists(viewModel.BackupPath));
    }

    private static async Task<AdministrationPageViewModel> CreateLoadedNutConfAsync(TestPipeline pipeline)
    {
        var viewModel = await CreateInitializedViewModelAsync(pipeline, "nut.conf");
        await viewModel.SelectFileAsync(viewModel.ConfigurationFiles.Single(file => file.FileName == "nut.conf"));
        return viewModel;
    }

    [Fact]
    public async Task PermissionRepairReviewExposesOnlyAclPlanMetadata()
    {
        var pipeline = new TestPipeline();
        var installation = CreateInstallation("/session/nut", "/session/nut/etc", "nut.conf");
        var administration = new TestWindowsAdministration();
        var viewModel = new AdministrationPageViewModel(new TestInstallationDetector(installation), pipeline, administration);

        await viewModel.InitializeAsync();
        viewModel.PreparePermissionRepair();

        Assert.True(viewModel.IsPermissionRepairPending);
        Assert.Equal("TEST\\user", viewModel.PendingPermissionIdentity);
        Assert.Equal("S-1-5-21-123", viewModel.PendingPermissionSid);
        Assert.Equal("/session/nut/etc", viewModel.PendingPermissionDirectory);
        Assert.Contains("/session/nut/etc/ups.conf", viewModel.PendingPermissionTargets);
    }

    [Fact]
    public async Task EventLogAccessDiagnosticRemainsDistinctFromNoEvents()
    {
        var pipeline = new TestPipeline();
        var installation = CreateInstallation("/session/nut", "/session/nut/etc", "nut.conf");
        var administration = new TestWindowsAdministration { EventStatus = NutEventLogStatus.AccessDenied, EventDiagnostic = "Acesso negado ao Event Log." };
        var viewModel = new AdministrationPageViewModel(new TestInstallationDetector(installation), pipeline, administration);

        await viewModel.InitializeAsync();

        Assert.Empty(viewModel.WindowsEvents);
        Assert.Equal(NutEventLogStatus.AccessDenied, viewModel.WindowsEventLogStatus);
        Assert.Equal("Acesso negado ao Event Log.", viewModel.WindowsEventLogDiagnosticMessage);
    }

    [Fact]
    public async Task DisabledWindowsServiceCannotBeStartedOrRestarted()
    {
        var pipeline = new TestPipeline();
        var installation = CreateInstallation("/session/nut", "/session/nut/etc", "nut.conf");
        var viewModel = new AdministrationPageViewModel(new TestInstallationDetector(installation), pipeline, new TestWindowsAdministration());
        await viewModel.InitializeAsync();
        viewModel.SelectedWindowsService = new NutServiceInfo("NetworkUpsTools", "Network UPS Tools", NutServiceState.Stopped, NutServiceStartMode.Disabled, "C:\\NUT\\bin\\nut.exe", NutAssociationConfidence.BinaryPath);

        Assert.False(viewModel.CanStartWindowsService);
        Assert.False(viewModel.CanRestartWindowsService);
    }

    private static async Task<AdministrationPageViewModel> CreateInitializedViewModelAsync(TestPipeline pipeline, params string[] availableFiles)
    {
        var installation = CreateInstallation("/session/nut", "/session/nut/etc", availableFiles);
        var viewModel = new AdministrationPageViewModel(new TestInstallationDetector(installation), pipeline);
        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static NutInstallationInfo CreateInstallation(string installationPath, string configurationPath, params string[] availableFiles)
    {
        var available = new HashSet<string>(availableFiles, StringComparer.OrdinalIgnoreCase);
        return new NutInstallationInfo(
            true,
            installationPath,
            configurationPath,
            "2.8.2",
            new Dictionary<string, string>(),
            [
                new NutConfigurationFileInfo("nut.conf", $"{configurationPath}/nut.conf", available.Contains("nut.conf"), true),
                new NutConfigurationFileInfo("ups.conf", $"{configurationPath}/ups.conf", available.Contains("ups.conf"), true),
                new NutConfigurationFileInfo("upsd.conf", $"{configurationPath}/upsd.conf", available.Contains("upsd.conf"), true),
                new NutConfigurationFileInfo("upsd.users", $"{configurationPath}/upsd.users", available.Contains("upsd.users"), true),
                new NutConfigurationFileInfo("upsmon.conf", $"{configurationPath}/upsmon.conf", available.Contains("upsmon.conf"), true)
            ],
            "Teste");
    }

    private static NutConfigurationEntryViewModel GetEntry(AdministrationPageViewModel viewModel, string name) =>
        viewModel.Sections.SelectMany(section => section.Entries).Single(entry => entry.Name == name);

    private static string GetPublicStringValues(object value) => string.Join(
        "\n",
        value.GetType().GetProperties()
            .Where(property => property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
            .Select(property => property.GetValue(value) as string)
            .Where(text => text is not null));

    private sealed class TestInstallationDetector : ILocalNutInstallationDetector
    {
        public TestInstallationDetector(NutInstallationInfo installation)
        {
            DetectResult = installation;
        }

        public string? LastManualDirectory { get; private set; }

        public NutInstallationInfo DetectResult { get; set; }

        public NutInstallationInfo? InspectionResult { get; set; }

        public int DetectCalls { get; private set; }

        public int InspectCalls { get; private set; }

        public TaskCompletionSource<bool>? DetectStarted { get; set; }

        public TaskCompletionSource<NutInstallationInfo>? NextDetectCompletion { get; set; }

        public TaskCompletionSource<bool>? InspectStarted { get; set; }

        public TaskCompletionSource<NutInstallationInfo>? NextInspectCompletion { get; set; }

        public Task<NutInstallationInfo> DetectAsync(CancellationToken cancellationToken)
        {
            DetectCalls++;
            DetectStarted?.TrySetResult(true);
            return NextDetectCompletion?.Task ?? Task.FromResult(DetectResult);
        }

        public Task<NutInstallationInfo> InspectDirectoryAsync(string installationOrConfigurationDirectory, CancellationToken cancellationToken)
        {
            InspectCalls++;
            LastManualDirectory = installationOrConfigurationDirectory;
            InspectStarted?.TrySetResult(true);
            return NextInspectCompletion?.Task ?? Task.FromResult(InspectionResult ?? DetectResult with
            {
                InstallationDirectory = installationOrConfigurationDirectory,
                ConfigurationDirectory = installationOrConfigurationDirectory
            });
        }
    }

    private sealed class TestPipeline : INutConfigurationFilePipeline
    {
        private readonly Dictionary<string, (NutConfigurationFileKind Kind, string Text)> _files = new(StringComparer.Ordinal);
        private readonly NutConfigurationParser _parser = new();

        public int LoadCalls { get; private set; }

        public int ApplyCalls { get; private set; }

        public int PrepareCalls { get; private set; }

        public string? LastLoadPath { get; private set; }

        public TaskCompletionSource<bool>? LoadStarted { get; set; }

        public TaskCompletionSource<NutConfigurationLoadResult>? NextLoadCompletion { get; set; }

        public TaskCompletionSource<bool>? ApplyStarted { get; set; }

        public TaskCompletionSource<NutConfigurationApplyResult>? NextApplyCompletion { get; set; }

        public NutConfigurationLoadStatus? ForcedLoadStatus { get; set; }

        public NutConfigurationApplyResult? NextApplyResult { get; set; }

        public NutConfigurationPreparedChange? LastPreparedChange { get; private set; }

        public NutConfigurationPreparedChange? LastAppliedChange { get; private set; }

        public NutConfigurationDocument? LastLoadedDocument { get; private set; }

        public void SetFile(string path, NutConfigurationFileKind kind, string text) => _files[path] = (kind, text);

        public Task<NutConfigurationLoadResult> LoadAsync(string targetPath, NutConfigurationFileKind fileKind, CancellationToken cancellationToken = default)
        {
            LoadCalls++;
            LastLoadPath = targetPath;
            LoadStarted?.TrySetResult(true);
            if (NextLoadCompletion is { } completion)
            {
                return completion.Task;
            }

            if (ForcedLoadStatus is { } status)
            {
                return Task.FromResult(new NutConfigurationLoadResult(status));
            }

            if (!_files.TryGetValue(targetPath, out var file))
            {
                return Task.FromResult(new NutConfigurationLoadResult(NutConfigurationLoadStatus.TargetNotFound));
            }

            return Task.FromResult(CreateSuccessLoadResult(targetPath, fileKind, file.Text));
        }

        public NutConfigurationLoadResult CreateSuccessLoadResult(string targetPath, NutConfigurationFileKind fileKind, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var document = _parser.Parse(fileKind, text);
            LastLoadedDocument = document;
            return new NutConfigurationLoadResult(
                NutConfigurationLoadStatus.Success,
                new NutConfigurationFileSnapshot(
                    targetPath,
                    fileKind,
                    document,
                    NutConfigurationTextEncoding.Utf8,
                    Convert.ToHexString(SHA256.HashData(bytes)),
                    bytes.LongLength));
        }

        public NutConfigurationPreparedChange Prepare(NutConfigurationFileSnapshot snapshot)
        {
            PrepareCalls++;
            var candidateText = snapshot.Document.Serialize();
            var candidateBytes = Encoding.UTF8.GetBytes(candidateText);
            var candidateFingerprint = Convert.ToHexString(SHA256.HashData(candidateBytes));
            var candidateLines = candidateText.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
            var previewLines = snapshot.Document.Nodes
                .Select((node, index) => (Node: node, Index: index))
                .Where(pair => pair.Node.IsModified)
                .Select(pair =>
                {
                    var sensitive = pair.Node is NutConfigurationAssignmentNode { IsSensitive: true } or NutConfigurationDirectiveNode { IsSensitive: true };
                    return new NutConfigurationPreviewLine(
                        pair.Index + 1,
                        sensitive ? "<redacted>" : pair.Node.RawText,
                        sensitive ? "<redacted>" : candidateLines[pair.Index],
                        sensitive);
                })
                .ToArray();
            LastPreparedChange = new NutConfigurationPreparedChange(
                snapshot,
                candidateText,
                candidateBytes,
                candidateFingerprint,
                new NutConfigurationChangePreview(snapshot.TargetPath, candidateFingerprint, previewLines));
            return LastPreparedChange;
        }

        public Task<NutConfigurationApplyResult> ApplyAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            LastAppliedChange = change;
            ApplyStarted?.TrySetResult(true);
            if (NextApplyCompletion is { } completion)
            {
                return CompleteApplyAsync(completion.Task, change);
            }

            var result = NextApplyResult ?? new NutConfigurationApplyResult(NutConfigurationApplyStatus.Success, "/session/backup.bak");
            if (result.Status == NutConfigurationApplyStatus.Success)
            {
                _files[change.Snapshot.TargetPath] = (change.Snapshot.FileKind, change.CandidateText);
            }

            return Task.FromResult(result);
        }

        private async Task<NutConfigurationApplyResult> CompleteApplyAsync(
            Task<NutConfigurationApplyResult> completion,
            NutConfigurationPreparedChange change)
        {
            var result = await completion;
            if (result.Status == NutConfigurationApplyStatus.Success)
            {
                _files[change.Snapshot.TargetPath] = (change.Snapshot.FileKind, change.CandidateText);
            }

            return result;
        }
    }

    private sealed class TestWindowsAdministration : ILocalNutWindowsAdministration
    {
        public NutEventLogStatus EventStatus { get; set; } = NutEventLogStatus.Success;

        public string? EventDiagnostic { get; set; }

        public Task<NutWindowsAdministrationSnapshot> InspectAsync(NutInstallationInfo installation, CancellationToken cancellationToken) =>
            Task.FromResult(new NutWindowsAdministrationSnapshot(
                true,
                PrivilegeState.StandardUser,
                Array.Empty<NutServiceInfo>(),
                new NutPermissionAssessment(NutPermissionState.Modifiable, "TEST\\user", "S-1-5-21-123", false, "Modify confirmado.", [installation.ConfigurationDirectory!, installation.ConfigurationDirectory! + "/ups.conf"]),
                Array.Empty<NutProcessInfo>(),
                Array.Empty<NutEventLogEntry>(),
                EventLogStatus: EventStatus,
                EventLogDiagnosticMessage: EventDiagnostic));

        public Task<NutAdministrativeActionResult> ExecuteAsync(NutAdministrativeActionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new NutAdministrativeActionResult(NutAdministrativeActionStatus.Success, request.Action, "ok"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"NutManager.T15.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
