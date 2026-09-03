using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IConfigSnapshotService
{
    ConfigSnapshotManifest Capture(string xamppRoot, XamppComponentType type, string? version, string stage, string? note = null);
    ConfigSnapshotManifest CaptureTemporary(string xamppRoot, XamppComponentType type, string? version, string stage = "Current");
    IReadOnlyList<ConfigSnapshotManifest> List(string xamppRoot, XamppComponentType type);
    ConfigSnapshotManifest? Load(string manifestPath);
    ConfigSnapshotIntegrityResult Verify(ConfigSnapshotManifest snapshot);
    ConfigSnapshotManifest UpdateNote(ConfigSnapshotManifest snapshot, string? note);
    void Delete(ConfigSnapshotManifest snapshot);
}

public sealed record ConfigSnapshotIntegrityResult(bool Valid, int VerifiedFiles, IReadOnlyList<string> Errors);

public sealed class ConfigSnapshotService : IConfigSnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ConfigSnapshotManifest Capture(string xamppRoot, XamppComponentType type, string? version, string stage, string? note = null)
    {
        var fullRoot = Path.GetFullPath(xamppRoot);
        var capturedAt = DateTimeOffset.Now;
        var snapshotRoot = Path.Combine(GetComponentHistoryRoot(fullRoot, type), $"{capturedAt:yyyyMMdd-HHmmssfff}-{Sanitize(stage)}");
        return CaptureInto(fullRoot, type, version, stage, note, capturedAt, snapshotRoot);
    }

    public ConfigSnapshotManifest CaptureTemporary(string xamppRoot, XamppComponentType type, string? version, string stage = "Current")
    {
        var fullRoot = Path.GetFullPath(xamppRoot);
        var capturedAt = DateTimeOffset.Now;
        var snapshotRoot = Path.Combine(Path.GetTempPath(), "XamppUpdater", "ConfigCompare", Guid.NewGuid().ToString("N"));
        return CaptureInto(fullRoot, type, version, stage, null, capturedAt, snapshotRoot);
    }

    public IReadOnlyList<ConfigSnapshotManifest> List(string xamppRoot, XamppComponentType type)
    {
        var root = GetComponentHistoryRoot(Path.GetFullPath(xamppRoot), type);
        if (!Directory.Exists(root)) return Array.Empty<ConfigSnapshotManifest>();
        return Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories)
            .Select(Load)
            .Where(item => item is not null)
            .Cast<ConfigSnapshotManifest>()
            .OrderByDescending(item => item.CapturedAt)
            .ToArray();
    }

    public ConfigSnapshotManifest? Load(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath)) return null;
            return JsonSerializer.Deserialize<ConfigSnapshotManifest>(File.ReadAllText(manifestPath));
        }
        catch { return null; }
    }

    public ConfigSnapshotIntegrityResult Verify(ConfigSnapshotManifest snapshot)
    {
        var errors = new List<string>();
        var verified = 0;
        if (!File.Exists(snapshot.ManifestPath))
            errors.Add("manifest 파일이 없습니다.");

        var filesRoot = Path.Combine(Path.GetDirectoryName(snapshot.ManifestPath)!, "files");
        foreach (var entry in snapshot.Files)
        {
            try
            {
                var path = SafeCombine(filesRoot, entry.RelativePath);
                if (!File.Exists(path)) { errors.Add($"파일 없음: {entry.RelativePath}"); continue; }
                var info = new FileInfo(path);
                if (info.Length != entry.Size) { errors.Add($"크기 불일치: {entry.RelativePath}"); continue; }
                if (!string.Equals(ComputeSha256(path), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                { errors.Add($"SHA256 불일치: {entry.RelativePath}"); continue; }
                verified++;
            }
            catch (Exception ex) { errors.Add($"{entry.RelativePath}: {ex.Message}"); }
        }
        return new ConfigSnapshotIntegrityResult(errors.Count == 0, verified, errors);
    }

    public ConfigSnapshotManifest UpdateNote(ConfigSnapshotManifest snapshot, string? note)
    {
        var updated = snapshot with { Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() };
        File.WriteAllText(snapshot.ManifestPath, JsonSerializer.Serialize(updated, JsonOptions), new UTF8Encoding(false));
        return updated;
    }

    public void Delete(ConfigSnapshotManifest snapshot)
    {
        var folder = Path.GetDirectoryName(snapshot.ManifestPath) ?? throw new InvalidOperationException("snapshot 폴더를 확인할 수 없습니다.");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static ConfigSnapshotManifest CaptureInto(string fullRoot, XamppComponentType type, string? version, string stage, string? note, DateTimeOffset capturedAt, string snapshotRoot)
    {
        var componentRoot = GetComponentRoot(fullRoot, type);
        var files = EnumerateConfigFiles(componentRoot, type).ToArray();
        if (files.Length == 0) throw new InvalidOperationException($"{type}에서 저장할 설정 파일을 찾지 못했습니다.");

        var filesRoot = Path.Combine(snapshotRoot, "files");
        Directory.CreateDirectory(filesRoot);
        var entries = new List<ConfigSnapshotFile>();
        foreach (var source in files)
        {
            var relative = Path.GetRelativePath(componentRoot, source).Replace('\\', '/');
            var destination = SafeCombine(filesRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            var info = new FileInfo(destination);
            entries.Add(new ConfigSnapshotFile(relative, info.Length, ComputeSha256(destination)));
        }

        var manifestPath = Path.Combine(snapshotRoot, "manifest.json");
        var manifest = new ConfigSnapshotManifest(manifestPath, capturedAt, fullRoot, type, version, stage, entries,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim());
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
        return manifest;
    }

    private static string GetComponentRoot(string fullRoot, XamppComponentType type) => type switch
    {
        XamppComponentType.Apache => Path.Combine(fullRoot, "apache"),
        XamppComponentType.Php => Path.Combine(fullRoot, "php"),
        XamppComponentType.MariaDb => Path.Combine(fullRoot, "mysql"),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static IEnumerable<string> EnumerateConfigFiles(string componentRoot, XamppComponentType type)
    {
        if (!Directory.Exists(componentRoot)) yield break;
        if (type == XamppComponentType.Php)
        {
            var ini = Path.Combine(componentRoot, "php.ini");
            if (File.Exists(ini)) yield return ini;
            yield break;
        }
        if (type == XamppComponentType.MariaDb)
        {
            foreach (var relative in new[] { "my.ini", "my.cnf", "bin/my.ini", "bin/my.cnf" })
            {
                var path = Path.Combine(componentRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) yield return path;
            }
            yield break;
        }

        var confRoot = Path.Combine(componentRoot, "conf");
        if (!Directory.Exists(confRoot)) yield break;
        foreach (var file in Directory.EnumerateFiles(confRoot, "*.conf", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(confRoot, file).Replace('\\', '/');
            if (!relative.StartsWith("original/", StringComparison.OrdinalIgnoreCase)) yield return file;
        }
    }

    private static string GetComponentHistoryRoot(string xamppRoot, XamppComponentType type)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var rootId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(xamppRoot).ToUpperInvariant())))[..16];
        return Path.Combine(local, "XamppUpdater", "ConfigHistory", rootId, type.ToString());
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("snapshot 상대 경로가 허용된 루트를 벗어납니다: " + relative);
        return full;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(value.Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : ch).ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "Snapshot" : result;
    }
}

public sealed record ConfigSnapshotManifest(
    string ManifestPath,
    DateTimeOffset CapturedAt,
    string XamppRoot,
    XamppComponentType Type,
    string? Version,
    string Stage,
    IReadOnlyList<ConfigSnapshotFile> Files,
    string? Note = null);

public sealed record ConfigSnapshotFile(string RelativePath, long Size, string Sha256);
