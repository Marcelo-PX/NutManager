using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using NutManager.Core.Administration;
using NutManager.Core.Configuration;
using NutManager.Core.Models;
using NutManager.Core.Services;
using NutManager.Infrastructure.Configuration;

namespace NutManager.Infrastructure.Platform.Windows;

/// <summary>
/// Passive Windows serial-device source. It never opens a COM port.
/// </summary>
public interface IWindowsComPortSource
{
    IReadOnlyList<NutComPortInfo> GetPorts();
}

public interface IWindowsDriverFileSystem
{
    bool FileExists(string path);
}

public interface IWindowsDriverProcessInspector
{
    bool IsProcessRunning(string executablePath);
}

public interface INutDiagnosticProcessRunner
{
    Task<NutDiagnosticProcessResult> RunAsync(NutDiagnosticProcessSpec specification, CancellationToken cancellationToken);
}

public sealed record NutDiagnosticProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string ConfigurationDirectory,
    TimeSpan Timeout,
    bool QuietInitializationBanner);

public sealed record NutDiagnosticProcessResult(
    NutDriverDiagnosticStatus Status,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool OutputTruncated,
    TimeSpan Duration,
    string Message);

public sealed class WindowsWmiComPortSource : IWindowsComPortSource
{
    public IReadOnlyList<NutComPortInfo> GetPorts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<NutComPortInfo>();
        }

        var ports = new Dictionary<string, NutComPortInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Name, Manufacturer, PNPDeviceID, Status, ConfigManagerErrorCode FROM Win32_SerialPort");
            using var results = searcher.Get();
            foreach (ManagementObject port in results)
            {
                var rawPort = port["DeviceID"]?.ToString();
                if (!WindowsComPortNormalizer.TryNormalize(rawPort, out var normalized))
                {
                    continue;
                }

                int? errorCode = port["ConfigManagerErrorCode"] is null
                    ? null
                    : Convert.ToInt32(port["ConfigManagerErrorCode"], System.Globalization.CultureInfo.InvariantCulture);
                ports[normalized] = new NutComPortInfo(
                    normalized,
                    port["Name"]?.ToString(),
                    port["Manufacturer"]?.ToString(),
                    port["PNPDeviceID"]?.ToString(),
                    port["Status"]?.ToString(),
                    errorCode,
                    true);
            }
        }
        catch
        {
            // The snapshot reports an empty passive list when WMI is unavailable. No port is opened as fallback.
        }

        return ports.Values.OrderBy(port => port.PortName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public sealed class WindowsNutDriverDiagnostics : ILocalNutDriverDiagnostics
{
    private readonly INutConfigurationFilePipeline _configurationPipeline;
    private readonly IWindowsComPortSource _comPortSource;
    private readonly IWindowsDriverFileSystem _fileSystem;
    private readonly IWindowsDriverProcessInspector _processInspector;
    private readonly INutDiagnosticProcessRunner _processRunner;

    public WindowsNutDriverDiagnostics()
        : this(
            new NutConfigurationFilePipeline(),
            new WindowsWmiComPortSource(),
            new WindowsDriverFileSystem(),
            new WindowsDriverProcessInspector(),
            new WindowsNutDiagnosticProcessRunner())
    {
    }

    public WindowsNutDriverDiagnostics(
        INutConfigurationFilePipeline configurationPipeline,
        IWindowsComPortSource comPortSource,
        IWindowsDriverFileSystem fileSystem,
        IWindowsDriverProcessInspector processInspector,
        INutDiagnosticProcessRunner processRunner)
    {
        _configurationPipeline = configurationPipeline;
        _comPortSource = comPortSource;
        _fileSystem = fileSystem;
        _processInspector = processInspector;
        _processRunner = processRunner;
    }

    public async Task<NutDriverDiagnosticsSnapshot> InspectAsync(NutInstallationInfo installation, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return NutDriverDiagnosticsSnapshot.Unsupported();
        }

        if (!TryGetConfigurationContext(installation, out var installationDirectory, out var configurationDirectory))
        {
            return new NutDriverDiagnosticsSnapshot(true, Array.Empty<NutComPortInfo>(), Array.Empty<NutConfiguredDriver>(), null, "Nenhuma instalação NUT local válida foi selecionada.");
        }

        var ports = _comPortSource.GetPorts();
        var load = await _configurationPipeline.LoadAsync(configurationDirectory + "\\ups.conf", NutConfigurationFileKind.UpsConf, cancellationToken);
        if (load.Status != NutConfigurationLoadStatus.Success || load.Snapshot is null)
        {
            return new NutDriverDiagnosticsSnapshot(true, ports, Array.Empty<NutConfiguredDriver>(), ResolveUpsdrvctlPath(installationDirectory), LoadMessage(load.Status));
        }

        var drivers = WindowsUpsConfigurationInterpreter.Interpret(
            load.Snapshot.Document,
            installationDirectory,
            ports,
            ResolveDriver,
            _processInspector.IsProcessRunning);
        return new NutDriverDiagnosticsSnapshot(true, ports, drivers, ResolveUpsdrvctlPath(installationDirectory));
    }

    public async Task<NutDriverDiagnosticResult> ExecuteAsync(NutDriverDiagnosticRequest request, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return NutDriverDiagnosticResult.Unsupported(request.Kind, "Diagnósticos locais de portas e drivers do Windows não estão disponíveis nesta plataforma.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.CancelledBeforeLaunch, "O diagnóstico foi cancelado antes de iniciar.");
        }

        if (!WindowsNutDriverDiagnosticValidator.IsValidContext(request.InstallationDirectory, request.ConfigurationDirectory))
        {
            return CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.InvalidConfiguration, "O contexto da instalação NUT não é válido.");
        }

        (NutDriverDiagnosticRequest? Request, NutDriverDiagnosticResult? Result) prepared;
        try
        {
            prepared = await LoadCurrentRequestAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.CancelledBeforeLaunch, "O diagnóstico foi cancelado antes de iniciar.");
        }
        if (prepared.Result is not null)
        {
            return prepared.Result;
        }

        if (prepared.Request!.Kind == NutDriverDiagnosticKind.DriverDataDump)
        {
            var interlock = await ValidateHardwareInterlocksAsync(prepared.Request, cancellationToken);
            if (interlock is not null)
            {
                return interlock;
            }
        }

        var specification = WindowsNutDiagnosticCommandBuilder.Create(prepared.Request!, ResolveUpsdrvctlPath(prepared.Request!.InstallationDirectory));
        if (specification is null)
        {
            return CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.InvalidExecutable, "A ferramenta NUT não está disponível ou não é confiável para este diagnóstico.");
        }

        var raw = await _processRunner.RunAsync(specification, cancellationToken);
        return new NutDriverDiagnosticResult(
            request.Kind,
            raw.Status,
            Path.GetFileName(specification.FileName),
            DateTimeOffset.UtcNow - raw.Duration,
            raw.Duration,
            raw.ExitCode,
            WindowsNutDiagnosticOutput.Redact(raw.StandardOutput),
            WindowsNutDiagnosticOutput.Redact(raw.StandardError),
            raw.OutputTruncated,
            request.Kind == NutDriverDiagnosticKind.DriverDataDump,
            raw.Message);
    }

    private async Task<(NutDriverDiagnosticRequest? Request, NutDriverDiagnosticResult? Result)> LoadCurrentRequestAsync(
        NutDriverDiagnosticRequest request,
        CancellationToken cancellationToken)
    {
        var load = await _configurationPipeline.LoadAsync(request.ConfigurationDirectory + "\\ups.conf", NutConfigurationFileKind.UpsConf, cancellationToken);
        if (load.Status != NutConfigurationLoadStatus.Success || load.Snapshot is null)
        {
            return (null, CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.InvalidConfiguration, LoadMessage(load.Status)));
        }

        if (request.Kind is NutDriverDiagnosticKind.UpsdrvctlHelp or NutDriverDiagnosticKind.UpsdrvctlList or NutDriverDiagnosticKind.UpsdrvctlStatus)
        {
            if (request.Driver is null)
            {
                return (request, null);
            }
        }

        if (request.Driver is null)
        {
            return (null, CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.InvalidConfiguration, "Selecione um dispositivo configurado antes de executar este diagnóstico."));
        }

        var current = WindowsUpsConfigurationInterpreter.Interpret(
                load.Snapshot.Document,
                request.InstallationDirectory,
                Array.Empty<NutComPortInfo>(),
                ResolveDriver,
                _processInspector.IsProcessRunning)
            .FirstOrDefault(driver => string.Equals(driver.UpsName, request.Driver.UpsName, StringComparison.Ordinal));
        if (current is null || !string.Equals(current.DriverName, request.Driver.DriverName, StringComparison.Ordinal) ||
            !string.Equals(current.Executable.Path, request.Driver.Executable.Path, StringComparison.OrdinalIgnoreCase))
        {
            return (null, CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.InvalidConfiguration, "A configuração do driver foi alterada desde a revisão do diagnóstico."));
        }

        if ((request.Kind is NutDriverDiagnosticKind.DriverHelp or NutDriverDiagnosticKind.DriverVersion or NutDriverDiagnosticKind.DriverVariableList or NutDriverDiagnosticKind.DriverDataDump) &&
            (!current.Executable.IsAvailable || !current.Executable.IsTrusted))
        {
            return (null, CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.InvalidExecutable, "O executável do driver não está disponível ou não é confiável."));
        }

        return (request with { Driver = current }, null);
    }

    private NutDriverExecutableInfo ResolveDriver(string installationDirectory, string? driverPath, string? driverName)
    {
        return WindowsNutDriverResolver.Resolve(installationDirectory, driverPath, driverName, _fileSystem.FileExists);
    }

    private async Task<NutDriverDiagnosticResult?> ValidateHardwareInterlocksAsync(NutDriverDiagnosticRequest request, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.Unsupported, "Diagnósticos locais de portas e drivers do Windows não estão disponíveis nesta plataforma.");
        }

        var driver = request.Driver!;
        if (!driver.Executable.IsAvailable || !driver.Executable.IsTrusted)
        {
            return CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.Conflict, "O executável do driver não está disponível ou não é confiável.");
        }

        var services = await WindowsNutServiceController.DiscoverAsync(request.InstallationDirectory, cancellationToken);
        if (services.Count == 0 || services.Any(service => service.State != NutServiceState.Stopped))
        {
            return CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.Conflict, "O serviço NUT não está confirmado como parado e pode estar usando o dispositivo.");
        }

        if (_processInspector.IsProcessRunning(driver.Executable.Path!))
        {
            return CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.Conflict, "Há um processo do driver configurado em execução.");
        }

        if (driver.NormalizedComPort is not null && !_comPortSource.GetPorts().Any(port =>
                string.Equals(port.PortName, driver.NormalizedComPort, StringComparison.OrdinalIgnoreCase) && port.IsPresent))
        {
            return CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.Conflict, "A porta COM configurada não foi detectada pelo Windows.");
        }

        return null;
    }

    private string? ResolveUpsdrvctlPath(string installationDirectory)
    {
        foreach (var candidate in new[] { installationDirectory + "\\bin\\upsdrvctl.exe", installationDirectory + "\\upsdrvctl.exe" })
        {
            if (WindowsPath.IsInside(candidate, installationDirectory) && _fileSystem.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool TryGetConfigurationContext(NutInstallationInfo installation, out string installationDirectory, out string configurationDirectory)
    {
        installationDirectory = string.Empty;
        configurationDirectory = string.Empty;
        return installation.IsDetected &&
            WindowsPath.TryCanonicalize(installation.InstallationDirectory, out installationDirectory) &&
            WindowsPath.TryCanonicalize(installation.ConfigurationDirectory, out configurationDirectory) &&
            WindowsPath.IsSameOrInside(configurationDirectory, installationDirectory);
    }

    private static NutDriverDiagnosticResult CreateImmediateResult(NutDriverDiagnosticKind kind, NutDriverDiagnosticStatus status, string message) =>
        new(kind, status, string.Empty, DateTimeOffset.UtcNow, TimeSpan.Zero, null, string.Empty, string.Empty, false, kind == NutDriverDiagnosticKind.DriverDataDump, message);

    private static string LoadMessage(NutConfigurationLoadStatus status) => status switch
    {
        NutConfigurationLoadStatus.TargetNotFound => "O arquivo ups.conf não existe neste diretório.",
        NutConfigurationLoadStatus.AccessDenied => "Não há permissão para ler ups.conf.",
        NutConfigurationLoadStatus.UnsupportedEncoding => "A codificação de ups.conf não é suportada.",
        NutConfigurationLoadStatus.Cancelled => "O carregamento de ups.conf foi cancelado.",
        _ => "Não foi possível carregar ups.conf para o diagnóstico."
    };
}

public static class WindowsComPortNormalizer
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.StartsWith("\\\\.\\", StringComparison.Ordinal))
        {
            candidate = candidate[4..];
        }

        if (!candidate.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(candidate[3..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) ||
            number < 1)
        {
            return false;
        }

        normalized = "COM" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }
}

