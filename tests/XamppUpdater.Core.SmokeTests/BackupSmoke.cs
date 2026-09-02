using System.Runtime.CompilerServices;
using System.Text.Json;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

internal static class BackupSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xampp-backup-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(Path.GetTempPath(), $"xampp-backup-output-{Guid.NewGuid():N}");
        try
        {
            var phpRoot = Path.Combine(root, "php");
            Directory.CreateDirectory(phpRoot);
            File.WriteAllText(Path.Combine(phpRoot, "php.ini"), "memory_limit=128M\n");
            File.WriteAllText(Path.Combine(phpRoot, "php.exe"), "fake executable");

            var preflight = new UpdatePreflightReport(
                XamppComponentType.Php,
                "7.3.11",
                "8.5.10",
                phpRoot,
                false,
                null,
                null,
                0,
                0,
                Array.Empty<PreflightConfigFile>(),
                Array.Empty<string>(),
                backupRoot);

            var result = new UpdateBackupService().CreateBackup(preflight);
            if (!File.Exists(result.ManifestPath))
            {
                throw new InvalidOperationException("backup manifest was not created.");
            }

            if (result.CopiedFiles != 2)
            {
                throw new InvalidOperationException($"backup copied files: expected 2, actual {result.CopiedFiles}.");
            }

            var copiedIni = Path.Combine(backupRoot, "files", "php.ini");
            if (!File.Exists(copiedIni))
            {
                throw new InvalidOperationException("php.ini was not copied to backup.");
            }

            var sourceHash = UpdateBackupService.ComputeSha256(Path.Combine(phpRoot, "php.ini"));
            var backupHash = UpdateBackupService.ComputeSha256(copiedIni);
            if (!string.Equals(sourceHash, backupHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("backup SHA256 verification failed.");
            }

            var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(result.ManifestPath));
            if (manifest is null || manifest.Files.Count != 2 || manifest.TargetVersion != "8.5.10")
            {
                throw new InvalidOperationException("backup manifest contents are invalid.");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, recursive: true);
        }
    }
}
