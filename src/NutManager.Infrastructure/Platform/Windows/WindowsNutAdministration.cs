using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;
using Microsoft.Win32;
using NutManager.Core.Administration;
using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.Platform.Windows;

public interface IWindowsNutAdministrationBackend
{
    Task<NutWindowsAdministrationSnapshot> InspectAsync(NutInstallationInfo installation, CancellationToken cancellationToken);
    Task<NutAdministrativeActionResult> ExecuteAsync(NutAdministrativeActionRequest request, CancellationToken cancellationToken);
}

public interface IWindowsPrivilegeElevationBroker
{
    PrivilegeState GetPrivilegeState();
    Task<NutAdministrativeActionResult> ExecuteElevatedAsync(NutAdministrativeActionRequest request, CancellationToken cancellationToken);
}

public interface IWindowsNutInstallationRevalidator
{
    Task<NutInstallationInfo> InspectAsync(string installationDirectory, CancellationToken cancellationToken);
}

public sealed class WindowsNutInstallationRevalidator : IWindowsNutInstallationRevalidator
{
    public Task<NutInstallationInfo> InspectAsync(string installationDirectory, CancellationToken cancellationToken) =>
        new WindowsNutInstallationDetector().InspectDirectoryAsync(installationDirectory, cancellationToken);
}

public sealed class WindowsLocalNutAdministration : ILocalNutWindowsAdministration
{
    private readonly IWindowsNutAdministrationBackend _backend;
    private readonly IWindowsPrivilegeElevationBroker _elevationBroker;
    private readonly IWindowsNutInstallationRevalidator _installationRevalidator;

    public WindowsLocalNutAdministration()
        : this(new WindowsNutAdministrationBackend(), new WindowsPrivilegeElevationBroker(), new WindowsNutInstallationRevalidator()) { }

    public WindowsLocalNutAdministration(IWindowsNutAdministrationBackend backend, IWindowsPrivilegeElevationBroker elevationBroker, IWindowsNutInstallationRevalidator? installationRevalidator = null)
    {
        _backend = backend;
        _elevationBroker = elevationBroker;
        _installationRevalidator = installationRevalidator ?? new WindowsNutInstallationRevalidator();
    }

    public Task<NutWindowsAdministrationSnapshot> InspectAsync(NutInstallationInfo installation, CancellationToken cancellationToken) =>
        OperatingSystem.IsWindows() ? _backend.InspectAsync(installation, cancellationToken) : Task.FromResult(NutWindowsAdministrationSnapshot.Unsupported());

    public async Task<NutAdministrativeActionResult> ExecuteAsync(NutAdministrativeActionRequest request, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new NutAdministrativeActionResult(NutAdministrativeActionStatus.PlatformUnsupported, request.Action, "A administração local do Windows não está disponível nesta plataforma.");
        }

        if (!WindowsNutAdministrativeRequestValidator.IsValid(request))
        {
            return new NutAdministrativeActionResult(NutAdministrativeActionStatus.InvalidRequest, request.Action, "A ação administrativa não é válida para a instalação atual.");
        }

        try
        {
            var detected = await _installationRevalidator.InspectAsync(request.InstallationDirectory, cancellationToken);
            if (!WindowsNutAdministrativeRequestValidator.MatchesDetectedInstallation(detected, request)) return new NutAdministrativeActionResult(NutAdministrativeActionStatus.InvalidRequest, request.Action, "A instalação NUT não corresponde mais ao contexto da ação.", request.ServiceName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new NutAdministrativeActionResult(NutAdministrativeActionStatus.Cancelled, request.Action, "A ação administrativa foi cancelada antes da validação final.", request.ServiceName);
        }
        catch
        {
            return new NutAdministrativeActionResult(NutAdministrativeActionStatus.InvalidRequest, request.Action, "Não foi possível validar a instalação NUT antes da ação.", request.ServiceName);
        }

        return _elevationBroker.GetPrivilegeState() == PrivilegeState.Elevated
            ? await _backend.ExecuteAsync(request, cancellationToken)
            : await _elevationBroker.ExecuteElevatedAsync(request, cancellationToken);
    }

}

