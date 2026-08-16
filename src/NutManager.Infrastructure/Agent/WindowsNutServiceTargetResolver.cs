using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Win32;
using NutManager.Core.Administration;
using NutManager.Core.Agent;
using NutManager.Core.Services;
using NutManager.Infrastructure.Platform.Windows;

namespace NutManager.Infrastructure.Agent;

/// <summary>
/// Decides, on the agent's own machine, which service the agent is allowed to control.
///
/// This is where the agent earns its existence. T34 identified a remote service by its name alone,
/// because there is no trusted filesystem root on a host NutManager may not touch, and a name is
/// exactly what an attacker gets to choose. The agent runs on that host, so it can do what T34 could
/// not: detect the installation and require the service binary to live inside it. Containment is
/// mandatory here — <see cref="NutAssociationConfidence.NameFallback"/> resolves nothing, so a service
/// that merely calls itself "Network UPS Tools" while pointing somewhere else is never adopted.
///
/// Nothing about the choice is influenced by a caller. The resolver reads the local SCM and the local
/// installation, and the answer is the same whoever happens to be connected.
/// </summary>
public sealed class WindowsNutServiceTargetResolver : INutServiceTargetResolver
{
    private readonly ILocalNutInstallationDetector _detector;

    public WindowsNutServiceTargetResolver()
        : this(new WindowsNutInstallationDetector())
    {
    }

    public WindowsNutServiceTargetResolver(ILocalNutInstallationDetector detector)
    {
        ArgumentNullException.ThrowIfNull(detector);
        _detector = detector;
    }

    public async Task<NutServiceTargetResolution> ResolveAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new NutServiceTargetResolution(
                NutServiceTargetStatus.QueryFailed, null, "The agent only runs on Windows.");
        }

        string? root;
        try
        {
            var installation = await _detector.DetectAsync(cancellationToken).ConfigureAwait(false);
            root = installation.IsDetected ? installation.InstallationDirectory : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new NutServiceTargetResolution(
                NutServiceTargetStatus.QueryFailed, null, $"The NUT installation could not be inspected: {exception.GetType().Name}.");
        }

        var enumeration = await WindowsAgentServiceEnumeration.EnumerateAsync(cancellationToken).ConfigureAwait(false);
        return enumeration.Failure ?? Select(enumeration.Candidates, root);
    }

    /// <summary>
    /// Re-runs the whole decision and then insists it produced the same answer.
    ///
    /// Re-reading only the pinned service would miss the case this method exists for: the identity is
    /// unchanged, the image path now points elsewhere, and the agent would happily start whatever is
    /// there. Comparing the binary path as well as the name closes that, and re-running the full
    /// selection means a second NUT-looking service appearing after startup makes the agent ambiguous
    /// rather than quietly confident.
    /// </summary>
    public async Task<NutServiceTargetResolution> RevalidateAsync(NutServiceTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var current = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        return Confirm(target, current);
    }

    /// <summary>
    /// The selection rule, pure so it can be tested without an SCM or an installed product.
    ///
    /// A missing installation root is not a reason to relax: without it containment cannot be checked,
    /// and an unverifiable target is refused rather than accepted on the strength of its name.
    /// </summary>
    public static NutServiceTargetResolution Select(IReadOnlyList<WindowsAgentServiceCandidate> candidates, string? installationRoot)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var namedLikeNut = candidates
            .Where(candidate => WindowsNutServiceAssociation.IsKnownIdentity(candidate.ServiceName, candidate.DisplayName))
            .ToArray();

        if (string.IsNullOrWhiteSpace(installationRoot))
        {
            return namedLikeNut.Length == 0
                ? new NutServiceTargetResolution(
                    NutServiceTargetStatus.NotFound, null, "No NUT installation and no NUT service were found on this machine.")
                : new NutServiceTargetResolution(
                    NutServiceTargetStatus.ValidationFailed, null,
                    "A NUT service is installed but no NUT installation directory was detected, so its binary cannot be verified.",
                    [.. namedLikeNut.Select(candidate => candidate.ServiceName).Order(StringComparer.OrdinalIgnoreCase)]);
        }

        var contained = candidates
            .Select(candidate =>
            {
                var (binaryPath, confidence) = WindowsNutServiceAssociation.Determine(
                    candidate.ServiceName, candidate.DisplayName, candidate.ImagePath, installationRoot);
                return (candidate, binaryPath, confidence);
            })
            .Where(evaluated => evaluated.confidence == NutAssociationConfidence.BinaryPath)
            .ToArray();

        if (contained.Length == 0)
        {
            return namedLikeNut.Length == 0
                ? new NutServiceTargetResolution(
                    NutServiceTargetStatus.NotFound, null, "No service on this machine runs a binary inside the detected NUT installation.")
                : new NutServiceTargetResolution(
                    NutServiceTargetStatus.ValidationFailed, null,
                    "A service carries a NUT identity but its binary is not inside the detected NUT installation.",
                    [.. namedLikeNut.Select(candidate => candidate.ServiceName).Order(StringComparer.OrdinalIgnoreCase)]);
        }

        if (contained.Length > 1)
        {
            // Choosing one would be a guess with service-control rights attached to it.
            return new NutServiceTargetResolution(
                NutServiceTargetStatus.Ambiguous, null, "More than one service validates as NUT on this machine.",
                [.. contained.Select(evaluated => evaluated.candidate.ServiceName).Order(StringComparer.OrdinalIgnoreCase)]);
        }

        var match = contained[0];
        return new NutServiceTargetResolution(
            NutServiceTargetStatus.Resolved,
            new NutServiceTarget(match.candidate.ServiceName, match.candidate.DisplayName, match.binaryPath, match.confidence));
    }

    /// <summary>
    /// Compares a fresh resolution against the pinned target. Pure, and deliberately strict: anything
    /// other than "the same service, running the same binary" is a refusal.
    /// </summary>
    public static NutServiceTargetResolution Confirm(NutServiceTarget pinned, NutServiceTargetResolution current)
    {
        ArgumentNullException.ThrowIfNull(pinned);
        ArgumentNullException.ThrowIfNull(current);

        if (!current.IsResolved) return current;

        var fresh = current.Target!;
        if (!string.Equals(fresh.ServiceName, pinned.ServiceName, StringComparison.OrdinalIgnoreCase))
        {
            return new NutServiceTargetResolution(
                NutServiceTargetStatus.ValidationFailed, null,
                $"The NUT service is now '{fresh.ServiceName}' rather than the pinned '{pinned.ServiceName}'.");
        }

        if (!string.Equals(fresh.BinaryPath, pinned.BinaryPath, StringComparison.OrdinalIgnoreCase))
        {
            // Same name, different image. This is the substitution the revalidation step exists for.
            return new NutServiceTargetResolution(
                NutServiceTargetStatus.ValidationFailed, null,
                $"The binary of service '{pinned.ServiceName}' changed since it was validated.");
        }

        return current;
    }
}

