using System.Runtime.CompilerServices;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

internal static class PreflightSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"xampp-preflight-{Guid.NewGuid():N}");
        try
        {
            var apacheRoot = Path.Combine(root, "apache");
            var bin = Path.Combine(apacheRoot, "bin");
            var conf = Path.Combine(apacheRoot, "conf");
            Directory.CreateDirectory(bin);
            Directory.CreateDirectory(Path.Combine(conf, "extra"));

            var httpd = Path.Combine(bin, "httpd.exe");
            File.WriteAllBytes(httpd, Array.Empty<byte>());
            File.WriteAllText(Path.Combine(conf, "httpd.conf"), "ServerRoot C:/xampp/apache\nListen 80\n");
            File.WriteAllText(Path.Combine(conf, "extra", "httpd-vhosts.conf"), "# vhosts\n");
            File.WriteAllText(Path.Combine(apacheRoot, "README.txt"), "apache");

            var installation = new XamppInstallation(
                root,
                "SmokeTest",
                new[]
                {
                    new XamppComponentInfo(
                        XamppComponentType.Apache,
                        true,
                        "2.4.41",
                        httpd,
                        null)
                });

            var report = new UpdatePreflightService().Inspect(
                installation,
                XamppComponentType.Apache,
                "2.4.68");

            AssertEqual("preflight current", "2.4.41", report.CurrentVersion);
            AssertEqual("preflight target", "2.4.68", report.TargetVersion);
            AssertEqual("preflight config count", "2", report.ConfigFiles.Count.ToString());

            if (report.BackupFileCount < 3)
            {
                throw new InvalidOperationException($"preflight backup file count: expected >= 3, actual {report.BackupFileCount}.");
            }

            if (report.ConfigFiles.Any(item => item.Sha256.Length != 64))
            {
                throw new InvalidOperationException("preflight SHA256 manifest length is invalid.");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void AssertEqual(string name, string expected, string? actual)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual ?? "<null>"}'.");
        }
    }
}