public sealed class WindowsNutAdministrationBackend : IWindowsNutAdministrationBackend
{
    public async Task<NutWindowsAdministrationSnapshot> InspectAsync(NutInstallationInfo installation, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return NutWindowsAdministrationSnapshot.Unsupported();
        if (!installation.IsDetected || string.IsNullOrWhiteSpace(installation.InstallationDirectory) || string.IsNullOrWhiteSpace(installation.ConfigurationDirectory))
        {
            // Without a detected installation the permission assessment cannot be produced, but an
            // installed NUT service can still be reported by identity so the user sees its real
            // state. Association stays at name level, which keeps every mutating action disabled.
            const string notSelected = "Nenhuma instalação NUT local foi selecionada.";
            var identityOnly = await WindowsNutServiceController.DiscoverAsync(string.Empty, cancellationToken);
            return new NutWindowsAdministrationSnapshot(true, GetPrivilegeState(), identityOnly.Services,
                NutPermissionAssessment.NotDetermined(notSelected), Array.Empty<NutProcessInfo>(), Array.Empty<NutEventLogEntry>(),
                identityOnly.DiagnosticMessage ?? notSelected)
            {
                ServiceDiscoveryStatus = identityOnly.Status,
                Trace = identityOnly.Trace
            };
        }

        // Each subsystem is isolated. Previously a single throw anywhere below - an ACL read, a
        // process query, the event log - aborted the whole inspection, so the snapshot was never
        // produced and the page reported "no service found" for a machine that has one. Service
        // discovery is the primary information and must survive a failure in the others.
        var failed = new List<string>();

        var discovery = await WindowsNutServiceController.DiscoverAsync(installation.InstallationDirectory, cancellationToken);
        var services = discovery.Services;

        // Written as inline try/catch rather than lambdas so the platform flow analysis established
        // by the OperatingSystem.IsWindows() guard above still applies to each call.
        NutPermissionAssessment permissions;
        try { permissions = WindowsNutPermissions.Assess(installation.ConfigurationDirectory, installation.ConfigurationFiles); }
        catch (OperationCanceledException) { throw; }
        catch { failed.Add("permissões"); permissions = NutPermissionAssessment.NotDetermined("Não foi possível avaliar as permissões."); }

        IReadOnlyList<NutProcessInfo> processes;
        var processesDenied = false;
        try { processes = WindowsNutProcessInspector.Inspect(installation.InstallationDirectory, out processesDenied); }
        catch (OperationCanceledException) { throw; }
        catch { failed.Add("processos"); processes = Array.Empty<NutProcessInfo>(); }

        IReadOnlyList<NutEventLogEntry> events;
        NutEventLogStatus eventStatus;
        string? eventDiagnostic;
        try { (events, eventStatus, eventDiagnostic) = WindowsNutEventLogReader.Read(services, 10); }
        catch (OperationCanceledException) { throw; }
        catch { failed.Add("eventos"); events = Array.Empty<NutEventLogEntry>(); eventStatus = NutEventLogStatus.Failed; eventDiagnostic = "Não foi possível ler o log de eventos."; }

        PrivilegeState privilege;
        try { privilege = GetPrivilegeState(); }
        catch (OperationCanceledException) { throw; }
        catch { failed.Add("privilégio"); privilege = PrivilegeState.Unknown; }

        var diagnostic = failed.Count == 0
            ? discovery.DiagnosticMessage
            : $"{discovery.DiagnosticMessage} (subsistemas indisponíveis: {string.Join(", ", failed)})".TrimStart();

        return new NutWindowsAdministrationSnapshot(true, privilege, services, permissions, processes, events, diagnostic, eventStatus, eventDiagnostic)
        {
            ServiceDiscoveryStatus = discovery.Status,
            Trace = discovery.Trace,
            ProcessInspectionDenied = processesDenied
        };
    }

    public async Task<NutAdministrativeActionResult> ExecuteAsync(NutAdministrativeActionRequest request, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new(NutAdministrativeActionStatus.PlatformUnsupported, request.Action, "A administração local do Windows não está disponível nesta plataforma.");
        var requesterSid = request.Action == NutAdministrativeAction.RepairConfigurationPermissions
            ? TryGetCurrentUserSid()
            : null;
        return await ExecuteAsRequesterAsync(request, requesterSid, cancellationToken);
    }

