using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace NutManager.Infrastructure.Agent;

/// <summary>
/// What kind of account a name resolved to, as Windows reports it in <c>SID_NAME_USE</c>.
///
/// The agent inspects this rather than trusting that a name which resolved is a name it may treat as
/// a group: a user, a computer or a well-known SID answering to the operators group's name must not
/// silently become the authority over a UPS.
/// </summary>
public enum WindowsAccountKind
{
    Unknown = 0,
    User = 1,
    Group = 2,
    Domain = 3,
    Alias = 4,
    WellKnownGroup = 5,
    DeletedAccount = 6,
    Invalid = 7,
    Computer = 9,
}

/// <summary>
/// The server's own local security database, as the agent is allowed to question it.
///
/// This exists as an interface for one reason: the difference this abstraction hides — a member
/// server answering from its SAM and a domain controller answering from the directory it uses as its
/// local database — cannot be exercised on one test machine. The fake lets the domain-controller
/// case be proven without a domain controller.
///
/// Every member reads. Nothing here creates a group, changes a membership or adjusts a privilege.
/// </summary>
public interface IWindowsLocalSecurityDatabase
{
    /// <summary>
    /// Whether the name exists as a group in <em>this server's</em> local group database — the SAM on
    /// a member server, the corresponding directory representation on a domain controller.
    /// </summary>
    (bool Exists, string? Failure) FindLocalGroup(string groupName);

    /// <summary>
    /// Translates a name to a SID, starting the search at the local system so that a local group wins
    /// over a domain account of the same name.
    /// </summary>
    (string? Sid, WindowsAccountKind Kind, string? Domain, string? Failure) LookupAccount(string accountName);

    /// <summary>The local groups an account belongs to, directly or through another group.</summary>
    IReadOnlyList<string> GetLocalGroupNames(string accountName);
}

