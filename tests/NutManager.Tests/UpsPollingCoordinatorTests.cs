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

    [Fact]
    public async Task PollCadenceUsesVirtualTimeWithoutOverlap()
    {
        var time = new ManualTimeProvider();
        var client = new QueuedClient();
        using var coordinator = new UpsPollingCoordinator(client, Endpoint, TimeSpan.FromSeconds(5), time);
        await coordinator.MonitorAsync("ups");
        await client.WaitForCallsAsync(1);
        var firstPublished = WaitForStateAsync(coordinator, state => state.Snapshot?.Identity.Name == "one");
        client.CompleteNext(Snapshot("one"));
        await firstPublished;

        await time.WaitForScheduledTimerCountAsync(1);
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(1, client.TotalCalls);
        time.Advance(TimeSpan.FromSeconds(1));
        await client.WaitForCallsAsync(2);
        Assert.Equal(1, client.MaximumConcurrent);
        var secondPublished = WaitForStateAsync(coordinator, state => state.Snapshot?.Identity.Name == "two");
        client.CompleteNext(Snapshot("two"));
        await secondPublished;

        await time.WaitForScheduledTimerCountAsync(2);
        time.Advance(TimeSpan.FromSeconds(15));
        await client.WaitForCallsAsync(3);
        Assert.Equal(3, client.TotalCalls);
    }

    [Fact]
    public async Task SuccessFailureAndRecoveryRetainTheStaleSnapshotUntilANewSuccess()
    {
        var time = new ManualTimeProvider();
        var client = new QueuedClient();
        var snapshotA = new UpsSnapshot(new UpsIdentity("ups", "A"), [new UpsStatusToken("OL", StatusSemanticState.Online, StatusSeverity.Normal, true)], new Dictionary<string, UpsVariable> { ["battery.charge"] = new("battery.charge", "80") }, new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), DataSource.Simulated, batteryChargePercentage: 80);
        var snapshotB = new UpsSnapshot(new UpsIdentity("ups", "B"), Array.Empty<UpsStatusToken>(), new Dictionary<string, UpsVariable>(), snapshotA.LastSuccessfulUpdate.AddMinutes(1), DataSource.Simulated, batteryChargePercentage: 90);
        using var coordinator = new UpsPollingCoordinator(client, Endpoint, TimeSpan.FromSeconds(5), time);

        await coordinator.MonitorAsync("ups"); await client.WaitForCallsAsync(1); var fresh = WaitForStateAsync(coordinator, state => ReferenceEquals(state.Snapshot, snapshotA)); client.CompleteNext(snapshotA); await fresh;
        Assert.Equal(ConnectionState.Connected, coordinator.State.ConnectionState); Assert.Equal(DataFreshness.Fresh, coordinator.State.DataFreshness); Assert.Null(coordinator.State.LastError);

        await time.WaitForScheduledTimerCountAsync(1); time.Advance(TimeSpan.FromSeconds(5)); await client.WaitForCallsAsync(2); var stale = WaitForStateAsync(coordinator, state => state.DataFreshness == DataFreshness.Stale); client.FailNext(new IOException()); await stale;
        Assert.Same(snapshotA, coordinator.State.Snapshot); Assert.Equal(ConnectionState.Reconnecting, coordinator.State.ConnectionState); Assert.NotNull(coordinator.State.LastError); Assert.Equal(snapshotA.LastSuccessfulUpdate, coordinator.State.Snapshot?.LastSuccessfulUpdate); Assert.Equal(80m, coordinator.State.Snapshot?.BatteryChargePercentage); Assert.Equal("80", coordinator.State.Snapshot?.Variables["battery.charge"].Value);

        await time.WaitForScheduledTimerCountAsync(2); time.Advance(TimeSpan.FromSeconds(4)); Assert.Equal(2, client.TotalCalls);
        time.Advance(TimeSpan.FromSeconds(1)); await client.WaitForCallsAsync(3); var recovered = WaitForStateAsync(coordinator, state => ReferenceEquals(state.Snapshot, snapshotB)); client.CompleteNext(snapshotB); await recovered;
        Assert.NotSame(snapshotA, coordinator.State.Snapshot); Assert.Equal(ConnectionState.Connected, coordinator.State.ConnectionState); Assert.Equal(DataFreshness.Fresh, coordinator.State.DataFreshness); Assert.Null(coordinator.State.LastError); Assert.Equal(snapshotB.LastSuccessfulUpdate, coordinator.State.Snapshot?.LastSuccessfulUpdate);
    }

    [Fact]
    public async Task MonitorWaitsForASlowCancelledTargetBeforeStartingTheNextTarget()
    {
        var client = new SlowCancellationClient();
        using var coordinator = new UpsPollingCoordinator(client, Endpoint, TimeSpan.FromDays(1));
        await coordinator.MonitorAsync("A");
        await client.AStarted.Task;
        var switching = coordinator.MonitorAsync("B");
        await client.ACancellationObserved.Task;
        Assert.Equal(1, client.TotalCalls); Assert.Equal(1, client.ActiveCalls); Assert.Equal(1, client.MaximumConcurrent);
        client.ACompletion.SetResult(Snapshot("A"));
        await switching; await client.BStarted.Task;
        Assert.Equal(2, client.TotalCalls); Assert.Equal(1, client.ActiveCalls); Assert.Equal(1, client.MaximumConcurrent);
        Assert.Equal("B", coordinator.State.UpsName); Assert.Null(coordinator.State.Snapshot); Assert.Equal(ConnectionState.Connecting, coordinator.State.ConnectionState); Assert.Equal(DataFreshness.Unavailable, coordinator.State.DataFreshness);
        var snapshotB = Snapshot("B"); var published = WaitForStateAsync(coordinator, state => ReferenceEquals(state.Snapshot, snapshotB)); client.BCompletion.SetResult(snapshotB); await published;
        Assert.Same(snapshotB, coordinator.State.Snapshot); Assert.Equal(ConnectionState.Connected, coordinator.State.ConnectionState); Assert.Equal(DataFreshness.Fresh, coordinator.State.DataFreshness);
    }

    [Fact]
    public async Task RefreshStartsAnImmediatePollWithoutWaitingForTheRegularInterval()
    {
        var time = new ManualTimeProvider(); var client = new QueuedClient();
        var snapshotA = Snapshot("ups"); var snapshotB = new UpsSnapshot(new UpsIdentity("ups", "new"), Array.Empty<UpsStatusToken>(), new Dictionary<string, UpsVariable>(), DateTimeOffset.UnixEpoch.AddMinutes(1), DataSource.Simulated);
        using var coordinator = new UpsPollingCoordinator(client, Endpoint, TimeSpan.FromSeconds(5), time);
        await coordinator.MonitorAsync("ups"); await client.WaitForCallsAsync(1); var freshA = WaitForStateAsync(coordinator, state => ReferenceEquals(state.Snapshot, snapshotA)); client.CompleteNext(snapshotA); await freshA;
        await time.WaitForScheduledTimerCountAsync(1); var callsBeforeRefresh = client.TotalCalls;
        var refreshTask = coordinator.RefreshAsync(); await client.WaitForCallsAsync(2);
        Assert.Equal(1, callsBeforeRefresh); Assert.Equal(2, client.TotalCalls); Assert.Equal(1, client.MaximumConcurrent); Assert.Same(snapshotA, coordinator.State.Snapshot); Assert.Equal(DataFreshness.Fresh, coordinator.State.DataFreshness);
        var freshB = WaitForStateAsync(coordinator, state => ReferenceEquals(state.Snapshot, snapshotB)); client.CompleteNext(snapshotB); await Task.WhenAll(refreshTask, freshB);
        Assert.Same(snapshotB, coordinator.State.Snapshot); Assert.Equal(ConnectionState.Connected, coordinator.State.ConnectionState); Assert.Equal(DataFreshness.Fresh, coordinator.State.DataFreshness); Assert.Null(coordinator.State.LastError);
    }

    [Fact]
    public async Task FailedRefreshRetainsTheLastSuccessfulSnapshotAsStale()
    {
        var time = new ManualTimeProvider(); var client = new QueuedClient(); var snapshotA = Snapshot("ups");
        using var coordinator = new UpsPollingCoordinator(client, Endpoint, TimeSpan.FromSeconds(5), time);
        await coordinator.MonitorAsync("ups"); await client.WaitForCallsAsync(1); var fresh = WaitForStateAsync(coordinator, state => ReferenceEquals(state.Snapshot, snapshotA)); client.CompleteNext(snapshotA); await fresh;
        var successfulTimestamp = coordinator.State.Snapshot!.LastSuccessfulUpdate; await time.WaitForScheduledTimerCountAsync(1);
        var refreshTask = coordinator.RefreshAsync(); await client.WaitForCallsAsync(2);
        Assert.Same(snapshotA, coordinator.State.Snapshot); Assert.Equal(successfulTimestamp, coordinator.State.Snapshot?.LastSuccessfulUpdate); Assert.Equal(1, client.MaximumConcurrent);
        var stale = WaitForStateAsync(coordinator, state => state.DataFreshness == DataFreshness.Stale); client.FailNext(new InvalidOperationException("refresh failed")); await Task.WhenAll(refreshTask, stale);
        Assert.Same(snapshotA, coordinator.State.Snapshot); Assert.Equal(successfulTimestamp, coordinator.State.Snapshot?.LastSuccessfulUpdate); Assert.Equal(ConnectionState.Reconnecting, coordinator.State.ConnectionState); Assert.Equal(DataFreshness.Stale, coordinator.State.DataFreshness); Assert.NotNull(coordinator.State.LastError); Assert.Equal(2, client.TotalCalls); Assert.Equal(1, client.MaximumConcurrent);
    }

    [Fact]
    public async Task ConcurrentRefreshRequestsAreBoundedAndNeverOverlapPolls()
    {
        var time = new ManualTimeProvider(); var client = new QueuedClient(); var snapshotA = Snapshot("ups"); var snapshotB = Snapshot("ups"); var snapshotC = Snapshot("ups");
        using var coordinator = new UpsPollingCoordinator(client, Endpoint, TimeSpan.FromSeconds(5), time);
        await coordinator.MonitorAsync("ups"); await client.WaitForCallsAsync(1); var freshA = WaitForStateAsync(coordinator, state => ReferenceEquals(state.Snapshot, snapshotA)); client.CompleteNext(snapshotA); await freshA; await time.WaitForScheduledTimerCountAsync(1);
        var r1 = coordinator.RefreshAsync(); await client.WaitForCallsAsync(2); var r2 = coordinator.RefreshAsync(); var r3 = coordinator.RefreshAsync();
        Assert.Equal(1, client.MaximumConcurrent); Assert.Equal(2, client.TotalCalls); Assert.Same(snapshotA, coordinator.State.Snapshot);
        client.CompleteNext(snapshotB); await Task.WhenAll(r1, r2, r3); await client.WaitForCallsAsync(3);
        Assert.Equal(3, client.TotalCalls); Assert.Equal(1, client.MaximumConcurrent);
        var freshC = WaitForStateAsync(coordinator, state => ReferenceEquals(state.Snapshot, snapshotC)); client.CompleteNext(snapshotC); await freshC;
        Assert.Equal(0, client.ActiveCalls); Assert.Equal(ConnectionState.Connected, coordinator.State.ConnectionState); Assert.Equal(DataFreshness.Fresh, coordinator.State.DataFreshness); Assert.Null(coordinator.State.LastError);
    }

    [Fact]
    public async Task DisposeCancelsTheActivePollAndPreventsFuturePolling()
    {
        var time = new ManualTimeProvider(); var client = new FakeClient(); var coordinator = new UpsPollingCoordinator(client, Endpoint, TimeSpan.FromSeconds(5), time);
        await coordinator.MonitorAsync("ups"); await client.Started.Task;
        coordinator.Dispose(); await client.Finished.Task;
        Assert.Equal(0, client.ActiveCalls); Assert.Equal(1, client.TotalCalls); Assert.Equal(1, client.MaximumConcurrent);
        Assert.Null(coordinator.State.LastError); Assert.Equal(ConnectionState.Connecting, coordinator.State.ConnectionState); Assert.Equal(DataFreshness.Unavailable, coordinator.State.DataFreshness);
        time.Advance(TimeSpan.FromSeconds(25)); Assert.Equal(1, client.TotalCalls); Assert.Equal(1, client.MaximumConcurrent);
    }

    [Fact]
    public async Task OperationsAfterDisposeCannotRestartPolling()
    {
        using var coordinator = new UpsPollingCoordinator(new FakeClient(), Endpoint, TimeSpan.FromDays(1));
        coordinator.Dispose(); coordinator.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => coordinator.MonitorAsync("ups"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => coordinator.RefreshAsync());
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
        private int _active; private int _totalCalls;
        public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<UpsSnapshot> Completion { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int TotalCalls => _totalCalls;
        public int ActiveCalls => _active;
        public int MaximumConcurrent { get; private set; }
        public void Reset() { Started = new(TaskCreationOptions.RunContinuationsAsynchronously); Completion = new(TaskCreationOptions.RunContinuationsAsynchronously); CancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously); Finished = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public Task<IReadOnlyList<UpsIdentity>> ListUpsAsync(NutEndpoint endpoint, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UpsIdentity>>(Array.Empty<UpsIdentity>());
        public async Task<UpsSnapshot> GetSnapshotAsync(NutEndpoint endpoint, string upsName, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _totalCalls);
            MaximumConcurrent = Math.Max(MaximumConcurrent, Interlocked.Increment(ref _active));
            using var registration = cancellationToken.Register(() => CancellationObserved.TrySetResult());
            Started.TrySetResult();
            try { return await Completion.Task.WaitAsync(cancellationToken); } finally { Interlocked.Decrement(ref _active); Finished.TrySetResult(); }
        }
    }

    private sealed class QueuedClient : INutClient
    {
        private readonly Queue<TaskCompletionSource<UpsSnapshot>> _operations = new();
        private TaskCompletionSource _calls = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _gate = new();
        private int _active;
        public int TotalCalls { get; private set; }
        public int ActiveCalls => _active;
        public int MaximumConcurrent { get; private set; }
        public Task<IReadOnlyList<UpsIdentity>> ListUpsAsync(NutEndpoint endpoint, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UpsIdentity>>(Array.Empty<UpsIdentity>());
        public Task<UpsSnapshot> GetSnapshotAsync(NutEndpoint endpoint, string upsName, CancellationToken cancellationToken)
        {
            var operation = new TaskCompletionSource<UpsSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate) { _operations.Enqueue(operation); TotalCalls++; _calls.TrySetResult(); _calls = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            MaximumConcurrent = Math.Max(MaximumConcurrent, Interlocked.Increment(ref _active));
            return AwaitAsync(operation, cancellationToken);
        }
        public async Task WaitForCallsAsync(int count) { while (true) { Task wait; lock (_gate) { if (TotalCalls >= count) return; wait = _calls.Task; } await wait; } }
        public void CompleteNext(UpsSnapshot snapshot) => _operations.Dequeue().SetResult(snapshot);
        public void FailNext(Exception exception) => _operations.Dequeue().SetException(exception);
        private async Task<UpsSnapshot> AwaitAsync(TaskCompletionSource<UpsSnapshot> operation, CancellationToken token) { try { return await operation.Task.WaitAsync(token); } finally { Interlocked.Decrement(ref _active); } }
    }

    private sealed class SlowCancellationClient : INutClient
    {
        private int _active;
        public TaskCompletionSource AStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BCStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BStarted => BCStarted;
        public TaskCompletionSource ACancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<UpsSnapshot> ACompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<UpsSnapshot> BCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int TotalCalls { get; private set; }
        public int ActiveCalls => _active;
        public int MaximumConcurrent { get; private set; }
        public Task<IReadOnlyList<UpsIdentity>> ListUpsAsync(NutEndpoint endpoint, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UpsIdentity>>(Array.Empty<UpsIdentity>());
        public async Task<UpsSnapshot> GetSnapshotAsync(NutEndpoint endpoint, string upsName, CancellationToken cancellationToken)
        { TotalCalls++; MaximumConcurrent = Math.Max(MaximumConcurrent, Interlocked.Increment(ref _active)); try { if (upsName == "A") { using var registration = cancellationToken.Register(() => ACancellationObserved.TrySetResult()); AStarted.TrySetResult(); return await ACompletion.Task; } BCStarted.TrySetResult(); return await BCompletion.Task; } finally { Interlocked.Decrement(ref _active); } }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new(); private DateTimeOffset _now = DateTimeOffset.UnixEpoch; private readonly List<Timer> _timers = []; private TaskCompletionSource _timerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously); private int _createdTimerCount;
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var timer = new Timer(this, callback, state, dueTime); lock (_gate) { _timers.Add(timer); _createdTimerCount++; _timerCreated.TrySetResult(); _timerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously); } return timer; }
        public async Task WaitForScheduledTimerCountAsync(int expected) { while (true) { Task wait; lock (_gate) { if (_createdTimerCount >= expected) return; wait = _timerCreated.Task; } await wait; } }
        public void Advance(TimeSpan value) { List<Timer> due; lock (_gate) { _now += value; due = _timers.Where(timer => !timer.Disposed && timer.Due <= _now).ToList(); } foreach (var timer in due) timer.Fire(); }
        private sealed class Timer(ManualTimeProvider owner, TimerCallback callback, object? state, TimeSpan due) : ITimer
        {
            public bool Disposed { get; private set; }
            public DateTimeOffset Due { get; private set; } = owner._now + due;
            public bool Change(TimeSpan dueTime, TimeSpan period) { Due = owner._now + dueTime; return !Disposed; }
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
            public void Fire() { Dispose(); callback(state); }
        }
    }
}
