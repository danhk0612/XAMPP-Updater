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

        return result;
    }
}
