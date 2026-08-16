using System.ServiceProcess;

namespace NutManager.Agent;

/// <summary>
/// The agent runs as a Windows service and offers no other mode.
///
/// There is deliberately no console or interactive mode. Everything this process does is privileged,
/// and a second way to start it would be a second path to audit, to authorize and to get wrong. A
/// deployment is verified through the client, which is the same route an operator uses.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("The NutManager agent only runs on Windows.");
            return 1;
        }

        ServiceBase.Run(new NutAgentWindowsService());
        return 0;
    }
}
