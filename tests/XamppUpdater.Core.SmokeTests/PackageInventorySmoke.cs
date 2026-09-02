using System.IO.Compression;
using System.Runtime.CompilerServices;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

internal static class PackageInventorySmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xampp-inventory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var php = Path.Combine(root, "php");
            Directory.CreateDirectory(Path.Combine(php, "ext"));
            File.WriteAllText(Path.Combine(php, "php.exe"), "x");
            File.WriteAllText(Path.Combine(php, "ext", "php_curl.dll"), "x");
            File.WriteAllText(Path.Combine(php, "ext", "php_legacy.dll"), "x");

            var zip = Path.Combine(root, "php.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (archive.CreateEntry("php.exe").Open()) { }
                using (archive.CreateEntry("ext/php_curl.dll").Open()) { }
                using (archive.CreateEntry("ext/php_mbstring.dll").Open()) { }
            }

            var result = PackageInventoryService.Compare(root, zip, XamppComponentType.Php, "php.exe");
            if (result.CommonFiles != 2 || result.CurrentOnlyFiles != 1 || result.PackageOnlyFiles != 1)
            {
                throw new InvalidOperationException("Package inventory counts are invalid.");
            }
            if (!result.CompatibilityItems.Contains("php_legacy.dll", StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Missing PHP extension was not detected.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
