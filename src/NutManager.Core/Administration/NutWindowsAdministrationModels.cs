namespace NutManager.Core.Administration;

// PausePending and ContinuePending are appended rather than slotted in beside Paused: the values are
// compared, never persisted, but keeping the existing ones at their numbers costs nothing. Windows
// reports all four transitional states and the remote probe passes them through, so collapsing them
// into Unknown would throw away the difference between a service settling and a service in trouble.
public enum NutServiceState { Unknown, Stopped, StartPending, StopPending, Running, Paused, Failed, PausePending, ContinuePending }
public enum NutServiceStartMode { Unknown, Automatic, Manual, Disabled }
public enum NutAssociationConfidence { None, NameFallback, BinaryPath }

/// <summary>
/// Outcome of enumerating Windows services. Keeping these apart stops an access or query failure
/// from being reported as "no NUT service is installed".
/// </summary>
public enum NutServiceDiscoveryStatus { Completed, AccessDenied, QueryFailed }

public sealed record NutServiceDiscoveryResult(
    IReadOnlyList<NutServiceInfo> Services,
    NutServiceDiscoveryStatus Status,
    string? DiagnosticMessage = null)
{
    /// <summary>
    /// Sanitized technical trace of the discovery attempt, shown only while discovery is failing so
    /// the exact stage can be identified. It never contains the machine's full service list, any
    /// credential, or anything beyond the NUT candidate and the paths already shown elsewhere.
    /// </summary>
    public NutServiceDiscoveryTrace? Trace { get; init; }
}

public sealed record NutServiceDiscoveryTrace(
    bool PlatformSupported,
    bool EnumerationSucceeded,
    int EnumeratedServiceCount,
    bool ExactKnownServiceFound,
    string? CandidateServiceName,
    string? CandidateDisplayName,
    string? CandidateExecutable,
    string? NormalizedExecutable,
    string? InstallationRoot,
    bool? ContainmentResult,
    NutAssociationConfidence Association,
    string? FailureReason);
public enum NutPermissionState { Unknown, Readable, Writable, Modifiable, Insufficient, AccessDenied, ManualInterventionRequired }
public enum NutEventLogStatus { Success, AccessDenied, Unavailable, Failed }
public enum NutAdministrativeAction { StartService, StopService, RestartService, RepairConfigurationPermissions }
public enum NutAdministrativeActionStatus { Success, AlreadyInRequestedState, ServiceNotFound, ServiceNotAssociated, AccessDenied, ElevationCancelled, Timeout, InvalidRequest, PlatformUnsupported, ManualInterventionRequired, Cancelled, Failed }
public enum PrivilegeState { PlatformUnsupported, StandardUser, Elevated, Unknown }

public sealed record NutServiceInfo(string ServiceName, string DisplayName, NutServiceState State, NutServiceStartMode StartMode, string? BinaryPath, NutAssociationConfidence AssociationConfidence)
{
    public bool IsAssociated => AssociationConfidence is not NutAssociationConfidence.None;
}

public sealed record NutPermissionAssessment(NutPermissionState State, string? Identity, string? UserSid, bool HasExplicitDeny, string? Message, IReadOnlyList<string> AffectedPaths, IReadOnlyList<string>? EffectiveIdentitySids = null)
{
    /// <summary>The host operating system cannot provide local Windows administration at all.</summary>
    public static NutPermissionAssessment Unsupported() => new(NutPermissionState.Unknown, null, null, false, "A administração local do Windows não está disponível nesta plataforma.", Array.Empty<string>());

    /// <summary>
    /// The platform supports local administration but the assessment could not be produced, for
    /// example because no local NUT installation is selected. This is deliberately distinct from
    /// <see cref="Unsupported"/> so an unresolved installation never reads as a wrong platform.
    /// </summary>
    public static NutPermissionAssessment NotDetermined(string message) =>
        new(NutPermissionState.Unknown, null, null, false, message, Array.Empty<string>());
}

public sealed record NutPermissionRepairPlan(string ConfigurationDirectory, string UserIdentity, string UserSid, IReadOnlyList<string> AffectedPaths, string Right = "Modify", IReadOnlyList<string>? EffectiveIdentitySids = null);

public sealed record NutProcessInfo(string Name, int ProcessId, string? ExecutablePath, NutAssociationConfidence AssociationConfidence);
public sealed record NutEventLogEntry(DateTimeOffset Timestamp, string LogName, string Provider, int EventId, string Level, string Message);

public sealed record NutWindowsAdministrationSnapshot(
    bool IsPlatformSupported,
    PrivilegeState PrivilegeState,
    IReadOnlyList<NutServiceInfo> Services,
    NutPermissionAssessment Permissions,
    IReadOnlyList<NutProcessInfo> Processes,
    IReadOnlyList<NutEventLogEntry> Events,
    string? DiagnosticMessage = null,
    NutEventLogStatus EventLogStatus = NutEventLogStatus.Success,
    string? EventLogDiagnosticMessage = null)
{
    /// <summary>Why the service list is empty, when it is.</summary>
    public NutServiceDiscoveryStatus ServiceDiscoveryStatus { get; init; } = NutServiceDiscoveryStatus.Completed;

    /// <summary>Sanitized technical trace of the discovery attempt, when one was produced.</summary>
    public NutServiceDiscoveryTrace? Trace { get; init; }

    /// <summary>
    /// True when process association could not be evaluated because the session lacks the rights to
    /// read process modules. Distinct from "no NUT process is running".
    /// </summary>
    public bool ProcessInspectionDenied { get; init; }

    public static NutWindowsAdministrationSnapshot Unsupported() => new(false, PrivilegeState.PlatformUnsupported, Array.Empty<NutServiceInfo>(), NutPermissionAssessment.Unsupported(), Array.Empty<NutProcessInfo>(), Array.Empty<NutEventLogEntry>(), "A administração local do Windows não está disponível nesta plataforma.");
}

public sealed record NutAdministrativeActionRequest(Guid RequestId, NutAdministrativeAction Action, string InstallationDirectory, string ConfigurationDirectory, string? ServiceName = null, NutPermissionRepairPlan? PermissionRepairPlan = null);

public sealed record NutAdministrativeActionResult(NutAdministrativeActionStatus Status, NutAdministrativeAction Action, string Message, string? ServiceName = null)
{
    public bool IsSuccess => Status is NutAdministrativeActionStatus.Success or NutAdministrativeActionStatus.AlreadyInRequestedState;
}
