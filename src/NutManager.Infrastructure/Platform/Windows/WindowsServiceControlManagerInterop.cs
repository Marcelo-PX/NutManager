using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace NutManager.Infrastructure.Platform.Windows;

/// <summary>
/// The only place in NutManager that speaks to the Service Control Manager through Win32.
///
/// It exists because the managed <c>ServiceController</c> reports a remote service's state and
/// identity but not its process id or its binary path, and T34 needs both to answer "is the NUT
/// executable actually running over there". Everything here is a query. The access masks below are
/// the whole security story: <see cref="ScManagerConnect"/> to reach the SCM, and query rights on the
/// service. No start, stop, configuration, delete or security right is declared anywhere in this
/// file, so a future mutation cannot be written without first adding one — which is a reviewable
/// change rather than a silent one.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsServiceControlManagerInterop
{
    // Read-only rights. Deliberately the complete list.
    internal const uint ScManagerConnect = 0x0001;
    internal const uint ServiceQueryConfig = 0x0001;
    internal const uint ServiceQueryStatus = 0x0004;

    private const int ScStatusProcessInfo = 0;

    /// <summary>Connects to a remote SCM with connect rights only.</summary>
    internal static SafeServiceHandle OpenServiceControlManager(string host) =>
        OpenSCManagerW(host, null, ScManagerConnect);

    /// <summary>Opens one service with query rights only.</summary>
    internal static SafeServiceHandle OpenServiceForQuery(SafeServiceHandle manager, string serviceName) =>
        OpenServiceW(manager, serviceName, ServiceQueryStatus | ServiceQueryConfig);

    /// <summary>
    /// The process id the SCM has recorded for the service, or null when it reports none. A stopped
    /// service reports zero, which is an absence rather than a process, so it does not become an id.
    /// </summary>
    internal static int? TryGetProcessId(SafeServiceHandle service)
    {
        var size = Marshal.SizeOf<ServiceStatusProcess>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryServiceStatusEx(service, ScStatusProcessInfo, buffer, (uint)size, out _)) return null;
            var status = Marshal.PtrToStructure<ServiceStatusProcess>(buffer);
            return status.ProcessId == 0 ? null : (int)status.ProcessId;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// The service's configured image path, still in its raw form: quoted, possibly with arguments.
    /// Parsing it is the caller's job, using the same extraction the local detector already applies.
    /// </summary>
    internal static string? TryGetBinaryPath(SafeServiceHandle service)
    {
        // The first call fails on purpose: it only reports how large the buffer has to be.
        QueryServiceConfigW(service, IntPtr.Zero, 0, out var needed);
        if (needed == 0) return null;

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!QueryServiceConfigW(service, buffer, needed, out _)) return null;
            return Marshal.PtrToStructure<QueryServiceConfig>(buffer).BinaryPathName;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeServiceHandle OpenServiceW(SafeServiceHandle manager, string serviceName, uint desiredAccess);

    [DllImport("Advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(SafeServiceHandle service, int infoLevel, IntPtr buffer, uint bufferSize, out uint bytesNeeded);

    [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryServiceConfigW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceConfigW(SafeServiceHandle service, IntPtr config, uint bufferSize, out uint bytesNeeded);

    [DllImport("Advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseServiceHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct QueryServiceConfig
    {
        public uint ServiceType;
        public uint StartType;
        public uint ErrorControl;
        [MarshalAs(UnmanagedType.LPWStr)] public string BinaryPathName;
        [MarshalAs(UnmanagedType.LPWStr)] public string LoadOrderGroup;
        public uint TagId;
        [MarshalAs(UnmanagedType.LPWStr)] public string Dependencies;
        // The account the service runs as. Named to avoid reading like a start right, which it is not.
        [MarshalAs(UnmanagedType.LPWStr)] public string ServiceAccountName;
        [MarshalAs(UnmanagedType.LPWStr)] public string DisplayName;
    }
}

/// <summary>An SC_HANDLE that closes itself, so no raw handle is carried around the application.</summary>
[SupportedOSPlatform("windows")]
internal sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeServiceHandle() : base(true)
    {
    }

    protected override bool ReleaseHandle() => WindowsServiceControlManagerInterop.CloseServiceHandle(handle);
}
