using XamppUpdater.Core.Services;

internal static class DllDependencyAuditSmoke
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "xampp-updater-dll-audit-" + Guid.NewGuid().ToString("N"));
        var php = Path.Combine(root, "php");
        var ext = Path.Combine(php, "ext");
        var apache = Path.Combine(root, "apache");
        var bin = Path.Combine(apache, "bin");
        var modules = Path.Combine(apache, "modules");
        var conf = Path.Combine(apache, "conf");
        try
        {
            foreach (var directory in new[] { php, ext, bin, modules, conf })
                Directory.CreateDirectory(directory);
            void Make(string path, string value = "binary") => File.WriteAllText(path, value);

            Make(Path.Combine(php, "php.exe"));
            Make(Path.Combine(php, "php8apache2_4.dll"));
            Make(Path.Combine(ext, "php_curl.dll"));
            Make(Path.Combine(ext, "php_test.dll"));
            Make(Path.Combine(ext, "php_disabled.dll"));
            Make(Path.Combine(php, "libshared.dll"), "PHP");
            Make(Path.Combine(bin, "libshared.dll"), "APACHE");
            Make(Path.Combine(php, "libsafe.dll"), "PHP SAFE");
            Make(Path.Combine(bin, "libsafe.dll"), "OLDER SAFE");
            Make(Path.Combine(php, "libssl-3-x64.dll"), "PHP SSL");
            Make(Path.Combine(bin, "libssl-3-x64.dll"), "APACHE SSL");
            Make(Path.Combine(bin, "httpd.exe"));
            Make(Path.Combine(modules, "mod_test.so"));
            Make(Path.Combine(conf, "httpd.conf"),
                "LoadModule test_module \"modules/mod_test.so\"\n" +
                "# LoadModule ignored_module \"modules/missing.so\"\n");
            Make(Path.Combine(php, "php.ini"),
                "extension=curl\nextension=php_test.dll\n;extension=disabled\n" +
                "zend_extension=php_missing.dll\n");

            IReadOnlyList<string> Imports(string file) => Path.GetFileName(file).ToLowerInvariant() switch
            {
                "php.exe" => ["KERNEL32.dll"],
                "php8apache2_4.dll" => ["php8ts.dll"],
                "php_curl.dll" => ["libshared.dll", "libsafe.dll", "libssl-3-x64.dll"],
                "php_test.dll" => ["unknown-runtime.dll"],
                "php_disabled.dll" => ["should-never-be-scanned.dll"],
                "mod_test.so" => ["libshared.dll"],
                _ => []
            };
            var report = DllDependencyAuditService.Analyze(php, apache, Imports);
            if (report.PhpRootCount != 4 || report.ApacheRootCount != 2)
                throw new InvalidOperationException("Incorrect active module counts.");
            if (!report.ApacheOwnedDependencies.Contains("libshared.dll") ||
                report.ApacheOwnedDependencies.Contains("libsafe.dll"))
                throw new InvalidOperationException("Apache module dependency ownership was misclassified.");
            if (!report.Findings.Any(x => x.Code == "SHARED_COLLISION" &&
                x.Name == "libshared.dll" && x.Severity == DllAuditSeverity.Warning))
                throw new InvalidOperationException("A shared, mismatching DLL was not blocked.");
            if (!report.Findings.Any(x => x.Code == "SAME_NAME_DIFFERENT_BINARY" &&
                x.Name == "libsafe.dll" && x.Severity == DllAuditSeverity.Warning))
                throw new InvalidOperationException("PHP-only collision warning is missing.");
            if (!report.Findings.Any(x => x.Code == "EXTENSION_NOT_FOUND" &&
                x.Name == "php_missing.dll"))
                throw new InvalidOperationException("Missing enabled PHP extension not reported.");
            if (!report.Findings.Any(x => x.Code == "DEPENDENCY_NOT_RESOLVED" &&
                x.Name == "unknown-runtime.dll"))
                throw new InvalidOperationException("Missing transitive DLL not reported.");
            if (report.Findings.Any(x => x.Name == "should-never-be-scanned.dll"))
                throw new InvalidOperationException("Disabled PHP extension was scanned.");
            if (!report.Summarize("smoke").Any(x => x.Contains("DLL 의존성 진단")))
                throw new InvalidOperationException("DLL audit summary not available.");

            var blockedChange = new ApachePhpDllSyncService.Change("libshared.dll",
                Path.Combine(php, "libshared.dll"), Path.Combine(bin, "libshared.dll"),
                true, "old", "new");
            try
            {
                report.EnsureSafeCurlCopy([blockedChange]);
                throw new InvalidOperationException("Apache-owned DLL was allowed to be overwritten.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("자동 동기화 차단", StringComparison.Ordinal))
            {
            }
            var safeChange = blockedChange with { Name = "libsafe.dll" };
            report.EnsureSafeCurlCopy([safeChange]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
