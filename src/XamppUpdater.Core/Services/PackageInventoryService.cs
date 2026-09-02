using System.IO.Compression;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public static class PackageInventoryService
{
    public static PackageInventoryResult Compare(
        string xamppRoot,
        string packagePath,
        XamppComponentType type,
        string payloadEntry)
    {
        var componentRoot = type switch
        {
            XamppComponentType.Apache => Path.Combine(xamppRoot, "apache"),
            XamppComponentType.Php => Path.Combine(xamppRoot, "php"),
            XamppComponentType.MariaDb => Path.Combine(xamppRoot, "mysql"),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

        var current = Directory.Exists(componentRoot)
            ? Directory.EnumerateFiles(componentRoot, "*", SearchOption.AllDirectories)
                .Where(file => !ShouldIgnoreCurrent(componentRoot, file, type))
                .Select(file => Normalize(Path.GetRelativePath(componentRoot, file)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var archive = ZipFile.OpenRead(packagePath);
        var prefix = FindArchiveRootPrefix(payloadEntry, type);
        var package = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => Normalize(entry.FullName))
            .Where(entry => entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry[prefix.Length..].TrimStart('/'))
            .Where(entry => entry.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var common = current.Intersect(package, StringComparer.OrdinalIgnoreCase).Count();
        var currentOnly = current.Except(package, StringComparer.OrdinalIgnoreCase).ToArray();
        var packageOnly = package.Except(current, StringComparer.OrdinalIgnoreCase).ToArray();
        var compatibility = type switch
        {
            XamppComponentType.Apache => FindMissingByFolder(current, package, "modules/", ".so"),
            XamppComponentType.Php => FindMissingByFolder(current, package, "ext/", ".dll"),
            _ => Array.Empty<string>()
        };

        return new PackageInventoryResult(
            type,
            current.Count,
            package.Count,
            common,
            currentOnly.Length,
            packageOnly.Length,
            compatibility);
    }

    internal static string FindArchiveRootPrefix(string payloadEntry, XamppComponentType type)
    {
        var normalized = Normalize(payloadEntry);
        var marker = type switch
        {
            XamppComponentType.Apache => "bin/httpd.exe",
            XamppComponentType.Php => "php.exe",
            XamppComponentType.MariaDb when normalized.EndsWith("bin/mariadbd.exe", StringComparison.OrdinalIgnoreCase) => "bin/mariadbd.exe",
            XamppComponentType.MariaDb => "bin/mysqld.exe",
            _ => string.Empty
        };

        if (marker.Length == 0 || !normalized.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return normalized[..^marker.Length];
    }

    private static string[] FindMissingByFolder(
        IReadOnlySet<string> current,
        IReadOnlySet<string> package,
        string folder,
        string extension)
    {
        var targetNames = package
            .Where(path => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileName(path) ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return current
            .Where(path => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileName(path) ?? string.Empty)
            .Where(name => name.Length > 0 && !targetNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ShouldIgnoreCurrent(string componentRoot, string file, XamppComponentType type)
    {
        var relative = Normalize(Path.GetRelativePath(componentRoot, file));
        return type switch
        {
            XamppComponentType.Apache => relative.StartsWith("logs/", StringComparison.OrdinalIgnoreCase),
            XamppComponentType.MariaDb => relative.StartsWith("data/", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string Normalize(string value) => value.Replace('\\', '/').TrimStart('/');
}
