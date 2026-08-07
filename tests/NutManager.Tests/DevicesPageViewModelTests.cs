using NutManager.App.ViewModels;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Core.Status;
using Xunit;

namespace NutManager.Tests;

public sealed class DevicesPageViewModelTests
{
    private static readonly NutEndpoint Endpoint = new("mock.nut.local");

    [Fact]
    public async Task DiscoveryKeepsTheServerOrderAndSelectsTheFirstUps()
    {
        var alpha = new UpsIdentity("alpha", "UPS Alpha");
        var beta = new UpsIdentity("beta", "UPS Beta");
        var client = new FakeNutClient
        {
            ListHandler = _ => Task.FromResult<IReadOnlyList<UpsIdentity>>([beta, alpha]),
            SnapshotHandler = (name, _) => Task.FromResult(CreateSnapshot(name, DataSource.Live))
        };
        using var viewModel = new DevicesPageViewModel(client, Endpoint);

        await viewModel.InitializeAsync();

        Assert.Equal(new[] { "beta", "alpha" }, viewModel.Devices.Select(device => device.Name));
        Assert.Equal("beta", viewModel.SelectedDevice?.Name);
        Assert.Equal("beta", viewModel.SelectedSnapshot?.Identity.Name);
    }

    [Fact]
    public async Task DetailsExposeSortedRawVariablesAndSimulatedSource()
    {
        var ups = new UpsIdentity("mockups", null, null, null, null);
        var client = new FakeNutClient
        {
            ListHandler = _ => Task.FromResult<IReadOnlyList<UpsIdentity>>([ups]),
            SnapshotHandler = (_, _) => Task.FromResult(CreateSnapshot(
                "mockups",
                DataSource.Simulated,
                new Dictionary<string, UpsVariable>
                {
                    ["z.last"] = new("z.last", "original-z"),
                    ["a.first"] = new("a.first", "original-a")
                }))
        };
        using var viewModel = new DevicesPageViewModel(client, Endpoint);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsSimulated);
        Assert.Equal("Indisponível", viewModel.SelectedDeviceDescription);
        Assert.Equal(new[] { "a.first", "z.last" }, viewModel.RawVariables.Select(variable => variable.Name));
        Assert.Equal(new[] { "original-a", "original-z" }, viewModel.RawVariables.Select(variable => variable.Value));
    }

    [Fact]
    public async Task RefreshPreservesSelectionByOrdinalNameAndFallsBackWhenItDisappears()
    {
        var alpha = new UpsIdentity("alpha");
        var beta = new UpsIdentity("beta");
        var gamma = new UpsIdentity("gamma");
        var discoveries = new Queue<IReadOnlyList<UpsIdentity>>([
            [alpha, beta],
            [beta, alpha],
            [gamma, alpha]
        ]);
        var client = new FakeNutClient
        {
            ListHandler = _ => Task.FromResult(discoveries.Dequeue()),
            SnapshotHandler = (name, _) => Task.FromResult(CreateSnapshot(name, DataSource.Live))
        };
        using var viewModel = new DevicesPageViewModel(client, Endpoint);

        await viewModel.InitializeAsync();
        await viewModel.SelectDeviceCommand.ExecuteAsync(beta);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("beta", viewModel.SelectedDevice?.Name);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("gamma", viewModel.SelectedDevice?.Name);
    }

    [Fact]
    public async Task EmptyDiscoveryShowsNoSelectionOrVariables()
    {
        var client = new FakeNutClient
        {
            ListHandler = _ => Task.FromResult<IReadOnlyList<UpsIdentity>>(Array.Empty<UpsIdentity>())
        };
        using var viewModel = new DevicesPageViewModel(client, Endpoint);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasNoDevices);
        Assert.True(viewModel.HasNoSelectedDevice);
        Assert.True(viewModel.HasNoRawVariables);
        Assert.Null(viewModel.SelectedSnapshot);
    }

    [Fact]
    public async Task DiscoveryFailureRetainsTheLastSuccessfulState()
    {
        var ups = new UpsIdentity("alpha");
        var client = new FakeNutClient
        {
            ListHandler = _ => Task.FromResult<IReadOnlyList<UpsIdentity>>([ups]),
            SnapshotHandler = (name, _) => Task.FromResult(CreateSnapshot(name, DataSource.Live))
        };
        using var viewModel = new DevicesPageViewModel(client, Endpoint);
        await viewModel.InitializeAsync();
        client.ListHandler = _ => throw new InvalidOperationException("network unavailable");

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("alpha", Assert.Single(viewModel.Devices).Name);
        Assert.Equal("alpha", viewModel.SelectedDevice?.Name);
        Assert.True(viewModel.HasDiscoveryError);
    }

    [Fact]
    public async Task DetailsFailureShowsAnErrorWithoutInventingVariables()
    {
        var ups = new UpsIdentity("alpha");
        var client = new FakeNutClient
        {
            ListHandler = _ => Task.FromResult<IReadOnlyList<UpsIdentity>>([ups]),
            SnapshotHandler = (_, _) => throw new InvalidOperationException("timeout")
        };
        using var viewModel = new DevicesPageViewModel(client, Endpoint);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasDetailsError);
        Assert.Empty(viewModel.RawVariables);
        Assert.Null(viewModel.SelectedSnapshot);
    }

    [Fact]
    public async Task DiscoveryCancellationDoesNotShowAnError()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<UpsIdentity>>();
        var client = new FakeNutClient
        {
            ListHandler = cancellationToken => completion.Task.WaitAsync(cancellationToken)
        };
        using var viewModel = new DevicesPageViewModel(client, Endpoint);
        using var cancellation = new CancellationTokenSource();
        var task = viewModel.InitializeAsync(cancellation.Token);

        cancellation.Cancel();
        await task;

        Assert.False(viewModel.HasDiscoveryError);
        Assert.False(viewModel.IsDiscovering);
    }

    [Fact]
    public async Task ACancelledOlderSelectionCannotOverwriteTheNewerSelection()
    {
        var alpha = new UpsIdentity("alpha");
        var beta = new UpsIdentity("beta");
        var alphaCompletion = new TaskCompletionSource<UpsSnapshot>();
        var client = new FakeNutClient
        {
            ListHandler = _ => Task.FromResult<IReadOnlyList<UpsIdentity>>([alpha, beta]),
            SnapshotHandler = (name, _) => name == "alpha"
                ? alphaCompletion.Task
                : Task.FromResult(CreateSnapshot("beta", DataSource.Live))
        };
        using var viewModel = new DevicesPageViewModel(client, Endpoint);

        var olderSelection = viewModel.SelectDeviceCommand.ExecuteAsync(alpha);
        await viewModel.SelectDeviceCommand.ExecuteAsync(beta);
        alphaCompletion.SetResult(CreateSnapshot("alpha", DataSource.Simulated));
        await olderSelection;

        Assert.Equal("beta", viewModel.SelectedDevice?.Name);
        Assert.Equal("beta", viewModel.SelectedSnapshot?.Identity.Name);
        Assert.False(viewModel.IsSimulated);
    }

    private static UpsSnapshot CreateSnapshot(
        string name,
        DataSource source,
        IReadOnlyDictionary<string, UpsVariable>? variables = null) =>
        new(
            new UpsIdentity(name),
            Array.Empty<UpsStatusToken>(),
            variables ?? new Dictionary<string, UpsVariable>(),
            new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero),
            source);

    private sealed class FakeNutClient : INutClient
    {
        public Func<CancellationToken, Task<IReadOnlyList<UpsIdentity>>> ListHandler { get; set; } =
            _ => Task.FromResult<IReadOnlyList<UpsIdentity>>(Array.Empty<UpsIdentity>());

        public Func<string, CancellationToken, Task<UpsSnapshot>> SnapshotHandler { get; set; } =
            (_, _) => throw new InvalidOperationException("No snapshot handler configured.");

        public Task<IReadOnlyList<UpsIdentity>> ListUpsAsync(NutEndpoint endpoint, CancellationToken cancellationToken) =>
            ListHandler(cancellationToken);

        public Task<UpsSnapshot> GetSnapshotAsync(NutEndpoint endpoint, string upsName, CancellationToken cancellationToken) =>
            SnapshotHandler(upsName, cancellationToken);
    }
}
