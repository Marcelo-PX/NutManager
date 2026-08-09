using NutManager.Core.Administration;

namespace NutManager.Infrastructure.Platform.Windows;

/// <summary>Pure, conservative evaluation of the effective identities relevant to one ACL target.</summary>
public static class WindowsAclPermissionEvaluation
{
    private const WindowsAclRights ModifyRights = WindowsAclRights.Modify;

    public static NutPermissionState Assess(IEnumerable<WindowsAclRule> rules, IReadOnlySet<string> effectiveIdentitySids)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(effectiveIdentitySids);

        var applicable = rules.Where(rule => effectiveIdentitySids.Contains(rule.IdentitySid)).ToArray();
        if (applicable.Any(rule => rule.AccessControlType == WindowsAclAccessControlType.Deny && (rule.Rights & ModifyRights) != 0))
        {
            return NutPermissionState.ManualInterventionRequired;
        }

        var allowed = applicable
            .Where(rule => rule.AccessControlType == WindowsAclAccessControlType.Allow)
            .Aggregate(WindowsAclRights.None, (rights, rule) => rights | rule.Rights);
        return (allowed & ModifyRights) == ModifyRights
            ? NutPermissionState.Modifiable
            : NutPermissionState.Insufficient;
    }
}

public enum WindowsAclAccessControlType { Allow, Deny }

[Flags]
public enum WindowsAclRights
{
    None = 0,
    Read = 1,
    Write = 2,
    Delete = 4,
    ReadPermissions = 8,
    Synchronize = 16,
    Modify = Read | Write | Delete | ReadPermissions | Synchronize
}

public sealed record WindowsAclRule(string IdentitySid, WindowsAclAccessControlType AccessControlType, WindowsAclRights Rights);