public static class WindowsNutDriverResolver
{
    public static NutDriverExecutableInfo Resolve(string installationDirectory, string? configuredDriverPath, string? driverName, Func<string, bool> fileExists)
    {
        if (!WindowsPath.TryCanonicalize(installationDirectory, out var installation) || !IsValidDriverName(driverName))
        {
            return new NutDriverExecutableInfo(null, NutDriverExecutableState.InvalidName, false);
        }

        var driverDirectory = string.IsNullOrWhiteSpace(configuredDriverPath)
            ? installation + "\\bin"
            : WindowsPath.TryCanonicalize(configuredDriverPath, out var configured) ? configured : null;
        if (driverDirectory is null)
        {
            return new NutDriverExecutableInfo(null, NutDriverExecutableState.Untrusted, false);
        }

        var executable = driverDirectory + "\\" + driverName + ".exe";
        if (!WindowsPath.IsInside(executable, installation))
        {
            return new NutDriverExecutableInfo(executable, NutDriverExecutableState.Untrusted, false);
        }

        if (!fileExists(executable))
        {
            return new NutDriverExecutableInfo(executable, NutDriverExecutableState.Missing, true);
        }

        return new NutDriverExecutableInfo(executable, NutDriverExecutableState.Available, true);
    }

    public static bool IsValidDriverName(string? driverName) =>
        !string.IsNullOrWhiteSpace(driverName) &&
        driverName.IndexOfAny(['\\', '/', ':']) < 0 &&
        !driverName.Contains("..", StringComparison.Ordinal) &&
        !driverName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
        driverName.All(character => !char.IsControl(character));
}

