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

public sealed class WindowsLocalNutAdministration : ILocalNutWindowsAdministration
{
    private readonly IWindowsNutAdministrationBackend _backend;
    private readonly IWindowsPrivilegeElevationBroker _elevationBroker;

    public WindowsLocalNutAdministration()
        : this(new WindowsNutAdministrationBackend(), new WindowsPrivilegeElevationBroker()) { }

    public WindowsLocalNutAdministration(IWindowsNutAdministrationBackend backend, IWindowsPrivilegeElevationBroker elevationBroker)
    {
        _backend = backend;
        _elevationBroker = elevationBroker;
    }

    public Task<NutWindowsAdministrationSnapshot> InspectAsync(NutInstallationInfo installation, CancellationToken cancellationToken) =>
        OperatingSystem.IsWindows() ? _backend.InspectAsync(installation, cancellationToken) : Task.FromResult(NutWindowsAdministrationSnapshot.Unsupported());

    public Task<NutAdministrativeActionResult> ExecuteAsync(NutAdministrativeActionRequest request, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new NutAdministrativeActionResult(NutAdministrativeActionStatus.PlatformUnsupported, request.Action, "A administração local do Windows não está disponível nesta plataforma."));
        }

        if (!WindowsNutAdministrativeRequestValidator.IsValid(request))
        {
            return Task.FromResult(new NutAdministrativeActionResult(NutAdministrativeActionStatus.InvalidRequest, request.Action, "A ação administrativa não é válida para a instalação atual."));
        }

        return _elevationBroker.GetPrivilegeState() == PrivilegeState.Elevated
            ? _backend.ExecuteAsync(request, cancellationToken)
            : _elevationBroker.ExecuteElevatedAsync(request, cancellationToken);
    }
}

public sealed class WindowsNutAdministrationBackend : IWindowsNutAdministrationBackend
{
    public async Task<NutWindowsAdministrationSnapshot> InspectAsync(NutInstallationInfo installation, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return NutWindowsAdministrationSnapshot.Unsupported();
        if (!installation.IsDetected || string.IsNullOrWhiteSpace(installation.InstallationDirectory) || string.IsNullOrWhiteSpace(installation.ConfigurationDirectory))
        {
            return new NutWindowsAdministrationSnapshot(true, GetPrivilegeState(), Array.Empty<NutServiceInfo>(), NutPermissionAssessment.Unsupported(), Array.Empty<NutProcessInfo>(), Array.Empty<NutEventLogEntry>(), "Nenhuma instalação NUT local foi selecionada.");
        }

        var services = await WindowsNutServiceController.DiscoverAsync(installation.InstallationDirectory, cancellationToken);
        var permissions = WindowsNutPermissions.Assess(installation.ConfigurationDirectory, installation.ConfigurationFiles);
        var processes = WindowsNutProcessInspector.Inspect(installation.InstallationDirectory);
        var events = WindowsNutEventLogReader.Read(services, 50);
        return new NutWindowsAdministrationSnapshot(true, GetPrivilegeState(), services, permissions, processes, events);
    }

    public async Task<NutAdministrativeActionResult> ExecuteAsync(NutAdministrativeActionRequest request, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new(NutAdministrativeActionStatus.PlatformUnsupported, request.Action, "A administração local do Windows não está disponível nesta plataforma.");
        if (!WindowsNutAdministrativeRequestValidator.IsValid(request)) return new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "A ação administrativa não é válida para a instalação atual.");
        return request.Action switch
        {
            NutAdministrativeAction.StartService or NutAdministrativeAction.StopService or NutAdministrativeAction.RestartService => await WindowsNutServiceController.ExecuteAsync(request, cancellationToken),
            NutAdministrativeAction.RepairConfigurationPermissions => WindowsNutPermissions.Repair(request),
            _ => new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "A ação administrativa não é permitida.")
        };
    }

    internal static PrivilegeState GetPrivilegeState()
    {
        if (!OperatingSystem.IsWindows()) return PrivilegeState.PlatformUnsupported;
        try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator) ? PrivilegeState.Elevated : PrivilegeState.StandardUser; }
        catch { return PrivilegeState.Unknown; }
    }
}

