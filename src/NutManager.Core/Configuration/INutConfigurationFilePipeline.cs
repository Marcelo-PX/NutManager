namespace NutManager.Core.Configuration;

/// <summary>
/// Provides the explicit load, prepare, and apply stages for a single NUT configuration file.
/// </summary>
public interface INutConfigurationFilePipeline
{
    Task<NutConfigurationLoadResult> LoadAsync(
        string targetPath,
        NutConfigurationFileKind fileKind,
        CancellationToken cancellationToken = default);

    NutConfigurationPreparedChange Prepare(NutConfigurationFileSnapshot snapshot);

    Task<NutConfigurationApplyResult> ApplyAsync(
        NutConfigurationPreparedChange change,
        CancellationToken cancellationToken = default);
}