public static class WindowsUpsConfigurationInterpreter
{
    public static IReadOnlyList<NutConfiguredDriver> Interpret(
        NutConfigurationDocument document,
        string installationDirectory,
        IReadOnlyList<NutComPortInfo> ports,
        Func<string, string?, string?, NutDriverExecutableInfo> resolveDriver,
        Func<string, bool> isProcessRunning)
    {
        var driverPath = document.FindAssignments("driverpath", sectionName: null, StringComparison.OrdinalIgnoreCase).FirstOrDefault()?.Value;
        var configuredPorts = ports.Select(port => port.PortName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return document.Sections.Select(section =>
        {
            string? Get(string name) => document.FindAssignments(name, section.Name, StringComparison.OrdinalIgnoreCase).FirstOrDefault()?.Value;
            var driverName = Get("driver");
            var port = Get("port");
            var normalizedCom = WindowsComPortNormalizer.TryNormalize(port, out var com) ? com : null;
            var executable = resolveDriver(installationDirectory, driverPath, driverName);
            return new NutConfiguredDriver(
                section.Name,
                Get("desc"),
                driverName,
                port,
                normalizedCom,
                Get("protocol"),
                driverPath,
                executable,
                normalizedCom is null || configuredPorts.Contains(normalizedCom),
                executable.Path is not null && isProcessRunning(executable.Path) ? NutDriverRuntimeState.Running : NutDriverRuntimeState.NotRunning,
                driverName is null ? "A seção UPS não define um driver." : null);
        }).ToArray();
    }
}

internal static class WindowsNutDriverDiagnosticValidator
{
    public static bool IsValidContext(string installationDirectory, string configurationDirectory) =>
        WindowsPath.TryCanonicalize(installationDirectory, out var installation) &&
        WindowsPath.TryCanonicalize(configurationDirectory, out var configuration) &&
        WindowsPath.IsSameOrInside(configuration, installation);
}

public static class WindowsNutDiagnosticCommandBuilder
{
    public static NutDiagnosticProcessSpec? Create(NutDriverDiagnosticRequest request, string? upsdrvctlPath)
    {
        var driver = request.Driver;
        var driverCommand = driver?.Executable.Path;
        return request.Kind switch
        {
            NutDriverDiagnosticKind.UpsdrvctlHelp when upsdrvctlPath is not null => new(upsdrvctlPath, ["-h"], request.ConfigurationDirectory, TimeSpan.FromSeconds(10), false),
            NutDriverDiagnosticKind.UpsdrvctlList when upsdrvctlPath is not null => new(upsdrvctlPath, driver is null ? ["list"] : ["list", driver.UpsName], request.ConfigurationDirectory, TimeSpan.FromSeconds(15), true),
            NutDriverDiagnosticKind.UpsdrvctlStatus when upsdrvctlPath is not null => new(upsdrvctlPath, driver is null ? ["status"] : ["status", driver.UpsName], request.ConfigurationDirectory, TimeSpan.FromSeconds(15), true),
            NutDriverDiagnosticKind.UpsdrvctlDryRunStart when upsdrvctlPath is not null && driver is not null => new(upsdrvctlPath, ["-t", "start", driver.UpsName], request.ConfigurationDirectory, TimeSpan.FromSeconds(15), true),
            NutDriverDiagnosticKind.DriverHelp when driverCommand is not null => new(driverCommand, ["-h"], request.ConfigurationDirectory, TimeSpan.FromSeconds(10), false),
            NutDriverDiagnosticKind.DriverVersion when driverCommand is not null => new(driverCommand, ["-V"], request.ConfigurationDirectory, TimeSpan.FromSeconds(10), false),
            NutDriverDiagnosticKind.DriverVariableList when driverCommand is not null => new(driverCommand, ["-L"], request.ConfigurationDirectory, TimeSpan.FromSeconds(10), false),
            NutDriverDiagnosticKind.DriverDataDump when driverCommand is not null => new(driverCommand, ["-a", driver!.UpsName, "-d"], request.ConfigurationDirectory, TimeSpan.FromSeconds(30), false),
            _ => null
        };
    }
}

public sealed class WindowsDriverFileSystem : IWindowsDriverFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
}

