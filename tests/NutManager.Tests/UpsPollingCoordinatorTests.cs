using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Core.Status;
using NutManager.Infrastructure.Polling;
using Xunit;

namespace NutManager.Tests;

public sealed class UpsPollingCoordinatorTests
{
    private static readonly NutEndpoint Endpoint = new("test");

    [Fact]
    public async Task MonitoringStartsAnImmediatePollAndPublishesFreshSuccess()
    {
        var client = new FakeClient();
        using var coordinator = new UpsPollingCoordinator(client, Endpoint, TimeSpan.FromDays(1));
        await coordinator.MonitorAsync("ups");
        await client.Started.Task;
        var published = WaitForStateAsync(coordinator, state => state.DataFreshness == DataFreshness.Fresh);
        client.Completion.SetResult(Snapshot("ups"));
        await published;
        Assert.Equal(ConnectionState.Connected, coordinator.State.ConnectionState);
        Assert.Equal("ups", coordinator.State.Snapshot?.Identity.Name);
        Assert.Equal(1, client.MaximumConcurrent);
    }

    [Fact]
    public async Task InitialFailureIsUnavailableAndSelectionChangeDiscardsOldResult()
    {
        var client = new FakeClient();
        using var coordinator = new UpsPollingCoordinator(client, Endpoint, TimeSpan.FromDays(1));
        await coordinator.MonitorAsync("a");
        await client.Started.Task;
        var failed = WaitForStateAsync(coordinator, state => state.ConnectionState == ConnectionState.ConnectionFailed);
        client.Completion.SetException(new IOException());
        await failed;
        Assert.Equal(DataFreshness.Unavailable, coordinator.State.DataFreshness);

        client.Reset();
        await coordinator.MonitorAsync("b");
        await client.Started.Task;
        var secondPublished = WaitForStateAsync(coordinator, state => state.Snapshot?.Identity.Name == "b");
        client.Completion.SetResult(Snapshot("b"));
        await secondPublished;
        Assert.Equal("b", coordinator.State.UpsName);
    }

    [Fact]
    public async Task NullSelectionAndDisposeCancelTheActivePoll()
    {
        var client = new FakeClient();
        using var coordinator = new UpsPollingCoordinator(client, Endpoint, TimeSpan.FromDays(1));
        await coordinator.MonitorAsync("ups");
        await client.Started.Task;
        await coordinator.MonitorAsync(null);
        Assert.Equal(DataFreshness.Unavailable, coordinator.State.DataFreshness);
    }

    private static async Task WaitForStateAsync(UpsPollingCoordinator coordinator, Func<PollingState, bool> predicate)
    {
        if (predicate(coordinator.State)) return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(PollingState state) { if (predicate(state)) completion.TrySetResult(); }
        coordinator.StateChanged += Handler;
        try { await completion.Task; } finally { coordinator.StateChanged -= Handler; }
    }

    private static UpsSnapshot Snapshot(string name) => new(new UpsIdentity(name), Array.Empty<UpsStatusToken>(), new Dictionary<string, UpsVariable>(), DateTimeOffset.UnixEpoch, DataSource.Simulated);

    private sealed class FakeClient : INutClient
    {
        private int _active;
        public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<UpsSnapshot> Completion { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaximumConcurrent { get; private set; }
        public void Reset() { Started = new(TaskCreationOptions.RunContinuationsAsynchronously); Completion = new(TaskCreationOptions.RunContinuationsAsynchronously); CancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public Task<IReadOnlyList<UpsIdentity>> ListUpsAsync(NutEndpoint endpoint, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UpsIdentity>>(Array.Empty<UpsIdentity>());
        public async Task<UpsSnapshot> GetSnapshotAsync(NutEndpoint endpoint, string upsName, CancellationToken cancellationToken)
        {
            MaximumConcurrent = Math.Max(MaximumConcurrent, Interlocked.Increment(ref _active)); Started.TrySetResult();
            using var registration = cancellationToken.Register(() => CancellationObserved.TrySetResult());
            try { return await Completion.Task.WaitAsync(cancellationToken); } finally { Interlocked.Decrement(ref _active); }
        }
    }
}
