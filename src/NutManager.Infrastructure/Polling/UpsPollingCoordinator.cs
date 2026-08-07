using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.Polling;

public sealed class UpsPollingCoordinator : IUpsPollingCoordinator
{
    private readonly INutClient _client; private readonly NutEndpoint _endpoint; private readonly TimeSpan _interval; private readonly TimeProvider _timeProvider;
    private CancellationTokenSource? _session; private Task? _loop; private int _generation; private bool _disposed;
    public UpsPollingCoordinator(INutClient client, NutEndpoint endpoint, TimeSpan interval, TimeProvider? timeProvider = null)
    { _client = client; _endpoint = endpoint; _interval = interval; _timeProvider = timeProvider ?? TimeProvider.System; State = PollingState.Unavailable; }
    public PollingState State { get; private set; }
    public event Action<PollingState>? StateChanged;
    public async Task MonitorAsync(string? upsName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed(); _session?.Cancel(); var previous = _loop; var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _session = source; var generation = ++_generation;
        if (string.IsNullOrWhiteSpace(upsName)) { Publish(PollingState.Unavailable); return; }
        Publish(new(upsName, null, ConnectionState.Connecting, DataFreshness.Unavailable, null));
        _loop = RunAsync(upsName, generation, source.Token); if (previous is not null) await ObserveAsync(previous);
    }
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); if (_loop is not null) await _loop; }
    private async Task RunAsync(string upsName, int generation, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { var snapshot = await _client.GetSnapshotAsync(_endpoint, upsName, token); if (generation != _generation) return; Publish(new(upsName, snapshot, ConnectionState.Connected, DataFreshness.Fresh, null)); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception) { if (generation != _generation) return; var stale = State.Snapshot is not null && State.UpsName == upsName; Publish(new(upsName, stale ? State.Snapshot : null, stale ? ConnectionState.Reconnecting : ConnectionState.ConnectionFailed, stale ? DataFreshness.Stale : DataFreshness.Unavailable, "Não foi possível atualizar os dados do UPS.")); }
            try { await Task.Delay(_interval, _timeProvider, token); } catch (OperationCanceledException) { return; }
        }
    }
    private void Publish(PollingState state) { State = state; StateChanged?.Invoke(state); }
    private static async Task ObserveAsync(Task task) { try { await task; } catch (OperationCanceledException) { } }
    public void Dispose() { if (_disposed) return; _disposed = true; _session?.Cancel(); _session?.Dispose(); }
    private void ThrowIfDisposed() { ObjectDisposedException.ThrowIf(_disposed, this); }
}