public sealed class WindowsDriverProcessInspector : IWindowsDriverProcessInspector
{
    public bool IsProcessRunning(string executablePath)
    {
        if (!OperatingSystem.IsWindows() || !WindowsPath.TryCanonicalize(executablePath, out var expected))
        {
            return false;
        }

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (WindowsPath.TryCanonicalize(process.MainModule?.FileName, out var actual) &&
                        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Process path inspection is best effort and never falls back to an ambiguous process name.
                }
            }
        }

        return false;
    }
}

public sealed class WindowsNutDiagnosticProcessRunner : INutDiagnosticProcessRunner
{
    private const int OutputLimit = 1024 * 1024;

    public async Task<NutDiagnosticProcessResult> RunAsync(NutDiagnosticProcessSpec specification, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new(NutDriverDiagnosticStatus.CancelledBeforeLaunch, null, string.Empty, string.Empty, false, TimeSpan.Zero, "O diagnóstico foi cancelado antes de iniciar.");
        }

        var start = Stopwatch.StartNew();
        try
        {
            using var process = new Process { StartInfo = CreateStartInfo(specification), EnableRaisingEvents = true };
            if (!process.Start())
            {
                return new(NutDriverDiagnosticStatus.Failed, null, string.Empty, string.Empty, false, start.Elapsed, "Não foi possível iniciar o diagnóstico do NUT.");
            }

            var standardOutput = ReadBoundedAsync(process.StandardOutput);
            var standardError = ReadBoundedAsync(process.StandardError);
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waitCts.CancelAfter(specification.Timeout);
            try
            {
                await process.WaitForExitAsync(waitCts.Token);
            }
            catch (OperationCanceledException)
            {
                TryKillCreatedProcess(process);
                await process.WaitForExitAsync(CancellationToken.None);
                var output = await standardOutput;
                var error = await standardError;
                return new(cancellationToken.IsCancellationRequested ? NutDriverDiagnosticStatus.Failed : NutDriverDiagnosticStatus.Timeout, null, output.Text, error.Text, output.Truncated || error.Truncated, start.Elapsed, cancellationToken.IsCancellationRequested ? "O diagnóstico foi cancelado após iniciar e o processo criado foi encerrado." : "O diagnóstico excedeu o tempo limite e o processo criado foi encerrado.");
            }

            var stdout = await standardOutput;
            var stderr = await standardError;
            var outputTruncated = stdout.Truncated || stderr.Truncated;
            var status = process.ExitCode == 0
                ? outputTruncated ? NutDriverDiagnosticStatus.OutputTruncated : NutDriverDiagnosticStatus.Success
                : unchecked((int)0xC0000135) == process.ExitCode ? NutDriverDiagnosticStatus.MissingDependency : NutDriverDiagnosticStatus.NonZeroExit;
            return new(status, process.ExitCode, stdout.Text, stderr.Text, outputTruncated, start.Elapsed, status switch
            {
                NutDriverDiagnosticStatus.Success => "O diagnóstico foi concluído.",
                NutDriverDiagnosticStatus.OutputTruncated => "O diagnóstico foi concluído, mas a saída foi truncada por segurança.",
                NutDriverDiagnosticStatus.MissingDependency => "O executável não pôde carregar uma dependência necessária.",
                _ => "A ferramenta NUT terminou com erro."
            });
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 2)
        {
            return new(NutDriverDiagnosticStatus.ExecutableNotFound, null, string.Empty, string.Empty, false, start.Elapsed, "O executável de diagnóstico não foi encontrado.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            return new(NutDriverDiagnosticStatus.AccessDenied, null, string.Empty, string.Empty, false, start.Elapsed, "Permissão insuficiente para iniciar o diagnóstico.");
        }
        catch (Exception)
        {
            return new(NutDriverDiagnosticStatus.Failed, null, string.Empty, string.Empty, false, start.Elapsed, "Não foi possível executar o diagnóstico do NUT.");
        }
    }

    public static ProcessStartInfo CreateStartInfo(NutDiagnosticProcessSpec specification)
    {
        var startInfo = new ProcessStartInfo(specification.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in specification.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["NUT_CONFPATH"] = specification.ConfigurationDirectory;
        if (specification.QuietInitializationBanner)
        {
            startInfo.Environment["NUT_QUIET_INIT_BANNER"] = "true";
        }

        return startInfo;
    }

    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        var captured = new StringBuilder();
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            var remaining = OutputLimit - captured.Length;
            if (remaining > 0)
            {
                captured.Append(buffer, 0, Math.Min(remaining, read));
            }

            if (read > remaining)
            {
                truncated = true;
            }
        }

        return (captured.ToString(), truncated);
    }

    private static void TryKillCreatedProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Only the child started above is ever targeted, and a failed cleanup is reported through the timeout result.
        }
    }
}

public static partial class WindowsNutDiagnosticOutput
{
    [GeneratedRegex("(password|passwd|secret|token|community|credential|authpassword|privpassword)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveToken();

    public static string Redact(string value) => string.Join(Environment.NewLine, value
        .Split(["\r\n", "\n"], StringSplitOptions.None)
        .Select(line => SensitiveToken().IsMatch(line) ? "<redacted>" : line));
}
