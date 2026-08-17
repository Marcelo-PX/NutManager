using System.Runtime.Versioning;
using System.Security.Principal;
using System.ServiceProcess;
using NutManager.Core.Administration;
using NutManager.Core.Agent;

namespace NutManager.Agent;

/// <summary>
/// The Windows service host.
///
/// Startup is a sequence of things that must all be true, and any one of them being false stops the
/// agent rather than starting a reduced version of it. The account must be LocalSystem, the operators
/// group must resolve, and the audit sink must be usable. An agent that starts without those is an
/// agent whose refusals and records cannot be relied on, and it is more useful to an administrator as
/// a service that failed to start with a reason in the Event Log than as one that is running and
/// quietly unable to do anything.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class NutAgentWindowsService : ServiceBase
{
    internal const string WindowsServiceName = "NutManagerAgent";

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _stopping = new();
    private Task? _listener;
    private Task? _https;

    internal NutAgentWindowsService()
    {
        ServiceName = WindowsServiceName;
        CanStop = true;
        CanShutdown = true;
        AutoLog = true;
    }

    protected override void OnStart(string[] args)
    {
        var (isLocalSystem, account) = NutAgentBootstrap.VerifyAccount();
        var composition = NutAgentBootstrap.Create();

        if (!isLocalSystem)
        {
            // Recorded before stopping, because "the agent refused to run as this account" is exactly
            // the kind of thing that is impossible to diagnose from the outside.
            WriteStartupFailure(composition, $"The agent must run as LocalSystem; it is running as {account}.");
            FailToStart();
            return;
        }

        // InitializeAsync pins the service the agent may control and records a security startup
        // failure of its own if the operators group is missing.
        composition.Service.InitializeAsync(_stopping.Token).GetAwaiter().GetResult();

        if (composition.Authorization.GroupSid is not { } operatorsGroup)
        {
            // Without the group there is no principal to grant the pipe to, so there is no listener
            // worth opening: every caller would be refused by an ACL that names nobody.
            WriteStartupFailure(composition, composition.Authorization.ConfigurationFailure);
            FailToStart();
            return;
        }

        var server = new NutAgentNamedPipeServer(composition.Dispatcher, operatorsGroup);
        _listener = Task.Run(() => server.RunAsync(_stopping.Token), CancellationToken.None);

        StartHttpsIfConfigured(composition, operatorsGroup);
    }

    /// <summary>
    /// Starts the optional HTTPS listener, and only if it can be started as configured.
    ///
    /// Off is the default: an installation that does nothing gets a named pipe and no open port.
    /// When it is on and something is wrong — no prefix, a plain-text prefix, a wildcard host, a
    /// certificate that is absent or has no private key — the listener does not start at all. It
    /// never degrades into something weaker, and the named pipe keeps working, so a mistake in the
    /// HTTPS configuration cannot take away the transport that was already secure.
    /// </summary>
    private void StartHttpsIfConfigured(NutAgentComposition composition, SecurityIdentifier operatorsGroup)
    {
        var options = NutAgentHttpsOptions.Load();
        if (!options.HttpsEnabled) return;

        if (!NutAgentHttpsOptions.Validate(options, out var failure))
        {
            WriteStartupFailure(composition, $"The HTTPS transport was not started: {failure}");
            return;
        }

        if (!NutAgentCertificateCheck.Exists(options.CertificateThumbprint!, out var certificateFailure))
        {
            WriteStartupFailure(composition, $"The HTTPS transport was not started: {certificateFailure}");
            return;
        }

        try
        {
            var https = new NutAgentHttpsServer(composition.Dispatcher, operatorsGroup, options.HttpsPrefix!);
            _https = Task.Run(() => https.RunAsync(_stopping.Token), CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Binding failures are the common case here: no SSL certificate bound to the port, or
            // no URL reservation. Both are deployment steps, and both are named rather than hidden.
            WriteStartupFailure(composition, $"The HTTPS transport could not bind to {options.HttpsPrefix}: {exception.GetType().Name}.");
        }
    }

    protected override void OnStop()
    {
        _stopping.Cancel();

        try
        {
            _listener?.Wait(StopTimeout);
            _https?.Wait(StopTimeout);
        }
        catch (Exception)
        {
            // Stopping is not allowed to fail: the listener is cancelled either way and the process
            // is going down.
        }
    }

    protected override void OnShutdown() => OnStop();

    private void FailToStart()
    {
        // A non-zero exit code is what makes the SCM report this as a failed start rather than a
        // service that started and immediately stopped for no stated reason.
        ExitCode = 1;
        Stop();
    }

    private void WriteStartupFailure(NutAgentComposition composition, string? detail)
    {
        try
        {
            composition.Audit.WriteAsync(
                new NutAgentAuditEntry(
                    NutAgentAuditKind.SecurityStartupFailure, DateTimeOffset.UtcNow, Guid.Empty, "-", "-",
                    Environment.MachineName, NutAgentOperation.Handshake, null,
                    NutServiceState.Unknown, NutServiceState.Unknown, NutAgentResultCode.Unauthorized,
                    null, TimeSpan.Zero, detail),
                CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // If the Event Log cannot be written either, the SCM's own record of a failed start is
            // the remaining signal, and it is enough to know to look.
        }
    }
}
