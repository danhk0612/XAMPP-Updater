using System.Diagnostics;
using System.Security.Cryptography;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IUpdatePreflightService
{
    UpdatePreflightReport Inspect(
        XamppInstallation installation,
        XamppComponentType type,
        string targetVersion);
}

public sealed class UpdatePreflightService : IUpdatePreflightService
{
    public UpdatePreflightReport Inspect(
        XamppInstallation installation,
        XamppComponentType type,
        string targetVersion)
    {
        var component = installation.Components.First(item => item.Type == type);
        var currentVersion = component.Version ?? "unknown";
        var componentRoot = GetComponentRoot(installation.RootPath, type);
        var warnings = new List<string>();

        if (!Directory.Exists(componentRoot))
        {
            warnings.Add($"구성요소 폴더를 찾을 수 없습니다: {componentRoot}");
        }

        var processName = Path.GetFileNameWithoutExtension(component.ExecutablePath);
        var processRunning = !string.IsNullOrWhiteSpace(processName) &&
                             Process.GetProcessesByName(processName).Length > 0;

        var serviceState = component.ServiceName is null
            ? null
            : WindowsServiceStateReader.Read(component.ServiceName);

        var files = Directory.Exists(componentRoot)
            ? EnumerateBackupFiles(componentRoot, type).ToArray()
            : Array.Empty<string>();

        long totalBytes = 0;
        foreach (var file in files)
        {
            try
            {
                totalBytes += new FileInfo(file).Length;
            }
            catch
            {
                warnings.Add($"파일 크기 확인 실패: {Path.GetRelativePath(componentRoot, file)}");
            }
        }

        var configs = Directory.Exists(componentRoot)
            ? EnumerateConfigFiles(componentRoot, type)
                .Select(file => BuildConfigFile(componentRoot, file, warnings))
                .Where(item => item is not null)
                .Cast<PreflightConfigFile>()
                .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<PreflightConfigFile>();

        if (processRunning || IsRunningServiceState(serviceState))
        {
            warnings.Add("현재 구성요소가 실행 중입니다. 실제 교체 단계에서는 백업 직전에 안전하게 중지해야 합니다.");
        }

        if (type == XamppComponentType.MariaDb)
        {
            var dataPath = Path.Combine(componentRoot, "data");
            if (Directory.Exists(dataPath))
            {
                warnings.Add("MariaDB data 디렉터리는 바이너리 백업과 별도로 보호하며, 계열 변경 시 논리 백업도 함께 생성하는 것이 필요합니다.");
            }
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupDestination = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XamppUpdater",
            "Backups",
            timestamp,
            type.ToString());

        return new UpdatePreflightReport(
            type,
            currentVersion,
            targetVersion,
            componentRoot,
            processRunning,
            component.ServiceName,
            serviceState,
            totalBytes,
            files.Length,
            configs,
            warnings,
            backupDestination);
    }

    internal static IEnumerable<string> EnumerateBackupFiles(string componentRoot, XamppComponentType type)
    {
        if (!Directory.Exists(componentRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(componentRoot, "*", SearchOption.AllDirectories);
    }

    internal static IEnumerable<string> EnumerateConfigFiles(string componentRoot, XamppComponentType type)
    {
        if (!Directory.Exists(componentRoot))
        {
            return Array.Empty<string>();
        }

        var files = type switch
        {
            XamppComponentType.Apache => Directory.EnumerateFiles(
                Path.Combine(componentRoot, "conf"),
                "*.conf",
                SearchOption.AllDirectories),
            XamppComponentType.Php => Directory.EnumerateFiles(
                componentRoot,
                "*.ini*",
                SearchOption.TopDirectoryOnly),
            XamppComponentType.MariaDb => EnumerateMariaDbConfigs(componentRoot),
            _ => Array.Empty<string>()
        };

        return files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateMariaDbConfigs(string componentRoot)
    {
        var candidates = new[]
        {
            Path.Combine(componentRoot, "bin", "my.ini"),
            Path.Combine(componentRoot, "my.ini"),
            Path.Combine(componentRoot, "bin", "my.cnf"),
            Path.Combine(componentRoot, "my.cnf")
        };

        return candidates.Where(File.Exists);
    }

    private static string GetComponentRoot(string xamppRoot, XamppComponentType type)
    {
        return type switch
        {
            XamppComponentType.Apache => Path.Combine(xamppRoot, "apache"),
            XamppComponentType.Php => Path.Combine(xamppRoot, "php"),
            XamppComponentType.MariaDb => Path.Combine(xamppRoot, "mysql"),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    private static PreflightConfigFile? BuildConfigFile(
        string componentRoot,
        string file,
        ICollection<string> warnings)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            var info = new FileInfo(file);
            return new PreflightConfigFile(
                Path.GetRelativePath(componentRoot, file),
                info.Length,
                hash);
        }
        catch (Exception ex)
        {
            warnings.Add($"설정 파일 manifest 생성 실패: {Path.GetRelativePath(componentRoot, file)} ({ex.Message})");
            return null;
        }
    }

    private static bool IsRunningServiceState(string? state)
    {
        return string.Equals(state, "RUNNING", StringComparison.OrdinalIgnoreCase);
    }
}
