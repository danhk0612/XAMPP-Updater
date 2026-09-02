using System.Text.RegularExpressions;

namespace XamppUpdater.Core.Services;

public interface IPhpIniMigrationService
{
    PhpIniMigrationResult Migrate(string currentIniPath, string newPhpRoot);
}

public sealed partial class PhpIniMigrationService : IPhpIniMigrationService
{
    public PhpIniMigrationResult Migrate(string currentIniPath, string newPhpRoot)
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
        var availableDlls = EnumerateAvailableDlls(newPhpRoot, extRoot);

        foreach (var line in lines)
        {
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
            warnings.Add($"새 PHP 패키지에 없어 비활성화: {configuredName}");
        }

        var destination = Path.Combine(newPhpRoot, "php.ini");
        File.WriteAllText(destination, string.Join(Environment.NewLine, migrated));
        File.Copy(currentIniPath, Path.Combine(newPhpRoot, "php.ini.xampp-updater-original"), overwrite: true);
        return new PhpIniMigrationResult(true, destination, warnings);
    }

    internal static string? ResolveExtensionDll(string configuredName, IReadOnlySet<string> availableDlls)
    {
        var fileName = Path.GetFileName(configuredName.Trim().Trim('"', '\''));
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var candidates = new List<string>();
        if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(fileName);
            if (!fileName.StartsWith("php_", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add("php_" + fileName);
            }
        }
        else
        {
            candidates.Add(fileName + ".dll");
            if (!fileName.StartsWith("php_", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add("php_" + fileName + ".dll");
            }
        }

        return candidates.FirstOrDefault(availableDlls.Contains);
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
}

public sealed record PhpIniMigrationResult(
    bool Migrated,
    string? IniPath,
    IReadOnlyList<string> Warnings);
