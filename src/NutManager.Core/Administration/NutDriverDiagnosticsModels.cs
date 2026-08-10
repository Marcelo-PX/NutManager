namespace NutManager.Core.Administration;

/// <summary>
/// Describes a serial device reported by Windows without opening the port.
/// </summary>
public sealed record NutComPortInfo(
    string PortName,
    string? FriendlyName,
    string? Manufacturer,
    string? PnpDeviceId,
    string? Status,
    int? ConfigManagerErrorCode,
    bool IsPresent);

public enum NutDriverExecutableState
{
    NotApplicable,
    Available,
    Missing,
    Untrusted,
    InvalidName
}

public enum NutDriverRuntimeState
{
    Unknown,
    NotRunning,
    Running
}

public sealed record NutDriverExecutableInfo(
    string? Path,
    NutDriverExecutableState State,
    bool IsTrusted)
{
    public bool IsAvailable => State == NutDriverExecutableState.Available;
}

/// <summary>
/// One section from ups.conf interpreted for diagnostics only. The source document is never changed.
/// </summary>
public sealed record NutConfiguredDriver(
    string UpsName,
    string? Description,
    string? DriverName,
    string? ConfiguredPort,
    string? NormalizedComPort,
    string? Protocol,
    string? DriverPath,
    NutDriverExecutableInfo Executable,
    bool IsConfiguredComPortPresent,
    NutDriverRuntimeState RuntimeState,
    string? StatusMessage = null);

public enum NutDriverDiagnosticKind
{
    UpsdrvctlHelp,
    UpsdrvctlList,
    UpsdrvctlStatus,
    UpsdrvctlDryRunStart,
    DriverHelp,
    DriverVersion,
    DriverVariableList,
    DriverDataDump
}

public enum NutDriverDiagnosticStatus
{
    Success,
    NonZeroExit,
    ExecutableNotFound,
    AccessDenied,
    InvalidExecutable,
    MissingDependency,
    Timeout,
    CleanupFailed,
    OutputTruncated,
    CancelledBeforeLaunch,
    CancelledAfterLaunch,
    Conflict,
    InvalidConfiguration,
    Unsupported,
    Failed
}

/// <summary>
/// A typed diagnostic request. It deliberately has no arbitrary executable or argument fields.
/// </summary>
public sealed record NutDriverDiagnosticRequest(
    NutDriverDiagnosticKind Kind,
    string InstallationDirectory,
    string ConfigurationDirectory,
    NutConfiguredDriver? Driver = null,
    string? UpsConfFingerprint = null);

public sealed record NutDriverDiagnosticResult(
    NutDriverDiagnosticKind Kind,
    NutDriverDiagnosticStatus Status,
    string ToolName,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool OutputTruncated,
    bool ContactsHardware,
    string Message)
{
    public static NutDriverDiagnosticResult Unsupported(NutDriverDiagnosticKind kind, string message) =>
        new(kind, NutDriverDiagnosticStatus.Unsupported, string.Empty, DateTimeOffset.UtcNow, TimeSpan.Zero, null, string.Empty, string.Empty, false, false, message);
}

public sealed record NutDriverDiagnosticsSnapshot(
    bool IsPlatformSupported,
    IReadOnlyList<NutComPortInfo> ComPorts,
    IReadOnlyList<NutConfiguredDriver> ConfiguredDrivers,
    string? UpsdrvctlPath,
    string? DiagnosticMessage = null,
    string? UpsConfFingerprint = null)
{
    public static NutDriverDiagnosticsSnapshot Unsupported() => new(
        false,
        Array.Empty<NutComPortInfo>(),
        Array.Empty<NutConfiguredDriver>(),
        null,
        "Diagnósticos locais de portas e drivers do Windows não estão disponíveis nesta plataforma.");
}
