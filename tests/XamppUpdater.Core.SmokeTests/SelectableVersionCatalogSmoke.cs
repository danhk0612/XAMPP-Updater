using System.Runtime.CompilerServices;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

internal static class SelectableVersionCatalogSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var apache = SelectableVersionCatalogService.ParseApacheArchiveVersions(
            "CHANGES_2.4.41 httpd-2.4.62.tar.bz2 httpd-2.4.68.tar.bz2",
            "2.4.41");
        AssertContains("Apache selectable 2.4.62", apache.Select(item => item.Version), "2.4.62");
        AssertContains("Apache selectable 2.4.68", apache.Select(item => item.Version), "2.4.68");

        var php = SelectableVersionCatalogService.ParsePhpArchiveVersions(
            "php-7.3.33-Win32-VC15-x64.zip " +
            "php-8.4.24-Win32-vs17-x64.zip " +
            "php-8.5.10-Win32-vs17-x64.zip " +
            "php-8.5.10-nts-Win32-vs17-x64.zip",
            "7.3.11",
            BinaryArchitecture.X64,
            requireThreadSafe: true);
        AssertContains("PHP selectable 7.3.33", php.Select(item => item.Version), "7.3.33");
        AssertContains("PHP selectable 8.5.10", php.Select(item => item.Version), "8.5.10");
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
        AssertContains("MariaDB selectable 10.4.34", mariaDb.Select(item => item.Version), "10.4.34");
    }

    private static void AssertContains(string name, IEnumerable<string> values, string expected)
    {
        if (!values.Contains(expected, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{name}: expected '{expected}'.");
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