    internal async Task<NutAdministrativeActionResult> ExecuteAsRequesterAsync(NutAdministrativeActionRequest request, string? requesterSid, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new(NutAdministrativeActionStatus.PlatformUnsupported, request.Action, "A administração local do Windows não está disponível nesta plataforma.");
        if (!WindowsNutAdministrativeRequestValidator.IsValid(request)) return new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "A ação administrativa não é válida para a instalação atual.");
        try
        {
            var detected = await new WindowsNutInstallationDetector().InspectDirectoryAsync(request.InstallationDirectory, cancellationToken);
            if (!WindowsNutAdministrativeRequestValidator.MatchesDetectedInstallation(detected, request))
            {
                return new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "A instalação NUT não corresponde mais ao contexto da ação.", request.ServiceName);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(NutAdministrativeActionStatus.Cancelled, request.Action, "A ação administrativa foi cancelada antes da validação final.", request.ServiceName);
        }
        catch
        {
            return new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "Não foi possível validar a instalação NUT antes da ação.", request.ServiceName);
        }
        if (request.Action == NutAdministrativeAction.RepairConfigurationPermissions && !WindowsNutAdministrativeRequestValidator.IsAuthorizedAclRequester(request, requesterSid))
        {
            return new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "O SID do plano não corresponde ao solicitante autorizado.");
        }
        return request.Action switch
        {
            NutAdministrativeAction.StartService or NutAdministrativeAction.StopService or NutAdministrativeAction.RestartService => await WindowsNutServiceController.ExecuteAsync(request, cancellationToken),
            NutAdministrativeAction.RepairConfigurationPermissions => WindowsNutPermissions.Repair(request, requesterSid),
            _ => new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "A ação administrativa não é permitida.")
        };
    }

    internal static PrivilegeState GetPrivilegeState()
    {
        if (!OperatingSystem.IsWindows()) return PrivilegeState.PlatformUnsupported;
        try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator) ? PrivilegeState.Elevated : PrivilegeState.StandardUser; }
        catch { return PrivilegeState.Unknown; }
    }

    [SupportedOSPlatform("windows")]
    internal static string? TryGetCurrentUserSid()
    {
        try { return WindowsIdentity.GetCurrent().User?.Value; }
        catch { return null; }
    }
}

public static class WindowsNutAdministrativeRequestValidator
{
    private static readonly string[] RecognizedConfigurationFiles = ["nut.conf", "ups.conf", "upsd.conf", "upsd.users", "upsmon.conf"];
    public static bool IsPathInsideDirectory(string candidate, string directory)
        => WindowsPath.IsInside(candidate, directory);

    public static bool IsValid(NutAdministrativeActionRequest request)
    {
        if (!Enum.IsDefined(request.Action) || request.RequestId == Guid.Empty || !WindowsPath.TryCanonicalize(request.InstallationDirectory, out var install) || !WindowsPath.TryCanonicalize(request.ConfigurationDirectory, out var config)) return false;
        if (!WindowsPath.IsSameOrInside(config, install)) return false;
        return request.Action == NutAdministrativeAction.RepairConfigurationPermissions
            ? request.PermissionRepairPlan is { UserSid.Length: > 0, Right: "Modify" } plan &&
              IsSidLike(plan.UserSid) &&
              plan.EffectiveIdentitySids is { Count: > 0 } identities &&
              identities.All(IsSidLike) &&
              identities.Contains(plan.UserSid, StringComparer.OrdinalIgnoreCase) &&
              WindowsPath.TryCanonicalize(plan.ConfigurationDirectory, out var planDirectory) &&
              string.Equals(planDirectory, config, StringComparison.OrdinalIgnoreCase) &&
              plan.AffectedPaths.All(path => IsRecognizedConfigurationTarget(path, config))
            : !string.IsNullOrWhiteSpace(request.ServiceName) && request.PermissionRepairPlan is null;
    }

