using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.App.Services;

/// <summary>
/// Detection for a local profile. It reads what the installation detector already reports, so no
/// extra file system walk happens and no process is started: the detector's own presence flags are
/// the answer.
/// </summary>
public sealed class LocalNutManagedFileDetector : INutManagedFileDetector
{
    private readonly ILocalNutInstallationDetector _detector;

    public LocalNutManagedFileDetector(ILocalNutInstallationDetector detector) =>
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));

    public bool CanDetect => true;

    public async Task<NutManagedFileDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var installation = await _detector.DetectAsync(cancellationToken).ConfigureAwait(false);
            if (!installation.IsDetected)
            {
                return NutManagedFileDetectionResult.Unavailable();
            }

            // Only the five known names are considered. Anything else in the directory, including
            // the .sample files NUT ships, is not a supported configuration file.
            var found = installation.ConfigurationFiles
                .Where(file => file.Exists)
                .Select(file => ManagedNutConfigurationFiles.TryParseFileName(file.Name, out var kind) ? kind : (NutConfigurationFileKind?)null)
                .Where(kind => kind is not null)
                .Select(kind => kind!.Value);

            return NutManagedFileDetectionResult.Success(found);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return NutManagedFileDetectionResult.Cancelled();
        }
        catch (UnauthorizedAccessException)
        {
            return NutManagedFileDetectionResult.AccessDenied();
        }
        catch
        {
            return NutManagedFileDetectionResult.Failed();
        }
    }
}

/// <summary>
/// Detection for a remote profile. It adds no I/O of its own: validating a remote configuration
/// directory already lists which of the recognised NUT files are present, over the same session,
/// the same pinned host key for SFTP, and the same exact-share confinement and resolved credential
/// for SMB. This reads that result.
///
/// Without a validated directory there is nothing to report, and the action says so rather than
/// connecting on the administrator's behalf.
/// </summary>
public sealed class RemoteNutManagedFileDetector : INutManagedFileDetector
{
    private readonly Func<RemoteNutDirectoryValidationResult?> _validation;

    public RemoteNutManagedFileDetector(Func<RemoteNutDirectoryValidationResult?> validation) =>
        _validation = validation ?? throw new ArgumentNullException(nameof(validation));

    public bool CanDetect => _validation() is { IsValid: true };

    public Task<NutManagedFileDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_validation() is not { IsValid: true } validation)
        {
            return Task.FromResult(NutManagedFileDetectionResult.Unavailable());
        }

        // Only names the transport already recognised as NUT configuration files appear here, so a
        // directory full of unrelated content cannot become a list of options.
        var found = validation.PresentFileNames
            .Select(name => ManagedNutConfigurationFiles.TryParseFileName(name, out var kind) ? kind : (NutConfigurationFileKind?)null)
            .Where(kind => kind is not null)
            .Select(kind => kind!.Value);

        return Task.FromResult(NutManagedFileDetectionResult.Success(found));
    }
}
