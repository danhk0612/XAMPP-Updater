using System.Text.RegularExpressions;

namespace XamppUpdater.Core.Services;

/// <summary>
/// Adds a final filesystem reconciliation pass after the normal php.ini migration.
/// This is intentionally performed after persisted user overrides are applied so an
/// override cannot leave an active browscap directive pointing at a file that was
/// moved into the rollback PHP directory during the directory swap.
/// </summary>
public sealed partial class RobustPhpIniMigrationService : IPhpIniMigrationService
{
    private readonly IPhpIniMigrationService _inner;

    public RobustPhpIniMigrationService(IPhpIniMigrationService? inner = null)
    {
        _inner = inner ?? new PhpIniMigrationService();
    }

    public PhpIniMigrationResult Migrate(string currentIniPath, string newPhpRoot, string? targetVersion = null)
    {
        var result = _inner.Migrate(currentIniPath, newPhpRoot, targetVersion);
        if (!result.Migrated || string.IsNullOrWhiteSpace(result.IniPath) || !File.Exists(result.IniPath))
        {
            return result;
        }

        var oldPhpRoot = Path.GetDirectoryName(currentIniPath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(oldPhpRoot)) return result;

        var warnings = result.Warnings.ToList();
        var original = File.ReadAllText(result.IniPath);
        var lines = original.Replace("\r\n", "\n").Split('\n');
        var changed = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var match = BrowscapRegex().Match(lines[i]);
            if (!match.Success) continue;

            var configured = match.Groups["value"].Value.Trim().Trim('"', '\'');
            var destination = ResolveDestination(configured, newPhpRoot);
            if (destination is null) continue;
            if (File.Exists(destination)) continue;

            var source = ResolveRollbackSource(configured, oldPhpRoot, newPhpRoot);
            if (source is not null && File.Exists(source) &&
                IsInsideRoot(source, oldPhpRoot) && IsInsideRoot(destination, newPhpRoot))
            {
                var directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.Copy(source, destination, overwrite: true);
                warnings.Add($"최종 php.ini browscap 파일 자동 복구: {source} → {destination}");
                continue;
            }

            lines[i] = $"; XAMPP Updater disabled missing browscap after final override: {lines[i].Trim()}";
            warnings.Add($"최종 php.ini가 존재하지 않는 browscap 파일을 참조하여 안전하게 비활성화: {configured}");
            changed = true;
        }

        if (changed)
        {
            File.WriteAllText(result.IniPath, string.Join(Environment.NewLine, lines));
        }

        return result with { Warnings = warnings };
    }

    private static string? ResolveDestination(string configured, string newPhpRoot)
    {
        try
        {
            if (Path.IsPathFullyQualified(configured)) return Path.GetFullPath(configured);
            return Path.GetFullPath(Path.Combine(newPhpRoot, configured));
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveRollbackSource(string configured, string oldPhpRoot, string newPhpRoot)
    {
        try
        {
            if (!Path.IsPathFullyQualified(configured))
            {
                return Path.GetFullPath(Path.Combine(oldPhpRoot, configured));
            }

            var configuredFull = Path.GetFullPath(configured);
            var newRootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(newPhpRoot));
            if (configuredFull.Equals(newRootFull, StringComparison.OrdinalIgnoreCase) ||
                configuredFull.StartsWith(newRootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(newRootFull, configuredFull);
                return Path.GetFullPath(Path.Combine(oldPhpRoot, relative));
            }

            return configuredFull;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsInsideRoot(string path, string root)
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

    [GeneratedRegex(@"^\s*browscap\s*=\s*(?<value>[^;#]+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex BrowscapRegex();
}
