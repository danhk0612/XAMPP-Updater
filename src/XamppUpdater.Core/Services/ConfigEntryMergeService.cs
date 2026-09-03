using System.Text;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public enum ConfigEntryMergeKind
{
    Changed,
    AddedInSnapshot,
    RemovedFromSnapshot,
    Conflict
}

public sealed record ConfigEntryMergeItem(
    string RelativePath,
    string Identity,
    string DisplayName,
    string? SnapshotValue,
    string? CurrentValue,
    ConfigEntryMergeKind Kind,
    bool CanApply,
    string? Reason = null);

public interface IConfigEntryMergeService
{
    IReadOnlyList<ConfigEntryMergeItem> Compare(ConfigSnapshotManifest snapshot, ConfigSnapshotManifest current);
    string ApplySelections(string currentText, string snapshotText, XamppComponentType type, IReadOnlyCollection<string> identities);
}

public sealed class ConfigEntryMergeService : IConfigEntryMergeService
{
    public IReadOnlyList<ConfigEntryMergeItem> Compare(ConfigSnapshotManifest snapshot, ConfigSnapshotManifest current)
    {
        if (snapshot.Type != current.Type) throw new ArgumentException("구성요소가 다른 snapshot은 항목 비교할 수 없습니다.");
        var result = new List<ConfigEntryMergeItem>();
        var paths = snapshot.Files.Select(x => x.RelativePath)
            .Concat(current.Files.Select(x => x.RelativePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            var oldText = Read(snapshot, path);
            var newText = Read(current, path);
            if (oldText is null || newText is null)
            {
                result.Add(new ConfigEntryMergeItem(path, "@file", path, oldText, newText, ConfigEntryMergeKind.Conflict, false,
                    "파일 자체가 한쪽 snapshot에만 존재하므로 파일 단위 복원을 사용하세요."));
                continue;
            }

            var oldParsed = Parse(oldText, snapshot.Type);
            var newParsed = Parse(newText, snapshot.Type);
            foreach (var key in oldParsed.Keys.Concat(newParsed.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                oldParsed.TryGetValue(key, out var oldEntry);
                newParsed.TryGetValue(key, out var newEntry);
                if (oldEntry?.Ambiguous == true || newEntry?.Ambiguous == true)
                {
                    result.Add(new ConfigEntryMergeItem(path, key, oldEntry?.DisplayName ?? newEntry?.DisplayName ?? key,
                        oldEntry?.Value, newEntry?.Value, ConfigEntryMergeKind.Conflict, false, "같은 설정 항목이 여러 번 등장하여 자동 병합하지 않습니다."));
                    continue;
                }
                if (oldEntry is not null && newEntry is not null && string.Equals(oldEntry.Value, newEntry.Value, StringComparison.Ordinal)) continue;
                var kind = oldEntry is null ? ConfigEntryMergeKind.RemovedFromSnapshot : newEntry is null ? ConfigEntryMergeKind.AddedInSnapshot : ConfigEntryMergeKind.Changed;
                var canApply = oldEntry is not null && newEntry is not null;
                var reason = canApply ? null : "항목 추가/삭제는 위치와 주석 의미가 달라질 수 있어 자동 병합하지 않습니다.";
                result.Add(new ConfigEntryMergeItem(path, key, oldEntry?.DisplayName ?? newEntry?.DisplayName ?? key,
                    oldEntry?.Value, newEntry?.Value, canApply ? kind : ConfigEntryMergeKind.Conflict, canApply, reason));
            }
        }
        return result;
    }

    public string ApplySelections(string currentText, string snapshotText, XamppComponentType type, IReadOnlyCollection<string> identities)
    {
        if (identities.Count == 0) return currentText;
        var wanted = identities.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var snapshot = Parse(snapshotText, type);
        var current = Parse(currentText, type);
        var lines = SplitLines(currentText).ToList();

        foreach (var identity in wanted)
        {
            if (!snapshot.TryGetValue(identity, out var source) || source.Ambiguous || !current.TryGetValue(identity, out var target) || target.Ambiguous)
                throw new InvalidOperationException("자동 병합할 수 없는 설정 항목입니다: " + identity);
            lines[target.LineIndex] = RebuildLine(lines[target.LineIndex], source.Value, type);
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static Dictionary<string, ParsedEntry> Parse(string text, XamppComponentType type) =>
        type == XamppComponentType.Apache ? ParseApache(text) : ParseIni(text);

    private static Dictionary<string, ParsedEntry> ParseIni(string text)
    {
        var map = new Dictionary<string, ParsedEntry>(StringComparer.OrdinalIgnoreCase);
        var section = string.Empty;
        var lines = SplitLines(text);
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#')) continue;
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed[1..^1].Trim();
                continue;
            }
            var equals = trimmed.IndexOf('=');
            if (equals <= 0) continue;
            var key = trimmed[..equals].Trim();
            var value = trimmed[(equals + 1)..].Trim();
            var identity = $"{section}\u001f{key}";
            var display = string.IsNullOrWhiteSpace(section) ? key : $"[{section}] {key}";
            Add(map, identity, new ParsedEntry(display, value, i, false));
        }
        return map;
    }

    private static Dictionary<string, ParsedEntry> ParseApache(string text)
    {
        var map = new Dictionary<string, ParsedEntry>(StringComparer.OrdinalIgnoreCase);
        var scope = new Stack<string>();
        var lines = SplitLines(text);
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            if (trimmed.StartsWith("</", StringComparison.Ordinal))
            {
                if (scope.Count > 0) scope.Pop();
                continue;
            }
            if (trimmed.StartsWith('<') && trimmed.EndsWith('>'))
            {
                var tag = trimmed[1..^1].Trim();
                var nameEnd = tag.IndexOfAny([' ', '\t']);
                var name = (nameEnd < 0 ? tag : tag[..nameEnd]).Trim();
                if (!name.StartsWith('/')) scope.Push(name);
                continue;
            }
            if (trimmed.EndsWith('\\')) continue;
            var split = trimmed.IndexOfAny([' ', '\t']);
            if (split <= 0) continue;
            var directive = trimmed[..split].Trim();
            var value = trimmed[(split + 1)..].Trim();
            var context = scope.Count == 0 ? "global" : string.Join('/', scope.Reverse());
            var identity = $"{context}\u001f{directive}";
            Add(map, identity, new ParsedEntry($"[{context}] {directive}", value, i, false));
        }
        return map;
    }

    private static void Add(Dictionary<string, ParsedEntry> map, string identity, ParsedEntry entry)
    {
        if (!map.TryGetValue(identity, out var existing))
        {
            map[identity] = entry;
            return;
        }
        map[identity] = existing with { Ambiguous = true };
    }

    private static string RebuildLine(string original, string value, XamppComponentType type)
    {
        var leadingLength = original.Length - original.TrimStart().Length;
        var leading = original[..leadingLength];
        var trimmed = original.TrimStart();
        if (type == XamppComponentType.Apache)
        {
            var split = trimmed.IndexOfAny([' ', '\t']);
            if (split <= 0) return original;
            return leading + trimmed[..split] + " " + value;
        }
        var equals = trimmed.IndexOf('=');
        if (equals <= 0) return original;
        return leading + trimmed[..equals].TrimEnd() + " = " + value;
    }

    private static string? Read(ConfigSnapshotManifest snapshot, string relativePath)
    {
        if (!snapshot.Files.Any(x => string.Equals(x.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))) return null;
        var root = Path.Combine(Path.GetDirectoryName(snapshot.ManifestPath)!, "files");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        return File.ReadAllText(path);
    }

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private sealed record ParsedEntry(string DisplayName, string Value, int LineIndex, bool Ambiguous);
}