    public static bool MatchesDetectedInstallation(NutInstallationInfo detected, NutAdministrativeActionRequest request) =>
        detected.IsDetected &&
        WindowsPath.TryCanonicalize(detected.InstallationDirectory, out var detectedInstallation) &&
        WindowsPath.TryCanonicalize(detected.ConfigurationDirectory, out var detectedConfiguration) &&
        WindowsPath.TryCanonicalize(request.InstallationDirectory, out var requestedInstallation) &&
        WindowsPath.TryCanonicalize(request.ConfigurationDirectory, out var requestedConfiguration) &&
        string.Equals(detectedInstallation, requestedInstallation, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(detectedConfiguration, requestedConfiguration, StringComparison.OrdinalIgnoreCase);

    public static bool IsAuthorizedAclRequester(NutAdministrativeActionRequest request, string? requesterSid) =>
        request.Action == NutAdministrativeAction.RepairConfigurationPermissions &&
        request.PermissionRepairPlan is { } plan &&
        !string.IsNullOrWhiteSpace(requesterSid) &&
        string.Equals(plan.UserSid, requesterSid, StringComparison.OrdinalIgnoreCase) &&
        plan.EffectiveIdentitySids?.Contains(requesterSid, StringComparer.OrdinalIgnoreCase) == true;

    private static bool IsRecognizedConfigurationTarget(string path, string configurationDirectory)
    {
        if (!WindowsPath.TryCanonicalize(path, out var target)) return false;
        if (string.Equals(target, configurationDirectory, StringComparison.OrdinalIgnoreCase)) return true;
        var separator = target.LastIndexOf('\\');
        return separator > 2 && string.Equals(target[..separator], configurationDirectory, StringComparison.OrdinalIgnoreCase) && RecognizedConfigurationFiles.Contains(target[(separator + 1)..], StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSidLike(string value)
    {
        var parts = value.Split('-', StringSplitOptions.None);
        return parts.Length >= 3 && parts[0].Equals("S", StringComparison.OrdinalIgnoreCase) && parts.Skip(1).All(part => uint.TryParse(part, out _));
    }
}

[SupportedOSPlatform("windows")]
internal static class WindowsNutServiceController
{
    public static Task<NutServiceDiscoveryResult> DiscoverAsync(string installationDirectory, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var all = System.ServiceProcess.ServiceController.GetServices();

            // Resolve the exact known identity first: it is deterministic and independent of how
            // the rest of the machine's services happen to be named.
            var exact = all.FirstOrDefault(service =>
                WindowsNutServiceAssociation.IsKnownIdentity(service.ServiceName, service.DisplayName));

            var evaluated = all.Select(service => CreateInfo(service, installationDirectory)).ToArray();
            var services = evaluated.Where(service => service.IsAssociated).ToArray();
            var candidate = services.FirstOrDefault()
                ?? evaluated.FirstOrDefault(info => WindowsNutServiceAssociation.IsKnownIdentity(info.ServiceName, info.DisplayName));

            var trace = new NutServiceDiscoveryTrace(
                PlatformSupported: true,
                EnumerationSucceeded: true,
                EnumeratedServiceCount: all.Length,
                ExactKnownServiceFound: exact is not null,
                CandidateServiceName: candidate?.ServiceName ?? exact?.ServiceName,
                CandidateDisplayName: candidate?.DisplayName ?? exact?.DisplayName,
                CandidateExecutable: candidate?.BinaryPath,
                NormalizedExecutable: candidate?.BinaryPath,
                InstallationRoot: string.IsNullOrWhiteSpace(installationDirectory) ? null : installationDirectory,
                ContainmentResult: candidate?.BinaryPath is { } path && !string.IsNullOrWhiteSpace(installationDirectory)
                    ? WindowsPath.IsInside(path, installationDirectory)
                    : null,
                Association: candidate?.AssociationConfidence ?? NutAssociationConfidence.None,
                FailureReason: services.Length > 0 ? null
                    : exact is null ? "Nenhum serviço com identidade NUT conhecida apareceu na enumeração do SCM."
                    : "O serviço com identidade NUT foi encontrado, mas não passou na validação de associação.");

            return new NutServiceDiscoveryResult(services, NutServiceDiscoveryStatus.Completed) { Trace = trace };
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            // Insufficient rights to query the SCM is not the same as "no service installed".
            return new NutServiceDiscoveryResult(Array.Empty<NutServiceInfo>(), NutServiceDiscoveryStatus.AccessDenied,
                "Permissão insuficiente para consultar os serviços do Windows.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new NutServiceDiscoveryResult(Array.Empty<NutServiceInfo>(), NutServiceDiscoveryStatus.QueryFailed,
                "Não foi possível consultar os serviços do Windows.");
        }
    }, cancellationToken);

    public static async Task<NutAdministrativeActionResult> ExecuteAsync(NutAdministrativeActionRequest request, CancellationToken cancellationToken)
    {
        var mutationStarted = false;
        try
        {
            using var service = new System.ServiceProcess.ServiceController(request.ServiceName!);
            service.Refresh();
            var info = CreateInfo(service, request.InstallationDirectory);
            if (info.AssociationConfidence != NutAssociationConfidence.BinaryPath) return new(NutAdministrativeActionStatus.ServiceNotAssociated, request.Action, "O serviço não está associado à instalação NUT atual.", request.ServiceName);
            var current = ToState(service.Status);
            if (request.Action == NutAdministrativeAction.StartService && current == NutServiceState.Running) return new(NutAdministrativeActionStatus.AlreadyInRequestedState, request.Action, "O serviço já está em execução.", request.ServiceName);
            if (request.Action == NutAdministrativeAction.StopService && current == NutServiceState.Stopped) return new(NutAdministrativeActionStatus.AlreadyInRequestedState, request.Action, "O serviço já está parado.", request.ServiceName);
            if (request.Action is NutAdministrativeAction.StopService or NutAdministrativeAction.RestartService && current != NutServiceState.Stopped)
            {
                cancellationToken.ThrowIfCancellationRequested();
                service.Stop();
                mutationStarted = true;
                await WaitAsync(service, System.ServiceProcess.ServiceControllerStatus.Stopped);
            }
            if (request.Action is NutAdministrativeAction.StartService or NutAdministrativeAction.RestartService)
            {
                if (!mutationStarted) cancellationToken.ThrowIfCancellationRequested();
                service.Start();
                mutationStarted = true;
                await WaitAsync(service, System.ServiceProcess.ServiceControllerStatus.Running);
            }
            return new(NutAdministrativeActionStatus.Success, request.Action, "A ação administrativa foi concluída.", request.ServiceName);
        }
        catch (System.ServiceProcess.TimeoutException) { return new(NutAdministrativeActionStatus.Timeout, request.Action, mutationStarted ? "A ação iniciou, mas o serviço não alcançou o estado esperado. Atualize o estado antes de tentar novamente." : "O serviço não alcançou o estado esperado no tempo limite.", request.ServiceName); }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 5) { return new(NutAdministrativeActionStatus.AccessDenied, request.Action, "Permissão insuficiente para controlar o serviço.", request.ServiceName); }
        catch (InvalidOperationException) { return new(NutAdministrativeActionStatus.ServiceNotFound, request.Action, "O serviço não foi encontrado.", request.ServiceName); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !mutationStarted) { return new(NutAdministrativeActionStatus.Cancelled, request.Action, "A ação administrativa foi cancelada.", request.ServiceName); }
        catch { return new(NutAdministrativeActionStatus.Failed, request.Action, "Não foi possível executar a ação administrativa.", request.ServiceName); }
    }

