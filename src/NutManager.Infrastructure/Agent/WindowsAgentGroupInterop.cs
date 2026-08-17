using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NutManager.Infrastructure.Agent;

/// <summary>
/// The only place the agent asks Windows about group membership through Win32.
///
/// One function, and it reads. <c>NetUserGetLocalGroups</c> returns the local groups an account
/// belongs to; <c>LG_INCLUDE_INDIRECT</c> asks Windows to expand membership held through another
/// group, which is how an operator who was authorized by way of a domain group is recognised. No
/// function that creates an account, creates a group, changes a membership or adjusts a privilege
/// appears in this file, so adding one would be a reviewable change rather than a silent one.
///
/// The account name always originates from the transport's authenticated identity. Nothing a client
/// sends in a request payload ever reaches this call.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsAgentGroupInterop
{
    /// <summary>Include groups held indirectly, through another group.</summary>
    private const int LgIncludeIndirect = 0x0001;

    /// <summary>Let the API size its own buffer.</summary>
    private const int MaxPreferredLength = -1;

    private const int NerrSuccess = 0;
    private const int LocalGroupInfoLevel = 0;

    /// <summary>
    /// The local groups the account belongs to. An empty list is the answer for every failure: the
    /// caller treats "no groups" as "not a member", which is the fail-closed direction.
    /// </summary>
    internal static IReadOnlyList<string> GetLocalGroups(string accountName)
    {
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

    [DllImport("Netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LocalGroupUsersInfo0
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
    }
}
