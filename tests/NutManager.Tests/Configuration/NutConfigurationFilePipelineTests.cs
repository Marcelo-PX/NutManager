using System.Text;
using NutManager.Core.Configuration;
using NutManager.Infrastructure.Configuration;
using Xunit;

namespace NutManager.Tests.Configuration;

public sealed class NutConfigurationFilePipelineTests
{
    public static IEnumerable<object[]> UnsupportedUtf32BomCases()
    {
        yield return [new byte[] { 0xFF, 0xFE, 0x00, 0x00 }];
        yield return [new byte[] { 0x00, 0x00, 0xFE, 0xFF }];
    }

    [Theory]
    [InlineData(NutConfigurationTextEncoding.Utf8)]
    [InlineData(NutConfigurationTextEncoding.Utf8Bom)]
    [InlineData(NutConfigurationTextEncoding.Utf16LittleEndian)]
    [InlineData(NutConfigurationTextEncoding.Utf16BigEndian)]
    public async Task LoadAndPreparePreserveEverySupportedEncodingByteForByte(NutConfigurationTextEncoding encoding)
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("ups.conf");
        const string text = "[demo]\r\n    desc = \"UPS\"\r\n";
        var originalBytes = Encode(text, encoding);
        await File.WriteAllBytesAsync(targetPath, originalBytes);
        var pipeline = new NutConfigurationFilePipeline();

        var load = await pipeline.LoadAsync(targetPath, NutConfigurationFileKind.UpsConf);
        var prepared = pipeline.Prepare(Assert.IsType<NutConfigurationFileSnapshot>(load.Snapshot));

