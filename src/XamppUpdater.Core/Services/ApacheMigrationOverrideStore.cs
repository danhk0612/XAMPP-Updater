using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XamppUpdater.Core.Services;

public sealed record ApacheMigrationOverride(
    string XamppRoot,
    string TargetVersion,
    string SourceConfHash,
    DateTimeOffset ConfirmedAt,
    IReadOnlyDictionary<string, string> Files);

public interface IApacheMigrationOverrideStore
{
    string Save(string xamppRoot, string targetVersion, string sourceConfRoot, IReadOnlyDictionary<string, string> files);
    ApacheMigrationOverride? TryLoad(string xamppRoot, string targetVersion, string sourceConfRoot);
}

public sealed class ApacheMigrationOverrideStore : IApacheMigrationOverrideStore
{
    private readonly string _root;

    public ApacheMigrationOverrideStore(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XamppUpdater", "MigrationOverrides", "Apache");
    }

    public string Save(string xamppRoot, string targetVersion, string sourceConfRoot, IReadOnlyDictionary<string, string> files)
    {
        Directory.CreateDirectory(_root);
        var copiedFiles = files.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        var record = new ApacheMigrationOverride(
            Path.GetFullPath(xamppRoot),
            targetVersion,
            ComputeConfHash(sourceConfRoot),
            DateTimeOffset.Now,
            copiedFiles);
        var path = GetPath(xamppRoot, targetVersion);
        File.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        return path;
    }

    public ApacheMigrationOverride? TryLoad(string xamppRoot, string targetVersion, string sourceConfRoot)
    {
        var path = GetPath(xamppRoot, targetVersion);
        if (!File.Exists(path) || !Directory.Exists(sourceConfRoot)) return null;
        try
        {
            var record = JsonSerializer.Deserialize<ApacheMigrationOverride>(File.ReadAllText(path, Encoding.UTF8));
            if (record is null ||
                !PathsEqual(record.XamppRoot, xamppRoot) ||
                !string.Equals(record.TargetVersion, targetVersion, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(record.SourceConfHash, ComputeConfHash(sourceConfRoot), StringComparison.OrdinalIgnoreCase))
                return null;
            return record;
        }
        catch
        {
            return null;
        }
    }

    internal static string ComputeConfHash(string confRoot)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(confRoot, "*.conf", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(confRoot, path), StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(confRoot, file).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relative + "\n"));
            hash.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private string GetPath(string xamppRoot, string targetVersion)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(xamppRoot).ToUpperInvariant())))[..16];
        var safeVersion = string.Concat(targetVersion.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' ? ch : '_'));
        return Path.Combine(_root, $"{key}-{safeVersion}.json");
    }

    private static bool PathsEqual(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}
