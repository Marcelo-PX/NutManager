using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NutManager.App.Localization;
using NutManager.Core.Administration;
using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.App.ViewModels;

/// <summary>
/// Watches the Windows service running NUT on a remote host, and does nothing else.
///
/// There is no start, stop or restart here, and that is the point rather than an omission: the view
/// binds to this object, so a control the view could invoke would have to exist as a command on this
/// class. None does, which makes "remote monitoring cannot act on the server" checkable in one file.
///
/// The reading it produces is deliberately separate from NUT's own health. A refused SCM query says
/// nothing about whether upsd is answering on 3493, so a failure here never touches the connection
/// state the rest of the shell shows.
/// </summary>
public sealed partial class RemoteWindowsServiceViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>Conservative on purpose: the SCM is an RPC round trip, not a local field read.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);

    private readonly IRemoteWindowsNutServiceProbe _probe;
    private readonly NutManagerLocalizer _strings;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();

    private Task? _inFlight;
    private CancellationTokenSource? _lifetime;
    private Task? _polling;
    private int _generation;
    private bool _disposed;

    public RemoteWindowsServiceViewModel(
        string host,
        IRemoteWindowsNutServiceProbe probe,
        UiLanguagePreference language = UiLanguagePreference.PtBr,
        TimeSpan? interval = null)
    {
        Host = string.IsNullOrWhiteSpace(host) ? string.Empty : host.Trim();
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _strings = new NutManagerLocalizer(language);
        _interval = interval ?? DefaultInterval;
    }

    public string Host { get; }

    public NutManagerLocalizer Strings => _strings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServiceIdentityText))]
    [NotifyPropertyChangedFor(nameof(ServiceStateText))]
    [NotifyPropertyChangedFor(nameof(ProcessText))]
    [NotifyPropertyChangedFor(nameof(ProcessIdText))]
    [NotifyPropertyChangedFor(nameof(QueryStateText))]
    [NotifyPropertyChangedFor(nameof(IsServiceRunning))]
    [NotifyPropertyChangedFor(nameof(IsServiceStopped))]
    [NotifyPropertyChangedFor(nameof(IsServiceTransitioning))]
    [NotifyPropertyChangedFor(nameof(IsQueryUnavailable))]
    [NotifyPropertyChangedFor(nameof(HasObservation))]
    [NotifyPropertyChangedFor(nameof(ObservedAtText))]
    [NotifyPropertyChangedFor(nameof(DiagnosticText))]
    [NotifyPropertyChangedFor(nameof(HasDiagnostic))]
    private RemoteWindowsNutServiceSnapshot? _snapshot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingStaleReading))]
    private bool _isRefreshing;

    public bool HasObservation => Snapshot is not null;

    /// <summary>
    /// A refresh in progress does not blank the panel, so the previous reading stays legible. It is
    /// labelled while it does, because an old state presented as current is worse than no state.
    /// </summary>
    public bool IsShowingStaleReading => IsRefreshing && Snapshot is not null;

    public string HostText => string.IsNullOrEmpty(Host) ? _strings.Get("Common.Unavailable") : Host;

    public string ServiceIdentityText => Snapshot?.DisplayName
        ?? Snapshot?.ServiceName
        ?? _strings.Get("Common.Unavailable");

    public string ServiceStateText => Snapshot is not { IsQuerySuccessful: true }
        ? _strings.Get("Status.Unavailable")
        : Snapshot.ServiceState switch
        {
            NutServiceState.Running => _strings.Get("ServiceState.Running"),
            NutServiceState.Stopped => _strings.Get("ServiceState.Stopped"),
            NutServiceState.StartPending => _strings.Get("ServiceState.StartPending"),
            NutServiceState.StopPending => _strings.Get("ServiceState.StopPending"),
            NutServiceState.Paused => _strings.Get("ServiceState.Paused"),
            NutServiceState.PausePending => _strings.Get("ServiceState.PausePending"),
            NutServiceState.ContinuePending => _strings.Get("ServiceState.ContinuePending"),
            NutServiceState.Failed => _strings.Get("ServiceState.Failed"),
            _ => _strings.Get("Status.Unavailable")
        };

    /// <summary>The executable the SCM reported. Absent stays absent rather than becoming a guess.</summary>
    public string ProcessText => Snapshot switch
    {
        { IsQuerySuccessful: true, HasProcess: true, ExecutableName: { } executable } => executable,
        { IsQuerySuccessful: true, HasProcess: true } => _strings.Get("RemoteService.Process.Unnamed"),
        { IsQuerySuccessful: true } => _strings.Get("RemoteService.Process.NotRunning"),
        _ => _strings.Get("Status.Unavailable")
    };

    public string ProcessIdText => Snapshot is { IsQuerySuccessful: true, ProcessId: { } pid and > 0 }
        ? pid.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "—";

    public string QueryStateText => Snapshot?.ProbeState switch
    {
        RemoteWindowsServiceProbeState.Success => _strings.Get("RemoteService.Query.Available"),
        RemoteWindowsServiceProbeState.AccessDenied => _strings.Get("RemoteService.Query.AccessDenied"),
        RemoteWindowsServiceProbeState.RpcUnavailable => _strings.Get("RemoteService.Query.RpcUnavailable"),
        RemoteWindowsServiceProbeState.ServiceNotFound => _strings.Get("RemoteService.Query.ServiceNotFound"),
        RemoteWindowsServiceProbeState.AmbiguousService => _strings.Get("RemoteService.Query.Ambiguous"),
        RemoteWindowsServiceProbeState.TimedOut => _strings.Get("RemoteService.Query.TimedOut"),
        RemoteWindowsServiceProbeState.Unsupported => _strings.Get("RemoteService.Query.Unsupported"),
        RemoteWindowsServiceProbeState.UnknownFailure => _strings.Get("RemoteService.Query.Failed"),
        _ => _strings.Get("Status.Unavailable")
    };

    public bool IsServiceRunning => Snapshot is { IsQuerySuccessful: true, ServiceState: NutServiceState.Running };

    public bool IsServiceStopped => Snapshot is { IsQuerySuccessful: true, ServiceState: NutServiceState.Stopped };

    public bool IsServiceTransitioning => Snapshot is { IsQuerySuccessful: true } snapshot &&
        snapshot.ServiceState is NutServiceState.StartPending or NutServiceState.StopPending
            or NutServiceState.PausePending or NutServiceState.ContinuePending or NutServiceState.Paused;

    public bool IsQueryUnavailable => Snapshot is not null && !Snapshot.IsQuerySuccessful;

    /// <summary>The numeric Windows code, kept for diagnostics; never a localized message parsed back.</summary>
    public string? DiagnosticText => Snapshot switch
    {
        { ProbeState: RemoteWindowsServiceProbeState.AmbiguousService, CandidateServiceNames: { Count: > 0 } names } =>
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                _strings.Get("RemoteService.Query.AmbiguousDetail"),
                string.Join(", ", names)),
        { Win32ErrorCode: { } code } => string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            _strings.Get("RemoteService.Query.Win32Code"),
            code),
        _ => null
    };

    public bool HasDiagnostic => !string.IsNullOrWhiteSpace(DiagnosticText);

    public string ObservedAtText => Snapshot is { } snapshot
        ? string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            _strings.Get("RemoteService.ObservedAt"),
            snapshot.ObservedAt.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture))
        : string.Empty;

    public string TitleText => _strings.Get("RemoteService.Title");
    public string HostLabel => _strings.Get("RemoteService.Host");
    public string ServiceLabel => _strings.Get("RemoteService.Service");
    public string StateLabel => _strings.Get("RemoteService.State");
    public string ProcessLabel => _strings.Get("RemoteService.Process");
    public string ProcessIdLabel => _strings.Get("RemoteService.ProcessId");
    public string QueryLabel => _strings.Get("RemoteService.Query");
    public string RefreshText => _strings.Get("RemoteService.Refresh");
    public string ReadOnlyNoticeText => _strings.Get("RemoteService.ReadOnlyNotice");
    public string ControlUnavailableText => _strings.Get("RemoteService.ControlUnavailable");
    public string RefreshingText => _strings.Get("RemoteService.Refreshing");

    /// <summary>
    /// Runs one probe, or joins the one already running. Never two: a blocked RPC can outlive its
    /// interval, and starting a second call each tick would pile threads onto a host that is already
    /// not answering.
    /// </summary>
    [RelayCommand]
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            if (_inFlight is { IsCompleted: false }) return _inFlight;

            _inFlight = RunProbeAsync(Interlocked.Increment(ref _generation), cancellationToken);
            return _inFlight;
        }
    }

    private async Task RunProbeAsync(int generation, CancellationToken cancellationToken)
    {
        IsRefreshing = true;
        try
        {
            var result = await _probe.ProbeAsync(Host, cancellationToken).ConfigureAwait(true);

            // Only the newest probe may publish. Stopping the monitor or disposing it bumps the
            // generation, so a call that returns after the surface is gone lands nowhere.
            if (generation == Volatile.Read(ref _generation)) Snapshot = result;
        }
        catch (OperationCanceledException)
        {
            // A cancelled probe publishes nothing. It does not clear the previous reading either:
            // navigating away is not evidence that the service changed.
        }
        finally
        {
            if (generation == Volatile.Read(ref _generation)) IsRefreshing = false;
        }
    }

    /// <summary>
    /// Starts polling. Called when the surface becomes visible, so an unwatched panel costs nothing
    /// and no timer outlives the screen that needed it.
    /// </summary>
    public void StartMonitoring()
    {
        lock (_gate)
        {
            if (_disposed || _lifetime is not null) return;
            _lifetime = new CancellationTokenSource();
            _polling = PollAsync(_lifetime.Token);
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true))
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task StopMonitoringAsync()
    {
        CancellationTokenSource? lifetime;
        Task? polling;
        lock (_gate)
        {
            lifetime = _lifetime;
            polling = _polling;
            _lifetime = null;
            _polling = null;
        }

        // Invalidated first and unconditionally. A probe already inside Win32 cannot be recalled, so
        // this guard is what keeps its late result from writing into a view model nobody is watching —
        // and it has to happen even when no polling loop was ever started, because a single manual
        // refresh can still be in flight when the surface goes away.
        Interlocked.Increment(ref _generation);

        if (lifetime is null)
        {
            IsRefreshing = false;
            return;
        }

        await lifetime.CancelAsync().ConfigureAwait(false);

        if (polling is not null)
        {
            try
            {
                await polling.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        lifetime.Dispose();
        IsRefreshing = false;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        await StopMonitoringAsync().ConfigureAwait(false);
    }
}
