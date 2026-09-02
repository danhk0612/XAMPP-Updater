using System.Runtime.CompilerServices;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

internal static class SelectableVersionCatalogSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var apache = SelectableVersionCatalogService.ParseApacheArchiveVersions(
            "httpd-2.2.34.tar.bz2 httpd-2.2.35.tar.bz2 httpd-2.4.62.tar.bz2 httpd-2.4.68.tar.bz2",
            "2.2.10");
        AssertContains("Apache 2.2.x latest", apache.Select(item => item.Version), "2.2.35");
        AssertContains("Apache 2.4.x latest", apache.Select(item => item.Version), "2.4.68");
        AssertNotContains("Apache older patch filtered", apache.Select(item => item.Version), "2.4.62");

        var php = SelectableVersionCatalogService.ParsePhpArchiveVersions(
            "php-7.3.33-Win32-VC15-x64.zip " +
            "php-8.3.24-Win32-vs16-x64.zip " +
            "php-8.3.27-Win32-vs16-x64.zip " +
            "php-8.4.24-Win32-vs17-x64.zip " +
            "php-8.4.28-Win32-vs17-x64.zip " +
            "php-8.5.10-Win32-vs17-x64.zip " +
            "php-8.5.10-nts-Win32-vs17-x64.zip",
            "7.3.11",
            BinaryArchitecture.X64,
            requireThreadSafe: true);
        AssertContains("PHP 7.3.x latest", php.Select(item => item.Version), "7.3.33");
        AssertContains("PHP 8.3.x latest", php.Select(item => item.Version), "8.3.27");
        AssertContains("PHP 8.4.x latest", php.Select(item => item.Version), "8.4.28");
        AssertContains("PHP 8.5.x latest", php.Select(item => item.Version), "8.5.10");
        AssertNotContains("PHP older patch filtered", php.Select(item => item.Version), "8.3.24");
        var phpLatest = php.First(item => item.Version == "8.5.10");
        AssertEqual("PHP latest package", "php-8.5.10-Win32-vs17-x64.zip", phpLatest.PackageFileName);

        var series = SelectableVersionCatalogService.ParseMariaDbSeries(
            "Community Server 10.4 (EOL) Community Server 10.11 Community Server 11.4 Community Server 12.3",
            "10.4.8");
        AssertContains("MariaDB series 10.4", series.Select(item => item.Series), "10.4");
        AssertContains("MariaDB series 12.3", series.Select(item => item.Series), "12.3");

        var mariaDb = SelectableVersionCatalogService.ParseMariaDbSeriesVersions(
            "Community Server 10.4.8 Community Server 10.4.32 Community Server 10.4.34",
            new SelectableVersionCatalogService.MariaDbSeriesEntry("10.4", true),
            "10.4.8",
            BinaryArchitecture.X64);
        AssertEqual("MariaDB one latest per series", "1", mariaDb.Count.ToString());
        AssertContains("MariaDB selectable 10.4.34", mariaDb.Select(item => item.Version), "10.4.34");
    }

    private static void AssertContains(string name, IEnumerable<string> values, string expected)
    {
        if (!values.Contains(expected, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{name}: expected '{expected}'.");
        }
    }

    private static void AssertNotContains(string name, IEnumerable<string> values, string unexpected)
    {
        if (values.Contains(unexpected, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{name}: unexpected '{unexpected}'.");
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
