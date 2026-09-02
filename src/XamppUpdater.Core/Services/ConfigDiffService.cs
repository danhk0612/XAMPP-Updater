using System.IO.Compression;
using System.Security.Cryptography;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IConfigDiffService
{
    ConfigDiffResult Compare(UpdatePreflightReport preflight, PackagePreparationResult package);
}

public sealed class ConfigDiffService : IConfigDiffService
{
    public ConfigDiffResult Compare(UpdatePreflightReport preflight, PackagePreparationResult package)
    {
        if (preflight.Type != package.Type)
        {
            throw new ArgumentException("준비 점검 대상과 패키지 구성요소가 다릅니다.");
        }

        return preflight.Type switch
        {
            XamppComponentType.Apache => CompareApache(preflight, package),
            XamppComponentType.Php => ComparePhp(preflight, package),
            XamppComponentType.MariaDb => CompareMariaDb(preflight, package),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    internal static IReadOnlyDictionary<string, string> ParseIniLike(string text)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var section = string.Empty;

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            var qualified = string.IsNullOrWhiteSpace(section) ? key : $"{section}.{key}";
            if (!values.TryGetValue(qualified, out var list))
            {
                list = new List<string>();
                values[qualified] = list;
            }
            list.Add(value);
        }

        return values.ToDictionary(
            pair => pair.Key,
            pair => string.Join(" | ", pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private static ConfigDiffResult CompareApache(UpdatePreflightReport preflight, PackagePreparationResult package)
    {
        using var archive = ZipFile.OpenRead(package.PackagePath);
        var httpd = archive.Entries.FirstOrDefault(entry => Normalize(entry.FullName).EndsWith("conf/httpd.conf", StringComparison.OrdinalIgnoreCase));
        if (httpd is null)
        {
            return new ConfigDiffResult(preflight.Type, "Apache package conf", Array.Empty<ConfigDiffItem>(),
                new[] { "패키지에서 conf/httpd.conf를 찾지 못했습니다." });
        }

        var normalizedHttpd = Normalize(httpd.FullName);
        var marker = "conf/httpd.conf";
        var prefix = normalizedHttpd[..^marker.Length];

        var target = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Where(entry => Normalize(entry.FullName).StartsWith(prefix + "conf/", StringComparison.OrdinalIgnoreCase))
            .Where(entry => entry.Name.EndsWith(".conf", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                entry => Normalize(entry.FullName)[prefix.Length..],
                HashEntry,
                StringComparer.OrdinalIgnoreCase);

        var confRoot = Path.Combine(preflight.ComponentRoot, "conf");
        var current = Directory.Exists(confRoot)
            ? Directory.EnumerateFiles(confRoot, "*.conf", SearchOption.AllDirectories)
                .ToDictionary(
                    file => "conf/" + Normalize(Path.GetRelativePath(confRoot, file)),
                    HashFile,
                    StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new ConfigDiffResult(preflight.Type, "Apache conf 파일", BuildDiff(current, target), Array.Empty<string>());
    }

    private static ConfigDiffResult ComparePhp(UpdatePreflightReport preflight, PackagePreparationResult package)
    {
        var currentPath = Path.Combine(preflight.ComponentRoot, "php.ini");
        if (!File.Exists(currentPath))
        {
            return new ConfigDiffResult(preflight.Type, "php.ini-production", Array.Empty<ConfigDiffItem>(),
                new[] { "현재 php.ini를 찾지 못했습니다." });
        }

        using var archive = ZipFile.OpenRead(package.PackagePath);
        var baseline = archive.Entries.FirstOrDefault(entry => entry.Name.Equals("php.ini-production", StringComparison.OrdinalIgnoreCase))
                       ?? archive.Entries.FirstOrDefault(entry => entry.Name.Equals("php.ini-development", StringComparison.OrdinalIgnoreCase));
        if (baseline is null)
        {
            return new ConfigDiffResult(preflight.Type, "PHP 기본 ini", Array.Empty<ConfigDiffItem>(),
                new[] { "패키지에서 php.ini-production/development를 찾지 못했습니다." });
        }

        var current = ParseIniLike(File.ReadAllText(currentPath));
        var target = ParseIniLike(ReadEntryText(baseline));
        return new ConfigDiffResult(preflight.Type, baseline.Name, BuildDiff(current, target), Array.Empty<string>());
    }

    private static ConfigDiffResult CompareMariaDb(UpdatePreflightReport preflight, PackagePreparationResult package)
    {
        var currentConfig = preflight.ConfigFiles.FirstOrDefault(item =>
            Path.GetFileName(item.RelativePath).Equals("my.ini", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(item.RelativePath).Equals("my.cnf", StringComparison.OrdinalIgnoreCase));
        if (currentConfig is null)
        {
            return new ConfigDiffResult(preflight.Type, "MariaDB 기본 설정", Array.Empty<ConfigDiffItem>(),
                new[] { "현재 MariaDB 설정 파일을 찾지 못했습니다." });
        }

        var currentPath = Path.Combine(preflight.ComponentRoot, currentConfig.RelativePath);
        using var archive = ZipFile.OpenRead(package.PackagePath);
        var baseline = archive.Entries.FirstOrDefault(entry =>
            entry.Name.Equals("my.ini", StringComparison.OrdinalIgnoreCase) ||
            entry.Name.Equals("my.cnf", StringComparison.OrdinalIgnoreCase));
        if (baseline is null)
        {
            return new ConfigDiffResult(preflight.Type, "MariaDB 기본 설정", Array.Empty<ConfigDiffItem>(),
                new[] { "패키지에 비교 가능한 my.ini/my.cnf 기본 파일이 없습니다. 기존 설정은 별도 보존 대상으로 처리합니다." });
        }

        var current = ParseIniLike(File.ReadAllText(currentPath));
        var target = ParseIniLike(ReadEntryText(baseline));
        return new ConfigDiffResult(preflight.Type, baseline.FullName, BuildDiff(current, target), Array.Empty<string>());
    }

    private static IReadOnlyList<ConfigDiffItem> BuildDiff(
        IReadOnlyDictionary<string, string> current,
        IReadOnlyDictionary<string, string> target)
    {
        var keys = current.Keys.Concat(target.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase);
        var result = new List<ConfigDiffItem>();
        foreach (var key in keys)
        {
            var hasCurrent = current.TryGetValue(key, out var currentValue);
            var hasTarget = target.TryGetValue(key, out var targetValue);
            var kind = (hasCurrent, hasTarget) switch
            {
                (true, true) when string.Equals(currentValue, targetValue, StringComparison.Ordinal) => ConfigDiffKind.Same,
                (true, true) => ConfigDiffKind.Changed,
                (true, false) => ConfigDiffKind.CurrentOnly,
                _ => ConfigDiffKind.TargetOnly
            };
            result.Add(new ConfigDiffItem(key, kind, currentValue, targetValue));
        }
        return result;
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string HashEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Normalize(string value) => value.Replace('\\', '/').TrimStart('/');
}
