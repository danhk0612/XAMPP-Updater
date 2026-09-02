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

        foreach (var line in lines)
        {
            var match = ExtensionRegex().Match(line);
            if (!match.Success)
            {
                migrated.Add(line);
                continue;
            }

            var value = match.Groups["value"].Value.Trim().Trim('"', '\'');
            var fileName = Path.GetFileName(value);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                migrated.Add(line);
                continue;
            }

            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".dll";
            }

            if (File.Exists(Path.Combine(extRoot, fileName)) || File.Exists(Path.Combine(newPhpRoot, fileName)))
            {
                migrated.Add(line);
                continue;
            }

            migrated.Add($"; XAMPP Updater disabled missing/incompatible extension: {line.Trim()}");
            warnings.Add($"새 PHP 패키지에 없어 비활성화: {fileName}");
        }

        var destination = Path.Combine(newPhpRoot, "php.ini");
        File.WriteAllText(destination, string.Join(Environment.NewLine, migrated));
        File.Copy(currentIniPath, Path.Combine(newPhpRoot, "php.ini.xampp-updater-original"), overwrite: true);
        return new PhpIniMigrationResult(true, destination, warnings);
    }

    [GeneratedRegex(@"^\s*(?:zend_)?extension\s*=\s*(?<value>[^;#]+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ExtensionRegex();
}

public sealed record PhpIniMigrationResult(
    bool Migrated,
    string? IniPath,
    IReadOnlyList<string> Warnings);
