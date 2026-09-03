using System.Runtime.CompilerServices;
using XamppUpdater.Core.Services;

internal static class PhpRollbackSapiRegression
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "xampp-updater-php-rollback-" + Guid.NewGuid().ToString("N"));
        try
        {
            var confRoot = Path.Combine(root, "apache", "conf", "extra");
            var phpRoot = Path.Combine(root, "php");
            Directory.CreateDirectory(confRoot);
            Directory.CreateDirectory(phpRoot);

            File.WriteAllText(Path.Combine(phpRoot, "php7apache2_4.dll"), string.Empty);
            File.WriteAllText(Path.Combine(phpRoot, "php7ts.dll"), string.Empty);

            var conf = Path.Combine(confRoot, "httpd-xampp.conf");
            File.WriteAllText(conf,
                "LoadFile \"C:/xampp/php/php8ts.dll\"\r\n" +
                "LoadModule php_module \"C:/xampp/php/php8apache2_4.dll\"\r\n" +
                "<IfModule php_module>\r\nPHPIniDir \"C:/xampp/php\"\r\n</IfModule>\r\n");

            ComponentRollbackService.ReconcileApachePhpSapi(root, phpRoot);
            var updated = File.ReadAllText(conf);
            var expectedModule = Path.Combine(phpRoot, "php7apache2_4.dll").Replace('\\', '/');
            var expectedTs = Path.Combine(phpRoot, "php7ts.dll").Replace('\\', '/');

            if (!updated.Contains("LoadModule php7_module \"" + expectedModule + "\"", StringComparison.OrdinalIgnoreCase) ||
                !updated.Contains("LoadFile \"" + expectedTs + "\"", StringComparison.OrdinalIgnoreCase) ||
                !updated.Contains("<IfModule php7_module>", StringComparison.OrdinalIgnoreCase) ||
                updated.Contains("php8apache2_4.dll", StringComparison.OrdinalIgnoreCase) ||
                updated.Contains("php8ts.dll", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("PHP rollback Apache SAPI regression test failed.\n" + updated);
            }
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }
}
