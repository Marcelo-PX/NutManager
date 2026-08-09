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
        var eventResult = WindowsNutEventLogReader.Read(services, 50);
        return new NutWindowsAdministrationSnapshot(true, GetPrivilegeState(), services, permissions, processes, eventResult.Events, EventLogStatus: eventResult.Status, EventLogDiagnosticMessage: eventResult.DiagnosticMessage);
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
    private static readonly string[] RecognizedConfigurationFiles = ["nut.conf", "ups.conf", "upsd.conf", "upsd.users", "upsmon.conf"];
    public static bool IsPathInsideDirectory(string candidate, string directory)
        => WindowsPath.IsInside(candidate, directory);

    public static bool IsValid(NutAdministrativeActionRequest request)
    {
        if (!Enum.IsDefined(request.Action) || request.RequestId == Guid.Empty || !WindowsPath.TryCanonicalize(request.InstallationDirectory, out var install) || !WindowsPath.TryCanonicalize(request.ConfigurationDirectory, out var config)) return false;
        if (!WindowsPath.IsSameOrInside(config, install)) return false;
        return request.Action == NutAdministrativeAction.RepairConfigurationPermissions
            ? request.PermissionRepairPlan is { UserSid.Length: > 0, Right: "Modify" } plan && WindowsPath.TryCanonicalize(plan.ConfigurationDirectory, out var planDirectory) && string.Equals(planDirectory, config, StringComparison.OrdinalIgnoreCase) && plan.AffectedPaths.All(path => IsRecognizedConfigurationTarget(path, config))
            : !string.IsNullOrWhiteSpace(request.ServiceName) && request.PermissionRepairPlan is null;
    }

    private static bool IsRecognizedConfigurationTarget(string path, string configurationDirectory)
    {
        if (!WindowsPath.TryCanonicalize(path, out var target)) return false;
        if (string.Equals(target, configurationDirectory, StringComparison.OrdinalIgnoreCase)) return true;
        var separator = target.LastIndexOf('\\');
        return separator > 2 && string.Equals(target[..separator], configurationDirectory, StringComparison.OrdinalIgnoreCase) && RecognizedConfigurationFiles.Contains(target[(separator + 1)..], StringComparer.OrdinalIgnoreCase);
    }
}

[SupportedOSPlatform("windows")]
internal static class WindowsNutServiceController
{
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
    private static readonly string[] KnownServiceNames = ["NetworkUpsTools", "NUT"];
    private static readonly string[] KnownDisplayNames = ["Network UPS Tools"];
    private static readonly string[] RecognizedExecutables = ["nut.exe", "upsd.exe", "upsmon.exe", "upsdrvctl.exe"];

    public static (string? BinaryPath, NutAssociationConfidence Confidence) Determine(string serviceName, string displayName, string? imagePath, string installationDirectory)
    {
        var executable = TryExtractExecutablePath(imagePath);
        var binaryPath = executable is not null && WindowsPath.TryCanonicalize(executable, out var canonicalExecutable) ? canonicalExecutable : null;
        if (binaryPath is not null)
        {
            var name = binaryPath[(binaryPath.LastIndexOf('\\') + 1)..];
            return (binaryPath, RecognizedExecutables.Contains(name, StringComparer.OrdinalIgnoreCase) && WindowsPath.IsInside(binaryPath, installationDirectory)
                ? NutAssociationConfidence.BinaryPath
                : NutAssociationConfidence.None);
        }

        return (null, KnownServiceNames.Contains(serviceName, StringComparer.OrdinalIgnoreCase) || KnownDisplayNames.Contains(displayName, StringComparer.OrdinalIgnoreCase)
            ? NutAssociationConfidence.NameFallback
            : NutAssociationConfidence.None);
    }

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
    private static readonly FileSystemRights ModifyRights = FileSystemRights.Modify;
    private static readonly string[] RecognizedConfigurationFiles = ["nut.conf", "ups.conf", "upsd.conf", "upsd.users", "upsmon.conf"];

