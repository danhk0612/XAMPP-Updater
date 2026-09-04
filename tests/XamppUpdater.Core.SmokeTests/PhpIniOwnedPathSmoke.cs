using System.Runtime.CompilerServices;
using XamppUpdater.Core.Services;

internal static class PhpIniOwnedPathSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "xampp-updater-php-path-smoke-" + Guid.NewGuid().ToString("N"));
        var xamppRoot = Path.Combine(root, "xampp");
        var configuredRoot = Path.Combine(xamppRoot, "php");
        var sourceRoot = Path.Combine(xamppRoot, ".xampp-updater-php-old-smoke");
        var destinationRoot = configuredRoot;
        var externalRoot = Path.Combine(root, "external");

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceRoot, "certs"));
            Directory.CreateDirectory(Path.Combine(sourceRoot, "PEAR"));
            Directory.CreateDirectory(Path.Combine(sourceRoot, "sessions"));
            Directory.CreateDirectory(Path.Combine(sourceRoot, "ext"));
            Directory.CreateDirectory(destinationRoot);
            Directory.CreateDirectory(Path.Combine(destinationRoot, "ext"));
            Directory.CreateDirectory(externalRoot);

            File.WriteAllText(Path.Combine(sourceRoot, "certs", "cacert.pem"), "CERT");
            File.WriteAllText(Path.Combine(sourceRoot, "PEAR", "Library.php"), "<?php // pear");
            File.WriteAllText(Path.Combine(sourceRoot, "sessions", "sess_old"), "old-session");
            File.WriteAllText(Path.Combine(sourceRoot, "ext", "php_stale.dll"), "stale");
            File.WriteAllText(Path.Combine(destinationRoot, "ext", "php_new.dll"), "new");
            var externalCert = Path.Combine(externalRoot, "external.pem");
            File.WriteAllText(externalCert, "EXTERNAL");

            var ini =
                $"extension_dir=\"{Path.Combine(configuredRoot, "ext")}\"{Environment.NewLine}" +
                $"curl.cainfo=\"{Path.Combine(configuredRoot, "certs", "cacert.pem")}\"{Environment.NewLine}" +
                $"include_path=\".;{Path.Combine(configuredRoot, "PEAR")};{Path.Combine(configuredRoot, "ext")};{externalRoot}\"{Environment.NewLine}" +
                $"session.save_path=\"5;0600;{Path.Combine(configuredRoot, "sessions")}\"{Environment.NewLine}" +
                $"error_log=\"{Path.Combine(configuredRoot, "logs", "php_error.log")}\"{Environment.NewLine}" +
                $"openssl.cafile=\"{externalCert}\"";

            var result = PhpIniOwnedPathReconciler.Reconcile(
                ini,
                configuredRoot,
                sourceRoot,
                destinationRoot,
                materialize: true);

            if (!File.Exists(Path.Combine(destinationRoot, "certs", "cacert.pem")))
                throw new InvalidOperationException("PHP owned-path smoke: cainfo file was not preserved.");
            if (!File.Exists(Path.Combine(destinationRoot, "PEAR", "Library.php")))
                throw new InvalidOperationException("PHP owned-path smoke: include_path directory was not preserved.");
            if (!Directory.Exists(Path.Combine(destinationRoot, "sessions")))
                throw new InvalidOperationException("PHP owned-path smoke: session directory was not created.");
            if (File.Exists(Path.Combine(destinationRoot, "sessions", "sess_old")))
                throw new InvalidOperationException("PHP owned-path smoke: stale session contents were copied.");
            if (!Directory.Exists(Path.Combine(destinationRoot, "logs")))
                throw new InvalidOperationException("PHP owned-path smoke: error_log parent directory was not created.");
            if (File.Exists(Path.Combine(destinationRoot, "ext", "php_stale.dll")))
                throw new InvalidOperationException("PHP owned-path smoke: package-managed ext directory copied a stale DLL.");
            if (!result.IniText.Contains(externalCert, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PHP owned-path smoke: external certificate path was changed.");
            if (!result.IniText.Contains(externalRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PHP owned-path smoke: external include_path entry was changed.");

            if (OperatingSystem.IsWindows())
            {
                RunRootRelativeBrowscapRecovery(xamppRoot, sourceRoot, destinationRoot);
            }
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void RunRootRelativeBrowscapRecovery(string xamppRoot, string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(Path.Combine(sourceRoot, "extras"));
        File.WriteAllText(Path.Combine(sourceRoot, "extras", "browscap.ini"), "[GJK_Browscap_Version]");

        var configuredRoot = Path.Combine(xamppRoot, "php");
        var driveRoot = Path.GetPathRoot(configuredRoot)!;
        var rootRelativePhp = "\\" + configuredRoot[driveRoot.Length..].TrimStart('\\', '/');
        var rootRelativeBrowscap = rootRelativePhp + "\\extras\\browscap.ini";
        var currentIni = Path.Combine(sourceRoot, "php.ini");
        File.WriteAllText(currentIni, $"browscap=\"{rootRelativeBrowscap}\"");

        var migration = new RobustPhpIniMigrationService().Migrate(currentIni, destinationRoot, "8.5.10");
        if (!migration.Migrated || migration.IniPath is null)
            throw new InvalidOperationException("PHP owned-path smoke: robust migration did not complete.");
        if (!File.Exists(Path.Combine(destinationRoot, "extras", "browscap.ini")))
            throw new InvalidOperationException("PHP owned-path smoke: root-relative browscap was not recovered/preserved after directory swap.");

        var migrated = File.ReadAllText(migration.IniPath);
        if (migrated.Contains("disabled missing browscap", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PHP owned-path smoke: updater-generated browscap disable marker remained after reconciliation.");
        if (!migrated.Contains(Path.Combine(destinationRoot, "extras", "browscap.ini"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PHP owned-path smoke: browscap was not rebased to the installed PHP root.");
    }
}
