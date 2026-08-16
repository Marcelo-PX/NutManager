using System.ComponentModel;
using System.Runtime.Versioning;
using System.ServiceProcess;
using NutManager.Core.Administration;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.Platform.Windows;

/// <summary>
/// Reads the state of the Windows service running NUT on another machine.
///
/// Two APIs, for two reasons. <see cref="ServiceController"/> enumerates the remote SCM and reports
/// each service's identity and state, which is all managed code and needs no interop. It does not
/// expose a process id or a binary path, so those come from a pair of query-only Win32 calls kept in
/// <see cref="WindowsServiceControlManagerInterop"/>. Nothing else is queried, and nothing is changed.
///
/// Identification reuses <see cref="WindowsNutServiceAssociation"/>, the same rules the local detector
/// applies, so a service NutManager recognises on this machine is recognised on that one. What cannot
/// follow is the containment check: locally the binary is verified to sit inside the detected
/// installation root, and there is no trusted root for a host whose filesystem this task is forbidden
/// to touch. So remote identification rests on the service's own identity, and the binary path is
/// reported rather than used as proof.
/// </summary>
public sealed class WindowsRemoteNutServiceProbe : IRemoteWindowsNutServiceProbe
{
    // Windows error numbers, not Windows APIs, so they live on the platform-neutral side where the
    // mapping that reads them can be compiled and tested without a platform guard.
    public const int ErrorAccessDenied = 5;
    public const int ErrorServiceDoesNotExist = 1060;
    public const int RpcServerUnavailable = 1722;

    private readonly TimeProvider _time;

    public WindowsRemoteNutServiceProbe(TimeProvider? timeProvider = null) => _time = timeProvider ?? TimeProvider.System;

    public Task<RemoteWindowsNutServiceSnapshot> ProbeAsync(string host, CancellationToken cancellationToken)
    {
        var target = NormalizeHost(host);
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(target))
        {
            return Task.FromResult(RemoteWindowsNutServiceSnapshot.Failure(
                host, RemoteWindowsServiceProbeState.Unsupported, _time.GetUtcNow()));
        }

        return WindowsRemoteServiceQuery.RunAsync(host, target, _time, cancellationToken);
    }

    /// <summary>
    /// The file name alone. The configured image path carries arguments and may be quoted, so it goes
    /// through the same extraction the local detector uses rather than being split on spaces, and only
    /// the leaf is kept: the full command line is not something this screen needs to publish.
    /// </summary>
    public static string? ExecutableNameOf(string? binaryPath)
    {
        var executable = WindowsNutServiceAssociation.TryExtractExecutablePath(binaryPath);
        if (string.IsNullOrWhiteSpace(executable)) return null;

        var trimmed = executable.TrimEnd('\\', '/');
        var separator = trimmed.LastIndexOfAny(['\\', '/']);
        var name = separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>
    /// Strips a UNC prefix and any share that came with it. The host normally arrives as the profile's
    /// NUT endpoint hostname and needs nothing done to it; this only stops a value shaped like
    /// <c>\\host\share</c> from being handed to Win32 as a machine name.
    /// </summary>
    public static string NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return string.Empty;

        var trimmed = host.Trim();
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal)) trimmed = trimmed[2..];

        var separator = trimmed.IndexOfAny(['\\', '/']);
        if (separator >= 0) trimmed = trimmed[..separator];

        return trimmed.Trim();
    }

    /// <summary>
    /// Maps the failure by its numeric Windows code rather than its message, which is localized on the
    /// machine running NutManager and would make the mapping depend on the operator's language.
    /// </summary>
    public static RemoteWindowsServiceProbeState MapFailure(Exception exception, out int? win32ErrorCode)
    {
        var win32 = exception as Win32Exception ?? exception.InnerException as Win32Exception;
        win32ErrorCode = win32?.NativeErrorCode;

        return win32?.NativeErrorCode switch
        {
            ErrorAccessDenied => RemoteWindowsServiceProbeState.AccessDenied,
            ErrorServiceDoesNotExist => RemoteWindowsServiceProbeState.ServiceNotFound,
            RpcServerUnavailable => RemoteWindowsServiceProbeState.RpcUnavailable,
            _ => RemoteWindowsServiceProbeState.UnknownFailure
        };
    }
}

