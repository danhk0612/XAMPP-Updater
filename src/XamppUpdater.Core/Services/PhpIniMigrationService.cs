using System.Text.RegularExpressions;

namespace XamppUpdater.Core.Services;

public interface IPhpIniMigrationService
{
    PhpIniMigrationResult Migrate(string currentIniPath, string newPhpRoot, string? targetVersion = null);
}

public sealed partial class PhpIniMigrationService : IPhpIniMigrationService
{
    private readonly IPhpMigrationOverrideStore _overrideStore;

    public PhpIniMigrationService(IPhpMigrationOverrideStore? overrideStore = null)
    {
        _overrideStore = overrideStore ?? new PhpMigrationOverrideStore();
    }

    public PhpIniMigrationResult Migrate(string currentIniPath, string newPhpRoot, string? targetVersion = null)
    {
        if (!File.Exists(currentIniPath))
        {
            return new PhpIniMigrationResult(false, null, Array.Empty<string>());
        }

        var text = File.ReadAllText(currentIniPath);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var warnings = new List<string>();
        var migrated = new List<string>(lines.Length + 4);
        var extRoot = Path.Combine(newPhpRoot, "ext");
        var oldPhpRoot = Path.GetDirectoryName(currentIniPath) ?? string.Empty;
        var availableDlls = EnumerateAvailableDlls(newPhpRoot, extRoot);
        var disableLegacySessionSettings = IsVersionAtLeast(targetVersion, 8, 4) || LooksLikePhp8(newPhpRoot);
        var displayVersion = string.IsNullOrWhiteSpace(targetVersion) ? "8.x" : targetVersion;

        foreach (var line in lines)
        {
            if (disableLegacySessionSettings && TryGetDirectiveName(line, out var directiveName) &&
                (directiveName.Equals("session.sid_length", StringComparison.OrdinalIgnoreCase) ||
                 directiveName.Equals("session.sid_bits_per_character", StringComparison.OrdinalIgnoreCase)))
            {
                migrated.Add($"; XAMPP Updater disabled deprecated setting for PHP {displayVersion}: {line.Trim()}");
                warnings.Add($"PHP {displayVersion}에서 deprecated 설정 비활성화: {directiveName}");
                continue;
            }

            var browscap = BrowscapRegex().Match(line);
            if (browscap.Success)
            {
                var rawValue = browscap.Groups["value"].Value.Trim().Trim('"', '\'');
                var migratedBrowscap = ResolveMigratedPath(rawValue, oldPhpRoot, newPhpRoot);
                if (migratedBrowscap is not null && !File.Exists(migratedBrowscap))
                {
                    var sourceBrowscap = ResolveExistingSourcePath(rawValue, oldPhpRoot);
                    if (sourceBrowscap is not null && IsPathInsideRoot(sourceBrowscap, oldPhpRoot) && IsPathInsideRoot(migratedBrowscap, newPhpRoot))
                    {
                        var destinationDirectory = Path.GetDirectoryName(migratedBrowscap);
                        if (!string.IsNullOrWhiteSpace(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);
                        File.Copy(sourceBrowscap, migratedBrowscap, overwrite: true);
                        warnings.Add($"browscap 파일 자동 보존: {sourceBrowscap} → {migratedBrowscap}");
                    }
                }

                if (migratedBrowscap is not null && File.Exists(migratedBrowscap))
                {
                    migrated.Add($"browscap=\"{migratedBrowscap}\"");
                    if (!PathEquals(rawValue, migratedBrowscap))
                    {
                        warnings.Add($"browscap 경로 자동 변환: {rawValue} → {migratedBrowscap}");
                    }
                }
                else
                {
                    migrated.Add($"; XAMPP Updater disabled missing browscap file: {line.Trim()}");
                    warnings.Add($"새 PHP 환경에 browscap 파일이 없어 비활성화: {rawValue}");
                }
                continue;
            }

            var match = ExtensionRegex().Match(line);
            if (!match.Success)
            {
                migrated.Add(line);
                continue;
            }

            var directive = match.Groups["directive"].Value;
            var value = match.Groups["value"].Value.Trim().Trim('"', '\'');
            var configuredName = Path.GetFileName(value);
            if (string.IsNullOrWhiteSpace(configuredName))
            {
                migrated.Add(line);
                continue;
            }

            var resolvedName = ResolveExtensionDll(configuredName, availableDlls);
            if (resolvedName is not null)
            {
                if (!string.Equals(configuredName, resolvedName, StringComparison.OrdinalIgnoreCase))
                {
                    migrated.Add($"{directive}={resolvedName}");
                    warnings.Add($"확장 모듈 이름 자동 변환: {configuredName} → {resolvedName}");
                }
                else
                {
                    migrated.Add(line);
                }
                continue;
            }

            migrated.Add($"; XAMPP Updater disabled missing/incompatible extension: {line.Trim()}");
            warnings.Add($"새 PHP 패키지/호환 확장에서 찾지 못해 비활성화: {configuredName}");
        }

        var finalText = string.Join(Environment.NewLine, migrated);
        if (!string.IsNullOrWhiteSpace(targetVersion))
        {
            var xamppRoot = ResolveXamppRootFromIni(currentIniPath);
            if (xamppRoot is not null)
            {
                var reviewed = _overrideStore.TryLoad(xamppRoot, targetVersion, currentIniPath);
                if (reviewed is not null)
                {
                    finalText = reviewed.IniText;
                    warnings.Add($"사용자가 확정한 php.ini 마이그레이션안을 적용했습니다. 확정 시각: {reviewed.ConfirmedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}");
                }
            }
        }

        var destination = Path.Combine(newPhpRoot, "php.ini");
        File.WriteAllText(destination, finalText);
        File.Copy(currentIniPath, Path.Combine(newPhpRoot, "php.ini.xampp-updater-original"), overwrite: true);
        return new PhpIniMigrationResult(true, destination, warnings);
    }

    internal static string? NormalizeExtensionName(string configuredName)
    {
        var fileName = Path.GetFileName(configuredName.Trim().Trim('"', '\''));
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[..^4];
        }

        if (fileName.StartsWith("php_", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[4..];
        }
        else if (fileName.StartsWith("ext-", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[4..];
        }
        else
        {
            var packageMatch = LegacyPackageNameRegex().Match(fileName);
            if (packageMatch.Success)
            {
                fileName = packageMatch.Groups["name"].Value;
            }
        }

        var normalized = fileName.Trim().Replace('-', '_');
        return normalized.Equals("gd2", StringComparison.OrdinalIgnoreCase) ? "gd" : normalized;
    }

    internal static string? ResolveExtensionDll(string configuredName, IReadOnlySet<string> availableDlls)
    {
        var fileName = Path.GetFileName(configuredName.Trim().Trim('"', '\''));
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var normalized = NormalizeExtensionName(fileName);
        var candidates = new List<string> { fileName };
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) candidates.Add(fileName + ".dll");
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            candidates.Add(normalized + ".dll");
            candidates.Add("php_" + normalized + ".dll");
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(availableDlls.Contains);
    }

    private static string? ResolveMigratedPath(string configuredPath, string oldPhpRoot, string newPhpRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)) return null;