public static class WindowsNutAdministrativeRequestValidator
{
    public static bool IsPathInsideDirectory(string candidate, string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(candidate);
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValid(NutAdministrativeActionRequest request)
    {
        if (!Enum.IsDefined(request.Action) || request.RequestId == Guid.Empty || !Path.IsPathFullyQualified(request.InstallationDirectory) || !Path.IsPathFullyQualified(request.ConfigurationDirectory)) return false;
        var install = Path.GetFullPath(request.InstallationDirectory);
        var config = Path.GetFullPath(request.ConfigurationDirectory);
        if (!IsPathInsideDirectory(config, install) && !string.Equals(config, install, StringComparison.OrdinalIgnoreCase)) return false;
        return request.Action == NutAdministrativeAction.RepairConfigurationPermissions
            ? request.PermissionRepairPlan is { UserSid.Length: > 0, Right: "Modify" } plan && string.Equals(Path.GetFullPath(plan.ConfigurationDirectory), config, StringComparison.OrdinalIgnoreCase) && plan.AffectedPaths.All(path => IsPathInsideDirectory(path, config) || string.Equals(Path.GetFullPath(path), config, StringComparison.OrdinalIgnoreCase))
            : !string.IsNullOrWhiteSpace(request.ServiceName) && request.PermissionRepairPlan is null;
    }
}

[SupportedOSPlatform("windows")]
internal static class WindowsNutServiceController
{
    private static readonly string[] KnownServiceNames = ["NetworkUpsTools", "NUT"];
    private static readonly string[] KnownDisplayNames = ["Network UPS Tools"];

    public static Task<IReadOnlyList<NutServiceInfo>> DiscoverAsync(string installationDirectory, CancellationToken cancellationToken) => Task.Run<IReadOnlyList<NutServiceInfo>>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return System.ServiceProcess.ServiceController.GetServices()
                .Select(service => CreateInfo(service, installationDirectory))
                .Where(service => service.IsAssociated)
                .ToArray();
        }
        catch { return Array.Empty<NutServiceInfo>(); }
    }, cancellationToken);

    public static async Task<NutAdministrativeActionResult> ExecuteAsync(NutAdministrativeActionRequest request, CancellationToken cancellationToken)
    {
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
                service.Stop();
                await WaitAsync(service, System.ServiceProcess.ServiceControllerStatus.Stopped, cancellationToken);
            }
            if (request.Action is NutAdministrativeAction.StartService or NutAdministrativeAction.RestartService)
            {
                service.Start();
                await WaitAsync(service, System.ServiceProcess.ServiceControllerStatus.Running, cancellationToken);
            }
            return new(NutAdministrativeActionStatus.Success, request.Action, "A ação administrativa foi concluída.", request.ServiceName);
        }
        catch (System.ServiceProcess.TimeoutException) { return new(NutAdministrativeActionStatus.Timeout, request.Action, "O serviço não alcançou o estado esperado no tempo limite.", request.ServiceName); }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 5) { return new(NutAdministrativeActionStatus.AccessDenied, request.Action, "Permissão insuficiente para controlar o serviço.", request.ServiceName); }
        catch (InvalidOperationException) { return new(NutAdministrativeActionStatus.ServiceNotFound, request.Action, "O serviço não foi encontrado.", request.ServiceName); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new(NutAdministrativeActionStatus.Cancelled, request.Action, "A ação administrativa foi cancelada.", request.ServiceName); }
        catch { return new(NutAdministrativeActionStatus.Failed, request.Action, "Não foi possível executar a ação administrativa.", request.ServiceName); }
    }

    private static async Task WaitAsync(System.ServiceProcess.ServiceController service, System.ServiceProcess.ServiceControllerStatus status, CancellationToken cancellationToken)
    {
        await Task.Run(() => service.WaitForStatus(status, TimeSpan.FromSeconds(30)), cancellationToken);
    }
    private static NutServiceInfo CreateInfo(System.ServiceProcess.ServiceController service, string installationDirectory)
    {
        TryGetMetadata(service.ServiceName, out var imagePath, out var startMode);
        var executable = TryExtractExecutablePath(imagePath);
        var binaryPath = executable is not null ? Path.GetFullPath(executable) : null;
        var isKnownComponent = binaryPath is not null && new[] { "upsd.exe", "upsmon.exe", "upsdrvctl.exe" }.Contains(Path.GetFileName(binaryPath), StringComparer.OrdinalIgnoreCase);
        var confidence = binaryPath is not null && isKnownComponent && WindowsNutAdministrativeRequestValidator.IsPathInsideDirectory(binaryPath, installationDirectory)
            ? NutAssociationConfidence.BinaryPath
            : binaryPath is null && IsExactKnownName(service) ? NutAssociationConfidence.NameFallback : NutAssociationConfidence.None;
        return new(service.ServiceName, service.DisplayName, ToState(service.Status), startMode, binaryPath, confidence);
    }
    private static bool IsExactKnownName(System.ServiceProcess.ServiceController service) => KnownServiceNames.Contains(service.ServiceName, StringComparer.OrdinalIgnoreCase) || KnownDisplayNames.Contains(service.DisplayName, StringComparer.OrdinalIgnoreCase);
    private static void TryGetMetadata(string serviceName, out string? imagePath, out NutServiceStartMode startMode)
    {
        imagePath = null; startMode = NutServiceStartMode.Unknown;
        try { using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}"); imagePath = key?.GetValue("ImagePath") as string; startMode = Convert.ToInt32(key?.GetValue("Start") ?? -1) switch { 2 => NutServiceStartMode.Automatic, 3 => NutServiceStartMode.Manual, 4 => NutServiceStartMode.Disabled, _ => NutServiceStartMode.Unknown }; } catch { }
    }
    internal static string? TryExtractExecutablePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(imagePath.Trim());
        if (expanded[0] == '"') { var end = expanded.IndexOf('"', 1); return end > 1 ? expanded[1..end] : null; }
        var exe = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); return exe >= 0 ? expanded[..(exe + 4)] : null;
    }
    private static NutServiceState ToState(System.ServiceProcess.ServiceControllerStatus status) => status switch { System.ServiceProcess.ServiceControllerStatus.Running => NutServiceState.Running, System.ServiceProcess.ServiceControllerStatus.Stopped => NutServiceState.Stopped, System.ServiceProcess.ServiceControllerStatus.StartPending => NutServiceState.StartPending, System.ServiceProcess.ServiceControllerStatus.StopPending => NutServiceState.StopPending, System.ServiceProcess.ServiceControllerStatus.Paused => NutServiceState.Paused, _ => NutServiceState.Unknown };
}

