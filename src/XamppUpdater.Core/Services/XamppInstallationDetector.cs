using Microsoft.Win32;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed class XamppInstallationDetector : IXamppInstallationDetector
{
    private static readonly IReadOnlyDictionary<XamppComponentType, string> ExecutableRelativePaths =
        new Dictionary<XamppComponentType, string>
        {
            [XamppComponentType.Apache] = Path.Combine("apache", "bin", "httpd.exe"),
            [XamppComponentType.Php] = Path.Combine("php", "php.exe"),
            [XamppComponentType.MariaDb] = Path.Combine("mysql", "bin", "mysqld.exe")
        };

    private readonly IComponentVersionDetector _versionDetector;

    public XamppInstallationDetector(IComponentVersionDetector? versionDetector = null)
    {
        _versionDetector = versionDetector ?? new ComponentVersionDetector();
    }

    public IReadOnlyList<string> FindCandidates()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidate(candidates, @"C:\xampp");

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady))
        {
            AddCandidate(candidates, Path.Combine(drive.RootDirectory.FullName, "xampp"));
        }

        foreach (var path in FindFromUninstallRegistry())
        {
            AddCandidate(candidates, path);
        }

        foreach (var service in EnumerateServices())
        {
            if (TryInferRootFromExecutable(service.ExecutablePath, out _, out var rootPath))
            {
                AddCandidate(candidates, rootPath);
            }
        }

        return candidates.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public XamppInstallation Inspect(string rootPath, string discoverySource = "Manual")
    {
        var normalizedRoot = NormalizePath(rootPath);
        if (!Directory.Exists(normalizedRoot))
        {
            throw new DirectoryNotFoundException($"XAMPP 폴더를 찾을 수 없습니다: {normalizedRoot}");
        }

        var services = EnumerateServices().ToArray();
        var components = new List<XamppComponentInfo>();

        foreach (var pair in ExecutableRelativePaths)
        {
            var executablePath = Path.Combine(normalizedRoot, pair.Value);
            var installed = File.Exists(executablePath);
            var serviceName = FindServiceName(services, executablePath);

            if (!installed)
            {
                components.Add(new XamppComponentInfo(pair.Key, false, null, executablePath, serviceName));
                continue;
            }

            try
            {
                var result = _versionDetector.Detect(pair.Key, executablePath);
                components.Add(new XamppComponentInfo(pair.Key, true, result.Version, executablePath, serviceName, result.Detail));
            }
            catch (Exception ex)
            {
                components.Add(new XamppComponentInfo(pair.Key, true, null, executablePath, serviceName, $"버전 확인 실패: {ex.Message}"));
            }
        }

        return new XamppInstallation(normalizedRoot, discoverySource, components);
    }

    private static void AddCandidate(ISet<string> candidates, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var normalized = NormalizePath(path);
            if (Directory.Exists(normalized) && ExecutableRelativePaths.Values.Any(relative => File.Exists(Path.Combine(normalized, relative))))
            {
                candidates.Add(normalized);
            }
        }
        catch (Exception) when (path.Length > 0)
        {
            // 후보 경로 하나가 잘못되어도 다른 감지 경로는 계속 확인한다.
        }
    }

    private static IEnumerable<string> FindFromUninstallRegistry()
    {
        const string uninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = baseKey.OpenSubKey(uninstallPath);
            if (uninstall is null)
            {
                continue;
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var subKey = uninstall.OpenSubKey(subKeyName);
                var displayName = subKey?.GetValue("DisplayName") as string;
                if (displayName?.Contains("XAMPP", StringComparison.OrdinalIgnoreCase) != true)
                {
                    continue;
                }

                if (subKey?.GetValue("InstallLocation") is string installLocation && !string.IsNullOrWhiteSpace(installLocation))
                {
                    yield return installLocation;
                }
            }
        }
    }

    private static IEnumerable<ServiceEntry> EnumerateServices()
    {
        const string servicesPath = @"SYSTEM\CurrentControlSet\Services";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var services = baseKey.OpenSubKey(servicesPath);
            if (services is null)
            {
                continue;
            }

            foreach (var serviceName in services.GetSubKeyNames())
            {
                using var service = services.OpenSubKey(serviceName);
                if (service?.GetValue("ImagePath") is not string imagePath)
                {
                    continue;
                }

                var executablePath = ExtractExecutablePath(imagePath);
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    continue;
                }

                var key = $"{serviceName}\0{executablePath}";
                if (seen.Add(key))
                {
                    yield return new ServiceEntry(serviceName, executablePath);
                }
            }
        }
    }

    private static string? FindServiceName(IEnumerable<ServiceEntry> services, string executablePath)
    {
        var normalizedExpected = NormalizePath(executablePath);
        return services.FirstOrDefault(service => PathsEqual(service.ExecutablePath, normalizedExpected))?.Name;
    }

    private static bool TryInferRootFromExecutable(string executablePath, out XamppComponentType type, out string rootPath)
    {
        foreach (var pair in ExecutableRelativePaths.Where(pair => pair.Key != XamppComponentType.Php))
        {
            var normalizedExecutable = executablePath.Replace('/', '\\');
            var normalizedRelative = pair.Value.Replace('/', '\\');
            if (!normalizedExecutable.EndsWith(normalizedRelative, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rootPath = normalizedExecutable[..^normalizedRelative.Length].TrimEnd('\\');
            type = pair.Key;
            return !string.IsNullOrWhiteSpace(rootPath);
        }

        type = default;
        rootPath = string.Empty;
        return false;
    }

    internal static string ExtractExecutablePath(string imagePath)
    {
        var expanded = Environment.ExpandEnvironmentVariables(imagePath.Trim());
        string executable;

        if (expanded.StartsWith('"'))
        {
            var closingQuote = expanded.IndexOf('"', 1);
            executable = closingQuote > 1 ? expanded[1..closingQuote] : expanded.Trim('"');
        }
        else
        {
            var exeIndex = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            executable = exeIndex >= 0 ? expanded[..(exeIndex + 4)] : expanded.Split(' ', 2)[0];
        }

        executable = executable.Trim().Trim('"');

        if (!Path.HasExtension(executable))
        {
            var withExe = executable + ".exe";
            if (File.Exists(withExe))
            {
                executable = withExe;
            }
        }

        try
        {
            return NormalizePath(executable);
        }
        catch
        {
            return executable;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            var normalizedLeft = NormalizePath(left);
            var normalizedRight = NormalizePath(right);

            if (string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(
                Path.ChangeExtension(normalizedLeft, null),
                Path.ChangeExtension(normalizedRight, null),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim().Trim('"')));
    }

    private sealed record ServiceEntry(string Name, string ExecutablePath);
}
