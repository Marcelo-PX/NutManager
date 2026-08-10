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

/// <summary>
/// Isolates the read-only service-state lookup required before a hardware-contacting driver diagnostic.
/// </summary>
public interface IWindowsNutServiceStateSource
{
    Task<IReadOnlyList<NutServiceInfo>> GetServicesAsync(string installationDirectory, CancellationToken cancellationToken);
}

public interface IWindowsDriverDiagnosticsPlatform
{
    bool IsWindows { get; }
}

public interface INutDiagnosticProcessRunner
{
    Task<NutDiagnosticProcessResult> RunAsync(NutDiagnosticProcessSpec specification, CancellationToken cancellationToken);
}

public interface INutDiagnosticProcessFactory
{
    INutDiagnosticProcess Create(NutDiagnosticProcessSpec specification);
}

/// <summary>
/// Represents only a process started by the diagnostics runner. It has no API for arbitrary process lookup or kill.
/// </summary>
public interface INutDiagnosticProcess : IDisposable
{
    bool Start();

    bool HasExited { get; }

    int ExitCode { get; }

    TextReader StandardOutput { get; }

    TextReader StandardError { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void KillCreatedProcessTree();
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

public sealed class RuntimeWindowsDriverDiagnosticsPlatform : IWindowsDriverDiagnosticsPlatform
{
    public bool IsWindows => OperatingSystem.IsWindows();
}

public sealed class WindowsNutServiceStateSource : IWindowsNutServiceStateSource
{
    public Task<IReadOnlyList<NutServiceInfo>> GetServicesAsync(string installationDirectory, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<IReadOnlyList<NutServiceInfo>>(Array.Empty<NutServiceInfo>());
        }

        return WindowsNutServiceController.DiscoverAsync(installationDirectory, cancellationToken);
    }
}

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
    private readonly IWindowsNutServiceStateSource _serviceStateSource;
    private readonly INutDiagnosticProcessRunner _processRunner;
    private readonly IWindowsDriverDiagnosticsPlatform _platform;

    public WindowsNutDriverDiagnostics()
        : this(
            new NutConfigurationFilePipeline(),
            new WindowsWmiComPortSource(),
            new WindowsDriverFileSystem(),
            new WindowsDriverProcessInspector(),
            new WindowsNutServiceStateSource(),
            new WindowsNutDiagnosticProcessRunner(),
            new RuntimeWindowsDriverDiagnosticsPlatform())
    {
    }

    public WindowsNutDriverDiagnostics(
        INutConfigurationFilePipeline configurationPipeline,
        IWindowsComPortSource comPortSource,
        IWindowsDriverFileSystem fileSystem,
        IWindowsDriverProcessInspector processInspector,
        INutDiagnosticProcessRunner processRunner)
        : this(
            configurationPipeline,
            comPortSource,
            fileSystem,
            processInspector,
            new WindowsNutServiceStateSource(),
            processRunner,
            new RuntimeWindowsDriverDiagnosticsPlatform())
    {
    }

    public WindowsNutDriverDiagnostics(
        INutConfigurationFilePipeline configurationPipeline,
        IWindowsComPortSource comPortSource,
        IWindowsDriverFileSystem fileSystem,
        IWindowsDriverProcessInspector processInspector,
        IWindowsNutServiceStateSource serviceStateSource,
        INutDiagnosticProcessRunner processRunner,
        IWindowsDriverDiagnosticsPlatform platform)
    {
        _configurationPipeline = configurationPipeline;
        _comPortSource = comPortSource;
        _fileSystem = fileSystem;
        _processInspector = processInspector;
        _serviceStateSource = serviceStateSource;
        _processRunner = processRunner;
        _platform = platform;
    }

    public async Task<NutDriverDiagnosticsSnapshot> InspectAsync(NutInstallationInfo installation, CancellationToken cancellationToken)
    {
        if (!_platform.IsWindows)
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
            return new NutDriverDiagnosticsSnapshot(true, ports, Array.Empty<NutConfiguredDriver>(), ResolveUpsdrvctlPath(installation), LoadMessage(load.Status));
        }