    public static NutPermissionAssessment Assess(string directory, IReadOnlyList<NutConfigurationFileInfo> files)
    {
        if (!OperatingSystem.IsWindows()) return NutPermissionAssessment.Unsupported();
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value;
            if (sid is null) return new(NutPermissionState.Unknown, identity.Name, null, false, "Não foi possível determinar o usuário atual.", Array.Empty<string>());
            var paths = files.Where(file => file.Exists).Select(file => file.FullPath).Prepend(directory).ToArray();
            var identities = identity.Groups?.Select(group => group.Value).Append(sid).Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>([sid], StringComparer.OrdinalIgnoreCase);
            var pathStates = paths.Select(path => AssessPath(path, identities)).ToArray();
            if (pathStates.Any(state => state == NutPermissionState.ManualInterventionRequired)) return new(NutPermissionState.ManualInterventionRequired, identity.Name, sid, true, "Há uma negação explícita relevante que exige intervenção manual.", paths);
            if (pathStates.Any(state => state == NutPermissionState.AccessDenied)) return new(NutPermissionState.AccessDenied, identity.Name, sid, false, "Não foi possível ler as permissões de todos os alvos.", paths);
            if (pathStates.Any(state => state == NutPermissionState.Unknown)) return new(NutPermissionState.Unknown, identity.Name, sid, false, "Não foi possível determinar as permissões efetivas de todos os alvos.", paths);
            return pathStates.All(state => state == NutPermissionState.Modifiable)
                ? new(NutPermissionState.Modifiable, identity.Name, sid, false, "O usuário atual possui Modify confirmado para o diretório e os arquivos reconhecidos.", paths)
                : new(NutPermissionState.Insufficient, identity.Name, sid, false, "O usuário atual não possui Modify confirmado para todos os alvos.", paths);
        }
        catch (UnauthorizedAccessException) { return new(NutPermissionState.AccessDenied, null, null, false, "Não foi possível ler as permissões.", Array.Empty<string>()); }
        catch { return new(NutPermissionState.Unknown, null, null, false, "Não foi possível determinar as permissões efetivas.", Array.Empty<string>()); }
    }

    public static NutAdministrativeActionResult Repair(NutAdministrativeActionRequest request)
    {
        var plan = request.PermissionRepairPlan!;
        if (!TryGetAllowedTargets(request, plan, out var targets)) return new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "O plano contém alvos de ACL não reconhecidos.");
        var before = Assess(request.ConfigurationDirectory, targets.Where(path => !IsDirectory(path)).Select(path => new NutConfigurationFileInfo(Path.GetFileName(path), path, true, true)).ToArray());
        if (before.HasExplicitDeny) return new(NutAdministrativeActionStatus.ManualInterventionRequired, request.Action, "Há uma negação explícita relevante; a correção automática não foi aplicada.");
        var originals = new List<(string Path, bool IsDirectory, ObjectSecurity Security)>();
        var modified = new List<(string Path, bool IsDirectory, ObjectSecurity Security)>();
        try
        {
            var sid = new SecurityIdentifier(plan.UserSid);
            foreach (var path in targets)
            {
                var isDirectory = IsDirectory(path);
                originals.Add((path, isDirectory, CloneSecurity(GetSecurity(path, isDirectory), isDirectory)));
            }
            foreach (var original in originals)
            {
                var security = original.Security;
                if (original.IsDirectory) ((DirectorySecurity)security).AddAccessRule(new FileSystemAccessRule(sid, ModifyRights, AccessControlType.Allow));
                else ((FileSecurity)security).AddAccessRule(new FileSystemAccessRule(sid, ModifyRights, AccessControlType.Allow));
                SetSecurity(original.Path, original.IsDirectory, security);
                modified.Add(original);
            }
            return new(NutAdministrativeActionStatus.Success, request.Action, "A permissão Modify foi adicionada sem substituir ACLs existentes.");
        }
        catch (Exception exception)
        {
            var restored = true;
            foreach (var original in modified.AsEnumerable().Reverse())
            {
                try { SetSecurity(original.Path, original.IsDirectory, original.Security); }
                catch { restored = false; }
            }
            if (!restored) return new(NutAdministrativeActionStatus.ManualInterventionRequired, request.Action, "A correção de permissões falhou parcialmente; é necessária recuperação manual.");
            return exception is UnauthorizedAccessException
                ? new(NutAdministrativeActionStatus.AccessDenied, request.Action, "Permissão insuficiente para ajustar ACL; as ACLs já alteradas foram restauradas.")
                : new(NutAdministrativeActionStatus.Failed, request.Action, "A correção de permissões falhou e as ACLs já alteradas foram restauradas.");
        }
    }

    private static NutPermissionState AssessPath(string path, IReadOnlySet<string> identities)
    {
        try
        {
            FileSystemSecurity security = IsDirectory(path)
                ? new DirectoryInfo(path).GetAccessControl()
                : new FileInfo(path).GetAccessControl();
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>()
                .Select(rule => new WindowsAclRule(
                    rule.IdentityReference.Value,
                    rule.AccessControlType == AccessControlType.Allow ? WindowsAclAccessControlType.Allow : WindowsAclAccessControlType.Deny,
                    ToAclRights(rule.FileSystemRights)));
            return WindowsAclPermissionEvaluation.Assess(rules, identities);
        }
        catch (UnauthorizedAccessException) { return NutPermissionState.AccessDenied; }
        catch { return NutPermissionState.Unknown; }
    }

    private static WindowsAclRights ToAclRights(FileSystemRights rights)
    {
        var result = WindowsAclRights.None;
        if ((rights & FileSystemRights.Read) == FileSystemRights.Read) result |= WindowsAclRights.Read;
        if ((rights & FileSystemRights.Write) == FileSystemRights.Write) result |= WindowsAclRights.Write;
        if ((rights & FileSystemRights.Delete) == FileSystemRights.Delete) result |= WindowsAclRights.Delete;
        if ((rights & FileSystemRights.ReadPermissions) == FileSystemRights.ReadPermissions) result |= WindowsAclRights.ReadPermissions;
        if ((rights & FileSystemRights.Synchronize) == FileSystemRights.Synchronize) result |= WindowsAclRights.Synchronize;
        return result;
    }

    private static bool TryGetAllowedTargets(NutAdministrativeActionRequest request, NutPermissionRepairPlan plan, out IReadOnlyList<string> targets)
    {
        targets = Array.Empty<string>();
        if (plan.Right != "Modify" || !WindowsPath.TryCanonicalize(request.ConfigurationDirectory, out var config) || !WindowsPath.TryCanonicalize(plan.ConfigurationDirectory, out var planDirectory) || !string.Equals(config, planDirectory, StringComparison.OrdinalIgnoreCase)) return false;
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { config };
        foreach (var name in RecognizedConfigurationFiles)
        {
            var candidate = config + "\\" + name;
            if (File.Exists(candidate)) allowed.Add(candidate);
        }
        var requested = plan.AffectedPaths.Select(path => WindowsPath.TryCanonicalize(path, out var canonical) ? canonical : null).ToArray();
        if (requested.Any(path => path is null) || requested.Length == 0 || requested.Any(path => !allowed.Contains(path!))) return false;
        targets = requested.Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return true;
    }

    private static bool IsDirectory(string path) => Directory.Exists(path);
    private static ObjectSecurity GetSecurity(string path, bool isDirectory) => isDirectory
        ? (ObjectSecurity)new DirectoryInfo(path).GetAccessControl()
        : new FileInfo(path).GetAccessControl();
    private static ObjectSecurity CloneSecurity(ObjectSecurity security, bool isDirectory)
    {
        if (isDirectory)
        {
            var copy = new DirectorySecurity();
            copy.SetSecurityDescriptorBinaryForm(security.GetSecurityDescriptorBinaryForm());
            return copy;
        }

        var fileCopy = new FileSecurity();
        fileCopy.SetSecurityDescriptorBinaryForm(security.GetSecurityDescriptorBinaryForm());
        return fileCopy;
    }
    private static void SetSecurity(string path, bool isDirectory, ObjectSecurity security)
    {
        if (isDirectory) new DirectoryInfo(path).SetAccessControl((DirectorySecurity)security);
        else new FileInfo(path).SetAccessControl((FileSecurity)security);
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