/// <summary>
/// The only place the agent asks Windows about groups through Win32.
///
/// Three calls, and all of them read. <c>NetLocalGroupGetInfo</c> proves that a name really is a
/// group in the local database; <c>LookupAccountName</c> translates it starting at the local system;
/// <c>NetUserGetLocalGroups</c> with <c>LG_INCLUDE_INDIRECT</c> asks Windows to expand membership
/// held through another group. No function that creates an account, creates a group, changes a
/// membership or adjusts a privilege appears in this file, so adding one would be a reviewable change
/// rather than a silent one.
///
/// Every call passes <c>NULL</c> for the server name, which is the documented way of saying "this
/// computer" and is what makes the same code correct on a member server and on a domain controller.
/// The previous implementation named the authority itself, by qualifying the group with the local
/// computer's name, and that is precisely the assumption a domain controller breaks.
///
/// The account name always originates from the transport's authenticated identity. Nothing a client
/// sends in a request payload ever reaches these calls.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAgentGroupInterop : IWindowsLocalSecurityDatabase
{
    /// <summary>Include groups held indirectly, through another group.</summary>
    private const int LgIncludeIndirect = 0x0001;

    /// <summary>Let the API size its own buffer.</summary>
    private const int MaxPreferredLength = -1;

    private const int NerrSuccess = 0;
    private const int NerrGroupNotFound = 2220;
    private const int ErrorNoneMapped = 1332;
    private const int ErrorInsufficientBuffer = 122;
    private const int LocalGroupInfoLevel = 0;

    /// <summary>
    /// A SID is at most 68 bytes and a domain name at most 256 characters. The bounds exist so a
    /// hostile or corrupted answer cannot turn a size request into an unbounded allocation.
    /// </summary>
    private const uint MaxSidBytes = 256;
    private const uint MaxDomainChars = 1024;

    public (bool Exists, string? Failure) FindLocalGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return (false, "No group name was configured.");

        var buffer = IntPtr.Zero;
        try
        {
            var status = NetLocalGroupGetInfo(null, groupName, LocalGroupInfoLevel, out buffer);

            return status switch
            {
                NerrSuccess when buffer != IntPtr.Zero => (true, null),
                NerrSuccess => (false, $"The local group '{groupName}' returned no information."),
                NerrGroupNotFound => (false, $"The local group '{groupName}' does not exist in this server's local group database."),
                _ => (false, $"The local group '{groupName}' could not be queried (NetAPI status {status})."),
            };
        }
        catch (Exception exception)
        {
            return (false, $"The local group '{groupName}' could not be queried ({exception.GetType().Name}).");
        }
        finally
        {
            if (buffer != IntPtr.Zero) NetApiBufferFree(buffer);
        }
    }

    public (string? Sid, WindowsAccountKind Kind, string? Domain, string? Failure) LookupAccount(string accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName)) return (null, WindowsAccountKind.Unknown, null, "No account name was supplied.");

        try
        {
            uint sidBytes = 0;
            uint domainChars = 0;

            // First call sizes the buffers. It is expected to fail with ERROR_INSUFFICIENT_BUFFER;
            // any other failure is the real answer and is reported rather than retried.
            if (!LookupAccountName(null, accountName, null, ref sidBytes, null, ref domainChars, out _))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ErrorInsufficientBuffer)
                {
                    return error == ErrorNoneMapped
                        ? (null, WindowsAccountKind.Unknown, null, $"The name '{accountName}' could not be translated to a SID.")
                        : (null, WindowsAccountKind.Unknown, null, $"The name '{accountName}' could not be translated (Win32 error {error}).");
                }
            }

            if (sidBytes == 0 || sidBytes > MaxSidBytes || domainChars > MaxDomainChars)
            {
                return (null, WindowsAccountKind.Unknown, null, $"The name '{accountName}' reported an implausible SID size.");
            }

            var sid = new byte[sidBytes];
            var domain = new char[Math.Max(domainChars, 1)];

            if (!LookupAccountName(null, accountName, sid, ref sidBytes, domain, ref domainChars, out var use))
            {
                var error = Marshal.GetLastWin32Error();
                return (null, WindowsAccountKind.Unknown, null, $"The name '{accountName}' could not be translated (Win32 error {error}).");
            }

            var identifier = new SecurityIdentifier(sid, 0);
            var domainName = domainChars > 0 ? new string(domain, 0, (int)domainChars) : null;

            return (identifier.Value, ToKind(use), domainName, null);
        }
        catch (Exception exception)
        {
            return (null, WindowsAccountKind.Unknown, null, $"The name '{accountName}' could not be translated ({exception.GetType().Name}).");
        }
    }

    /// <summary>
    /// The local groups the account belongs to. An empty list is the answer for every failure: the
    /// caller treats "no groups" as "not a member", which is the fail-closed direction.
    /// </summary>
    public IReadOnlyList<string> GetLocalGroupNames(string accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName)) return [];

        var buffer = IntPtr.Zero;
        try
        {
            var status = NetUserGetLocalGroups(
                null, accountName, LocalGroupInfoLevel, LgIncludeIndirect, out buffer, MaxPreferredLength, out var read, out _);

            if (status != NerrSuccess || buffer == IntPtr.Zero || read <= 0) return [];

            var names = new List<string>(read);
            var size = Marshal.SizeOf<LocalGroupUsersInfo0>();
            for (var index = 0; index < read; index++)
            {
                var entry = Marshal.PtrToStructure<LocalGroupUsersInfo0>(IntPtr.Add(buffer, index * size));
                if (!string.IsNullOrWhiteSpace(entry.Name)) names.Add(entry.Name);
            }

            return names;
        }
        finally
        {
            if (buffer != IntPtr.Zero) NetApiBufferFree(buffer);
        }
    }

    /// <summary>Unknown values map to <see cref="WindowsAccountKind.Unknown"/>, which is refused.</summary>
    private static WindowsAccountKind ToKind(int use) => use switch
    {
        1 => WindowsAccountKind.User,
        2 => WindowsAccountKind.Group,
        3 => WindowsAccountKind.Domain,
        4 => WindowsAccountKind.Alias,
        5 => WindowsAccountKind.WellKnownGroup,
        6 => WindowsAccountKind.DeletedAccount,
        7 => WindowsAccountKind.Invalid,
        9 => WindowsAccountKind.Computer,
        _ => WindowsAccountKind.Unknown,
    };

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int NetUserGetLocalGroups(
        [MarshalAs(UnmanagedType.LPWStr)] string? serverName,
        [MarshalAs(UnmanagedType.LPWStr)] string userName,
        int level,
        int flags,
        out IntPtr buffer,
        int preferredMaximumLength,
        out int entriesRead,
        out int totalEntries);

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int NetLocalGroupGetInfo(
        [MarshalAs(UnmanagedType.LPWStr)] string? serverName,
        [MarshalAs(UnmanagedType.LPWStr)] string groupName,
        int level,
        out IntPtr buffer);

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupAccountName(
        [MarshalAs(UnmanagedType.LPWStr)] string? systemName,
        [MarshalAs(UnmanagedType.LPWStr)] string accountName,
        byte[]? sid,
        ref uint sidSize,
        char[]? referencedDomainName,
        ref uint referencedDomainNameSize,
        out int use);

    [DllImport("Netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LocalGroupUsersInfo0
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
    }
}