    private static async Task WaitAsync(System.ServiceProcess.ServiceController service, System.ServiceProcess.ServiceControllerStatus status)
    {
        await Task.Run(() => service.WaitForStatus(status, TimeSpan.FromSeconds(30)), CancellationToken.None);
    }
    private static NutServiceInfo CreateInfo(System.ServiceProcess.ServiceController service, string installationDirectory)
    {
        TryGetMetadata(service.ServiceName, out var imagePath, out var startMode);
        var (binaryPath, confidence) = WindowsNutServiceAssociation.Determine(service.ServiceName, service.DisplayName, imagePath, installationDirectory);
        return new(service.ServiceName, service.DisplayName, ToState(service.Status), startMode, binaryPath, confidence);
    }
    private static void TryGetMetadata(string serviceName, out string? imagePath, out NutServiceStartMode startMode)
    {
        imagePath = null; startMode = NutServiceStartMode.Unknown;
        try { using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}"); imagePath = key?.GetValue("ImagePath") as string; startMode = Convert.ToInt32(key?.GetValue("Start") ?? -1) switch { 2 => NutServiceStartMode.Automatic, 3 => NutServiceStartMode.Manual, 4 => NutServiceStartMode.Disabled, _ => NutServiceStartMode.Unknown }; } catch { }
    }
    private static NutServiceState ToState(System.ServiceProcess.ServiceControllerStatus status) => status switch { System.ServiceProcess.ServiceControllerStatus.Running => NutServiceState.Running, System.ServiceProcess.ServiceControllerStatus.Stopped => NutServiceState.Stopped, System.ServiceProcess.ServiceControllerStatus.StartPending => NutServiceState.StartPending, System.ServiceProcess.ServiceControllerStatus.StopPending => NutServiceState.StopPending, System.ServiceProcess.ServiceControllerStatus.Paused => NutServiceState.Paused, _ => NutServiceState.Unknown };
}

