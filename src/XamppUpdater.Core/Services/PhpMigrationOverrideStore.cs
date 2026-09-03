using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XamppUpdater.Core.Services;

public sealed record PhpMigrationOverride(
    string XamppRoot,
    string TargetVersion,
    string SourceIniSha256,
    string IniText,
    DateTimeOffset ConfirmedAt);

public interface IPhpMigrationOverrideStore
{
    string Save(string xamppRoot, string targetVersion, string sourceIniPath, string iniText);
    PhpMigrationOverride? TryLoad(string xamppRoot, string targetVersion, string sourceIniPath);
}

public sealed class PhpMigrationOverrideStore : IPhpMigrationOverrideStore
{
    private static readonly string RootPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XamppUpdater",
        "MigrationOverrides",
        "PHP");

    public string Save(string xamppRoot, string targetVersion, string sourceIniPath, string iniText)
    {
        if (!File.Exists(sourceIniPath)) throw new FileNotFoundException("원본 php.ini를 찾을 수 없습니다.", sourceIniPath);
        if (string.IsNullOrWhiteSpace(iniText)) throw new ArgumentException("확정할 php.ini 내용이 비어 있습니다.", nameof(iniText));

        Directory.CreateDirectory(RootPath);
        var normalizedRoot = NormalizePath(xamppRoot);
        var record = new PhpMigrationOverride(
            normalizedRoot,
            targetVersion,
            ComputeSha256(sourceIniPath),
            iniText,
            DateTimeOffset.Now);
        var path = GetPath(normalizedRoot, targetVersion);
        File.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    public PhpMigrationOverride? TryLoad(string xamppRoot, string targetVersion, string sourceIniPath)
    {
        if (!File.Exists(sourceIniPath)) return null;
        try
        {
            var normalizedRoot = NormalizePath(xamppRoot);
            var path = GetPath(normalizedRoot, targetVersion);
            if (!File.Exists(path)) return null;
            var record = JsonSerializer.Deserialize<PhpMigrationOverride>(File.ReadAllText(path));
            if (record is null) return null;
            if (!string.Equals(record.XamppRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(record.TargetVersion, targetVersion, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(record.SourceIniSha256, ComputeSha256(sourceIniPath), StringComparison.OrdinalIgnoreCase)) return null;
            return record;
        }
        catch
        {
            return null;
        }
    }

    private static string GetPath(string normalizedRoot, string targetVersion)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot)))[..16];
        var safeVersion = string.Concat(targetVersion.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        return Path.Combine(RootPath, $"{key}-{safeVersion}.json");
    }

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
