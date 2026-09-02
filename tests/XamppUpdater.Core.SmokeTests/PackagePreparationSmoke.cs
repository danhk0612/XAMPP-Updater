using System.IO.Compression;
using System.Runtime.CompilerServices;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

internal static class PackagePreparationSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var mariaUrl = PackagePreparationService.ResolveMariaDbZipUrl(
            "https://dlm.mariadb.com/browse/mariadb_server/10.4.34/winx64-packages/",
            "<a href=\"mariadb-10.4.34-winx64.zip\">ZIP</a><a href=\"sha256sums.txt\">SHA</a>",
            "10.4.34");
        if (!string.Equals(
                mariaUrl,
                "https://dlm.mariadb.com/browse/mariadb_server/10.4.34/winx64-packages/mariadb-10.4.34-winx64.zip",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"MariaDB ZIP resolver failed: {mariaUrl ?? "<null>"}");
        }

        var root = Path.Combine(Path.GetTempPath(), $"xampp-package-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var phpZip = Path.Combine(root, "php.zip");
            using (var archive = ZipFile.Open(phpZip, ZipArchiveMode.Create))
            {
                using (archive.CreateEntry("php.exe").Open()) { }
                using (archive.CreateEntry("php8apache2_4.dll").Open()) { }
                using (archive.CreateEntry("php.ini-production").Open()) { }
            }

            var php = PackagePreparationService.InspectArchive(
                phpZip,
                XamppComponentType.Php,
                BinaryArchitecture.Unknown,
                requirePhpApacheModule: true);
            if (!php.PhpApacheModulePresent || !php.PayloadEntry.EndsWith("php.exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("PHP package structure inspection failed.");
            }

            var apacheZip = Path.Combine(root, "apache.zip");
            using (var archive = ZipFile.Open(apacheZip, ZipArchiveMode.Create))
            {
                using (archive.CreateEntry("Apache24/bin/httpd.exe").Open()) { }
                using (archive.CreateEntry("Apache24/conf/httpd.conf").Open()) { }
            }

            var apache = PackagePreparationService.InspectArchive(
                apacheZip,
                XamppComponentType.Apache,
                BinaryArchitecture.Unknown,
                requirePhpApacheModule: false);
            if (!apache.PayloadEntry.EndsWith("bin/httpd.exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Apache package structure inspection failed.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