public static class WindowsNutServiceAssociation
{
    // The Windows installer registers the service as "Network UPS Tools" for both the service name
    // and the display name; the compact spellings are kept for other packagings.
    private static readonly string[] KnownServiceNames = ["Network UPS Tools", "NetworkUpsTools", "NetworkUPSTools", "NUT"];
    private static readonly string[] KnownDisplayNames = ["Network UPS Tools"];
    private static readonly string[] RecognizedExecutables = ["nut.exe", "upsd.exe", "upsmon.exe", "upsdrvctl.exe"];

    public static (string? BinaryPath, NutAssociationConfidence Confidence) Determine(string serviceName, string displayName, string? imagePath, string installationDirectory)
    {
        var executable = TryExtractExecutablePath(imagePath);
        var binaryPath = executable is not null && WindowsPath.TryCanonicalize(executable, out var canonicalExecutable) ? canonicalExecutable : null;

        var matchesKnownIdentity = IsKnownIdentity(serviceName, displayName);

        // When a binary resolves *and* there is a detected installation to compare it against,
        // containment is authoritative: a service that merely borrows a known NUT name while
        // pointing somewhere else must not be associated. This is the anti name-squatting rule.
        if (binaryPath is not null && !string.IsNullOrWhiteSpace(installationDirectory))
        {
            if (!WindowsPath.IsInside(binaryPath, installationDirectory)) return (binaryPath, NutAssociationConfidence.None);

            // Inside the trusted root, either a known service identity or a known NUT executable is
            // enough. The executable list alone used to decide, which rejected a genuinely installed
            // service whenever the packaging named its binary differently.
            var name = binaryPath[(binaryPath.LastIndexOf('\\') + 1)..];
            return (binaryPath, matchesKnownIdentity || RecognizedExecutables.Contains(name, StringComparer.OrdinalIgnoreCase)
                ? NutAssociationConfidence.BinaryPath
                : NutAssociationConfidence.None);
        }

        // No binary to inspect, or no installation to verify it against. Fall back to the exact
        // known identities so an installed service is still reported, with the weaker confidence
        // that keeps every mutating action disabled.
        return (binaryPath, matchesKnownIdentity ? NutAssociationConfidence.NameFallback : NutAssociationConfidence.None);
    }

    /// <summary>Exact, case-insensitive match against the service names NutManager recognises.</summary>
    public static bool IsKnownIdentity(string? serviceName, string? displayName) =>
        (serviceName is not null && KnownServiceNames.Contains(serviceName, StringComparer.OrdinalIgnoreCase)) ||
        (displayName is not null && KnownDisplayNames.Contains(displayName, StringComparer.OrdinalIgnoreCase));

    public static string? TryExtractExecutablePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(imagePath.Trim());
        if (expanded[0] == '"') { var end = expanded.IndexOf('"', 1); return end > 1 ? expanded[1..end] : null; }
        var exe = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); return exe >= 0 ? expanded[..(exe + 4)] : null;
    }
}

[SupportedOSPlatform("windows")]
internal static class WindowsNutPermissions
{
    private static readonly string[] RecognizedConfigurationFiles = ["nut.conf", "ups.conf", "upsd.conf", "upsd.users", "upsmon.conf"];