[SupportedOSPlatform("windows")]
internal static class WindowsNutPermissions
{
    public static NutPermissionAssessment Assess(string directory, IReadOnlyList<NutConfigurationFileInfo> files)
    {
        if (!OperatingSystem.IsWindows()) return NutPermissionAssessment.Unsupported();
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value;
            if (sid is null) return new(NutPermissionState.Unknown, identity.Name, null, false, "Não foi possível determinar o usuário atual.", Array.Empty<string>());
            var rules = new DirectoryInfo(directory).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>().Where(rule => string.Equals(rule.IdentityReference.Value, sid, StringComparison.OrdinalIgnoreCase)).ToArray();
            var deny = rules.Any(rule => rule.AccessControlType == AccessControlType.Deny && (rule.FileSystemRights & FileSystemRights.Modify) != 0);
            var modify = rules.Any(rule => rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & FileSystemRights.Modify) != 0);
            var paths = files.Where(file => file.Exists).Select(file => file.FullPath).Prepend(directory).ToArray();
            return deny ? new(NutPermissionState.ManualInterventionRequired, identity.Name, sid, true, "Há uma negação explícita que exige intervenção manual.", paths) : modify ? new(NutPermissionState.Modifiable, identity.Name, sid, false, "O usuário atual possui Modify.", paths) : new(NutPermissionState.Insufficient, identity.Name, sid, false, "O usuário atual não possui Modify confirmado.", paths);
        }
        catch (UnauthorizedAccessException) { return new(NutPermissionState.AccessDenied, null, null, false, "Não foi possível ler as permissões.", Array.Empty<string>()); }
        catch { return new(NutPermissionState.Unknown, null, null, false, "Não foi possível determinar as permissões efetivas.", Array.Empty<string>()); }
    }

    public static NutAdministrativeActionResult Repair(NutAdministrativeActionRequest request)
    {
        var plan = request.PermissionRepairPlan!;
        try
        {
            var sid = new SecurityIdentifier(plan.UserSid);
            foreach (var path in plan.AffectedPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Directory.Exists(path))
                {
                    var directory = new DirectoryInfo(path); var security = directory.GetAccessControl(); security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.Modify, AccessControlType.Allow)); directory.SetAccessControl(security);
                }
                else if (File.Exists(path))
                {
                    var file = new FileInfo(path); var security = file.GetAccessControl(); security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.Modify, AccessControlType.Allow)); file.SetAccessControl(security);
                }
            }
            return new(NutAdministrativeActionStatus.Success, request.Action, "A permissão Modify foi adicionada sem substituir ACLs existentes.");
        }
        catch (UnauthorizedAccessException) { return new(NutAdministrativeActionStatus.AccessDenied, request.Action, "Permissão insuficiente para ajustar ACL."); }
        catch { return new(NutAdministrativeActionStatus.Failed, request.Action, "Não foi possível ajustar as permissões."); }
    }
}

internal static class WindowsNutProcessInspector
{
    public static IReadOnlyList<NutProcessInfo> Inspect(string installationDirectory) => Process.GetProcesses().Select(process =>
    {
        try { using (process) { var path = process.MainModule?.FileName; return new NutProcessInfo(process.ProcessName, process.Id, path, path is not null && WindowsNutAdministrativeRequestValidator.IsPathInsideDirectory(path, installationDirectory) ? NutAssociationConfidence.BinaryPath : NutAssociationConfidence.None); } }
        catch { return new NutProcessInfo(process.ProcessName, process.Id, null, NutAssociationConfidence.None); }
    }).Where(process => process.AssociationConfidence != NutAssociationConfidence.None).ToArray();
}

[SupportedOSPlatform("windows")]
internal static class WindowsNutEventLogReader
{
    public static IReadOnlyList<NutEventLogEntry> Read(IReadOnlyList<NutServiceInfo> services, int limit)
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
                    if (events.Count >= limit) return events;
                }
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidOperationException) { }
        }
        return events;
    }
}
