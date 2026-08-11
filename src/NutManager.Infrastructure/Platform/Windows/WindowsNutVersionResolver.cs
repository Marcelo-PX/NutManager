using System.Diagnostics;
using System.Text.RegularExpressions;
using NutManager.Core.Models;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.Platform.Windows;

public sealed class WindowsNutVersionResolver : ILocalNutVersionResolver
{
    private static readonly Regex VersionPattern = new(@"(?<!\d)(\d{1,4}\.\d{1,4}(?:\.\d{1,4})?)(?!\d)", RegexOptions.CultureInvariant);
    private readonly IWindowsNutVersionProcessRunner _runner;

    public WindowsNutVersionResolver() : this(new WindowsNutVersionProcessRunner()) { }

    public WindowsNutVersionResolver(IWindowsNutVersionProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<NutVersionResolution> ResolveAsync(NutInstallationInfo installation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(installation.Version))
        {
            return new NutVersionResolution(installation.Version, NutVersionSource.FileMetadata);
        }

        if (!installation.IsDetected ||
            !WindowsPath.TryCanonicalize(installation.InstallationDirectory, out var installationDirectory) ||
            !installation.Executables.TryGetValue("upsdrvctl.exe", out var candidate) ||
            !WindowsPath.TryCanonicalize(candidate, out var executable) ||
            !WindowsPath.IsInside(executable, installationDirectory) ||
            !string.Equals(Path.GetFileName(executable), "upsdrvctl.exe", StringComparison.OrdinalIgnoreCase))
        {
            return NutVersionResolution.Unavailable;
        }

        var output = await _runner.RunVersionAsync(executable, cancellationToken);
        if (!output.Completed) return NutVersionResolution.Unavailable;
        var text = output.Output ?? string.Empty;
        if (!text.Contains("Network UPS Tools", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("upsdrvctl", StringComparison.OrdinalIgnoreCase))
        {
            return NutVersionResolution.Unavailable;
        }

        var match = VersionPattern.Match(text);
        return match.Success
            ? new NutVersionResolution(match.Groups[1].Value, NutVersionSource.ExecutableFallback)
            : NutVersionResolution.Unavailable;
    }
}

public sealed record WindowsNutVersionProcessResult(bool Completed, string? Output);

public interface IWindowsNutVersionProcessRunner
{
    Task<WindowsNutVersionProcessResult> RunVersionAsync(string executablePath, CancellationToken cancellationToken);
}

internal sealed class WindowsNutVersionProcessRunner : IWindowsNutVersionProcessRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private const int CaptureLimit = 16 * 1024;

    public async Task<WindowsNutVersionProcessResult> RunVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !File.Exists(executablePath)) return new(false, null);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-V");

        try
        {
            if (!process.Start()) return new(false, null);
            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var timeout = new CancellationTokenSource(Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw;
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return new(false, null);
            }

            var output = string.Concat(await stdout, Environment.NewLine, await stderr);
            return new(true, output.Length <= CaptureLimit ? output : output[..CaptureLimit]);
        }
        catch (OperationCanceledException) { throw; }
        catch { return new(false, null); }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { }
    }
}
