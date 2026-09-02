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
                "zend_extension=php_xdebug.dll\n" +
                $"browscap=\"{oldBrowscap}\"\n" +
                "session.sid_length=26\n" +
                "session.sid_bits_per_character=5\n");
            File.WriteAllBytes(Path.Combine(extRoot, "php_curl.dll"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(extRoot, "php_bz2.dll"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(newRoot, "php8ts.dll"), Array.Empty<byte>());

            var result = new PhpIniMigrationService().Migrate(oldIni, newRoot);
            if (!result.Migrated || result.IniPath is null)
            {
                throw new InvalidOperationException("Phase 4 smoke: php.ini migration did not run.");
            }

            var migrated = File.ReadAllText(result.IniPath);
            if (!migrated.Contains("extension=php_curl.dll", StringComparison.Ordinal) ||
                !migrated.Contains("extension=php_bz2.dll", StringComparison.Ordinal) ||
                !migrated.Contains("disabled missing/incompatible extension: zend_extension=php_xdebug.dll", StringComparison.Ordinal) ||
                !migrated.Contains("disabled missing browscap file:", StringComparison.Ordinal) ||
                !migrated.Contains("disabled deprecated setting for PHP 8.x: session.sid_length=26", StringComparison.Ordinal) ||
                !migrated.Contains("disabled deprecated setting for PHP 8.x: session.sid_bits_per_character=5", StringComparison.Ordinal) ||
                !File.Exists(Path.Combine(newRoot, "php.ini.xampp-updater-original")))
            {
                throw new InvalidOperationException("Phase 4 smoke: php.ini migration result is incorrect.");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
