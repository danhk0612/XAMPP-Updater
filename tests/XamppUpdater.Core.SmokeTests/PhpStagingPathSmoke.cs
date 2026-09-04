using System.Runtime.CompilerServices;
using XamppUpdater.Core.Services;

internal static class PhpStagingPathSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var finalRoot = Path.Combine(Path.GetTempPath(), "xampp", "php");
        var stagingRoot = Path.Combine(Path.GetTempPath(), "xampp-stage", "php");
        var input =
            $"extension_dir=\"{Path.Combine(finalRoot, "ext")}\"{Environment.NewLine}" +
            $"browscap=\"{Path.Combine(finalRoot, "extras", "browscap.ini")}\"{Environment.NewLine}" +
            "extension=php_curl.dll";

        var rewritten = PhpStagingIniPathRewriter.RewriteText(input, finalRoot, stagingRoot);
        if (rewritten.Contains(finalRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PHP staging path smoke: final PHP root remained in validation ini.");
        if (!rewritten.Contains(Path.Combine(stagingRoot, "ext"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PHP staging path smoke: extension_dir was not rebased to staging PHP root.");
        if (!rewritten.Contains(Path.Combine(stagingRoot, "extras", "browscap.ini"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PHP staging path smoke: browscap path was not rebased to staging PHP root.");
        if (!rewritten.Contains("extension=php_curl.dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PHP staging path smoke: non-path php.ini directive changed unexpectedly.");

        if (OperatingSystem.IsWindows())
        {
            const string windowsFinalRoot = @"C:\xampp\php";
            const string windowsStageRoot = @"C:\xampp\.xampp-updater-php-stage-test\package";
            var rootRelative =
                "extension_dir=\"\\xampp\\php\\ext\"\r\n" +
                "include_path=\".;\\xampp\\php\\PEAR\"\r\n" +
                "extension=php_openssl.dll";
            var windowsRewritten = PhpStagingIniPathRewriter.RewriteText(rootRelative, windowsFinalRoot, windowsStageRoot);

            if (windowsRewritten.Contains(@"\xampp\php\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PHP staging path smoke: drive-root-relative XAMPP PHP path remained in validation ini.");
            if (!windowsRewritten.Contains(windowsStageRoot + @"\ext", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PHP staging path smoke: drive-root-relative extension_dir was not rebased.");
            if (!windowsRewritten.Contains(windowsStageRoot + @"\PEAR", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PHP staging path smoke: drive-root-relative include_path was not rebased.");
        }
    }
}
