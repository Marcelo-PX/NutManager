using System.Text;
using NutManager.App.ViewModels;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Core.Services;
using Xunit;

namespace NutManager.Tests;

/// <summary>
/// Navigation between configuration files. The file list is the only way to reach an editor, so a
/// load that is in flight must never latch it shut: the user has to stay free to pick another file,
/// and only the newest pick may publish an editor.
/// </summary>
public sealed class ConfigurationNavigationTests
{
    [Fact]
    public async Task NavigatingBetweenFilesLeavesTheListSelectableAndTheEditorMatchingTheSelection()
    {
        var (viewModel, pipeline) = await CreateAsync();

        foreach (var fileName in new[] { "nut.conf", "ups.conf", "upsd.conf", "nut.conf" })
        {
            await viewModel.SelectFileAsync(File(viewModel, fileName));

            Assert.Equal(fileName, viewModel.SelectedFile?.FileName);
            Assert.Equal(fileName, viewModel.SelectedFileName);
            Assert.False(viewModel.IsBusy);
            Assert.False(viewModel.IsLoadingFile);
            Assert.True(viewModel.CanSelectConfigurationFile);
        }

        Assert.Equal(4, pipeline.LoadCalls);
        Assert.NotNull(viewModel.NutGeneralConfigurationEditor);
    }

    [Fact]
    public async Task TheFileListStaysSelectableWhileALoadIsStillRunning()
    {
        // The list is bound to CanSelectConfigurationFile. If a load switched it off, the control
        // would be disabled in the middle of the very click that started the load.
        var (viewModel, pipeline) = await CreateAsync();
        pipeline.Gate("nut.conf");

        var navigation = viewModel.SelectFileAsync(File(viewModel, "nut.conf"));
        await pipeline.WaitUntilStartedAsync("nut.conf");

        Assert.True(viewModel.IsLoadingFile);
        Assert.True(viewModel.CanSelectConfigurationFile);

        pipeline.Release("nut.conf");
        await navigation;

        Assert.False(viewModel.IsLoadingFile);
        Assert.True(viewModel.CanSelectConfigurationFile);
    }

    [Fact]
    public async Task TheNewestSelectionWinsWhenLoadsCompleteOutOfOrder()
    {
        var (viewModel, pipeline) = await CreateAsync();
        pipeline.Gate("nut.conf");
        pipeline.Gate("ups.conf");
        pipeline.Gate("upsd.conf");

        var first = viewModel.SelectFileAsync(File(viewModel, "nut.conf"));
        await pipeline.WaitUntilStartedAsync("nut.conf");
        var second = viewModel.SelectFileAsync(File(viewModel, "ups.conf"));
        await pipeline.WaitUntilStartedAsync("ups.conf");
        var third = viewModel.SelectFileAsync(File(viewModel, "upsd.conf"));
        await pipeline.WaitUntilStartedAsync("upsd.conf");

        // The selection follows the click immediately, so the highlight can never point at a file
        // other than the one the editor is being built for.
        Assert.Equal("upsd.conf", viewModel.SelectedFile?.FileName);

        // Adverse completion order: the newest answer arrives first and the stale ones follow.
        pipeline.Release("upsd.conf");
        await third;
        pipeline.Release("nut.conf");
        await first;
        pipeline.Release("ups.conf");
        await second;

        Assert.Equal("upsd.conf", viewModel.SelectedFile?.FileName);
        Assert.NotNull(viewModel.UpsdConfigurationEditor);
        Assert.Null(viewModel.NutGeneralConfigurationEditor);
        Assert.Null(viewModel.UpsConfigurationEditor);
        Assert.False(viewModel.IsLoadingFile);
        Assert.True(viewModel.CanSelectConfigurationFile);
    }

    [Fact]
    public async Task SupersededLoadsAreCancelled()
    {
        var (viewModel, pipeline) = await CreateAsync();
        pipeline.Gate("nut.conf");

        var first = viewModel.SelectFileAsync(File(viewModel, "nut.conf"));
        await pipeline.WaitUntilStartedAsync("nut.conf");
        Assert.False(pipeline.WasCancelled("nut.conf"));

        var second = viewModel.SelectFileAsync(File(viewModel, "upsd.conf"));

        Assert.True(pipeline.WasCancelled("nut.conf"));

        pipeline.Release("nut.conf");
        await first;
        await second;

        // A superseded load is not a failure, so it must not leave an error on screen.
        Assert.Null(viewModel.StatusMessage);
        Assert.Equal("upsd.conf", viewModel.SelectedFile?.FileName);
        Assert.False(viewModel.IsLoadingFile);
    }

