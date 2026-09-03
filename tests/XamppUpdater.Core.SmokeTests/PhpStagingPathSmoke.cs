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
    }
}
