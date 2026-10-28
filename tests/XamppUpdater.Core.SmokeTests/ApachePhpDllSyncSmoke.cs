using XamppUpdater.Core.Services;

namespace XamppUpdater.Core.SmokeTests;

internal static class ApachePhpDllSyncSmoke
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "xampp-updater-curl-sync-" + Guid.NewGuid().ToString("N"));
        var php = Path.Combine(root, "php");
        var apache = Path.Combine(root, "apache");
        var bin = Path.Combine(apache, "bin");
        var backup = Path.Combine(root, "backup");
        var safety = Path.Combine(root, "safety");
        var names = new[] { "libssh2.dll", "nghttp2.dll", "brotlicommon.dll", "brotlidec.dll" };
        try
        {
            Directory.CreateDirectory(Path.Combine(php, "ext"));
            Directory.CreateDirectory(bin);
            Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(php, "ext", "php_curl.dll"), "curl");
            File.WriteAllText(Path.Combine(php, "libcurl.dll"), "curl dependency");
            File.WriteAllText(Path.Combine(php, "libssl-3-x64.dll"), "PHP OpenSSL");
            File.WriteAllText(Path.Combine(bin, "libssl-3-x64.dll"), "Apache OpenSSL");
            foreach (var name in names)
            {
                File.WriteAllText(Path.Combine(php, name), "NEW " + name);
                File.WriteAllText(Path.Combine(bin, name), "OLD " + name);
            }
            File.WriteAllText(Path.Combine(bin, "not-a-curl-dependency.dll"), "keep");

            IReadOnlyList<string> Imports(string file) => Path.GetFileName(file).ToLowerInvariant() switch
            {
                "php_curl.dll" => ["libcurl.dll"],
                "libcurl.dll" => ["libssh2.dll", "nghttp2.dll", "libssl-3-x64.dll"],
                "libssh2.dll" => ["brotlidec.dll"],
                "brotlidec.dll" => ["brotlicommon.dll"],
                _ => []
            };

            var changes = ApachePhpDllSyncService.Plan(php, apache, Imports);
            if (!changes.Select(item => item.Name).Order(StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(names.Order(StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Incorrect cURL collision detection.");
            ApachePhpDllSyncService.Capture(changes, backup);
            ApachePhpDllSyncService.VerifyBackup(backup);
            ApachePhpDllSyncService.Apply(changes);
            foreach (var name in names)
                Expect(Path.Combine(bin, name), "NEW " + name);

            // Failed later stages must be able to reconstruct the exact Apache bin state.
            var present = ApachePhpDllSyncService.CaptureCurrent(backup, apache, safety);
            ApachePhpDllSyncService.Restore(backup, apache);
            foreach (var name in names)
                Expect(Path.Combine(bin, name), "OLD " + name);
            ApachePhpDllSyncService.RestoreCurrent(backup, apache, safety, present);
            foreach (var name in names)
                Expect(Path.Combine(bin, name), "NEW " + name);

            // If Apache was updated separately, never overwrite its replacement DLL.
            File.WriteAllText(Path.Combine(bin, "nghttp2.dll"), "APACHE-NEW");
            var warnings = ApachePhpDllSyncService.Restore(backup, apache);
            if (warnings.Count != 1 || !warnings[0].Contains("nghttp2.dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Separate Apache update was not preserved.");
            Expect(Path.Combine(bin, "nghttp2.dll"), "APACHE-NEW");
            foreach (var name in names.Where(name => name != "nghttp2.dll"))
                Expect(Path.Combine(bin, name), "OLD " + name);
            Expect(Path.Combine(bin, "libssl-3-x64.dll"), "Apache OpenSSL");
            Expect(Path.Combine(bin, "not-a-curl-dependency.dll"), "keep");

            // Backup corruption must prevent restore.
            File.WriteAllText(Path.Combine(backup, "apache-php-dll-sync", "libssh2.dll"), "damaged");
            try
            {
                ApachePhpDllSyncService.VerifyBackup(backup);
                throw new InvalidOperationException("Corrupted DLL backup was accepted.");
            }
            catch (InvalidDataException) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Expect(string file, string expected)
    {
        if (File.ReadAllText(file) != expected)
            throw new InvalidOperationException("Unexpected DLL contents: " + file);
    }
}
