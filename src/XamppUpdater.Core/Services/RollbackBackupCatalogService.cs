using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, bool> IntegrityCache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<BackupResult> ListCandidates(string xamppRoot, XamppComponentType type, string currentVersion)
    {
        var root = GetBackupRoot();
        if (!Directory.Exists(root)) return Array.Empty<BackupResult>();

        var results = new List<BackupResult>();
        foreach (var manifestPath in Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath));
                if (manifest is null || manifest.Type != type) continue;
                if (!PathsEqual(manifest.XamppRoot, xamppRoot)) continue;

                // Schema 1/2 manifests did not have Kind. BackupKind.Rollback=0 keeps those manifests compatible.
                // New safety backups are never exposed as user rollback targets.
                if (manifest.Kind != BackupKind.Rollback) continue;

                // 업데이트 전 롤백 백업은 CurrentVersion -> TargetVersion 관계를 기록한다.
                // 현재 설치 버전이 당시 TargetVersion과 같고, 백업 버전이 더 낮을 때만 직접 롤백 가능하다.
                if (!string.Equals(manifest.TargetVersion, currentVersion, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsOlder(manifest.CurrentVersion, currentVersion)) continue;
                if (type == XamppComponentType.MariaDb && manifest.LogicalBackup is null) continue;

                if (!IsManifestLocationConsistent(manifest, manifestPath)) continue;

                var filesRoot = Path.Combine(manifest.BackupRoot, "files");
                if (!Directory.Exists(filesRoot)) continue;
                if (!HasAllExpectedFiles(manifest, filesRoot)) continue;

                var result = new BackupResult(
                    manifest,
                    manifestPath,
                    manifest.Files.Sum(file => file.Size),
                    manifest.Files.Count);

                // 후보가 처음 관찰될 때 전체 SHA256 검증을 수행한다.
                // 이후 UI의 1초 주기 갱신에서는 같은 manifest를 다시 해시하지 않는다.
                // 실제 롤백 실행 직전에는 UI에서 BackupIntegrityVerifier.Verify를 다시 호출한다.
                if (!IntegrityCache.TryGetValue(manifestPath, out var valid))
                {
                    try
                    {
                        BackupIntegrityVerifier.Verify(result, requireLogicalBackup: type == XamppComponentType.MariaDb);
                        valid = true;
                    }
                    catch
                    {
                        valid = false;
                    }
                    IntegrityCache[manifestPath] = valid;
                }
                if (!valid) continue;

                results.Add(result);
            }
            catch
            {
                // 손상되거나 이전 형식 중 해석할 수 없는 백업은 목록에서 제외한다.
            }
        }

        return results
            .OrderByDescending(item => item.Manifest.CreatedAt)
            .ToArray();
    }

    public BackupResult? FindLatestCandidate(string xamppRoot, XamppComponentType type, string currentVersion) =>
        ListCandidates(xamppRoot, type, currentVersion).FirstOrDefault();

    internal static string GetBackupRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XamppUpdater",
        "Backups");

    private static bool HasAllExpectedFiles(BackupManifest manifest, string filesRoot)
    {
        foreach (var file in manifest.Files)
        {
            var path = SafeCombine(filesRoot, file.RelativePath);
            if (!File.Exists(path)) return false;
            if (new FileInfo(path).Length != file.Size) return false;
        }

        if (manifest.LogicalBackup is not null)
        {
            var path = SafeCombine(manifest.BackupRoot, manifest.LogicalBackup.RelativePath);
            if (!File.Exists(path)) return false;
            if (new FileInfo(path).Length != manifest.LogicalBackup.Size) return false;
        }

        return true;
    }

    private static bool IsManifestLocationConsistent(BackupManifest manifest, string manifestPath)
    {
        try
        {
            var actual = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
            var declared = Path.TrimEndingDirectorySeparator(Path.GetFullPath(manifest.BackupRoot));
            return string.Equals(actual, declared, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("백업 상대 경로가 허용된 루트를 벗어납니다: " + relative);
        return full;
    }

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
