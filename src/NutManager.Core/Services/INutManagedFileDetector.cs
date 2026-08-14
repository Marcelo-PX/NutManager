using NutManager.Core.Configuration;
using NutManager.Core.Models;

namespace NutManager.Core.Services;

/// <summary>
/// Reports which of the supported NUT configuration files are actually present at a profile's
/// configuration location.
///
/// This answers "what is there", never "what should this profile manage". The two are kept apart on
/// purpose: a probe finding a file is not a reason to start managing it, and a file being briefly
/// unreachable is not a reason to drop it from a profile. The result is a proposal the
/// administrator applies explicitly.
///
/// Detection is read-only in every implementation. It looks for the five known names and nothing
/// else, so a directory full of unrelated files cannot turn into a list of options.
/// </summary>
public interface INutManagedFileDetector
{
    /// <summary>Whether there is enough context to look at all — a validated directory, a reachable share.</summary>
    bool CanDetect { get; }

    Task<NutManagedFileDetectionResult> DetectAsync(CancellationToken cancellationToken = default);
}

public enum NutManagedFileDetectionStatus
{
    Success,
    Unavailable,
    AccessDenied,
    Cancelled,
    Failed
}

public sealed class NutManagedFileDetectionResult
{
    private NutManagedFileDetectionResult(
        NutManagedFileDetectionStatus status,
        IReadOnlyList<NutConfigurationFileKind> found)
    {
        Status = status;
        Found = found;
    }

    public NutManagedFileDetectionStatus Status { get; }

    /// <summary>The supported files that exist, in the canonical presentation order.</summary>
    public IReadOnlyList<NutConfigurationFileKind> Found { get; }

    public bool IsSuccess => Status == NutManagedFileDetectionStatus.Success;

    public int Count => Found.Count;

    public static NutManagedFileDetectionResult Success(IEnumerable<NutConfigurationFileKind> found) =>
        new(NutManagedFileDetectionStatus.Success,
            [.. ManagedNutConfigurationFiles.SupportedKinds.Where(found.Contains)]);

    public static NutManagedFileDetectionResult Unavailable() =>
        new(NutManagedFileDetectionStatus.Unavailable, []);

    public static NutManagedFileDetectionResult AccessDenied() =>
        new(NutManagedFileDetectionStatus.AccessDenied, []);

    public static NutManagedFileDetectionResult Cancelled() =>
        new(NutManagedFileDetectionStatus.Cancelled, []);

    public static NutManagedFileDetectionResult Failed() =>
        new(NutManagedFileDetectionStatus.Failed, []);

    /// <summary>The detected set, ready to be applied to a profile once the administrator asks for it.</summary>
    public ManagedNutConfigurationFiles ToManagedFiles() => ManagedNutConfigurationFiles.Create(Found);
}
