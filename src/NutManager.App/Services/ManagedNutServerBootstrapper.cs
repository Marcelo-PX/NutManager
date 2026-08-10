using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.App.Services;

public sealed record ManagedNutServerRuntimeContext(
    ManagedNutServerProfile Profile,
    NutEndpoint Endpoint,
    ManagedServerCapabilities Capabilities)
{
    public static ManagedNutServerRuntimeContext FromProfiles(ManagedNutServerProfiles profiles, ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(settings);
        var profile = profiles.ActiveProfile;
        return new ManagedNutServerRuntimeContext(
            profile,
            new NutEndpoint(profile.Monitoring.Host, profile.Monitoring.Port, settings.ConnectionTimeout),
            ManagedServerCapabilities.FromProfile(profile));
    }
}

public sealed record ManagedNutServerBootstrapResult(
    ManagedNutServerProfiles Profiles,
    ManagedNutServerRuntimeContext RuntimeContext,
    string? Warning,
    bool WasMigrated,
    bool IsProfileDocumentLoadFailure);

public sealed class ManagedNutServerBootstrapper
{
    private readonly IManagedNutServerProfileStore _store;

    public ManagedNutServerBootstrapper(IManagedNutServerProfileStore store)
    {
        _store = store;
    }

    public async Task<ManagedNutServerBootstrapResult> LoadAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            var profiles = await _store.LoadAsync(cancellationToken);
            if (profiles is not null)
            {
                return CreateResult(profiles, settings, null, false, false);
            }

            var migrated = ManagedNutServerProfiles.CreateLegacyProfile(settings);
            try
            {
                await _store.SaveAsync(migrated, cancellationToken);
                return CreateResult(migrated, settings, null, true, false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return CreateResult(migrated, settings, "O perfil inicial foi mantido apenas nesta sessão porque não foi possível persistir os perfis.", true, false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            var fallback = ManagedNutServerProfiles.CreateLegacyProfile(settings);
            return CreateResult(fallback, settings, "Não foi possível carregar os perfis gerenciados. O arquivo existente não foi alterado.", false, true);
        }
    }

    private static ManagedNutServerBootstrapResult CreateResult(
        ManagedNutServerProfiles profiles,
        ApplicationSettings settings,
        string? warning,
        bool wasMigrated,
        bool isProfileDocumentLoadFailure) => new(
            profiles,
            ManagedNutServerRuntimeContext.FromProfiles(profiles, settings),
            warning,
            wasMigrated,
            isProfileDocumentLoadFailure);
}
