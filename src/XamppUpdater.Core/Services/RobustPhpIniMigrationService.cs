using System.Text.RegularExpressions;

namespace XamppUpdater.Core.Services;

/// <summary>
/// Adds a final filesystem reconciliation pass after the normal php.ini migration.
/// This runs after persisted user overrides are applied so PHP-owned paths are mapped
/// to the new PHP tree without touching paths that belong elsewhere in XAMPP/system.
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
        var sourceIni = File.Exists(currentIniPath) ? File.ReadAllText(currentIniPath) : string.Empty;
        var result = _inner.Migrate(currentIniPath, newPhpRoot, targetVersion);
        if (!result.Migrated || string.IsNullOrWhiteSpace(result.IniPath) || !File.Exists(result.IniPath))
        {
            return result;
        }

        var sourcePhpRoot = Path.GetDirectoryName(currentIniPath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePhpRoot)) return result;
        var configuredPhpRoot = ResolveConfiguredPhpRoot(sourcePhpRoot);
        if (configuredPhpRoot is null) return result;

        var warnings = result.Warnings.ToList();
        var migratedIni = File.ReadAllText(result.IniPath);

        // Older/base migration code may already have disabled browscap before this
        // final pass if php.ini used C:\xampp\php or \xampp\php while the old files
        // physically lived in .xampp-updater-php-old-... after the directory swap.
        // Recover only that updater-generated disable marker. A user override that
        // deliberately comments browscap has no such marker and is therefore respected.
        migratedIni = RecoverUpdaterDisabledBrowscap(migratedIni, sourceIni);

        var reconciled = PhpIniOwnedPathReconciler.Reconcile(
            migratedIni,
            configuredPhpRoot,
            sourcePhpRoot,
            newPhpRoot,
            materialize: true);
        warnings.AddRange(reconciled.Warnings);

        if (!string.Equals(migratedIni, reconciled.IniText, StringComparison.Ordinal))
        {
            File.WriteAllText(result.IniPath, reconciled.IniText);
        }

        return result with { Warnings = warnings };
    }

    private static string RecoverUpdaterDisabledBrowscap(string migratedIni, string sourceIni)
    {
        if (string.IsNullOrWhiteSpace(migratedIni) || string.IsNullOrWhiteSpace(sourceIni)) return migratedIni;
        if (ActiveBrowscapRegex().IsMatch(migratedIni)) return migratedIni;

        var sourceMatch = ActiveBrowscapRegex().Match(sourceIni);
        if (!sourceMatch.Success) return migratedIni;

        var sourceLine = sourceMatch.Value.TrimEnd('\r', '\n');
        return DisabledBrowscapRegex().Replace(migratedIni, sourceLine, 1);
    }

    private static string? ResolveConfiguredPhpRoot(string sourcePhpRoot)
    {
        try
        {
            var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePhpRoot));
            var name = Path.GetFileName(source);
            if (name.Equals("php", StringComparison.OrdinalIgnoreCase)) return source;

            if (name.StartsWith(".xampp-updater-php-old-", StringComparison.OrdinalIgnoreCase))
            {
                var xamppRoot = Directory.GetParent(source)?.FullName;
                return string.IsNullOrWhiteSpace(xamppRoot) ? null : Path.Combine(xamppRoot, "php");
            }

            return source;
        }
        catch
        {
            return null;
        }
    }

    [GeneratedRegex(@"(?im)^\s*browscap\s*=\s*[^;#]+?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ActiveBrowscapRegex();

    [GeneratedRegex(@"(?im)^\s*;\s*XAMPP Updater disabled (?:missing browscap file|missing browscap after final override):.*$", RegexOptions.IgnoreCase)]
    private static partial Regex DisabledBrowscapRegex();
}
