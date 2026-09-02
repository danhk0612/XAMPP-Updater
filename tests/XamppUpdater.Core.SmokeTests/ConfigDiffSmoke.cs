using System.IO.Compression;
using System.Runtime.CompilerServices;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

internal static class ConfigDiffSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var parsed = ConfigDiffService.ParseIniLike("memory_limit=128M\nextension=curl\nextension=mbstring\n[Date]\ndate.timezone=UTC\n");
        if (!parsed.TryGetValue("extension", out var extensions) || !extensions.Contains("curl") || !extensions.Contains("mbstring"))
        {
            throw new InvalidOperationException("INI duplicate-key parser failed.");
        }
        if (!parsed.TryGetValue("Date.date.timezone", out var timezone) || timezone != "UTC")
        {
            throw new InvalidOperationException("INI section parser failed.");
        }

        var root = Path.Combine(Path.GetTempPath(), $"xampp-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var phpRoot = Path.Combine(root, "php");
            Directory.CreateDirectory(phpRoot);
            File.WriteAllText(Path.Combine(phpRoot, "php.ini"), "memory_limit=128M\ndisplay_errors=On\n");

            var zip = Path.Combine(root, "php.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("php.ini-production");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("memory_limit=256M\nerror_reporting=E_ALL\n");
            }

            var preflight = new UpdatePreflightReport(
                XamppComponentType.Php, "7.3.11", "8.5.10", phpRoot, false, null, null, 0, 0,
                Array.Empty<PreflightConfigFile>(), Array.Empty<string>(), Path.Combine(root, "backup"));
            var package = new PackagePreparationResult(
                XamppComponentType.Php, "8.5.10", "test", "test", zip, "php.zip", new FileInfo(zip).Length,
                "TEST", BinaryArchitecture.Unknown, "php.exe", 1, true, Array.Empty<string>());
            var diff = new ConfigDiffService().Compare(preflight, package);
            if (diff.Changed != 1 || diff.CurrentOnly != 1 || diff.TargetOnly != 1)
            {
                throw new InvalidOperationException(
                    $"PHP config diff counts invalid: changed={diff.Changed}, currentOnly={diff.CurrentOnly}, targetOnly={diff.TargetOnly}");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