    [Fact]
    public async Task AFailedLoadReleasesTheListAndTheNextFileStillLoads()
    {
        var (viewModel, pipeline) = await CreateAsync();
        pipeline.Throw("ups.conf");

        await viewModel.SelectFileAsync(File(viewModel, "ups.conf"));

        Assert.NotNull(viewModel.StatusMessage);
        Assert.False(viewModel.IsLoadingFile);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanSelectConfigurationFile);

        await viewModel.SelectFileAsync(File(viewModel, "nut.conf"));

        Assert.Equal("nut.conf", viewModel.SelectedFile?.FileName);
        Assert.NotNull(viewModel.NutGeneralConfigurationEditor);
        Assert.False(viewModel.IsLoadingFile);
        Assert.True(viewModel.CanSelectConfigurationFile);
    }

    [Fact]
    public async Task AnUnavailableFileIsRejectedWithoutLoadingAnythingOrLatchingTheList()
    {
        var (viewModel, pipeline) = await CreateAsync();
        await viewModel.SelectFileAsync(File(viewModel, "nut.conf"));
        var loadsAfterFirstFile = pipeline.LoadCalls;

        await viewModel.SelectFileAsync(File(viewModel, "upsmon.conf"));

        Assert.Equal(loadsAfterFirstFile, pipeline.LoadCalls);
        Assert.Equal("nut.conf", viewModel.SelectedFile?.FileName);
        Assert.False(viewModel.IsLoadingFile);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanSelectConfigurationFile);
    }

    [Fact]
    public async Task PendingChangesStillBlockNavigationAndAreNeverDiscardedSilently()
    {
        var (viewModel, _) = await CreateAsync();
        await viewModel.SelectFileAsync(File(viewModel, "nut.conf"));
        var field = viewModel.NutGeneralConfigurationEditor!.BasicFields.First(item => item.TechnicalName == "MODE");
        field.DraftValue = "netserver";

        Assert.True(viewModel.HasDraftChanges);
        Assert.False(viewModel.CanSelectConfigurationFile);

        await viewModel.SelectFileAsync(File(viewModel, "upsd.conf"));

        Assert.Equal("nut.conf", viewModel.SelectedFile?.FileName);
        Assert.True(viewModel.HasDraftChanges);
        Assert.NotNull(viewModel.NutGeneralConfigurationEditor);
        Assert.Null(viewModel.UpsdConfigurationEditor);

        await viewModel.DiscardChangesAsync();

        Assert.False(viewModel.HasDraftChanges);
        Assert.True(viewModel.CanSelectConfigurationFile);

        await viewModel.SelectFileAsync(File(viewModel, "upsd.conf"));

        Assert.Equal("upsd.conf", viewModel.SelectedFile?.FileName);
    }

    [Fact]
    public async Task ReselectingTheCurrentFileDoesNotReload()
    {
        var (viewModel, pipeline) = await CreateAsync();
        await viewModel.SelectFileAsync(File(viewModel, "ups.conf"));
        var editor = viewModel.UpsConfigurationEditor;
        var loads = pipeline.LoadCalls;

        await viewModel.SelectFileAsync(File(viewModel, "ups.conf"));

        Assert.Equal(loads, pipeline.LoadCalls);
        Assert.Same(editor, viewModel.UpsConfigurationEditor);
    }

    private static NutConfigurationFileItemViewModel File(AdministrationPageViewModel viewModel, string fileName) =>
        viewModel.ConfigurationFiles.Single(file => file.FileName == fileName);

    private static async Task<(AdministrationPageViewModel ViewModel, NavigationPipeline Pipeline)> CreateAsync()
    {
        const string root = "/nav/nut";
        const string etc = "/nav/nut/etc";
        var pipeline = new NavigationPipeline();
        pipeline.SetFile($"{etc}/nut.conf", NutConfigurationFileKind.NutConf, "MODE=netserver\n");
        pipeline.SetFile($"{etc}/ups.conf", NutConfigurationFileKind.UpsConf, "[ups]\n\tdriver = nutdrv_qx\n\tport = COM4\n");
        pipeline.SetFile($"{etc}/upsd.conf", NutConfigurationFileKind.UpsdConf, "MAXAGE 15\n");

        var installation = new NutInstallationInfo(
            true, root, etc, "2.8.2", new Dictionary<string, string>(),
            [
                new NutConfigurationFileInfo("nut.conf", $"{etc}/nut.conf", true, true),
                new NutConfigurationFileInfo("ups.conf", $"{etc}/ups.conf", true, true),
                new NutConfigurationFileInfo("upsd.conf", $"{etc}/upsd.conf", true, true),
                new NutConfigurationFileInfo("upsd.users", $"{etc}/upsd.users", true, true),
                new NutConfigurationFileInfo("upsmon.conf", $"{etc}/upsmon.conf", false, true)
            ],
            "Teste");

        var viewModel = new AdministrationPageViewModel(new NavigationDetector(installation), pipeline);
        await viewModel.InitializeAsync();
        return (viewModel, pipeline);
    }

    private sealed class NavigationDetector(NutInstallationInfo installation) : ILocalNutInstallationDetector
    {
        public Task<NutInstallationInfo> DetectAsync(CancellationToken cancellationToken) => Task.FromResult(installation);

        public Task<NutInstallationInfo> InspectDirectoryAsync(string directory, CancellationToken cancellationToken) =>
            Task.FromResult(installation);
    }

    /// <summary>
    /// A pipeline whose loads can be held open per file so completions can be forced into an adverse
    /// order, and which records whether a superseded load actually observed its cancellation.
    /// </summary>
    private sealed class NavigationPipeline : INutConfigurationFilePipeline
    {
        private readonly Dictionary<string, (NutConfigurationFileKind Kind, string Text)> _files = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskCompletionSource<bool>> _gates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskCompletionSource<bool>> _started = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CancellationToken> _tokens = new(StringComparer.Ordinal);
        private readonly HashSet<string> _throwing = new(StringComparer.Ordinal);
        private readonly NutConfigurationParser _parser = new();

        public int LoadCalls { get; private set; }

        public void SetFile(string path, NutConfigurationFileKind kind, string text) => _files[path] = (kind, text);

        public void Gate(string fileName)
        {
            _gates[fileName] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _started[fileName] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Throw(string fileName) => _throwing.Add(fileName);

        public Task WaitUntilStartedAsync(string fileName) => _started[fileName].Task;

        public void Release(string fileName) => _gates[fileName].TrySetResult(true);

        public bool WasCancelled(string fileName) =>
            _tokens.TryGetValue(fileName, out var token) && token.IsCancellationRequested;

        public async Task<NutConfigurationLoadResult> LoadAsync(string targetPath, NutConfigurationFileKind fileKind, CancellationToken cancellationToken = default)
        {
            LoadCalls++;
            var name = targetPath[(targetPath.LastIndexOf('/') + 1)..];
            _tokens[name] = cancellationToken;
            if (_started.TryGetValue(name, out var started)) started.TrySetResult(true);
            if (_gates.TryGetValue(name, out var gate)) await gate.Task;
            if (_throwing.Contains(name)) throw new IOException("Simulated load failure.");

            if (!_files.TryGetValue(targetPath, out var file))
            {
                return new NutConfigurationLoadResult(NutConfigurationLoadStatus.TargetNotFound);
            }

            var bytes = Encoding.UTF8.GetBytes(file.Text);
            return new NutConfigurationLoadResult(
                NutConfigurationLoadStatus.Success,
                new NutConfigurationFileSnapshot(
                    targetPath,
                    fileKind,
                    _parser.Parse(fileKind, file.Text),
                    NutConfigurationTextEncoding.Utf8,
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                    bytes.LongLength));
        }

        public NutConfigurationPreparedChange Prepare(NutConfigurationFileSnapshot snapshot) =>
            throw new NotSupportedException("Navigation tests never write.");

        public Task<NutConfigurationApplyResult> ApplyAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Navigation tests never write.");
    }
}
