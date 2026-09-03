namespace XamppUpdater.Core.Services;

public interface IConfigSnapshotCompareService
{
    ConfigSnapshotDiff Compare(ConfigSnapshotManifest older, ConfigSnapshotManifest newer);
}

public sealed class ConfigSnapshotCompareService : IConfigSnapshotCompareService
{
    public ConfigSnapshotDiff Compare(ConfigSnapshotManifest older, ConfigSnapshotManifest newer)
    {
        if (older.Type != newer.Type) throw new ArgumentException("서로 다른 구성요소의 설정 snapshot은 비교할 수 없습니다.");
        if (!string.Equals(Path.GetFullPath(older.XamppRoot), Path.GetFullPath(newer.XamppRoot), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("서로 다른 XAMPP 설치의 설정 snapshot은 비교할 수 없습니다.");

        var left = older.Files.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var right = newer.Files.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var paths = left.Keys.Concat(right.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        var items = new List<ConfigSnapshotDiffItem>();

        foreach (var path in paths)
        {
            var hasOld = left.TryGetValue(path, out var oldFile);
            var hasNew = right.TryGetValue(path, out var newFile);
            var kind = (hasOld, hasNew) switch
            {
                (true, true) when string.Equals(oldFile!.Sha256, newFile!.Sha256, StringComparison.OrdinalIgnoreCase) => ConfigSnapshotDiffKind.Same,
                (true, true) => ConfigSnapshotDiffKind.Changed,
                (true, false) => ConfigSnapshotDiffKind.Removed,
                _ => ConfigSnapshotDiffKind.Added
            };
            items.Add(new ConfigSnapshotDiffItem(path, kind, oldFile?.Sha256, newFile?.Sha256));
        }

        return new ConfigSnapshotDiff(older, newer, items);
    }
}

public enum ConfigSnapshotDiffKind
{
    Same,
    Changed,
    Added,
    Removed
}

public sealed record ConfigSnapshotDiffItem(
    string RelativePath,
    ConfigSnapshotDiffKind Kind,
    string? OlderSha256,
    string? NewerSha256);

public sealed record ConfigSnapshotDiff(
    ConfigSnapshotManifest Older,
    ConfigSnapshotManifest Newer,
    IReadOnlyList<ConfigSnapshotDiffItem> Items)
{
    public int Changed => Items.Count(item => item.Kind == ConfigSnapshotDiffKind.Changed);
    public int Added => Items.Count(item => item.Kind == ConfigSnapshotDiffKind.Added);
    public int Removed => Items.Count(item => item.Kind == ConfigSnapshotDiffKind.Removed);
    public int Same => Items.Count(item => item.Kind == ConfigSnapshotDiffKind.Same);
}