/// <summary>
/// The Windows-typed half of the probe, split out for two reasons: the platform guard in
/// <see cref="WindowsRemoteNutServiceProbe.ProbeAsync"/> cannot follow a call into a lambda, and
/// keeping every SCM-typed member behind one annotation matches how the local detector already
/// separates <c>WindowsNutServiceController</c> from its callers.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsRemoteServiceQuery
{
    /// <summary>
    /// Runs the query off the calling thread. The SCM calls are synchronous and can sit on a blocked
    /// RPC for as long as the network makes them, so none of this belongs on the UI thread.
    /// Cancellation stops the result being published; it cannot recall a call already inside Win32,
    /// which is why the caller — not this method — is responsible for never starting two at once.
    /// </summary>
    internal static Task<RemoteWindowsNutServiceSnapshot> RunAsync(
        string originalHost,
        string target,
        TimeProvider time,
        CancellationToken cancellationToken) =>
        Task.Run(() => Probe(originalHost, target, time, cancellationToken), CancellationToken.None);

    private static RemoteWindowsNutServiceSnapshot Probe(
        string originalHost,
        string target,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var services = ServiceController.GetServices(target);
            try
            {
                var matches = services
                    .Where(service => WindowsNutServiceAssociation.IsKnownIdentity(service.ServiceName, service.DisplayName))
                    .ToArray();

                if (matches.Length == 0)
                {
                    return RemoteWindowsNutServiceSnapshot.Failure(
                        originalHost, RemoteWindowsServiceProbeState.ServiceNotFound, time.GetUtcNow());
                }

                if (matches.Length > 1)
                {
                    // Picking the first would be a guess presented as a fact, and the wrong guess names
                    // the wrong service on an administrator's screen.
                    return RemoteWindowsNutServiceSnapshot.Failure(
                        originalHost,
                        RemoteWindowsServiceProbeState.AmbiguousService,
                        time.GetUtcNow(),
                        candidates: [.. matches.Select(service => service.ServiceName).Order(StringComparer.OrdinalIgnoreCase)]);
                }

                var match = matches[0];
                var state = ToState(match.Status);
                var (processId, executable) = ReadProcessDetail(target, match.ServiceName, cancellationToken);

                return new RemoteWindowsNutServiceSnapshot(
                    originalHost,
                    RemoteWindowsServiceProbeState.Success,
                    match.ServiceName,
                    match.DisplayName,
                    state,
                    processId,
                    executable,
                    null,
                    time.GetUtcNow());
            }
            finally
            {
                foreach (var service in services) service.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return RemoteWindowsNutServiceSnapshot.Failure(
                originalHost,
                WindowsRemoteNutServiceProbe.MapFailure(exception, out var code),
                time.GetUtcNow(),
                code);
        }
    }

    /// <summary>
    /// Asks the SCM for the process id and image path. A failure here is not a failed probe: the
    /// service's state is already known and worth showing, so these degrade to null instead.
    /// </summary>
    private static (int? ProcessId, string? Executable) ReadProcessDetail(string target, string serviceName, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var manager = WindowsServiceControlManagerInterop.OpenServiceControlManager(target);
            if (manager.IsInvalid) return (null, null);

            using var service = WindowsServiceControlManagerInterop.OpenServiceForQuery(manager, serviceName);
            if (service.IsInvalid) return (null, null);

            var processId = WindowsServiceControlManagerInterop.TryGetProcessId(service);
            var binaryPath = WindowsServiceControlManagerInterop.TryGetBinaryPath(service);
            return (processId, WindowsRemoteNutServiceProbe.ExecutableNameOf(binaryPath));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Passes Windows' own vocabulary through. All four transitional states survive, because a service
    /// that is still starting is a different fact from one that has stopped.
    /// </summary>
    internal static NutServiceState ToState(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => NutServiceState.Running,
        ServiceControllerStatus.Stopped => NutServiceState.Stopped,
        ServiceControllerStatus.StartPending => NutServiceState.StartPending,
        ServiceControllerStatus.StopPending => NutServiceState.StopPending,
        ServiceControllerStatus.Paused => NutServiceState.Paused,
        ServiceControllerStatus.PausePending => NutServiceState.PausePending,
        ServiceControllerStatus.ContinuePending => NutServiceState.ContinuePending,
        _ => NutServiceState.Unknown
    };
}
