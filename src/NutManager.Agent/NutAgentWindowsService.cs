using System.Runtime.Versioning;
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
    }

    protected override void OnStop()
    {
        _stopping.Cancel();

        try
        {
            _listener?.Wait(StopTimeout);
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