    public static NutPermissionAssessment Assess(string directory, IReadOnlyList<NutConfigurationFileInfo> files)
    {
        if (!OperatingSystem.IsWindows()) return NutPermissionAssessment.Unsupported();
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value;
            if (sid is null) return new(NutPermissionState.Unknown, identity.Name, null, false, "Não foi possível determinar o usuário atual.", Array.Empty<string>());
            var identities = identity.Groups?.Select(group => group.Value).Append(sid).Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>([sid], StringComparer.OrdinalIgnoreCase);
            return AssessForIdentities(directory, files.Where(file => file.Exists).Select(file => file.FullPath).ToArray(), identity.Name, sid, identities, new WindowsNativeAclAccessor());
        }
        catch (UnauthorizedAccessException) { return new(NutPermissionState.AccessDenied, null, null, false, "Não foi possível ler as permissões.", Array.Empty<string>()); }
        catch { return new(NutPermissionState.Unknown, null, null, false, "Não foi possível determinar as permissões efetivas.", Array.Empty<string>()); }
    }

    public static NutAdministrativeActionResult Repair(NutAdministrativeActionRequest request, string? requesterSid)
    {
        var plan = request.PermissionRepairPlan!;
        if (!WindowsNutAdministrativeRequestValidator.IsAuthorizedAclRequester(request, requesterSid))
        {
            return new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "O SID do plano não corresponde ao solicitante autorizado.");
        }
        if (!TryGetAllowedTargets(request, plan, out var targets)) return new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "O plano contém alvos de ACL não reconhecidos.");
        var identities = (plan.EffectiveIdentitySids ?? [plan.UserSid]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!identities.Contains(plan.UserSid)) return new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "As identidades efetivas não incluem o SID do solicitante.");
        return WindowsAclRepairTransaction.Apply(new WindowsNativeAclAccessor(), targets, plan.UserSid, identities, request.Action);
    }

    public static NutPermissionAssessment AssessForIdentities(
        string directory,
        IReadOnlyList<string> files,
        string? identity,
        string userSid,
        IReadOnlySet<string> effectiveIdentitySids,
        IWindowsAclAccessor accessor)
    {
        var targets = files.Select(path => new WindowsAclTarget(path, false)).Prepend(new WindowsAclTarget(directory, true)).ToArray();
        try
        {
            var states = targets.Select(target => WindowsAclPermissionEvaluation.Assess(accessor.GetRules(accessor.CaptureSecurity(target)), effectiveIdentitySids)).ToArray();
            var paths = targets.Select(target => target.Path).ToArray();
            if (states.Any(state => state == NutPermissionState.ManualInterventionRequired)) return new(NutPermissionState.ManualInterventionRequired, identity, userSid, true, "Há uma negação explícita relevante que exige intervenção manual.", paths, effectiveIdentitySids.ToArray());
            return states.All(state => state == NutPermissionState.Modifiable)
                ? new(NutPermissionState.Modifiable, identity, userSid, false, "O usuário atual possui Modify confirmado para o diretório e os arquivos reconhecidos.", paths, effectiveIdentitySids.ToArray())
                : new(NutPermissionState.Insufficient, identity, userSid, false, "O usuário atual não possui Modify confirmado para todos os alvos.", paths, effectiveIdentitySids.ToArray());
        }
        catch (UnauthorizedAccessException) { return new(NutPermissionState.AccessDenied, identity, userSid, false, "Não foi possível ler as permissões.", targets.Select(target => target.Path).ToArray(), effectiveIdentitySids.ToArray()); }
        catch { return new(NutPermissionState.Unknown, identity, userSid, false, "Não foi possível determinar as permissões efetivas.", targets.Select(target => target.Path).ToArray(), effectiveIdentitySids.ToArray()); }
    }

    private static bool TryGetAllowedTargets(NutAdministrativeActionRequest request, NutPermissionRepairPlan plan, out IReadOnlyList<WindowsAclTarget> targets)
    {
        targets = Array.Empty<WindowsAclTarget>();
        if (plan.Right != "Modify" || !WindowsPath.TryCanonicalize(request.ConfigurationDirectory, out var config) || !WindowsPath.TryCanonicalize(plan.ConfigurationDirectory, out var planDirectory) || !string.Equals(config, planDirectory, StringComparison.OrdinalIgnoreCase)) return false;
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { config };
        foreach (var name in RecognizedConfigurationFiles)
        {
            var candidate = config + "\\" + name;
            if (File.Exists(candidate)) allowed.Add(candidate);
        }
        var requested = plan.AffectedPaths.Select(path => WindowsPath.TryCanonicalize(path, out var canonical) ? canonical : null).ToArray();
        if (requested.Any(path => path is null) || requested.Length == 0 || requested.Any(path => !allowed.Contains(path!))) return false;
        targets = requested.Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).Select(path => new WindowsAclTarget(path, string.Equals(path, config, StringComparison.OrdinalIgnoreCase))).ToArray();
        return true;
    }
}