/// <summary>One service as the local SCM reports it, before any judgement is applied.</summary>
public sealed record WindowsAgentServiceCandidate(string ServiceName, string DisplayName, string? ImagePath);

/// <summary>Either the machine's services, or the reason they could not be read.</summary>
public sealed record WindowsAgentServiceEnumerationResult(
    IReadOnlyList<WindowsAgentServiceCandidate> Candidates,
    NutServiceTargetResolution? Failure);

/// <summary>
/// The Windows-typed half of the resolver. Split out for the reason T34 established: the platform
/// guard on the public method cannot follow a call into a lambda, so every SCM- and registry-typed
/// member lives behind one annotation instead.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsAgentServiceEnumeration
{
    internal static Task<WindowsAgentServiceEnumerationResult> EnumerateAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Enumerate(cancellationToken), cancellationToken);

    private static WindowsAgentServiceEnumerationResult Enumerate(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var services = ServiceController.GetServices();
            try
            {
                var candidates = services
                    .Select(service => new WindowsAgentServiceCandidate(
                        service.ServiceName, service.DisplayName, TryReadImagePath(service.ServiceName)))
                    .ToArray();

                return new WindowsAgentServiceEnumerationResult(candidates, null);
            }
            finally
            {
                foreach (var service in services) service.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new WindowsAgentServiceEnumerationResult(
                [],
                new NutServiceTargetResolution(
                    NutServiceTargetStatus.QueryFailed, null, $"The local SCM could not be enumerated: {exception.GetType().Name}."));
        }
    }

    /// <summary>
    /// The configured image path, read the same way the local detector reads it. Unreadable is null
    /// rather than an exception: a service whose registry key this agent cannot open simply fails
    /// containment, which is the correct outcome and not a reason to abandon the whole enumeration.
    /// </summary>
    private static string? TryReadImagePath(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return key?.GetValue("ImagePath") as string;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
