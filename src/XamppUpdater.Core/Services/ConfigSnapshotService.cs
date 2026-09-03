using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IConfigSnapshotService
{
    ConfigSnapshotManifest Capture(string xamppRoot, XamppComponentType type, string? version, string stage);
    IReadOnlyList<ConfigSnapshotManifest> List(string xamppRoot, XamppComponentType type);
    ConfigSnapshotManifest? Load(string manifestPath);
}

public sealed class ConfigSnapshotService : IConfigSnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ConfigSnapshotManifest Capture(string xamppRoot, XamppComponentType type, string? version, string stage)
    {
        var fullRoot = Path.GetFullPath(xamppRoot);
        var componentRoot = type switch
        {
            XamppComponentType.Apache => Path.Combine(fullRoot, "apache"),
            XamppComponentType.Php => Path.Combine(fullRoot, "php"),
            XamppComponentType.MariaDb => Path.Combine(fullRoot, "mysql"),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        var files = EnumerateConfigFiles(componentRoot, type).ToArray();
        var capturedAt = DateTimeOffset.Now;
        var safeStage = Sanitize(stage);
        var snapshotRoot = Path.Combine(GetComponentHistoryRoot(fullRoot, type), $"{capturedAt:yyyyMMdd-HHmmssfff}-{safeStage}");
        var filesRoot = Path.Combine(snapshotRoot, "files");
        Directory.CreateDirectory(filesRoot);

        var entries = new List<ConfigSnapshotFile>();
        foreach (var source in files)
        {
            var relative = Path.GetRelativePath(componentRoot, source).Replace('\\', '/');
            var destination = Path.Combine(filesRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            var info = new FileInfo(destination);
            entries.Add(new ConfigSnapshotFile(relative, info.Length, ComputeSha256(destination)));
        }

        var manifestPath = Path.Combine(snapshotRoot, "manifest.json");
        var manifest = new ConfigSnapshotManifest(
            manifestPath,
            capturedAt,
            fullRoot,
            type,
            version,
            stage,
            entries);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));
        return manifest;
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
        catch
        {
            return null;
        }
    }

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
            if (relative.StartsWith("original/", StringComparison.OrdinalIgnoreCase)) continue;
            yield return file;
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
    IReadOnlyList<ConfigSnapshotFile> Files);

public sealed record ConfigSnapshotFile(string RelativePath, long Size, string Sha256);
