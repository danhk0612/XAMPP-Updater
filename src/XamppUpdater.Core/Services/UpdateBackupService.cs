using System.Security.Cryptography;
using System.Text.Json;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IUpdateBackupService
{
    BackupResult CreateBackup(
        UpdatePreflightReport preflight,
        LogicalBackupManifest? logicalBackup = null,
        BackupKind kind = BackupKind.Rollback);
}

public sealed class UpdateBackupService : IUpdateBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public BackupResult CreateBackup(
        UpdatePreflightReport preflight,
        LogicalBackupManifest? logicalBackup = null,
        BackupKind kind = BackupKind.Rollback)
    {
        if (!Directory.Exists(preflight.ComponentRoot))
        {
            throw new DirectoryNotFoundException($"백업 대상 폴더를 찾을 수 없습니다: {preflight.ComponentRoot}");
        }

        if (preflight.Type == XamppComponentType.MariaDb &&
            (preflight.ProcessRunning || preflight.ServiceState?.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) == true))
        {
            throw new InvalidOperationException(
                "MariaDB가 실행 중인 상태에서는 data 디렉터리 물리 백업을 만들지 않습니다. 서비스 중지와 논리 백업 후 진행해야 합니다.");
        }

        var backupRoot = preflight.BackupDestination;
        var filesRoot = Path.Combine(backupRoot, "files");
        Directory.CreateDirectory(filesRoot);

        var manifestFiles = new List<BackupManifestFile>();
        long copiedBytes = 0;

        foreach (var source in EnumerateStableBackupFiles(preflight.ComponentRoot, preflight.Type))
        {
            var relative = Path.GetRelativePath(preflight.ComponentRoot, source);
            var destination = Path.Combine(filesRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            File.Copy(source, destination, overwrite: true);

            var sourceHash = ComputeSha256(source);
            var destinationHash = ComputeSha256(destination);
            if (!string.Equals(sourceHash, destinationHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"백업 검증 실패: {relative}");
            }

            var size = new FileInfo(source).Length;
            copiedBytes += size;
            manifestFiles.Add(new BackupManifestFile(relative, size, sourceHash));
        }

        var xamppRoot = Directory.GetParent(preflight.ComponentRoot)?.FullName ?? preflight.ComponentRoot;
        var manifest = new BackupManifest(
            3,
            DateTimeOffset.Now,
            preflight.Type,
            xamppRoot,
            preflight.ComponentRoot,
            preflight.CurrentVersion,
            preflight.TargetVersion,
            backupRoot,
            preflight.ServiceName,
            preflight.ServiceState,
            preflight.ProcessRunning,
            manifestFiles,
            logicalBackup,
            kind);

        var manifestPath = Path.Combine(backupRoot, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

        return new BackupResult(manifest, manifestPath, copiedBytes, manifestFiles.Count);
    }

    internal static IEnumerable<string> EnumerateStableBackupFiles(string componentRoot, XamppComponentType type)
    {
        var files = UpdatePreflightService.EnumerateBackupFiles(componentRoot, type);
        if (type != XamppComponentType.Apache)
        {
            return files;
        }

        var logsRoot = Path.Combine(componentRoot, "logs") + Path.DirectorySeparatorChar;
        return files.Where(file => !file.StartsWith(logsRoot, StringComparison.OrdinalIgnoreCase));
    }

    internal static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
