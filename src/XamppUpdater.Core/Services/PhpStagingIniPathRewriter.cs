namespace XamppUpdater.Core.Services;

/// <summary>
/// Builds a validation-only php.ini for a staged PHP runtime.
/// The same PHP-owned path policy used by final migration is applied here so
/// pre-destructive php -v/php -m validation cannot accidentally use files from
/// the still-installed old PHP tree.
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
        var reconciled = PhpIniOwnedPathReconciler.Reconcile(
            source,
            finalPhpRoot,
            finalPhpRoot,
            stagingPhpRoot,
            materialize: true);

        var destination = Path.Combine(stagingPhpRoot, "php.ini.xampp-updater-validation");
        File.WriteAllText(destination, reconciled.IniText);
        return destination;
    }

    public static string RewriteText(string iniText, string finalPhpRoot, string stagingPhpRoot)
    {
        return PhpIniOwnedPathReconciler.Reconcile(
            iniText,
            finalPhpRoot,
            finalPhpRoot,
            stagingPhpRoot,
            materialize: false).IniText;
    }
}
