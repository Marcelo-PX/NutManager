using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using NutManager.Core.Administration;

namespace NutManager.Infrastructure.Platform.Windows;

public sealed class WindowsPrivilegeElevationBroker : IWindowsPrivilegeElevationBroker
{
    public PrivilegeState GetPrivilegeState() => WindowsNutAdministrationBackend.GetPrivilegeState();

    public async Task<NutAdministrativeActionResult> ExecuteElevatedAsync(NutAdministrativeActionRequest request, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new(NutAdministrativeActionStatus.PlatformUnsupported, request.Action, "A administração local do Windows não está disponível nesta plataforma.");
        if (!TryGetAdministrativeDirectory(create: true, out var directory)) return new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "O diretório administrativo não é seguro para a operação.");
        var requestPath = Path.Combine(directory, $"{request.RequestId:N}.request.json");
        var responsePath = GetResponsePath(requestPath);
        if (PathAlreadyExistsOrIsReparsePoint(responsePath)) return new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "Já existe uma resposta administrativa para esta solicitação.");
        var helperStarted = false;
        try
        {
            await WriteNewJsonAsync(requestPath, new ElevatedRequest(1, DateTimeOffset.UtcNow, request), cancellationToken);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            start.ArgumentList.Add("--elevated-nut-admin"); start.ArgumentList.Add(requestPath);
            try { using var process = Process.Start(start); if (process is null) return new(NutAdministrativeActionStatus.Failed, request.Action, "Não foi possível iniciar o helper elevado."); helperStarted = true; await process.WaitForExitAsync(); }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223) { return new(NutAdministrativeActionStatus.ElevationCancelled, request.Action, "A operação foi cancelada na solicitação de elevação.", request.ServiceName); }
            if (!File.Exists(responsePath)) return new(NutAdministrativeActionStatus.Failed, request.Action, "O helper elevado não retornou um resultado.", request.ServiceName);
            var response = JsonSerializer.Deserialize<ElevatedResponse>(await File.ReadAllTextAsync(responsePath, CancellationToken.None));
            return response is null || response.RequestId != request.RequestId ? new(NutAdministrativeActionStatus.InvalidRequest, request.Action, "A resposta administrativa não corresponde à solicitação.", request.ServiceName) : response.Result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !helperStarted) { return new(NutAdministrativeActionStatus.Cancelled, request.Action, "A ação administrativa foi cancelada.", request.ServiceName); }
        catch { return new(NutAdministrativeActionStatus.Failed, request.Action, "Não foi possível concluir a solicitação de elevação.", request.ServiceName); }
        finally { TryDelete(requestPath); TryDelete(responsePath); }
    }

    private static void TryDelete(string path) { try { if (File.Exists(path) && (new FileInfo(path).Attributes & FileAttributes.ReparsePoint) == 0) File.Delete(path); } catch { } }

    public static bool PathAlreadyExistsOrIsReparsePoint(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch { return true; }
    }

    private static async Task WriteNewJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, value, cancellationToken: cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    internal static bool TryGetAdministrativeDirectory(bool create, out string directory)
    {
        directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NutManager", "AdminRequests");
        try
        {
            var parent = Directory.GetParent(directory)?.FullName;
            if (parent is null) return false;
            if (create) Directory.CreateDirectory(parent);
            var parentInfo = new DirectoryInfo(parent);
            if (!parentInfo.Exists || (parentInfo.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (create) Directory.CreateDirectory(directory);
            var info = new DirectoryInfo(directory);
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch { return false; }
    }
    public static string GetResponsePath(string requestPath) => requestPath.EndsWith(".request.json", StringComparison.OrdinalIgnoreCase) ? requestPath[..^".request.json".Length] + ".response.json" : throw new ArgumentException("Nome de request inválido.", nameof(requestPath));

    public static bool TryValidateRequestPath(string requestPath, string expectedDirectory, out Guid requestId, out string canonicalRequestPath, out string canonicalResponsePath)
    {
        requestId = Guid.Empty; canonicalRequestPath = string.Empty; canonicalResponsePath = string.Empty;
        if (!WindowsPath.TryCanonicalize(requestPath, out var request) || !WindowsPath.TryCanonicalize(expectedDirectory, out var directory) || !WindowsPath.HasExactParent(request, directory)) return false;
        const string suffix = ".request.json";
        var name = request[(request.LastIndexOf('\\') + 1)..];
        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(name[..^suffix.Length], "N", out requestId)) return false;
        canonicalRequestPath = request;
        canonicalResponsePath = request[..^suffix.Length] + ".response.json";
        return WindowsPath.HasExactParent(canonicalResponsePath, directory);
    }
}

public static class WindowsElevatedHelper
{
    public static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length != 2 || args[0] != "--elevated-nut-admin") return false;
        if (!WindowsPrivilegeElevationBroker.TryGetAdministrativeDirectory(create: false, out var directory) || !WindowsPrivilegeElevationBroker.TryValidateRequestPath(args[1], directory, out _, out var requestPath, out var responsePath)) { exitCode = 2; return true; }
        if (!IsExpectedRequestFile(requestPath)) { exitCode = 2; return true; }
        exitCode = Handle(requestPath, responsePath).GetAwaiter().GetResult();
        return true;
    }

    private static bool IsExpectedRequestFile(string requestPath)
    {
        try
        {
            var info = new FileInfo(requestPath);
            var attributes = File.GetAttributes(requestPath);
            return info.Exists && (attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0;
        }
        catch { return false; }
    }

    private static async Task<int> Handle(string requestPath, string responsePath)
    {
        NutAdministrativeActionResult result;
        Guid requestId = Guid.Empty;
        try
        {
            var request = JsonSerializer.Deserialize<ElevatedRequest>(await File.ReadAllTextAsync(requestPath));
            if (!OperatingSystem.IsWindows() || WindowsNutAdministrationBackend.GetPrivilegeState() != PrivilegeState.Elevated || request is null || request.SchemaVersion != 1 || request.CreatedAtUtc < DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5) || request.CreatedAtUtc > DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1) || !string.Equals(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(requestPath)), request.Action.RequestId.ToString("N"), StringComparison.OrdinalIgnoreCase) || !WindowsNutAdministrativeRequestValidator.IsValid(request.Action))
            {
                result = new(NutAdministrativeActionStatus.InvalidRequest, request?.Action.Action ?? NutAdministrativeAction.StartService, "A solicitação administrativa não é válida.");
            }
            else
            {
                requestId = request.Action.RequestId;
                SecurityIdentifier? ownerSid;
                try { ownerSid = new FileInfo(requestPath).GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier; }
                catch { ownerSid = null; }
                if (ownerSid is null) result = new(NutAdministrativeActionStatus.InvalidRequest, request.Action.Action, "Não foi possível validar o solicitante da ação administrativa.", request.Action.ServiceName);
                else
                {
                    var detected = await new WindowsNutInstallationDetector().InspectDirectoryAsync(request.Action.InstallationDirectory, CancellationToken.None);
                    if (request.Action.PermissionRepairPlan is { } plan && !string.Equals(plan.UserSid, ownerSid.Value, StringComparison.OrdinalIgnoreCase)) result = new(NutAdministrativeActionStatus.InvalidRequest, request.Action.Action, "O SID do plano não corresponde ao solicitante.", request.Action.ServiceName);
                    else if (!detected.IsDetected || !WindowsPath.TryCanonicalize(detected.ConfigurationDirectory, out var detectedConfig) || !WindowsPath.TryCanonicalize(request.Action.ConfigurationDirectory, out var requestConfig) || !string.Equals(detectedConfig, requestConfig, StringComparison.OrdinalIgnoreCase)) result = new(NutAdministrativeActionStatus.InvalidRequest, request.Action.Action, "A solicitação não corresponde à instalação NUT atual.", request.Action.ServiceName);
                    else result = await new WindowsNutAdministrationBackend().ExecuteAsync(request.Action, CancellationToken.None);
                }
            }
        }
        catch { result = new(NutAdministrativeActionStatus.Failed, NutAdministrativeAction.StartService, "O helper administrativo não pôde concluir a ação."); }
        try
        {
            if (WindowsPrivilegeElevationBroker.PathAlreadyExistsOrIsReparsePoint(responsePath)) return 3;
            await using var stream = new FileStream(responsePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await JsonSerializer.SerializeAsync(stream, new ElevatedResponse(requestId, result));
            await stream.FlushAsync();
        }
        catch { return 3; }
        try { if ((new FileInfo(requestPath).Attributes & FileAttributes.ReparsePoint) == 0) File.Delete(requestPath); } catch { }
        return result.IsSuccess ? 0 : 1;
    }
}

internal sealed record ElevatedRequest(int SchemaVersion, DateTimeOffset CreatedAtUtc, NutAdministrativeActionRequest Action);
internal sealed record ElevatedResponse(Guid RequestId, NutAdministrativeActionResult Result);
