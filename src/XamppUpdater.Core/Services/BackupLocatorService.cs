using System.Text.Json;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IBackupLocatorService
{
    BackupResult? FindLatest(
        string xamppRoot,
        XamppComponentType type,
        string currentVersion,
        string targetVersion);
}

public sealed class BackupLocatorService : IBackupLocatorService
{
    public BackupResult? FindLatest(
        string xamppRoot,
        XamppComponentType type,
        string currentVersion,
        string targetVersion)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XamppUpdater",
            "Backups");
        if (!Directory.Exists(root)) return null;

        foreach (var manifestPath in Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories)
                     .OrderByDescending(path => File.GetLastWriteTimeUtc(path)))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath));
                if (manifest is null || manifest.Type != type) continue;
                if (!PathsEqual(manifest.XamppRoot, xamppRoot)) continue;
                if (!string.Equals(manifest.CurrentVersion, currentVersion, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(manifest.TargetVersion, targetVersion, StringComparison.OrdinalIgnoreCase)) continue;

                var filesRoot = Path.Combine(manifest.BackupRoot, "files");
                if (!Directory.Exists(filesRoot)) continue;
                return new BackupResult(
                    manifest,
                    manifestPath,
                    manifest.Files.Sum(file => file.Size),
                    manifest.Files.Count);
            }
            catch
            {
                // 손상되었거나 이전 형식인 manifest는 다음 후보를 확인한다.
            }
        }

        return null;
    }

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
