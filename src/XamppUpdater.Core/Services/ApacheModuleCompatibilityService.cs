using System.Text.RegularExpressions;

namespace XamppUpdater.Core.Services;

public sealed record ApacheModuleCompatibilityResult(
    IReadOnlyList<string> PreservedModules,
    IReadOnlyList<string> PreservedDependencies,
    IReadOnlyList<string> UnresolvedDependencies)
{
    public bool Success => UnresolvedDependencies.Count == 0;
}

public interface IApacheModuleCompatibilityService
{
    ApacheModuleCompatibilityResult Prepare(string sourceApacheRoot, string targetApacheRoot, string targetConfRoot);
}

public sealed partial class ApacheModuleCompatibilityService : IApacheModuleCompatibilityService
{
    public ApacheModuleCompatibilityResult Prepare(string sourceApacheRoot, string targetApacheRoot, string targetConfRoot)
    {
        var modules = new List<string>();
        var dependencies = new List<string>();
        var unresolved = new List<string>();

        if (!Directory.Exists(targetConfRoot))
            return new ApacheModuleCompatibilityResult(modules, dependencies, unresolved);

        foreach (var conf in Directory.EnumerateFiles(targetConfRoot, "*.conf", SearchOption.AllDirectories)
                     .Where(path => IsActiveConfig(targetConfRoot, path)))
        {
            foreach (var raw in File.ReadLines(conf))
            {
                var match = LoadModuleRegex().Match(raw);
                if (!match.Success) continue;

                var configured = match.Groups["path"].Value.Trim().Trim('"', '\'').Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathFullyQualified(configured)) continue;

                var targetModule = SafeCombine(targetApacheRoot, configured);
                if (targetModule is null) continue;

                if (!File.Exists(targetModule))
                {
                    var sourceModule = SafeCombine(sourceApacheRoot, configured);
                    if (sourceModule is null || !File.Exists(sourceModule))
                    {
                        unresolved.Add($"모듈 파일 없음: {configured.Replace('\\', '/')}");
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(targetModule)!);
                    File.Copy(sourceModule, targetModule, overwrite: true);
                    modules.Add(Path.GetRelativePath(targetApacheRoot, targetModule).Replace('\\', '/'));
                }

                PreserveDependencies(sourceApacheRoot, targetApacheRoot, targetModule, dependencies, unresolved);
            }
        }

        return new ApacheModuleCompatibilityResult(
            modules.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            dependencies.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            unresolved.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void PreserveDependencies(
        string sourceApacheRoot,
        string targetApacheRoot,
        string modulePath,
        ICollection<string> preserved,
        ICollection<string> unresolved)
    {
        // Re-evaluate after each copy because one preserved runtime DLL can itself expose another dependency.
        for (var pass = 0; pass < 6; pass++)
        {
            var searchDirectories = GetDependencySearchDirectories(targetApacheRoot, modulePath);
            var missing = PeDependencyInspector.FindMissingDependencies(modulePath, searchDirectories);
            var copiedAny = false;

            foreach (var item in missing)
            {
                if (item.DependencyName.StartsWith("[Windows 로더 오류", StringComparison.OrdinalIgnoreCase))
                {
                    unresolved.Add($"{Path.GetFileName(modulePath)}: {item.DependencyName}");
                    continue;
                }

                var source = PeDependencyInspector.FindAnywhere(sourceApacheRoot, item.DependencyName);
                if (source is null)
                {
                    unresolved.Add($"{Path.GetFileName(modulePath)} → {item.DependencyName}");
                    continue;
                }

                var destination = Path.Combine(targetApacheRoot, "bin", item.DependencyName);
                if (File.Exists(destination)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
                preserved.Add("bin/" + item.DependencyName);
                copiedAny = true;
            }

            if (!copiedAny) break;
            unresolved.Clear();
        }

        var finalMissing = PeDependencyInspector.FindMissingDependencies(modulePath, GetDependencySearchDirectories(targetApacheRoot, modulePath));
        foreach (var item in finalMissing)
        {
            unresolved.Add(item.DependencyName.StartsWith("[Windows 로더 오류", StringComparison.OrdinalIgnoreCase)
                ? $"{Path.GetFileName(modulePath)}: {item.DependencyName}"
                : $"{Path.GetFileName(modulePath)} → {item.DependencyName}");
        }
    }

    private static IReadOnlyList<string> GetDependencySearchDirectories(string apacheRoot, string module)
    {
        var result = new List<string>
        {
            Path.GetDirectoryName(module) ?? apacheRoot,
            Path.Combine(apacheRoot, "bin"),
            apacheRoot,
            Environment.SystemDirectory
        };
        if (Environment.Is64BitOperatingSystem)
            result.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"));
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
            result.AddRange(pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return result.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsActiveConfig(string confRoot, string path)
    {
        var relative = Path.GetRelativePath(confRoot, path).Replace('\\', '/');
        return !relative.StartsWith("original/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SafeCombine(string root, string relative)
    {
        try
        {
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
            return full.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                   full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? full
                : null;
        }
        catch
        {
            return null;
        }
    }

    [GeneratedRegex(@"^\s*LoadModule\s+\S+\s+(?<path>[^#]+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LoadModuleRegex();
}
