using System.Text.Json;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IRollbackBackupCatalogService
{
    IReadOnlyList<BackupResult> ListCandidates(string xamppRoot, XamppComponentType type, string currentVersion);
    BackupResult? FindLatestCandidate(string xamppRoot, XamppComponentType type, string currentVersion);
}

public sealed class RollbackBackupCatalogService : IRollbackBackupCatalogService
{
    public IReadOnlyList<BackupResult> ListCandidates(string xamppRoot, XamppComponentType type, string currentVersion)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XamppUpdater",
            "Backups");
        if (!Directory.Exists(root)) return Array.Empty<BackupResult>();

        var results = new List<BackupResult>();
        foreach (var manifestPath in Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath));
                if (manifest is null || manifest.Type != type) continue;
                if (!PathsEqual(manifest.XamppRoot, xamppRoot)) continue;
                // 업데이트 전 백업은 CurrentVersion -> TargetVersion 관계를 기록한다.
                // 현재 설치 버전이 당시 TargetVersion과 같고, 백업 버전이 더 낮을 때만 롤백 후보로 사용한다.
                if (!string.Equals(manifest.TargetVersion, currentVersion, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsOlder(manifest.CurrentVersion, currentVersion)) continue;
                if (type == XamppComponentType.MariaDb && manifest.LogicalBackup is null) continue;

                var filesRoot = Path.Combine(manifest.BackupRoot, "files");
                if (!Directory.Exists(filesRoot)) continue;
                results.Add(new BackupResult(
                    manifest,
                    manifestPath,
                    manifest.Files.Sum(file => file.Size),
                    manifest.Files.Count));
            }
            catch
            {
                // 손상되거나 이전 형식인 백업은 목록에서 제외한다.
            }
        }

        return results
            .OrderByDescending(item => item.Manifest.CreatedAt)
            .ToArray();
    }

    public BackupResult? FindLatestCandidate(string xamppRoot, XamppComponentType type, string currentVersion) =>
        ListCandidates(xamppRoot, type, currentVersion).FirstOrDefault();

    private static bool IsOlder(string candidate, string current) =>
        Version.TryParse(candidate, out var oldVersion) &&
        Version.TryParse(current, out var currentVersion) &&
        oldVersion < currentVersion;

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
