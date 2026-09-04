using System.Text.Json;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed record BackupRetentionResult(
    int DeletedBackupSets,
    long ReclaimedBytes,
    IReadOnlyList<string> Errors);

public interface IBackupRetentionService
{
    BackupRetentionResult CleanupSafetyBackups();
}

public sealed class BackupRetentionService : IBackupRetentionService
{
    private static readonly TimeSpan SafetyRetention = TimeSpan.FromDays(7);
    private const int MaxSafetyBackupsPerComponent = 3;

    public BackupRetentionResult CleanupSafetyBackups()
    {
        var root = RollbackBackupCatalogService.GetBackupRoot();
        if (!Directory.Exists(root)) return new BackupRetentionResult(0, 0, Array.Empty<string>());

        var errors = new List<string>();
        var candidates = new List<(BackupManifest Manifest, string Root, long Bytes)>();

        foreach (var manifestPath in Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath));
                if (manifest is null || !IsSafetyBackup(manifest)) continue;

                var manifestRoot = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
                if (manifestRoot is null) continue;
                candidates.Add((manifest, manifestRoot, GetDirectorySize(manifestRoot)));
            }
            catch
            {
                // Corrupt manifests are not deleted automatically. Manual storage cleanup remains available.
            }
        }

        var now = DateTimeOffset.Now;
        var delete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in candidates)
        {
            if (now - item.Manifest.CreatedAt > SafetyRetention)
                delete.Add(item.Root);
        }

        foreach (var group in candidates.GroupBy(
                     item => $"{Normalize(item.Manifest.XamppRoot)}|{item.Manifest.Type}",
                     StringComparer.OrdinalIgnoreCase))
        {
            foreach (var item in group
                         .OrderByDescending(value => value.Manifest.CreatedAt)
                         .Skip(MaxSafetyBackupsPerComponent))
            {
                delete.Add(item.Root);
            }
        }

        var deleted = 0;
        long reclaimed = 0;
        foreach (var path in delete)
        {
            try
            {
                var entry = candidates.First(item => string.Equals(item.Root, path, StringComparison.OrdinalIgnoreCase));
                Directory.Delete(path, recursive: true);
                deleted++;
                reclaimed += entry.Bytes;
                RemoveEmptyParents(path, root);
            }
            catch (Exception ex)
            {
                errors.Add($"{path}: {ex.Message}");
            }
        }

        return new BackupRetentionResult(deleted, reclaimed, errors);
    }

    internal static bool IsSafetyBackup(BackupManifest manifest)
    {
        if (manifest.Kind == BackupKind.Safety) return true;
        if (manifest.SchemaVersion >= 3) return false;

        // Schema 1/2 had no explicit Kind. Rollback-created safety backups can still be
        // recognized conservatively because they describe a higher current version moving
        // toward a lower rollback target. Normal updater backups describe old -> new.
        return Version.TryParse(manifest.CurrentVersion, out var current) &&
               Version.TryParse(manifest.TargetVersion, out var target) &&
               current > target;
    }

    private static string Normalize(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch { return path; }
    }

    private static long GetDirectorySize(string root)
    {
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try { bytes += new FileInfo(file).Length; }
            catch { }
        }
        return bytes;
    }

    private static void RemoveEmptyParents(string deletedRoot, string stopRoot)
    {
        var current = Directory.GetParent(deletedRoot);
        var normalizedStop = Normalize(stopRoot);
        while (current is not null &&
               !string.Equals(Normalize(current.FullName), normalizedStop, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(current.FullName).Any()) break;
                var parent = current.Parent;
                Directory.Delete(current.FullName);
                current = parent;
            }
            catch
            {
                break;
            }
        }
    }
}
