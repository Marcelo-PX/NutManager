using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace NutManager.Infrastructure.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsNativeAclAccessor : IWindowsAclAccessor
{
    public object CaptureSecurity(WindowsAclTarget target) => target.IsDirectory
        ? (object)new DirectoryInfo(target.Path).GetAccessControl()
        : new FileInfo(target.Path).GetAccessControl();

    public object CloneSecurity(object security, bool isDirectory)
    {
        var descriptor = ((ObjectSecurity)security).GetSecurityDescriptorBinaryForm();
        if (isDirectory)
        {
            var clone = new DirectorySecurity();
            clone.SetSecurityDescriptorBinaryForm(descriptor);
            return clone;
        }

        var fileClone = new FileSecurity();
        fileClone.SetSecurityDescriptorBinaryForm(descriptor);
        return fileClone;
    }

    public IReadOnlyList<WindowsAclRule> GetRules(object security) => ((FileSystemSecurity)security)
        .GetAccessRules(true, true, typeof(SecurityIdentifier))
        .OfType<FileSystemAccessRule>()
        .Select(rule => new WindowsAclRule(
            rule.IdentityReference.Value,
            rule.AccessControlType == AccessControlType.Allow ? WindowsAclAccessControlType.Allow : WindowsAclAccessControlType.Deny,
            ToAclRights(rule.FileSystemRights)))
        .ToArray();

    public void AddModify(object candidateSecurity, string userSid, bool isDirectory)
    {
        var rule = new FileSystemAccessRule(new SecurityIdentifier(userSid), FileSystemRights.Modify, AccessControlType.Allow);
        if (isDirectory) ((DirectorySecurity)candidateSecurity).AddAccessRule(rule);
        else ((FileSecurity)candidateSecurity).AddAccessRule(rule);
    }

    public void WriteSecurity(WindowsAclTarget target, object security)
    {
        if (target.IsDirectory) new DirectoryInfo(target.Path).SetAccessControl((DirectorySecurity)security);
        else new FileInfo(target.Path).SetAccessControl((FileSecurity)security);
    }

    private static WindowsAclRights ToAclRights(FileSystemRights rights)
    {
        var result = WindowsAclRights.None;
        if ((rights & FileSystemRights.ReadData) != 0) result |= WindowsAclRights.ReadData;
        if ((rights & FileSystemRights.WriteData) != 0) result |= WindowsAclRights.WriteData;
        if ((rights & FileSystemRights.AppendData) != 0) result |= WindowsAclRights.AppendData;
        if ((rights & FileSystemRights.ReadExtendedAttributes) != 0) result |= WindowsAclRights.ReadExtendedAttributes;
        if ((rights & FileSystemRights.WriteExtendedAttributes) != 0) result |= WindowsAclRights.WriteExtendedAttributes;
        if ((rights & FileSystemRights.ExecuteFile) != 0) result |= WindowsAclRights.ExecuteFile;
        if ((rights & FileSystemRights.ReadAttributes) != 0) result |= WindowsAclRights.ReadAttributes;
        if ((rights & FileSystemRights.WriteAttributes) != 0) result |= WindowsAclRights.WriteAttributes;
        if ((rights & FileSystemRights.Delete) != 0) result |= WindowsAclRights.Delete;
        if ((rights & FileSystemRights.ReadPermissions) != 0) result |= WindowsAclRights.ReadPermissions;
        return result;
    }
}
