using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

var failures = new List<string>();

CheckVersion(XamppComponentType.Apache, "Server version: Apache/2.4.65 (Win64)", "2.4.65");
CheckVersion(XamppComponentType.Php, "PHP 8.4.12 (cli) (built: Aug 26 2026 10:00:00)", "8.4.12");
CheckVersion(XamppComponentType.MariaDb, "mysqld  Ver 15.1 Distrib 10.4.32-MariaDB, for Win64 (AMD64)", "10.4.32");
CheckVersion(XamppComponentType.MariaDb, "mariadbd  Ver 11.8.3-MariaDB for Win64 on AMD64", "11.8.3");

CheckServiceImagePath(
    @"\"C:\xampp\mysql\bin\mysqld.exe\" --defaults-file=\"C:\xampp\mysql\bin\my.ini\" mysql",
    @"C:\xampp\mysql\bin\mysqld.exe");
CheckServiceImagePath(
    @"C:\xampp\mysql\bin\mysqld.exe --defaults-file=C:\xampp\mysql\bin\my.ini mysql",
    @"C:\xampp\mysql\bin\mysqld.exe");

var root = Path.Combine(Path.GetTempPath(), $"xampp-updater-smoke-{Guid.NewGuid():N}");
try
{
    var executables = new[]
    {
        Path.Combine(root, "apache", "bin", "httpd.exe"),
        Path.Combine(root, "php", "php.exe"),
        Path.Combine(root, "mysql", "bin", "mysqld.exe")
    };

    foreach (var executable in executables)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllBytes(executable, Array.Empty<byte>());
    }

    CheckServiceImagePath(
        $"{Path.Combine(root, "mysql", "bin", "mysqld")} --defaults-file={Path.Combine(root, "mysql", "bin", "my.ini")} mysql",
        Path.Combine(root, "mysql", "bin", "mysqld.exe"));

    var detector = new XamppInstallationDetector(new FakeVersionDetector());
    var installation = detector.Inspect(root, "SmokeTest");

    if (installation.Components.Count != 3 || installation.Components.Any(component => !component.IsInstalled))
    {
        failures.Add("Manual inspection did not detect all three component executable paths.");
    }

    var versions = installation.Components.ToDictionary(component => component.Type, component => component.Version);
    AssertEqual("manual Apache", "2.4.65", versions[XamppComponentType.Apache]);
    AssertEqual("manual PHP", "8.4.12", versions[XamppComponentType.Php]);
    AssertEqual("manual MariaDB", "10.4.32", versions[XamppComponentType.MariaDb]);
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Smoke tests failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine("All Phase 1 smoke tests passed.");
return 0;

void CheckVersion(XamppComponentType type, string output, string expected)
{
    var actual = ComponentVersionDetector.ParseVersion(type, output);
    AssertEqual($"{type} version parser", expected, actual);
}

void CheckServiceImagePath(string imagePath, string expected)
{
    var actual = XamppInstallationDetector.ExtractExecutablePath(imagePath);
    AssertEqual("service ImagePath parser", Path.GetFullPath(expected), actual);
}

void AssertEqual(string name, string expected, string? actual)
{
    if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
    {
        failures.Add($"{name}: expected '{expected}', actual '{actual ?? "<null>"}'.");
    }
}

sealed class FakeVersionDetector : IComponentVersionDetector
{
    public ComponentVersionResult Detect(XamppComponentType type, string executablePath)
    {
        var version = type switch
        {
            XamppComponentType.Apache => "2.4.65",
            XamppComponentType.Php => "8.4.12",
            XamppComponentType.MariaDb => "10.4.32",
            _ => null
        };

        return new ComponentVersionResult(version, string.Empty);
    }
}
