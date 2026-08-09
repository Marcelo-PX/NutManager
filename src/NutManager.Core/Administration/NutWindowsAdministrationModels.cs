namespace NutManager.Core.Administration;

public enum NutServiceState { Unknown, Stopped, StartPending, StopPending, Running, Paused, Failed }
public enum NutServiceStartMode { Unknown, Automatic, Manual, Disabled }
public enum NutAssociationConfidence { None, NameFallback, BinaryPath }
public enum NutPermissionState { Unknown, Readable, Writable, Modifiable, Insufficient, AccessDenied, ManualInterventionRequired }
public enum NutAdministrativeAction { StartService, StopService, RestartService, RepairConfigurationPermissions }
public enum NutAdministrativeActionStatus { Success, AlreadyInRequestedState, ServiceNotFound, ServiceNotAssociated, AccessDenied, ElevationCancelled, Timeout, InvalidRequest, PlatformUnsupported, ManualInterventionRequired, Cancelled, Failed }
public enum PrivilegeState { PlatformUnsupported, StandardUser, Elevated, Unknown }

public sealed record NutServiceInfo(string ServiceName, string DisplayName, NutServiceState State, NutServiceStartMode StartMode, string? BinaryPath, NutAssociationConfidence AssociationConfidence)
{
    public bool IsAssociated => AssociationConfidence is not NutAssociationConfidence.None;
}

public sealed record NutPermissionAssessment(NutPermissionState State, string? Identity, string? UserSid, bool HasExplicitDeny, string? Message, IReadOnlyList<string> AffectedPaths)
{
    public static NutPermissionAssessment Unsupported() => new(NutPermissionState.Unknown, null, null, false, "A administração local do Windows não está disponível nesta plataforma.", Array.Empty<string>());
}

public sealed record NutPermissionRepairPlan(string ConfigurationDirectory, string UserIdentity, string UserSid, IReadOnlyList<string> AffectedPaths, string Right = "Modify");

public sealed record NutProcessInfo(string Name, int ProcessId, string? ExecutablePath, NutAssociationConfidence AssociationConfidence);
public sealed record NutEventLogEntry(DateTimeOffset Timestamp, string LogName, string Provider, int EventId, string Level, string Message);

public sealed record NutWindowsAdministrationSnapshot(
    bool IsPlatformSupported,
    PrivilegeState PrivilegeState,
    IReadOnlyList<NutServiceInfo> Services,
    NutPermissionAssessment Permissions,
    IReadOnlyList<NutProcessInfo> Processes,
    IReadOnlyList<NutEventLogEntry> Events,
    string? DiagnosticMessage = null)
{
    public static NutWindowsAdministrationSnapshot Unsupported() => new(false, PrivilegeState.PlatformUnsupported, Array.Empty<NutServiceInfo>(), NutPermissionAssessment.Unsupported(), Array.Empty<NutProcessInfo>(), Array.Empty<NutEventLogEntry>(), "A administração local do Windows não está disponível nesta plataforma.");
}

public sealed record NutAdministrativeActionRequest(Guid RequestId, NutAdministrativeAction Action, string InstallationDirectory, string ConfigurationDirectory, string? ServiceName = null, NutPermissionRepairPlan? PermissionRepairPlan = null);

public sealed record NutAdministrativeActionResult(NutAdministrativeActionStatus Status, NutAdministrativeAction Action, string Message, string? ServiceName = null)
{
    public bool IsSuccess => Status is NutAdministrativeActionStatus.Success or NutAdministrativeActionStatus.AlreadyInRequestedState;
}
