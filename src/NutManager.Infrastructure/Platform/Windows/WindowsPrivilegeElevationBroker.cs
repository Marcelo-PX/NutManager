using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using NutManager.Core.Administration;

namespace NutManager.Infrastructure.Platform.Windows;

public sealed class WindowsPrivilegeElevationBroker : IWindowsPrivilegeElevationBroker
{
    public PrivilegeState GetPrivilegeState() => WindowsNutAdministrationBackend.GetPrivilegeState();

    public async Task<NutAdministrativeActionResult> ExecuteElevatedAsync(NutAdministrativeActionRequest request, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new(NutAdministrativeActionStatus.PlatformUnsupported, request.Action, "A administração local do Windows não está disponível nesta plataforma.");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NutManager", "AdminRequests");
        Directory.CreateDirectory(directory);
        var requestPath = Path.Combine(directory, $"{Guid.NewGuid():N}.request.json");
        var responsePath = Path.Combine(directory, $"{Guid.NewGuid():N}.response.json");
        try
        {
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(new ElevatedRequest(1, DateTimeOffset.UtcNow, request)), cancellationToken);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            start.ArgumentList.Add("--elevated-nut-admin"); start.ArgumentList.Add(requestPath); start.ArgumentList.Add(responsePath);
            try { using var process = Process.Start(start); if (process is null) return new(NutAdministrativeActionStatus.Failed, request.Action, "Não foi possível iniciar o helper elevado."); await process.WaitForExitAsync(cancellationToken); }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223) { return new(NutAdministrativeActionStatus.ElevationCancelled, request.Action, "A operação foi cancelada na solicitação de elevação.", request.ServiceName); }
            if (!File.Exists(responsePath)) return new(NutAdministrativeActionStatus.Failed, request.Action, "O helper elevado não retornou um resultado.", request.ServiceName);
            var response = JsonSerializer.Deserialize<ElevatedResponse>(await File.ReadAllTextAsync(responsePath, cancellationToken));
            return response is null || response.RequestId != request.RequestId ? new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "A resposta administrativa não corresponde à solicitação.", request.ServiceName) : response.Result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new(NutAdministrativeActionStatus.Cancelled, request.Action, "A ação administrativa foi cancelada.", request.ServiceName); }
        catch { return new(NutAdministrativeActionStatus.Failed, request.Action, "Não foi possível concluir a solicitação de elevação.", request.ServiceName); }
        finally { TryDelete(requestPath); TryDelete(responsePath); }
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}

public static class WindowsElevatedHelper
{
    public static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length != 3 || args[0] != "--elevated-nut-admin") return false;
        exitCode = Handle(args[1], args[2]).GetAwaiter().GetResult();
        return true;
    }

    private static async Task<int> Handle(string requestPath, string responsePath)
    {
        NutAdministrativeActionResult result;
        Guid requestId = Guid.Empty;
        try
        {
            var request = JsonSerializer.Deserialize<ElevatedRequest>(await File.ReadAllTextAsync(requestPath));
            if (request is null || request.SchemaVersion != 1 || DateTimeOffset.UtcNow - request.CreatedAtUtc > TimeSpan.FromMinutes(5) || !WindowsNutAdministrativeRequestValidator.IsValid(request.Action))
            {
                result = new(NutAdministrativeActionStatus.InvalidRequest, request?.Action.Action ?? NutAdministrativeAction.StartService, "A solicitação administrativa não é válida.");
            }
            else
            {
                requestId = request.Action.RequestId;
                var detected = await new WindowsNutInstallationDetector().InspectDirectoryAsync(request.Action.InstallationDirectory, CancellationToken.None);
                if (!detected.IsDetected || !string.Equals(Path.GetFullPath(detected.ConfigurationDirectory!), Path.GetFullPath(request.Action.ConfigurationDirectory), StringComparison.OrdinalIgnoreCase)) result = new(NutAdministrativeActionStatus.InvalidRequest, request.Action.Action, "A solicitação não corresponde à instalação NUT atual.", request.Action.ServiceName);
                else result = await new WindowsNutAdministrationBackend().ExecuteAsync(request.Action, CancellationToken.None);
            }
        }
        catch { result = new(NutAdministrativeActionStatus.Failed, NutAdministrativeAction.StartService, "O helper administrativo não pôde concluir a ação."); }
        await File.WriteAllTextAsync(responsePath, JsonSerializer.Serialize(new ElevatedResponse(requestId, result)));
        try { File.Delete(requestPath); } catch { }
        return result.IsSuccess ? 0 : 1;
    }
}

internal sealed record ElevatedRequest(int SchemaVersion, DateTimeOffset CreatedAtUtc, NutAdministrativeActionRequest Action);
internal sealed record ElevatedResponse(Guid RequestId, NutAdministrativeActionResult Result);