        Assert.Equal(NutConfigurationLoadStatus.Success, load.Status);
        Assert.Equal(encoding, load.Snapshot!.Encoding);
        Assert.Equal(originalBytes, prepared.CandidateBytes.ToArray());
        Assert.False(prepared.Preview.HasChanges);
    }

    [Fact]
    public async Task InvalidUtf8WithoutBomIsRejectedWithoutReplacement()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        var originalBytes = new byte[] { 0x80, 0x81 };
        await File.WriteAllBytesAsync(targetPath, originalBytes);

        var result = await new NutConfigurationFilePipeline().LoadAsync(targetPath, NutConfigurationFileKind.NutConf);

        Assert.Equal(NutConfigurationLoadStatus.UnsupportedEncoding, result.Status);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(targetPath));
    }

    [Theory]
    [MemberData(nameof(UnsupportedUtf32BomCases))]
    public async Task UnsupportedUtf32BomIsRejectedWithoutInterpretingItAsUtf16(byte[] originalBytes)
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        await File.WriteAllBytesAsync(targetPath, originalBytes);

        var result = await new NutConfigurationFilePipeline().LoadAsync(targetPath, NutConfigurationFileKind.NutConf);

        Assert.Equal(NutConfigurationLoadStatus.UnsupportedEncoding, result.Status);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(targetPath));
    }

    [Fact]
    public async Task UnchangedDocumentReturnsNoChangesWithoutBackupTempOrRewrite()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        var originalBytes = Encoding.UTF8.GetBytes("MODE=standalone\n");
        await File.WriteAllBytesAsync(targetPath, originalBytes);
        var originalWriteTime = File.GetLastWriteTimeUtc(targetPath);
        var pipeline = new NutConfigurationFilePipeline();
        var snapshot = await LoadSnapshotAsync(pipeline, targetPath, NutConfigurationFileKind.NutConf);
        var prepared = pipeline.Prepare(snapshot);

        var result = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.NoChanges, result.Status);
        Assert.Empty(directory.Files("*.bak"));
        Assert.Empty(directory.Files("*.tmp"));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(targetPath));
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(targetPath));
    }

    [Fact]
    public async Task PreparePreservesCrLfUnknownContentQuotingDuplicatesAndEofWhileShowingOnlyTheChangedLine()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("ups.conf");
        const string original = "# keep\r\n\r\n[demo]\r\n    driver = nutdrv_qx\r\n    port = \"\\\\\\\\.\\\\COM4\"\r\n    future_option = keep\r\n    driver = duplicate";
        await File.WriteAllBytesAsync(targetPath, Encoding.UTF8.GetBytes(original));
        var pipeline = new NutConfigurationFilePipeline();
        var snapshot = await LoadSnapshotAsync(pipeline, targetPath, NutConfigurationFileKind.UpsConf);
        Assert.Single(snapshot.Document.FindAssignments("port", "demo")).SetValue("new-port");

        var prepared = pipeline.Prepare(snapshot);

        Assert.Equal(
            "# keep\r\n\r\n[demo]\r\n    driver = nutdrv_qx\r\n    port = \"new-port\"\r\n    future_option = keep\r\n    driver = duplicate",
            prepared.CandidateText);
        var line = Assert.Single(prepared.Preview.Lines);
        Assert.Equal(5, line.LineNumber);
        Assert.Equal("    port = \"\\\\\\\\.\\\\COM4\"", line.OriginalText);
        Assert.Equal("    port = \"new-port\"", line.CandidateText);
        Assert.False(line.IsRedacted);
    }

    [Fact]
    public async Task PreviewRedactsUpsdUsersPassword()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("upsd.users");
        const string secret = "fictional-password";
        await File.WriteAllTextAsync(targetPath, $"[user]\npassword = {secret}\n");
        var pipeline = new NutConfigurationFilePipeline();
        var snapshot = await LoadSnapshotAsync(pipeline, targetPath, NutConfigurationFileKind.UpsdUsers);
        Assert.Single(snapshot.Document.FindAssignments("password", "user")).SetValue("new-fictional-password");

        var prepared = pipeline.Prepare(snapshot);

        AssertRedacted(prepared.Preview, secret, "new-fictional-password");
    }

    [Fact]
    public async Task PreviewRedactsUpsmonMonitor()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("upsmon.conf");
        const string secret = "fictional-monitor-password";
        await File.WriteAllTextAsync(targetPath, $"MONITOR demo@host 1 observer {secret} secondary\n");
        var pipeline = new NutConfigurationFilePipeline();
        var snapshot = await LoadSnapshotAsync(pipeline, targetPath, NutConfigurationFileKind.UpsmonConf);
        Assert.Single(snapshot.Document.FindDirectives("monitor")).SetArguments("demo@host 1 observer new-fictional-monitor-password secondary");

        var prepared = pipeline.Prepare(snapshot);

        AssertRedacted(prepared.Preview, secret, "new-fictional-monitor-password");
    }

    [Fact]
    public async Task PreviewRedactsUpsdCertident()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("upsd.conf");
        const string secret = "fictional-private-key-password";
        await File.WriteAllTextAsync(targetPath, $"CERTIDENT \"server cert\" {secret}\n");
        var pipeline = new NutConfigurationFilePipeline();
        var snapshot = await LoadSnapshotAsync(pipeline, targetPath, NutConfigurationFileKind.UpsdConf);
        Assert.Single(snapshot.Document.FindDirectives("certident")).SetArguments("\"server cert\" new-fictional-private-key-password");

        var prepared = pipeline.Prepare(snapshot);

        AssertRedacted(prepared.Preview, secret, "new-fictional-private-key-password");
    }

    [Fact]
    public async Task ApplySafelyReplacesTargetKeepsByteExactBackupAndCleansTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        var originalBytes = Encoding.UTF8.GetBytes("# original\nMODE=standalone\n");
        await File.WriteAllBytesAsync(targetPath, originalBytes);
        var pipeline = new NutConfigurationFilePipeline();
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");

        var result = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.Success, result.Status);
        var backupPath = Assert.IsType<string>(result.BackupPath);
        Assert.True(File.Exists(backupPath));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(backupPath));
        Assert.Equal(prepared.CandidateBytes.ToArray(), await File.ReadAllBytesAsync(targetPath));
        Assert.Empty(directory.Files("*.tmp"));
        Assert.DoesNotContain("standalone", Path.GetFileName(backupPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BackupNamesAreCollisionSafeAndRemainAfterSuccess()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        await File.WriteAllTextAsync(targetPath, "MODE=standalone\n");
        var pipeline = new NutConfigurationFilePipeline();

        var first = await pipeline.ApplyAsync(await PrepareModeChangeAsync(pipeline, targetPath, "netserver"));
        var second = await pipeline.ApplyAsync(await PrepareModeChangeAsync(pipeline, targetPath, "standalone"));

        Assert.Equal(NutConfigurationApplyStatus.Success, first.Status);
        Assert.Equal(NutConfigurationApplyStatus.Success, second.Status);
        Assert.NotEqual(first.BackupPath, second.BackupPath);
        Assert.Equal(2, directory.Files("*.bak").Count);
    }

    [Fact]
    public async Task TemporaryCandidateIsCreatedInTheTargetDirectory()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        await File.WriteAllTextAsync(targetPath, "MODE=standalone\n");
        var fileSystem = new RecordingFileSystem();
        var pipeline = new NutConfigurationFilePipeline(fileSystem);
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");

        var result = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.Success, result.Status);
        var temporaryPath = Assert.Single(fileSystem.WrittenPaths);
        Assert.Equal(Path.GetDirectoryName(targetPath), Path.GetDirectoryName(temporaryPath));
        Assert.EndsWith(".tmp", temporaryPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingTargetReturnsTargetNotFoundAndDoesNotCreateAFile()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("missing.conf");
        var pipeline = new NutConfigurationFilePipeline();

        var load = await pipeline.LoadAsync(targetPath, NutConfigurationFileKind.NutConf);

        Assert.Equal(NutConfigurationLoadStatus.TargetNotFound, load.Status);
        Assert.False(File.Exists(targetPath));
    }

    [Fact]
    public async Task TargetDeletedAfterLoadReturnsTargetNotFoundWithoutRecreatingIt()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        await File.WriteAllTextAsync(targetPath, "MODE=standalone\n");
        var pipeline = new NutConfigurationFilePipeline();
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");
        File.Delete(targetPath);

        var result = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.TargetNotFound, result.Status);
        Assert.False(File.Exists(targetPath));
        Assert.Empty(directory.Files("*.bak"));
    }

    [Fact]
    public async Task TempWriteFailureLeavesOriginalUntouchedAndCleansTheTemporaryPath()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        var originalBytes = Encoding.UTF8.GetBytes("MODE=standalone\n");
        await File.WriteAllBytesAsync(targetPath, originalBytes);
        var fileSystem = new FaultInjectingFileSystem { FailTempWrite = true };
        var pipeline = new NutConfigurationFilePipeline(fileSystem);
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");

        var result = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.TempWriteFailed, result.Status);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(targetPath));
        Assert.Empty(directory.Files("*.tmp"));
    }

    [Fact]
    public async Task CandidateValidationFailureDoesNotReplaceTheOriginal()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        var originalBytes = Encoding.UTF8.GetBytes("MODE=standalone\n");
        await File.WriteAllBytesAsync(targetPath, originalBytes);
        var validator = new StaticCandidateValidator(isValid: false);
        var fileSystem = new FaultInjectingFileSystem();
        var pipeline = new NutConfigurationFilePipeline(fileSystem, candidateValidator: validator);
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");

        var result = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.CandidateValidationFailed, result.Status);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(targetPath));
        Assert.Empty(directory.Files("*.bak"));
        Assert.Empty(directory.Files("*.tmp"));
        Assert.Equal(0, fileSystem.PrimaryReplaceCount);
    }

    [Fact]
    public async Task ReplaceFailureLeavesTheOriginalAndNoBackupBehind()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        var originalBytes = Encoding.UTF8.GetBytes("MODE=standalone\n");
        await File.WriteAllBytesAsync(targetPath, originalBytes);
        var fileSystem = new FaultInjectingFileSystem { FailPrimaryReplace = true };
        var pipeline = new NutConfigurationFilePipeline(fileSystem);
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");

        var result = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.ReplaceFailed, result.Status);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(targetPath));
        Assert.Empty(directory.Files("*.bak"));
        Assert.Empty(directory.Files("*.tmp"));
    }

    [Fact]
    public async Task ExternalModificationBeforeApplyAbortsWithoutOverwritingTheExternalContent()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        await File.WriteAllTextAsync(targetPath, "MODE=standalone\n");
        var pipeline = new NutConfigurationFilePipeline();
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");
        var externalBytes = Encoding.UTF8.GetBytes("MODE=external\n");
        await File.WriteAllBytesAsync(targetPath, externalBytes);

        var result = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.ChangedExternally, result.Status);
        Assert.Equal(externalBytes, await File.ReadAllBytesAsync(targetPath));
        Assert.Empty(directory.Files("*.bak"));
    }

    [Fact]
    public async Task RaceDuringReplaceRestoresTheVersionActuallyPresentAtReplacement()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        await File.WriteAllTextAsync(targetPath, "MODE=standalone\n");
        var externalBytes = Encoding.UTF8.GetBytes("MODE=race-external\n");
        var fileSystem = new FaultInjectingFileSystem
        {
            BeforePrimaryReplace = path => File.WriteAllBytes(path, externalBytes)
        };
        var pipeline = new NutConfigurationFilePipeline(fileSystem);
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");

        var result = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.ChangedExternally, result.Status);
        Assert.True(result.RollbackSucceeded);
        Assert.Equal(externalBytes, await File.ReadAllBytesAsync(targetPath));
        Assert.Equal(externalBytes, await File.ReadAllBytesAsync(Assert.IsType<string>(result.BackupPath)));
    }

    [Fact]
    public async Task PostApplyFailureRollsBackAndPreservesTheOriginalBackup()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        var originalBytes = Encoding.UTF8.GetBytes("MODE=standalone\n");
        await File.WriteAllBytesAsync(targetPath, originalBytes);
        var pipeline = new NutConfigurationFilePipeline(postApplyValidator: new StaticPostApplyValidator(isValid: false));
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");

        var result = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.PostApplyValidationFailedRolledBack, result.Status);
        Assert.True(result.RollbackSucceeded);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(targetPath));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(Assert.IsType<string>(result.BackupPath)));
        Assert.Equal(prepared.CandidateBytes.ToArray(), await File.ReadAllBytesAsync(Assert.IsType<string>(result.RecoveryPath)));
        Assert.Empty(directory.Files("*.tmp"));
    }

    [Fact]
    public async Task PostApplyExternalEditIsPreservedInRecoveryBackupWhileOriginalIsRestored()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        var originalBytes = Encoding.UTF8.GetBytes("MODE=standalone\n");
        var externalBytes = Encoding.UTF8.GetBytes("MODE=external-after-apply\n");
        await File.WriteAllBytesAsync(targetPath, originalBytes);
        var pipeline = new NutConfigurationFilePipeline(
            postApplyValidator: new ExternalWritingPostApplyValidator(targetPath, externalBytes));
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");

        var result = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.PostApplyValidationFailedRolledBack, result.Status);
        Assert.True(result.RollbackSucceeded);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(targetPath));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(Assert.IsType<string>(result.BackupPath)));
        Assert.Equal(externalBytes, await File.ReadAllBytesAsync(Assert.IsType<string>(result.RecoveryPath)));
    }

    [Fact]
    public async Task PostApplyExceptionAlsoRollsBack()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        var originalBytes = Encoding.UTF8.GetBytes("MODE=standalone\n");
        await File.WriteAllBytesAsync(targetPath, originalBytes);
        var pipeline = new NutConfigurationFilePipeline(postApplyValidator: new ThrowingPostApplyValidator());
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");

        var result = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.PostApplyValidationFailedRolledBack, result.Status);
        Assert.True(result.RollbackSucceeded);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(targetPath));
    }

    [Fact]
    public async Task RollbackFailureReturnsCriticalStatusAndRetainsBackup()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        var originalBytes = Encoding.UTF8.GetBytes("MODE=standalone\n");
        await File.WriteAllBytesAsync(targetPath, originalBytes);
        var fileSystem = new FaultInjectingFileSystem { FailRollbackReplace = true };
        var pipeline = new NutConfigurationFilePipeline(fileSystem, postApplyValidator: new StaticPostApplyValidator(isValid: false));
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");

        var result = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.PostApplyValidationFailedRollbackFailed, result.Status);
        Assert.False(result.RollbackSucceeded);
        Assert.Equal(prepared.CandidateBytes.ToArray(), await File.ReadAllBytesAsync(targetPath));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(Assert.IsType<string>(result.BackupPath)));
    }

    [Fact]
    public async Task PostReplaceVerificationFailureRollsBackTheOriginalBytes()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        var originalBytes = Encoding.UTF8.GetBytes("MODE=standalone\n");
        await File.WriteAllBytesAsync(targetPath, originalBytes);
        var fileSystem = new FaultInjectingFileSystem
        {
            AfterPrimaryReplace = path => File.WriteAllText(path, "MODE=corrupted\n")
        };
        var pipeline = new NutConfigurationFilePipeline(fileSystem);
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");

        var result = await pipeline.ApplyAsync(prepared);

        Assert.Equal(NutConfigurationApplyStatus.VerificationFailedRolledBack, result.Status);
        Assert.True(result.RollbackSucceeded);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(targetPath));
    }

    [Fact]
    public async Task CancellationBeforeCommitDoesNotWriteBackupTempOrTarget()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        var originalBytes = Encoding.UTF8.GetBytes("MODE=standalone\n");
        await File.WriteAllBytesAsync(targetPath, originalBytes);
        var pipeline = new NutConfigurationFilePipeline();
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await pipeline.ApplyAsync(prepared, cancellation.Token);

        Assert.Equal(NutConfigurationApplyStatus.Cancelled, result.Status);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(targetPath));
        Assert.Empty(directory.Files("*.bak"));
        Assert.Empty(directory.Files("*.tmp"));
    }

    [Fact]
    public async Task CancellationDuringInitialFileExistsReturnsCancelledWithoutThrowing()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        var originalBytes = Encoding.UTF8.GetBytes("MODE=standalone\n");
        await File.WriteAllBytesAsync(targetPath, originalBytes);
        using var cancellation = new CancellationTokenSource();
        var fileSystem = new FaultInjectingFileSystem { CancelDuringFileExists = cancellation };
        var pipeline = new NutConfigurationFilePipeline(fileSystem);
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");

        var result = await pipeline.ApplyAsync(prepared, cancellation.Token);

        Assert.Equal(NutConfigurationApplyStatus.Cancelled, result.Status);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(targetPath));
        Assert.Empty(directory.Files("*.bak"));
        Assert.Empty(directory.Files("*.tmp"));
    }

    [Fact]
    public async Task CallerCancellationDoesNotInterruptRollbackAfterCommit()
    {
        using var directory = new TemporaryDirectory();
        var targetPath = directory.FilePath("nut.conf");
        var originalBytes = Encoding.UTF8.GetBytes("MODE=standalone\n");
        await File.WriteAllBytesAsync(targetPath, originalBytes);
        using var cancellation = new CancellationTokenSource();
        var pipeline = new NutConfigurationFilePipeline(postApplyValidator: new CancellingPostApplyValidator(cancellation));
        var prepared = await PrepareModeChangeAsync(pipeline, targetPath, "netserver");

        var result = await pipeline.ApplyAsync(prepared, cancellation.Token);

        Assert.Equal(NutConfigurationApplyStatus.PostApplyValidationFailedRolledBack, result.Status);
        Assert.True(result.RollbackSucceeded);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(targetPath));
    }

    [Fact]
    public async Task AccessDeniedFromTheFileSystemIsReportedWithoutAStackTraceMessage()
    {
        var pipeline = new NutConfigurationFilePipeline(new FaultInjectingFileSystem { FailReadWithAccessDenied = true });

        var result = await pipeline.LoadAsync("fixture.conf", NutConfigurationFileKind.NutConf);

        Assert.Equal(NutConfigurationLoadStatus.AccessDenied, result.Status);
        Assert.DoesNotContain("UnauthorizedAccessException", result.Message, StringComparison.Ordinal);
    }

    private static void AssertRedacted(NutConfigurationChangePreview preview, string oldSecret, string newSecret)
    {
        var line = Assert.Single(preview.Lines);
        Assert.True(line.IsRedacted);
        Assert.Equal("<redacted>", line.OriginalText);
        Assert.Equal("<redacted>", line.CandidateText);
        Assert.DoesNotContain(oldSecret, line.OriginalText, StringComparison.Ordinal);
        Assert.DoesNotContain(newSecret, line.CandidateText, StringComparison.Ordinal);
        Assert.DoesNotContain(oldSecret, preview.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(newSecret, preview.ToString(), StringComparison.Ordinal);
    }

    private static async Task<NutConfigurationFileSnapshot> LoadSnapshotAsync(
        NutConfigurationFilePipeline pipeline,
        string targetPath,
        NutConfigurationFileKind fileKind)
    {
        var result = await pipeline.LoadAsync(targetPath, fileKind);
        Assert.Equal(NutConfigurationLoadStatus.Success, result.Status);
        return Assert.IsType<NutConfigurationFileSnapshot>(result.Snapshot);
    }

    private static async Task<NutConfigurationPreparedChange> PrepareModeChangeAsync(
        NutConfigurationFilePipeline pipeline,
        string targetPath,
        string mode)
    {
        var snapshot = await LoadSnapshotAsync(pipeline, targetPath, NutConfigurationFileKind.NutConf);
        Assert.Single(snapshot.Document.FindAssignments("MODE")).SetValue(mode);
        return pipeline.Prepare(snapshot);
    }

    private static byte[] Encode(string text, NutConfigurationTextEncoding encoding)
    {
        Encoding textEncoding = encoding switch
        {
            NutConfigurationTextEncoding.Utf8 or NutConfigurationTextEncoding.Utf8Bom => new UTF8Encoding(false, true),
            NutConfigurationTextEncoding.Utf16LittleEndian => new UnicodeEncoding(false, false, true),
            NutConfigurationTextEncoding.Utf16BigEndian => new UnicodeEncoding(true, false, true),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };
        var content = textEncoding.GetBytes(text);
        var bom = encoding switch
        {
            NutConfigurationTextEncoding.Utf8Bom => new byte[] { 0xEF, 0xBB, 0xBF },
            NutConfigurationTextEncoding.Utf16LittleEndian => new byte[] { 0xFF, 0xFE },
            NutConfigurationTextEncoding.Utf16BigEndian => new byte[] { 0xFE, 0xFF },
            _ => Array.Empty<byte>()
        };
        return bom.Concat(content).ToArray();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"NutManager.T14.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string FilePath(string name) => System.IO.Path.Combine(Path, name);

        public IReadOnlyList<string> Files(string searchPattern) => Directory.GetFiles(Path, searchPattern, SearchOption.TopDirectoryOnly);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best effort cleanup for test fixtures.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort cleanup for test fixtures.
            }
        }
    }

    private sealed class StaticCandidateValidator : INutConfigurationCandidateValidator
    {
        private readonly bool _isValid;

        public StaticCandidateValidator(bool isValid) => _isValid = isValid;

        public Task<NutConfigurationValidationResult> ValidateAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken) =>
            Task.FromResult(_isValid ? NutConfigurationValidationResult.Success() : NutConfigurationValidationResult.Failure());
    }

    private sealed class StaticPostApplyValidator : INutConfigurationPostApplyValidator
    {
        private readonly bool _isValid;

        public StaticPostApplyValidator(bool isValid) => _isValid = isValid;

        public Task<NutConfigurationValidationResult> ValidateAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken) =>
            Task.FromResult(_isValid ? NutConfigurationValidationResult.Success() : NutConfigurationValidationResult.Failure());
    }

    private sealed class ThrowingPostApplyValidator : INutConfigurationPostApplyValidator
    {
        public Task<NutConfigurationValidationResult> ValidateAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic post-apply failure.");
    }

    private sealed class ExternalWritingPostApplyValidator : INutConfigurationPostApplyValidator
    {
        private readonly string _targetPath;
        private readonly byte[] _externalBytes;

        public ExternalWritingPostApplyValidator(string targetPath, byte[] externalBytes)
        {
            _targetPath = targetPath;
            _externalBytes = externalBytes;
        }

        public Task<NutConfigurationValidationResult> ValidateAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken)
        {
            File.WriteAllBytes(_targetPath, _externalBytes);
            return Task.FromResult(NutConfigurationValidationResult.Failure());
        }
    }

    private sealed class CancellingPostApplyValidator : INutConfigurationPostApplyValidator
    {
        private readonly CancellationTokenSource _cancellation;

        public CancellingPostApplyValidator(CancellationTokenSource cancellation) => _cancellation = cancellation;

        public Task<NutConfigurationValidationResult> ValidateAsync(NutConfigurationPreparedChange change, CancellationToken cancellationToken)
        {
            _cancellation.Cancel();
            return Task.FromResult(NutConfigurationValidationResult.Failure());
        }
    }

    private sealed class RecordingFileSystem : INutConfigurationFileSystem
    {
        private readonly NutConfigurationFileSystem _inner = new();

        public List<string> WrittenPaths { get; } = [];

        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken) => _inner.FileExistsAsync(path, cancellationToken);

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) => _inner.ReadAllBytesAsync(path, cancellationToken);

        public Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken) => _inner.CopyFileAsync(sourcePath, destinationPath, cancellationToken);

        public Task ReplaceAsync(string sourcePath, string destinationPath, string? backupPath, CancellationToken cancellationToken) =>
            _inner.ReplaceAsync(sourcePath, destinationPath, backupPath, cancellationToken);

        public Task DeleteFileIfExistsAsync(string path, CancellationToken cancellationToken) => _inner.DeleteFileIfExistsAsync(path, cancellationToken);

        public Task WriteNewFileAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            WrittenPaths.Add(path);
            return _inner.WriteNewFileAsync(path, bytes, cancellationToken);
        }
    }

    private sealed class FaultInjectingFileSystem : INutConfigurationFileSystem
    {
        private readonly NutConfigurationFileSystem _inner = new();

        public bool FailTempWrite { get; init; }

        public bool FailRollbackReplace { get; init; }

        public bool FailPrimaryReplace { get; init; }

        public bool FailReadWithAccessDenied { get; init; }

        public CancellationTokenSource? CancelDuringFileExists { get; init; }

        public Action<string>? BeforePrimaryReplace { get; init; }

        public Action<string>? AfterPrimaryReplace { get; init; }

        public int PrimaryReplaceCount { get; private set; }

        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken)
        {
            if (cancellationToken.CanBeCanceled)
            {
                CancelDuringFileExists?.Cancel();
            }
            cancellationToken.ThrowIfCancellationRequested();
            return FailReadWithAccessDenied ? Task.FromResult(true) : _inner.FileExistsAsync(path, cancellationToken);
        }

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
        {
            if (FailReadWithAccessDenied)
            {
                throw new UnauthorizedAccessException();
            }

            return _inner.ReadAllBytesAsync(path, cancellationToken);
        }

        public Task WriteNewFileAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            if (FailTempWrite)
            {
                throw new IOException();
            }

            return _inner.WriteNewFileAsync(path, bytes, cancellationToken);
        }

        public Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken) =>
            _inner.CopyFileAsync(sourcePath, destinationPath, cancellationToken);

        public async Task ReplaceAsync(string sourcePath, string destinationPath, string? backupPath, CancellationToken cancellationToken)
        {
            var isPrimaryReplace = backupPath is not null && PrimaryReplaceCount == 0;
            if (backupPath is not null)
            {
                PrimaryReplaceCount++;
                if (isPrimaryReplace)
                {
                    BeforePrimaryReplace?.Invoke(destinationPath);
                    if (FailPrimaryReplace)
                    {
                        throw new IOException();
                    }
                }
                else if (FailRollbackReplace)
                {
                    throw new IOException();
                }
            }

            await _inner.ReplaceAsync(sourcePath, destinationPath, backupPath, cancellationToken);
            if (isPrimaryReplace)
            {
                AfterPrimaryReplace?.Invoke(destinationPath);
            }
        }

        public Task DeleteFileIfExistsAsync(string path, CancellationToken cancellationToken) => _inner.DeleteFileIfExistsAsync(path, cancellationToken);
    }
}
