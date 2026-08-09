using NutManager.Core.Administration;

namespace NutManager.Infrastructure.Platform.Windows;

public sealed record WindowsAclTarget(string Path, bool IsDirectory);

/// <summary>Windows-only ACL bridge. Tests supply an in-memory implementation; production preserves native descriptors.</summary>
public interface IWindowsAclAccessor
{
    object CaptureSecurity(WindowsAclTarget target);
    object CloneSecurity(object security, bool isDirectory);
    IReadOnlyList<WindowsAclRule> GetRules(object security);
    void AddModify(object candidateSecurity, string userSid, bool isDirectory);
    void WriteSecurity(WindowsAclTarget target, object security);
}

public static class WindowsAclRepairTransaction
{
    public static NutAdministrativeActionResult Apply(
        IWindowsAclAccessor accessor,
        IReadOnlyList<WindowsAclTarget> targets,
        string requesterSid,
        IReadOnlySet<string> effectiveIdentitySids,
        NutAdministrativeAction action)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterSid);
        ArgumentNullException.ThrowIfNull(effectiveIdentitySids);

        var originals = new List<(WindowsAclTarget Target, object OriginalSecurity)>();
        var modified = new List<(WindowsAclTarget Target, object OriginalSecurity)>();
        try
        {
            foreach (var target in targets)
            {
                var original = accessor.CloneSecurity(accessor.CaptureSecurity(target), target.IsDirectory);
                originals.Add((target, original));
            }

            if (originals.Any(item => WindowsAclPermissionEvaluation.Assess(accessor.GetRules(item.OriginalSecurity), effectiveIdentitySids) == NutPermissionState.ManualInterventionRequired))
            {
                return new NutAdministrativeActionResult(NutAdministrativeActionStatus.ManualInterventionRequired, action, "Há uma negação explícita relevante; a correção automática não foi aplicada.");
            }

            foreach (var original in originals)
            {
                var candidate = accessor.CloneSecurity(original.OriginalSecurity, original.Target.IsDirectory);
                accessor.AddModify(candidate, requesterSid, original.Target.IsDirectory);
                accessor.WriteSecurity(original.Target, candidate);
                modified.Add(original);
            }

            return new NutAdministrativeActionResult(NutAdministrativeActionStatus.Success, action, "A permissão Modify foi adicionada sem substituir ACLs existentes.");
        }
        catch (Exception exception)
        {
            var restored = true;
            foreach (var original in modified.AsEnumerable().Reverse())
            {
                try { accessor.WriteSecurity(original.Target, original.OriginalSecurity); }
                catch { restored = false; }
            }

            if (!restored) return new NutAdministrativeActionResult(NutAdministrativeActionStatus.ManualInterventionRequired, action, "A correção de permissões falhou parcialmente; é necessária recuperação manual.");
            return exception is UnauthorizedAccessException
                ? new NutAdministrativeActionResult(NutAdministrativeActionStatus.AccessDenied, action, "Permissão insuficiente para ajustar ACL; as ACLs já alteradas foram restauradas.")
                : new NutAdministrativeActionResult(NutAdministrativeActionStatus.Failed, action, "A correção de permissões falhou e as ACLs já alteradas foram restauradas.");
        }
    }
}
