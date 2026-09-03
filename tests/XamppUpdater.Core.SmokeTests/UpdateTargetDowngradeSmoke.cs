using System.Runtime.CompilerServices;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

internal static class UpdateTargetDowngradeSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var installation = new XamppInstallation(
            @"C:\xampp",
            "Smoke",
            new[]
            {
                new XamppComponentInfo(XamppComponentType.Apache, true, "2.4.68", @"C:\xampp\apache\bin\httpd.exe", "Apache2.4"),
                new XamppComponentInfo(XamppComponentType.Php, true, "8.5.10", @"C:\xampp\php\php.exe", null),
                new XamppComponentInfo(XamppComponentType.MariaDb, true, "12.3.3", @"C:\xampp\mysql\bin\mariadbd.exe", "mysql")
            });

        var online = new OnlineVersionCatalog(
            DateTimeOffset.Now,
            new[]
            {
                new OnlineComponentVersion(XamppComponentType.Apache, "2.4.70", "2.4.62", "", ""),
                new OnlineComponentVersion(XamppComponentType.Php, "8.6.1", "8.3.12", "", ""),
                new OnlineComponentVersion(XamppComponentType.MariaDb, "12.4.1", "10.4.32", "", "")
            });

        var candidates = new CandidatePackageCatalog(
            DateTimeOffset.Now,
            new[]
            {
                Candidate(XamppComponentType.Apache, "2.4.70"),
                Candidate(XamppComponentType.Php, "8.6.1"),
                Candidate(XamppComponentType.MariaDb, "12.4.1")
            });

        var selectable = new SelectableVersionCatalog(
            DateTimeOffset.Now,
            new[]
            {
                new SelectableVersionEntry(XamppComponentType.Apache, "2.4.62", "old", null, null),
                new SelectableVersionEntry(XamppComponentType.Apache, "2.4.69", "new", null, null),
                new SelectableVersionEntry(XamppComponentType.Php, "8.4.20", "old", null, null),
                new SelectableVersionEntry(XamppComponentType.Php, "8.6.0", "new", null, null),
                new SelectableVersionEntry(XamppComponentType.MariaDb, "11.8.6", "old", null, null),
                new SelectableVersionEntry(XamppComponentType.MariaDb, "12.4.0", "new", null, null),
                new SelectableVersionEntry(XamppComponentType.MariaDb, "13.0.0", "EOL test", null, null, true)
            });

        var catalog = UpdateTargetPlanner.BuildCatalog(installation, online, candidates, selectable);
        AssertOnlyHigher("Apache", new Version(2, 4, 68), catalog.Apache);
        AssertOnlyHigher("PHP", new Version(8, 5, 10), catalog.Php);
        AssertOnlyHigher("MariaDB", new Version(12, 3, 3), catalog.MariaDb);

        if (catalog.MariaDb.Any(item => item.IsEol))
            throw new InvalidOperationException("MariaDB EOL target must not be selectable.");
    }

    private static PackageCandidate Candidate(XamppComponentType type, string version) =>
        new(type, version, null, null, BinaryArchitecture.X64, null, null, null, null,
            CandidateCompatibilityStatus.Assisted, "smoke");

    private static void AssertOnlyHigher(string name, Version current, IReadOnlyList<UpdateTargetOption> options)
    {
        if (options.Count == 0)
            throw new InvalidOperationException($"{name}: expected at least one upgrade target.");

        foreach (var option in options)
        {
            if (!Version.TryParse(option.Version, out var parsed) || parsed <= current)
                throw new InvalidOperationException($"{name}: downgrade/same target leaked into selector: {option.Version}.");
        }
    }
}
