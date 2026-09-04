using System.Text.RegularExpressions;

namespace XamppUpdater.Core.Services;

/// <summary>
/// Builds a validation-only php.ini for a staged PHP runtime.
/// The real migrated php.ini keeps final XAMPP paths such as C:\xampp\php\ext,
/// but those paths must point at the temporary staged PHP tree while running
/// pre-destructive php -v/php -m validation.
/// </summary>
public static class PhpStagingIniPathRewriter
{
    public static string CreateValidationIni(
        string sourceIniPath,
        string finalPhpRoot,
        string stagingPhpRoot)
    {
        if (!File.Exists(sourceIniPath))
            throw new FileNotFoundException("마이그레이션된 php.ini를 찾지 못했습니다.", sourceIniPath);

        var source = File.ReadAllText(sourceIniPath);
        var rewritten = RewriteText(source, finalPhpRoot, stagingPhpRoot);
        MaterializeBrowscap(rewritten, finalPhpRoot, stagingPhpRoot);
        var destination = Path.Combine(stagingPhpRoot, "php.ini.xampp-updater-validation");
        File.WriteAllText(destination, rewritten);
        return destination;
    }

    public static string RewriteText(string iniText, string finalPhpRoot, string stagingPhpRoot)
    {
        if (string.IsNullOrWhiteSpace(finalPhpRoot) || string.IsNullOrWhiteSpace(stagingPhpRoot))
            return iniText;

        var finalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(finalPhpRoot));
        var stagingRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingPhpRoot));

        var result = iniText.Replace(finalRoot, stagingRoot, StringComparison.OrdinalIgnoreCase);

        var finalForward = finalRoot.Replace('\\', '/');
        var stagingForward = stagingRoot.Replace('\\', '/');
        if (!string.Equals(finalForward, finalRoot, StringComparison.Ordinal))
            result = result.Replace(finalForward, stagingForward, StringComparison.OrdinalIgnoreCase);

        // Some XAMPP php.ini files use drive-root-relative paths such as
        // \xampp\php\ext rather than C:\xampp\php\ext. Windows resolves those
        // against the current drive, which made staging validation load DLLs from
        // the still-installed old PHP tree and produced false ABI/entry-point errors.
        // Rebase those aliases to the absolute staging root as well.
        if (OperatingSystem.IsWindows())
        {
            var driveRoot = Path.GetPathRoot(finalRoot);
            if (!string.IsNullOrWhiteSpace(driveRoot) && finalRoot.Length > driveRoot.Length)
            {
                var relativeFromDrive = finalRoot[driveRoot.Length..]
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.IsNullOrWhiteSpace(relativeFromDrive))
                {
                    var rootRelativeBackslash = "\\" + relativeFromDrive.Replace('/', '\\');
                    var rootRelativeForward = "/" + relativeFromDrive.Replace('\\', '/');
                    result = result.Replace(rootRelativeBackslash, stagingRoot, StringComparison.OrdinalIgnoreCase);
                    result = result.Replace(rootRelativeForward, stagingForward, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        return result;
    }

    private static void MaterializeBrowscap(string validationIni, string finalPhpRoot, string stagingPhpRoot)
    {
        var match = Regex.Match(
            validationIni,
            @"(?im)^\s*browscap\s*=\s*(?<value>[^;#]+?)\s*$",
            RegexOptions.IgnoreCase);
        if (!match.Success) return;

        var configured = match.Groups["value"].Value.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(configured)) return;

        try
        {
            var stagingRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingPhpRoot));
            var finalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(finalPhpRoot));
            var destination = Path.IsPathFullyQualified(configured)
                ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(stagingRoot, configured));

            if (!IsInsideRoot(destination, stagingRoot) || File.Exists(destination)) return;

            var relative = Path.GetRelativePath(stagingRoot, destination);
            var source = Path.GetFullPath(Path.Combine(finalRoot, relative));
            if (!IsInsideRoot(source, finalRoot) || !File.Exists(source)) return;

            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.Copy(source, destination, overwrite: true);
        }
        catch
        {
            // If the source is genuinely missing, leave the directive untouched so
            // the runtime validator reports the real configuration problem.
        }
    }

    private static bool IsInsideRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
