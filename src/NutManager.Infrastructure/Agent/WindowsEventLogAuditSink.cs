using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using NutManager.Core.Agent;

namespace NutManager.Infrastructure.Agent;

/// <summary>
/// Writes the agent's control record to the Windows Event Log.
///
/// The source is created by deployment and never by the agent. Registering an event source needs
/// administrative rights on the machine, and an agent that creates its own source is an agent that
/// can be made to create one — so <see cref="IsReadyAsync"/> asks whether the source exists and
/// answers no when it does not, which is what stops every mutation. That refusal is the feature: a
/// privileged action nobody can account for is worse than no action.
///
/// Nothing written here can carry a secret, because <see cref="NutAgentAuditEntry"/> has no field one
/// could travel in. That property is enforced by the record's shape rather than by this formatter
/// remembering to leave things out.
/// </summary>
public sealed class WindowsEventLogAuditSink : INutAgentAuditSink
{
    public const string DefaultSourceName = "NutManager Agent";
    public const string DefaultLogName = "Application";

    private readonly string _source;

    public WindowsEventLogAuditSink(string? sourceName = null) =>
        _source = string.IsNullOrWhiteSpace(sourceName) ? DefaultSourceName : sourceName;

    public string SourceName => _source;

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromResult(false);
        return WindowsAgentEventLog.SourceExistsAsync(_source, cancellationToken);
    }

    public Task<bool> WriteAsync(NutAgentAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!OperatingSystem.IsWindows()) return Task.FromResult(false);
        return WindowsAgentEventLog.WriteAsync(_source, entry);
    }

    /// <summary>
    /// The record as one readable block. Pure, so what an operator will read can be asserted in a
    /// test rather than discovered on a server.
    /// </summary>
    public static string FormatEntry(NutAgentAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var lines = new List<string>
        {
            $"NutManager agent: {entry.Kind}",
            $"Operation: {entry.Operation}",
            $"Result: {entry.Code}",
            $"Caller: {entry.CallerIdentity}",
            $"Transport: {entry.Transport}",
            $"Machine: {entry.MachineName}",
            $"Service: {entry.ServiceName ?? "(none)"}",
            $"State: {entry.InitialState} -> {entry.FinalState}",
            $"Operation id: {entry.OperationId}",
            $"Timestamp: {entry.Timestamp.ToString("O", CultureInfo.InvariantCulture)}",
            $"Duration: {entry.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} ms"
        };

        if (entry.Win32ErrorCode is { } win32)
        {
            lines.Add($"Windows error: {win32.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Detail))
        {
            lines.Add($"Detail: {entry.Detail}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Stable event ids, so an administrator can filter for the security events without parsing text.
    /// These are part of the agent's external contract and must not be renumbered.
    /// </summary>
    public static int EventIdOf(NutAgentAuditKind kind) => kind switch
    {
        NutAgentAuditKind.SecurityStartupFailure => 1001,
        NutAgentAuditKind.UnauthorizedAttempt => 1002,
        NutAgentAuditKind.TargetRevalidationFailure => 1003,
        NutAgentAuditKind.OperationRequested => 1010,
        NutAgentAuditKind.OperationSucceeded => 1011,
        NutAgentAuditKind.OperationFailed => 1012,
        _ => 1000
    };

    /// <summary>
    /// Severity as an administrator would triage it: a refused caller and a failed operation are not
    /// routine, and a successful control action is a record rather than a problem.
    /// </summary>
    public static bool IsFailureKind(NutAgentAuditKind kind) => kind is
        NutAgentAuditKind.SecurityStartupFailure or
        NutAgentAuditKind.UnauthorizedAttempt or
        NutAgentAuditKind.TargetRevalidationFailure or
        NutAgentAuditKind.OperationFailed;
}

/// <summary>The Windows-typed half of the sink, behind one annotation.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsAgentEventLog
{
    // The Task.Run calls live inside this class rather than in the guarded callers: a platform guard
    // does not follow the call into a lambda, so a lambda written on the neutral side would report
    // CA1416 even with the guard directly above it.
    internal static Task<bool> SourceExistsAsync(string source, CancellationToken cancellationToken) =>
        Task.Run(() => SourceExists(source), cancellationToken);

    internal static Task<bool> WriteAsync(string source, NutAgentAuditEntry entry) =>
        Task.Run(() => Write(source, entry), CancellationToken.None);

    /// <summary>
    /// Whether the source exists — asked of Windows, not remembered from a previous answer. A source
    /// removed while the agent runs must turn control off, and a cached "yes" would keep it on.
    /// </summary>
    private static bool SourceExists(string source)
    {
        try
        {
            return EventLog.SourceExists(source);
        }
        catch (Exception)
        {
            // Unreadable is not usable, and the sink says so rather than assuming the best.
            return false;
        }
    }

    private static bool Write(string source, NutAgentAuditEntry entry)
    {
        try
        {
            if (!EventLog.SourceExists(source)) return false;

            EventLog.WriteEntry(
                source,
                WindowsEventLogAuditSink.FormatEntry(entry),
                WindowsEventLogAuditSink.IsFailureKind(entry.Kind) ? EventLogEntryType.Warning : EventLogEntryType.Information,
                WindowsEventLogAuditSink.EventIdOf(entry.Kind));

            return true;
        }
        catch (Exception)
        {
            // A failed write is reported as a failed write. The application service turns that into
            // CompletedWithAuditFailure rather than pretending the record exists.
            return false;
        }
    }
}
