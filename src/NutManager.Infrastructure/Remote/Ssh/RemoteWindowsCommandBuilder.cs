using System.Text;
using System.Text.Json;
using NutManager.Core.Services;

namespace NutManager.Infrastructure.Remote.Ssh;

/// <summary>
/// Builds the only remote command shapes used by this feature. It never accepts shell text.
/// </summary>
public static class RemoteWindowsCommandBuilder
{
    private static readonly string[] RecognizedFiles = RemoteNutConfigurationFiles.AllNames.ToArray();

    public static string BuildWindowsPlatformProbe() => "powershell.exe -NoProfile -NonInteractive -EncodedCommand " + Encode("if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) { Write-Output 'NUTMANAGER_WINDOWS' }");

    public static string BuildWindowsCommit(RemoteNutWindowsCommitRequest request) =>
        BuildStructuredCommand("commit", new
        {
            request.ConfigurationDirectory,
            request.TargetFileName,
            request.TemporaryFileName,
            request.BackupFileName,
            request.ExpectedOriginalFingerprint,
            request.ExpectedCandidateFingerprint
        });

    public static string BuildWindowsRollback(RemoteNutWindowsRollbackRequest request) =>
        BuildStructuredCommand("rollback", new
        {
            request.ConfigurationDirectory,
            request.TargetFileName,
            request.BackupFileName,
            request.RollbackFileName,
            request.RecoveryFileName,
            request.ExpectedOriginalFingerprint
        });

    public static string BuildWindowsCapabilityProbe(string configurationDirectory, string sourceName, string candidateName, string backupName) =>
        BuildStructuredCommand("probe", new { ConfigurationDirectory = configurationDirectory, SourceName = sourceName, CandidateName = candidateName, BackupName = backupName });

    public static bool IsExactSuccessMarker(int? exitStatus, string? output, string marker) =>
        exitStatus == 0 && string.Equals(output?.Trim(), marker, StringComparison.Ordinal);

    private static string BuildStructuredCommand(string operation, object payload)
    {
        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        var recognized = string.Join(",", RecognizedFiles.Select(name => $"'{name}'"));
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $payload = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{payloadBase64}}')) | ConvertFrom-Json
            $allowed = @({{recognized}})
            function Resolve-DirectChild([string] $directory, [string] $name) {
              if ([string]::IsNullOrWhiteSpace($name) -or $name.IndexOfAny([char[]]'\\/') -ge 0 -or $name.Contains('..')) { throw 'Invalid child name.' }
              $child = [IO.Path]::GetFullPath((Join-Path $directory $name))
              if ([IO.Path]::GetDirectoryName($child) -ne $directory) { throw 'Path is not a direct child.' }
              return $child
            }
            function Assert-GeneratedName([string] $name, [string] $suffix) {
              if (-not $name.StartsWith('.nutmanager-') -or -not $name.EndsWith($suffix) -or $name.Length -le ('.nutmanager-'.Length + $suffix.Length) -or $name.IndexOfAny([char[]]'\\/') -ge 0 -or $name.Contains('..')) { throw 'Generated file name is invalid.' }
            }
            $directory = [IO.Path]::GetFullPath($payload.ConfigurationDirectory)
            if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw 'Configuration directory is unavailable.' }
            if (((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Configuration directory is a reparse point.' }
            """;

        script += operation switch
        {
            "commit" => """
                if ($allowed -notcontains $payload.TargetFileName) { throw 'Target is not recognized.' }
                Assert-GeneratedName $payload.TemporaryFileName '.tmp'
                Assert-GeneratedName $payload.BackupFileName '.bak'
                $target = Resolve-DirectChild $directory $payload.TargetFileName
                $temp = Resolve-DirectChild $directory $payload.TemporaryFileName
                $backup = Resolve-DirectChild $directory $payload.BackupFileName
                foreach ($path in @($target, $temp)) {
                  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Required file is missing.' }
                  if (((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse point is not allowed.' }
                }
                if (Test-Path -LiteralPath $backup) { throw 'Backup already exists.' }
                if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $payload.ExpectedOriginalFingerprint) { throw 'Original fingerprint changed.' }
                if ((Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash -ne $payload.ExpectedCandidateFingerprint) { throw 'Candidate fingerprint changed.' }
                [IO.File]::Replace($temp, $target, $backup, $false)
                Write-Output 'NUTMANAGER_COMMIT_OK'
                """,
            "rollback" => """
                if ($allowed -notcontains $payload.TargetFileName) { throw 'Target is not recognized.' }
                Assert-GeneratedName $payload.BackupFileName '.bak'
                Assert-GeneratedName $payload.RollbackFileName '.tmp'
                Assert-GeneratedName $payload.RecoveryFileName '.bak'
                $target = Resolve-DirectChild $directory $payload.TargetFileName
                $backup = Resolve-DirectChild $directory $payload.BackupFileName
                $rollback = Resolve-DirectChild $directory $payload.RollbackFileName
                $recovery = Resolve-DirectChild $directory $payload.RecoveryFileName
                foreach ($path in @($target, $backup)) {
                  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Required file is missing.' }
                  if (((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse point is not allowed.' }
                }
                if (Test-Path -LiteralPath $recovery) { throw 'Recovery already exists.' }
                [IO.File]::Copy($backup, $rollback, $false)
                if ((Get-FileHash -LiteralPath $rollback -Algorithm SHA256).Hash -ne $payload.ExpectedOriginalFingerprint) { throw 'Rollback fingerprint changed.' }
                [IO.File]::Replace($rollback, $target, $recovery, $false)
                Write-Output 'NUTMANAGER_ROLLBACK_OK'
                """,
            "probe" => """
                $source = Resolve-DirectChild $directory $payload.SourceName
                $candidate = Resolve-DirectChild $directory $payload.CandidateName
                $backup = Resolve-DirectChild $directory $payload.BackupName
                foreach ($path in @($source, $candidate)) {
                  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Probe file is missing.' }
                  if (((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse point is not allowed.' }
                }
                if (Test-Path -LiteralPath $backup) { throw 'Probe backup already exists.' }
                [IO.File]::Replace($candidate, $source, $backup, $false)
                $replaced = [IO.File]::ReadAllBytes($source)
                $original = [IO.File]::ReadAllBytes($backup)
                if ($replaced.Length -ne 1 -or $replaced[0] -ne 0x32 -or $original.Length -ne 1 -or $original[0] -ne 0x31) { throw 'Probe replacement verification failed.' }
                [IO.File]::Delete($source)
                [IO.File]::Delete($backup)
                Write-Output 'NUTMANAGER_PROBE_OK'
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        return "powershell.exe -NoProfile -NonInteractive -EncodedCommand " + Encode(script);
    }

    private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
}