        var drivers = WindowsUpsConfigurationInterpreter.Interpret(
            load.Snapshot.Document,
            installationDirectory,
            ports,
            ResolveDriver,
            _processInspector.IsProcessRunning);
        return new NutDriverDiagnosticsSnapshot(true, ports, drivers, ResolveUpsdrvctlPath(installation), UpsConfFingerprint: load.Snapshot.OriginalFingerprint);
    }

    public async Task<NutDriverDiagnosticResult> ExecuteAsync(NutDriverDiagnosticRequest request, CancellationToken cancellationToken)
    {
        if (!_platform.IsWindows)
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

        NutDriverDiagnosticRequest preparedRequest;
        if (request.Kind == NutDriverDiagnosticKind.UpsdrvctlHelp)
        {
            preparedRequest = request;
        }
        else
        {
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

            preparedRequest = prepared.Request!;
        }

        if (preparedRequest.Kind == NutDriverDiagnosticKind.DriverDataDump)
        {
            var interlock = await ValidateHardwareInterlocksAsync(preparedRequest, cancellationToken);
            if (interlock is not null)
            {
                return interlock;
            }
        }

        var specification = WindowsNutDiagnosticCommandBuilder.Create(preparedRequest, ResolveUpsdrvctlPath(preparedRequest.InstallationDirectory));
        if (specification is null)
        {
            return CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.InvalidExecutable, "A ferramenta NUT não está disponível ou não é confiável para este diagnóstico.");
        }

        if (preparedRequest.Kind != NutDriverDiagnosticKind.UpsdrvctlHelp)
        {
            try
            {
                var finalValidation = await ValidateUpsConfFingerprintImmediatelyBeforeLaunchAsync(preparedRequest, cancellationToken);
                if (finalValidation is not null)
                {
                    return finalValidation;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.CancelledBeforeLaunch, "O diagnóstico foi cancelado antes de iniciar.");
            }
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

    private async Task<NutDriverDiagnosticResult?> ValidateUpsConfFingerprintImmediatelyBeforeLaunchAsync(
        NutDriverDiagnosticRequest request,
        CancellationToken cancellationToken)
    {
        var load = await _configurationPipeline.LoadAsync(request.ConfigurationDirectory + "\\ups.conf", NutConfigurationFileKind.UpsConf, cancellationToken);
        if (load.Status != NutConfigurationLoadStatus.Success ||
            load.Snapshot is null ||
            string.IsNullOrWhiteSpace(request.UpsConfFingerprint) ||
            !string.Equals(load.Snapshot.OriginalFingerprint, request.UpsConfFingerprint, StringComparison.Ordinal))
        {
            return CreateImmediateResult(
                request.Kind,
                NutDriverDiagnosticStatus.InvalidConfiguration,
                "O arquivo ups.conf foi alterado após a revisão. Atualize os dispositivos e prepare o diagnóstico novamente.");
        }

        return null;
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

        if (string.IsNullOrWhiteSpace(request.UpsConfFingerprint) ||
            !string.Equals(load.Snapshot.OriginalFingerprint, request.UpsConfFingerprint, StringComparison.Ordinal))
        {
            return (null, CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.InvalidConfiguration, "O arquivo ups.conf foi alterado desde a revisão. Atualize os dispositivos e prepare o diagnóstico novamente."));
        }

        if (request.Kind is NutDriverDiagnosticKind.UpsdrvctlList or NutDriverDiagnosticKind.UpsdrvctlStatus)
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
        if (current is null || !MatchesReviewedDriver(current, request.Driver))
        {
            return (null, CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.InvalidConfiguration, "O arquivo ups.conf foi alterado desde a revisão. Atualize os dispositivos e prepare o diagnóstico novamente."));
        }

        if ((request.Kind is NutDriverDiagnosticKind.DriverHelp or NutDriverDiagnosticKind.DriverVersion or NutDriverDiagnosticKind.DriverVariableList or NutDriverDiagnosticKind.DriverDataDump) &&
            (!current.Executable.IsAvailable || !current.Executable.IsTrusted))
        {
            return (null, CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.InvalidExecutable, "O executável do driver não está disponível ou não é confiável."));
        }

        return (request, null);
    }

    private NutDriverExecutableInfo ResolveDriver(string installationDirectory, string? driverPath, string? driverName)
    {
        return WindowsNutDriverResolver.Resolve(installationDirectory, driverPath, driverName, _fileSystem.FileExists);
    }

    private async Task<NutDriverDiagnosticResult?> ValidateHardwareInterlocksAsync(NutDriverDiagnosticRequest request, CancellationToken cancellationToken)
    {
        if (!_platform.IsWindows)
        {
            return CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.Unsupported, "Diagnósticos locais de portas e drivers do Windows não estão disponíveis nesta plataforma.");
        }

        var driver = request.Driver!;
        if (!driver.Executable.IsAvailable || !driver.Executable.IsTrusted)
        {
            return CreateImmediateResult(request.Kind, NutDriverDiagnosticStatus.Conflict, "O executável do driver não está disponível ou não é confiável.");
        }

        var services = await _serviceStateSource.GetServicesAsync(request.InstallationDirectory, cancellationToken);
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

    private string? ResolveUpsdrvctlPath(NutInstallationInfo installation)
    {
        if (WindowsPath.TryCanonicalize(installation.InstallationDirectory, out var installationDirectory) &&
            installation.Executables.TryGetValue("upsdrvctl.exe", out var detectedPath) &&
            WindowsPath.TryCanonicalize(detectedPath, out var canonicalDetectedPath) &&
            IsTrustedUpsdrvctlPath(canonicalDetectedPath, installationDirectory))
        {
            return canonicalDetectedPath;
        }

        return WindowsPath.TryCanonicalize(installation.InstallationDirectory, out installationDirectory)
            ? ResolveUpsdrvctlPath(installationDirectory)
            : null;
    }

    private string? ResolveUpsdrvctlPath(string installationDirectory)
    {
        foreach (var candidate in new[] { installationDirectory + "\\bin\\upsdrvctl.exe", installationDirectory + "\\upsdrvctl.exe" })
        {
            if (WindowsPath.TryCanonicalize(candidate, out var canonicalCandidate) &&
                IsTrustedUpsdrvctlPath(canonicalCandidate, installationDirectory))
            {
                return canonicalCandidate;
            }
        }

        return null;
    }

    private bool IsTrustedUpsdrvctlPath(string candidate, string installationDirectory) =>
        WindowsPath.TryCanonicalize(candidate, out var canonicalCandidate) &&
        string.Equals(canonicalCandidate[(canonicalCandidate.LastIndexOf('\\') + 1)..], "upsdrvctl.exe", StringComparison.OrdinalIgnoreCase) &&
        WindowsPath.IsInside(canonicalCandidate, installationDirectory) &&
        _fileSystem.FileExists(canonicalCandidate);

    private static bool MatchesReviewedDriver(NutConfiguredDriver current, NutConfiguredDriver reviewed) =>
        string.Equals(current.UpsName, reviewed.UpsName, StringComparison.Ordinal) &&
        string.Equals(current.DriverName, reviewed.DriverName, StringComparison.Ordinal) &&
        string.Equals(current.ConfiguredPort, reviewed.ConfiguredPort, StringComparison.Ordinal) &&
        string.Equals(current.NormalizedComPort, reviewed.NormalizedComPort, StringComparison.Ordinal) &&
        string.Equals(current.Protocol, reviewed.Protocol, StringComparison.Ordinal) &&
        string.Equals(current.Executable.Path, reviewed.Executable.Path, StringComparison.OrdinalIgnoreCase);

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
        // Passing null to FindAssignments means "any section". Only an assignment before the first UPS section is global.
        // For duplicate global entries, preserve source order and use the first declaration deterministically.
        var driverPath = document.Nodes
            .OfType<NutConfigurationAssignmentNode>()
            .FirstOrDefault(node => node.SectionName is null && string.Equals(node.Name, "driverpath", StringComparison.OrdinalIgnoreCase))
            ?.Value;
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
            NutDriverDiagnosticKind.DriverDataDump when driverCommand is not null => new(driverCommand, ["-a", driver!.UpsName, "-d", "1"], request.ConfigurationDirectory, TimeSpan.FromSeconds(30), false),
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
    // Character budget shared by stdout and stderr. Streams keep draining after it is exhausted to avoid pipe deadlocks.
    private const int CombinedOutputCaptureLimit = 1024 * 1024;
    private static readonly TimeSpan ProcessCleanupTimeout = TimeSpan.FromSeconds(5);
    private readonly INutDiagnosticProcessFactory _processFactory;

    public WindowsNutDiagnosticProcessRunner()
        : this(new WindowsNutDiagnosticProcessFactory())
    {
    }

    public WindowsNutDiagnosticProcessRunner(INutDiagnosticProcessFactory processFactory)
    {
        _processFactory = processFactory;
    }

    public async Task<NutDiagnosticProcessResult> RunAsync(NutDiagnosticProcessSpec specification, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new(NutDriverDiagnosticStatus.CancelledBeforeLaunch, null, string.Empty, string.Empty, false, TimeSpan.Zero, "O diagnóstico foi cancelado antes de iniciar.");
        }

        var start = Stopwatch.StartNew();
        try
        {
            using var process = _processFactory.Create(specification);
            if (!process.Start())
            {
                return new(NutDriverDiagnosticStatus.Failed, null, string.Empty, string.Empty, false, start.Elapsed, "Não foi possível iniciar o diagnóstico do NUT.");
            }

            using var outputReadCancellation = new CancellationTokenSource();
            var captureBudget = new CombinedOutputCaptureBudget(CombinedOutputCaptureLimit);
            var standardOutput = ReadBoundedAsync(process.StandardOutput, captureBudget, outputReadCancellation.Token);
            var standardError = ReadBoundedAsync(process.StandardError, captureBudget, outputReadCancellation.Token);
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waitCts.CancelAfter(specification.Timeout);
            try
            {
                await process.WaitForExitAsync(waitCts.Token);
            }
            catch (OperationCanceledException)
            {
                return await CleanupAfterInterruptedWaitAsync(
                    process,
                    standardOutput,
                    standardError,
                    captureBudget,
                    outputReadCancellation,
                    cancellationToken.IsCancellationRequested,
                    start.Elapsed);
            }

            var output = await CompleteReadersAsync(standardOutput, standardError, captureBudget, outputReadCancellation, start.Elapsed);
            if (output.Result is not null)
            {
                return output.Result;
            }

            var status = process.ExitCode == 0
                ? captureBudget.IsTruncated ? NutDriverDiagnosticStatus.OutputTruncated : NutDriverDiagnosticStatus.Success
                : unchecked((int)0xC0000135) == process.ExitCode ? NutDriverDiagnosticStatus.MissingDependency : NutDriverDiagnosticStatus.NonZeroExit;
            return new(status, process.ExitCode, output.StandardOutput, output.StandardError, captureBudget.IsTruncated, start.Elapsed, status switch
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

    private static async Task<NutDiagnosticProcessResult> CleanupAfterInterruptedWaitAsync(
        INutDiagnosticProcess process,
        Task<string> standardOutput,
        Task<string> standardError,
        CombinedOutputCaptureBudget captureBudget,
        CancellationTokenSource outputReadCancellation,
        bool cancelledByCaller,
        TimeSpan elapsed)
    {
        var killFailed = false;
        try
        {
            if (!process.HasExited)
            {
                process.KillCreatedProcessTree();
            }
        }
        catch
        {
            killFailed = true;
        }

        var exited = false;
        using (var cleanupWait = new CancellationTokenSource(ProcessCleanupTimeout))
        {
            try
            {
                await process.WaitForExitAsync(cleanupWait.Token);
                exited = true;
            }
            catch (OperationCanceledException)
            {
                // The caller token is deliberately not used after launch. This bounded internal timeout is authoritative.
            }
            catch
            {
                killFailed = true;
            }
        }

        var output = await CompleteReadersAsync(standardOutput, standardError, captureBudget, outputReadCancellation, elapsed);
        if (killFailed || !exited || output.Result is not null)
        {
            return output.Result ?? new(
                NutDriverDiagnosticStatus.CleanupFailed,
                null,
                output.StandardOutput,
                output.StandardError,
                captureBudget.IsTruncated,
                elapsed,
                "O processo de diagnóstico não pôde ser confirmado como encerrado. Verifique os processos do NUT antes de tentar novamente.");
        }

        return new(
            cancelledByCaller ? NutDriverDiagnosticStatus.CancelledAfterLaunch : NutDriverDiagnosticStatus.Timeout,
            null,
            output.StandardOutput,
            output.StandardError,
            captureBudget.IsTruncated,
            elapsed,
            cancelledByCaller
                ? "O diagnóstico foi cancelado após iniciar e o processo criado foi encerrado."
                : "O diagnóstico excedeu o tempo limite e o processo criado foi encerrado.");
    }

    private static async Task<(string StandardOutput, string StandardError, NutDiagnosticProcessResult? Result)> CompleteReadersAsync(
        Task<string> standardOutput,
        Task<string> standardError,
        CombinedOutputCaptureBudget captureBudget,
        CancellationTokenSource outputReadCancellation,
        TimeSpan elapsed)
    {
        var readers = Task.WhenAll(standardOutput, standardError);
        try
        {
            await readers.WaitAsync(ProcessCleanupTimeout);
            return (standardOutput.Result, standardError.Result, null);
        }
        catch
        {
            outputReadCancellation.Cancel();
            try
            {
                await readers.WaitAsync(ProcessCleanupTimeout);
                return (standardOutput.Status == TaskStatus.RanToCompletion ? standardOutput.Result : string.Empty,
                    standardError.Status == TaskStatus.RanToCompletion ? standardError.Result : string.Empty,
                    new NutDiagnosticProcessResult(
                        NutDriverDiagnosticStatus.CleanupFailed,
                        null,
                        string.Empty,
                        string.Empty,
                        captureBudget.IsTruncated,
                        elapsed,
                        "A leitura da saída do diagnóstico não pôde ser encerrada com segurança."));
            }
            catch
            {
                return (string.Empty, string.Empty, new NutDiagnosticProcessResult(
                    NutDriverDiagnosticStatus.CleanupFailed,
                    null,
                    string.Empty,
                    string.Empty,
                    captureBudget.IsTruncated,
                    elapsed,
                    "A leitura da saída do diagnóstico não pôde ser encerrada com segurança."));
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(TextReader reader, CombinedOutputCaptureBudget captureBudget, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var captured = new StringBuilder();
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                captureBudget.Capture(buffer, read, captured);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A bounded cleanup deliberately stops readers after the child has been handled or confirmation failed.
        }

        return captured.ToString();
    }

    private sealed class CombinedOutputCaptureBudget
    {
        private readonly object _sync = new();
        private int _remaining;

        public CombinedOutputCaptureBudget(int limit)
        {
            _remaining = limit;
        }

        public bool IsTruncated { get; private set; }

        public void Capture(char[] buffer, int count, StringBuilder destination)
        {
            lock (_sync)
            {
                var accepted = Math.Min(_remaining, count);
                if (accepted > 0)
                {
                    destination.Append(buffer, 0, accepted);
                    _remaining -= accepted;
                }

                if (accepted < count)
                {
                    IsTruncated = true;
                }
            }
        }
    }
}

public sealed class WindowsNutDiagnosticProcessFactory : INutDiagnosticProcessFactory
{
    public INutDiagnosticProcess Create(NutDiagnosticProcessSpec specification) => new WindowsNutDiagnosticProcess(WindowsNutDiagnosticProcessRunner.CreateStartInfo(specification));
}

internal sealed class WindowsNutDiagnosticProcess : INutDiagnosticProcess
{
    private readonly Process _process;

    public WindowsNutDiagnosticProcess(ProcessStartInfo startInfo)
    {
        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }

    public bool Start() => _process.Start();

    public bool HasExited => _process.HasExited;

    public int ExitCode => _process.ExitCode;

    public TextReader StandardOutput => _process.StandardOutput;

    public TextReader StandardError => _process.StandardError;

    public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);

    public void KillCreatedProcessTree() => _process.Kill(entireProcessTree: true);

    public void Dispose() => _process.Dispose();
}

public static partial class WindowsNutDiagnosticOutput
{
    [GeneratedRegex("(password|passwd|secret|token|community|credential|authpassword|privpassword)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveToken();

    public static string Redact(string value) => string.Join(Environment.NewLine, value
        .Split(["\r\n", "\n"], StringSplitOptions.None)
        .Select(line => SensitiveToken().IsMatch(line) ? "<redacted>" : line));
}