        try
        {
            if (Path.IsPathFullyQualified(configuredPath) && !string.IsNullOrWhiteSpace(oldPhpRoot))
            {
                var oldFull = Path.GetFullPath(oldPhpRoot);
                var configuredFull = Path.GetFullPath(configuredPath);
                var relative = Path.GetRelativePath(oldFull, configuredFull);
                if (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && relative != "..")
                {
                    return Path.GetFullPath(Path.Combine(newPhpRoot, relative));
                }

                return configuredFull;
            }

            return Path.GetFullPath(Path.Combine(newPhpRoot, configuredPath));
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveExistingSourcePath(string configuredPath, string oldPhpRoot)
    {
        try
        {
            var candidate = Path.IsPathFullyQualified(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(Path.Combine(oldPhpRoot, configuredPath));
            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPathInsideRoot(string path, string root)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveXamppRootFromIni(string currentIniPath)
    {
        try
        {
            var phpDirectory = Directory.GetParent(currentIniPath)?.FullName;
            if (string.IsNullOrWhiteSpace(phpDirectory)) return null;
            var name = Path.GetFileName(phpDirectory);
            if (name.Equals("php", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(".xampp-updater-php-old-", StringComparison.OrdinalIgnoreCase))
            {
                return Directory.GetParent(phpDirectory)?.FullName;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikePhp8(string phpRoot)
    {
        if (!Directory.Exists(phpRoot)) return false;
        return Directory.EnumerateFiles(phpRoot, "php8*.dll", SearchOption.TopDirectoryOnly).Any();
    }

    private static bool TryGetDirectiveName(string line, out string name)
    {
        name = string.Empty;
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#')) return false;
        var equals = trimmed.IndexOf('=');
        if (equals <= 0) return false;
        name = trimmed[..equals].Trim();
        return name.Length > 0;
    }

    private static bool IsVersionAtLeast(string? version, int major, int minor)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out var actualMajor) || !int.TryParse(parts[1], out var actualMinor))
        {
            return false;
        }

        return actualMajor > major || actualMajor == major && actualMinor >= minor;
    }

    private static bool PathEquals(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static HashSet<string> EnumerateAvailableDlls(string phpRoot, string extRoot)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(extRoot))
        {
            foreach (var file in Directory.EnumerateFiles(extRoot, "*.dll", SearchOption.TopDirectoryOnly))
            {
                result.Add(Path.GetFileName(file));
            }
        }

        if (Directory.Exists(phpRoot))
        {
            foreach (var file in Directory.EnumerateFiles(phpRoot, "*.dll", SearchOption.TopDirectoryOnly))
            {
                result.Add(Path.GetFileName(file));
            }
        }

        return result;
    }

    [GeneratedRegex(@"^\s*(?<directive>(?:zend_)?extension)\s*=\s*(?<value>[^;#]+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ExtensionRegex();

    [GeneratedRegex(@"^\s*browscap\s*=\s*(?<value>[^;#]+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex BrowscapRegex();

    [GeneratedRegex(@"^php(?:\d+(?:\.\d+)*)?[-_](?<name>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyPackageNameRegex();
}

public sealed record PhpIniMigrationResult(
    bool Migrated,
    string? IniPath,
    IReadOnlyList<string> Warnings);