internal static class WindowsNutProcessInspector
{
    /// <summary>
    /// Associates running processes with the installation by executable path. Reading a process's
    /// main module requires rights the current session may not have: NUT runs as LocalSystem, so an
    /// unelevated NutManager cannot inspect it. <paramref name="accessDenied"/> reports that case so
    /// the UI can say "cannot inspect" instead of asserting that no NUT process is running.
    /// </summary>
    public static IReadOnlyList<NutProcessInfo> Inspect(string installationDirectory, out bool accessDenied)
    {
        var denied = false;
        var processes = Process.GetProcesses().Select(process =>
        {
            try
            {
                using (process)
                {
                    var path = process.MainModule?.FileName;
                    return new NutProcessInfo(process.ProcessName, process.Id, path,
                        path is not null && WindowsNutAdministrativeRequestValidator.IsPathInsideDirectory(path, installationDirectory)
                            ? NutAssociationConfidence.BinaryPath
                            : NutAssociationConfidence.None);
                }
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // The process exists but its module list is not readable from this session.
                denied = true;
                return new NutProcessInfo(process.ProcessName, process.Id, null, NutAssociationConfidence.None);
            }
            catch
            {
                return new NutProcessInfo(process.ProcessName, process.Id, null, NutAssociationConfidence.None);
            }
        }).Where(process => process.AssociationConfidence != NutAssociationConfidence.None).ToArray();

        accessDenied = denied && processes.Length == 0;
        return processes;
    }
}

[SupportedOSPlatform("windows")]
internal static class WindowsNutEventLogReader
{
    public static (IReadOnlyList<NutEventLogEntry> Events, NutEventLogStatus Status, string? DiagnosticMessage) Read(IReadOnlyList<NutServiceInfo> services, int limit)
    {
        var names = services.Select(service => service.ServiceName).Append("Network UPS Tools").ToArray();
        var events = new List<NutEventLogEntry>();
        foreach (var logName in new[] { "System", "Application" })
        {
            try
            {
                using var log = new System.Diagnostics.EventLog(logName);
                foreach (System.Diagnostics.EventLogEntry entry in log.Entries.Cast<System.Diagnostics.EventLogEntry>().Reverse())
                {
                    if (!names.Any(name => entry.Source.Contains(name, StringComparison.OrdinalIgnoreCase) || entry.Message.Contains(name, StringComparison.OrdinalIgnoreCase))) continue;
                    events.Add(new NutEventLogEntry(entry.TimeGenerated, logName, entry.Source, (int)entry.InstanceId, entry.EntryType.ToString(), entry.Message));
                    if (events.Count >= limit) return (events, NutEventLogStatus.Success, null);
                }
            }
            catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 5) { return (events, NutEventLogStatus.AccessDenied, "Não foi possível ler o Event Log por falta de permissão."); }
            catch (UnauthorizedAccessException) { return (events, NutEventLogStatus.AccessDenied, "Não foi possível ler o Event Log por falta de permissão."); }
            catch (InvalidOperationException) { return (events, NutEventLogStatus.Unavailable, "O Event Log não está disponível nesta instalação."); }
            catch { return (events, NutEventLogStatus.Failed, "Não foi possível consultar o Event Log."); }
        }
        return (events, NutEventLogStatus.Success, null);
    }
}
