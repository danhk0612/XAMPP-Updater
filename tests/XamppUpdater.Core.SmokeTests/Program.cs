using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

var failures = new List<string>();

CheckVersion(XamppComponentType.Apache, "Server version: Apache/2.4.65 (Win64)", "2.4.65");
CheckVersion(XamppComponentType.Php, "PHP 8.4.12 (cli) (built: Aug 26 2026 10:00:00)", "8.4.12");
CheckVersion(XamppComponentType.MariaDb, "mysqld  Ver 15.1 Distrib 10.4.32-MariaDB, for Win64 (AMD64)", "10.4.32");
CheckVersion(XamppComponentType.MariaDb, "mariadbd  Ver 11.8.3-MariaDB for Win64 on AMD64", "11.8.3");

AssertEqual(
    "Apache online parser",
    "2.4.68",
    OnlineVersionCatalogService.ParseApacheLatest("<h1>Apache HTTP Server 2.4.68 (httpd)</h1>"));
AssertEqual(
    "PHP online parser",
    "8.5.10",
    OnlineVersionCatalogService.ParsePhpLatest("<h3>PHP 8.5 (8.5.10)</h3><h3>PHP 8.4 (8.4.17)</h3>"));
AssertEqual(
    "MariaDB online parser",
    "12.3.3",
    OnlineVersionCatalogService.ParseMariaDbLatest("<h3>Latest GA Versions</h3><div>MariaDB Community Server 12.3.3</div>"));

var bundle = OnlineVersionCatalogService.ParseXamppLatestBundle(
    "<div>XAMPP for Windows 8.0.30</div><div>XAMPP for Windows 8.2.12</div>" +
    "<div>Includes: Apache 2.4.58, MariaDB 10.4.32, PHP 8.2.12</div>");
AssertEqual("XAMPP Apache parser", "2.4.58", bundle.Apache);
AssertEqual("XAMPP PHP parser", "8.2.12", bundle.Php);
AssertEqual("XAMPP MariaDB parser", "10.4.32", bundle.MariaDb);

var phpProfile = InstallationCompatibilityDetector.ParsePhpInfo(
    "Thread Safety => enabled\n" +
    "Compiler => MSVC15 (Visual C++ 2017)\n" +
    "PHP Extension Build => API20180731,TS,VC15\n" +
    "PHP API => 20180731\n");
AssertEqual("PHP thread safety", "True", phpProfile.ThreadSafe?.ToString());
AssertEqual("PHP compiler", "MSVC15 (Visual C++ 2017)", phpProfile.Compiler);
AssertEqual("PHP extension build", "API20180731,TS,VC15", phpProfile.ExtensionBuild);
AssertEqual("PHP API", "20180731", phpProfile.ApiVersion);

var integration = InstallationCompatibilityDetector.ParseApachePhpIntegration(
    @"C:\xampp\apache\conf\extra\httpd-xampp.conf",
    "LoadModule php7_module \"C:/xampp/php/php7apache2_4.dll\"\nPHPIniDir \"C:/xampp/php\"");
AssertEqual("Apache PHP module detected", "True", integration.IsModuleLoaded.ToString());
AssertEqual("Apache PHP module name", "php7_module", integration.ModuleName);
AssertEqual("Apache PHP module path", "C:/xampp/php/php7apache2_4.dll", integration.ModulePath);

var compatibilityProfile = new InstallationCompatibilityProfile(
    @"C:\xampp",
    BinaryArchitecture.X64,
    BinaryArchitecture.X64,
    BinaryArchitecture.X64,
    phpProfile,
    integration,
    "10.4");

var phpOnline = new OnlineComponentVersion(
    XamppComponentType.Php,
    "8.5.10",
    "8.3.12",
    "https://www.php.net/downloads.php?os=windows",
    "");
var phpCompatibility = CompatibilityEvaluator.Evaluate(XamppComponentType.Php, "7.3.11", phpOnline, compatibilityProfile);
AssertContains("PHP major upgrade compatibility", phpCompatibility, "메이저 변경");

var mariaDbOnline = new OnlineComponentVersion(
    XamppComponentType.MariaDb,
    "12.3.3",
    "10.4.32",
    "https://mariadb.com/downloads/",
    "");
var mariaDbCompatibility = CompatibilityEvaluator.Evaluate(XamppComponentType.MariaDb, "10.4.8", mariaDbOnline, compatibilityProfile);
AssertContains("MariaDB patch compatibility", mariaDbCompatibility, "패치 업데이트 후보");

CheckServiceImagePath(
    "\"C:\\xampp\\mysql\\bin\\mysqld.exe\" --defaults-file=\"C:\\xampp\\mysql\\bin\\my.ini\" mysql",
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

Console.WriteLine("All smoke tests passed.");
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

void AssertContains(string name, string actual, string expectedPart)
{
    if (!actual.Contains(expectedPart, StringComparison.Ordinal))
    {
        failures.Add($"{name}: expected text containing '{expectedPart}', actual '{actual}'.");
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
