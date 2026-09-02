using System.Runtime.CompilerServices;
using XamppUpdater.Core.Services;

internal static class Phase4Smoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xampp-updater-phase4-{Guid.NewGuid():N}");
        try
        {
            var oldRoot = Path.Combine(root, "old");
            var newRoot = Path.Combine(root, "new");
            var extRoot = Path.Combine(newRoot, "ext");
            Directory.CreateDirectory(Path.Combine(oldRoot, "extras"));
            Directory.CreateDirectory(extRoot);

            var oldIni = Path.Combine(oldRoot, "php.ini");
            var oldBrowscap = Path.Combine(oldRoot, "extras", "browscap.ini");
            File.WriteAllText(oldBrowscap, "[browscap]");
            File.WriteAllText(oldIni,
                "memory_limit=512M\n" +
                "extension=php_curl.dll\n" +
                "extension=bz2\n" +
                "extension=php-zip\n" +
                "extension=php7.3-zip\n" +
                "zend_extension=php_xdebug.dll\n" +
                $"browscap=\"{oldBrowscap}\"\n" +
                "session.sid_length=26\n" +
                "session.sid_bits_per_character=5\n");
            File.WriteAllBytes(Path.Combine(extRoot, "php_curl.dll"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(extRoot, "php_bz2.dll"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(extRoot, "php_zip.dll"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(newRoot, "php8ts.dll"), Array.Empty<byte>());

            var result = new PhpIniMigrationService().Migrate(oldIni, newRoot, "8.5.10");
            if (!result.Migrated || result.IniPath is null)
            {
                throw new InvalidOperationException("Phase 4 smoke: php.ini migration did not run.");
            }

            var migrated = File.ReadAllText(result.IniPath);
            if (!migrated.Contains("extension=php_curl.dll", StringComparison.Ordinal) ||
                !migrated.Contains("extension=php_bz2.dll", StringComparison.Ordinal) ||
                migrated.Split("extension=php_zip.dll", StringSplitOptions.None).Length - 1 != 2 ||
                !migrated.Contains("disabled missing/incompatible extension: zend_extension=php_xdebug.dll", StringComparison.Ordinal) ||
                !migrated.Contains("disabled missing browscap file:", StringComparison.Ordinal) ||
                !migrated.Contains("disabled deprecated setting for PHP 8.5.10: session.sid_length=26", StringComparison.Ordinal) ||
                !migrated.Contains("disabled deprecated setting for PHP 8.5.10: session.sid_bits_per_character=5", StringComparison.Ordinal) ||
                !File.Exists(Path.Combine(newRoot, "php.ini.xampp-updater-original")))
            {
                throw new InvalidOperationException("Phase 4 smoke: php.ini migration result is incorrect.");
            }

            AssertEqual("php-zip alias", "zip", PhpIniMigrationService.NormalizeExtensionName("php-zip"));
            AssertEqual("php7.3-zip alias", "zip", PhpIniMigrationService.NormalizeExtensionName("php7.3-zip"));
            AssertEqual("ext-zip alias", "zip", PhpIniMigrationService.NormalizeExtensionName("ext-zip"));

            var stableHtml = "<td><a href='/package/zip/1.22.8'>1.22.8</a></td><td>stable</td>";
            AssertEqual("PECL stable release", "1.22.8", PhpExternalExtensionInstaller.ParseLatestStableRelease(stableHtml));
            var windowsHtml = "<a href='https://downloads.php.net/~windows/pecl/releases/zip/1.22.8/php_zip-1.22.8-8.5-ts-vs17-x64.zip'>8.5 Thread Safe (TS) x64</a>";
            var url = PhpExternalExtensionInstaller.ParseCompatibleWindowsDownload(
                "https://pecl.php.net/package/zip/1.22.8/windows",
                windowsHtml,
                "8.5 Thread Safe (TS) x64");
            if (url is null || !url.EndsWith("php_zip-1.22.8-8.5-ts-vs17-x64.zip", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Phase 4 smoke: PECL Windows download parser failed.");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertEqual(string name, string expected, string? actual)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Phase 4 smoke: {name}: expected '{expected}', actual '{actual ?? "<null>"}'.");
        }
    }
}
