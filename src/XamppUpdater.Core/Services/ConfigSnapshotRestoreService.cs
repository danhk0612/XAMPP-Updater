using System.Diagnostics;
using System.Security.Cryptography;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IConfigSnapshotRestoreService
{
    Task<ConfigSnapshotRestoreResult> RestoreAsync(
        XamppInstallation installation,
        ConfigSnapshotManifest snapshot,
        CancellationToken cancellationToken = default);
}

public sealed record ConfigSnapshotRestoreResult(
    bool Success,
    bool RolledBack,
    string? SafetySnapshotPath,
    IReadOnlyList<string> Steps,
    string? Error = null);

public sealed class ConfigSnapshotRestoreService : IConfigSnapshotRestoreService
{
    private readonly IConfigSnapshotService _snapshots;
    private readonly IWindowsServiceController _services;

    public ConfigSnapshotRestoreService(
        IConfigSnapshotService? snapshots = null,
        IWindowsServiceController? services = null)
    {
        _snapshots = snapshots ?? new ConfigSnapshotService();
        _services = services ?? new WindowsServiceController();
    }

    public async Task<ConfigSnapshotRestoreResult> RestoreAsync(
        XamppInstallation installation,
        ConfigSnapshotManifest snapshot,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<string>();
        ConfigSnapshotManifest? safety = null;
        var serviceName = ResolveServiceName(installation, snapshot.Type);
        var serviceWasRunning = false;
        var serviceStopped = false;

        try
        {
            ValidateSnapshot(installation, snapshot);
            var currentVersion = installation.Components.FirstOrDefault(item => item.Type == snapshot.Type)?.Version;
            safety = _snapshots.Capture(installation.RootPath, snapshot.Type, currentVersion, "BeforeRestore");
            steps.Add("복원 직전 안전 snapshot 저장: " + safety.ManifestPath);

            if (!string.IsNullOrWhiteSpace(serviceName))
            {
                var state = _services.GetState(serviceName);
                serviceWasRunning = state.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
                if (serviceWasRunning)
                {
                    await Task.Run(() => _services.Stop(serviceName, TimeSpan.FromSeconds(30)), cancellationToken);
                    serviceStopped = true;
                    steps.Add("서비스 중지: " + serviceName);
                }
            }

            ApplySnapshot(snapshot);
            steps.Add($"설정 snapshot 적용 완료: {snapshot.Files.Count}개 파일");

            var validation = await ValidateComponentAsync(installation, snapshot.Type, cancellationToken);
            steps.Add(validation);

            if (serviceWasRunning && !string.IsNullOrWhiteSpace(serviceName))
            {
                await Task.Run(() => _services.Start(serviceName, TimeSpan.FromSeconds(30)), cancellationToken);
                serviceStopped = false;
                steps.Add("서비스 재시작 및 RUNNING 확인: " + serviceName);
            }

            return new ConfigSnapshotRestoreResult(true, false, safety.ManifestPath, steps);
        }
        catch (Exception ex)
        {
            var rolledBack = false;
            var error = ex.Message;
            if (safety is not null)
            {
                try
                {
                    ApplySnapshot(safety);
                    steps.Add("복원 실패 후 직전 설정 snapshot 자동 원복 완료");
                    rolledBack = true;
                }
                catch (Exception rollbackEx)
                {
                    error += " / 자동 원복 실패: " + rollbackEx.Message;
                }
            }

            if (serviceWasRunning && !string.IsNullOrWhiteSpace(serviceName))
            {
                try
                {
                    if (!_services.GetState(serviceName).Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Run(() => _services.Start(serviceName, TimeSpan.FromSeconds(30)), CancellationToken.None);
                        steps.Add("서비스 원상복구 완료: " + serviceName);
                    }
                    serviceStopped = false;
                }
                catch (Exception restartEx)
                {
                    error += " / 서비스 원상복구 실패: " + restartEx.Message;
                }
            }

            return new ConfigSnapshotRestoreResult(false, rolledBack, safety?.ManifestPath, steps, error);
        }
        finally
        {
            if (serviceStopped && serviceWasRunning && !string.IsNullOrWhiteSpace(serviceName))
            {
                try { _services.Start(serviceName, TimeSpan.FromSeconds(30)); } catch { }
            }
        }
    }

    private static void ValidateSnapshot(XamppInstallation installation, ConfigSnapshotManifest snapshot)
    {
        var root = Path.GetFullPath(installation.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var snapshotRoot = Path.GetFullPath(snapshot.XamppRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(root, snapshotRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("다른 XAMPP 설치 경로에서 생성한 snapshot은 복원할 수 없습니다.");

        if (!File.Exists(snapshot.ManifestPath))
            throw new FileNotFoundException("snapshot manifest를 찾을 수 없습니다.", snapshot.ManifestPath);

        var filesRoot = Path.Combine(Path.GetDirectoryName(snapshot.ManifestPath)!, "files");
        foreach (var entry in snapshot.Files)
        {
            var source = SafeCombine(filesRoot, entry.RelativePath);
            if (!File.Exists(source))
                throw new FileNotFoundException("snapshot 설정 파일이 없습니다.", source);
            var info = new FileInfo(source);
            if (info.Length != entry.Size)
                throw new InvalidDataException($"snapshot 파일 크기가 manifest와 다릅니다: {entry.RelativePath}");
            using var stream = File.OpenRead(source);
            var sha = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(sha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"snapshot 파일 SHA256이 manifest와 다릅니다: {entry.RelativePath}");
        }
    }

    private static void ApplySnapshot(ConfigSnapshotManifest snapshot)
    {
        var componentRoot = GetComponentRoot(snapshot.XamppRoot, snapshot.Type);
        var filesRoot = Path.Combine(Path.GetDirectoryName(snapshot.ManifestPath)!, "files");
        var wanted = snapshot.Files.Select(item => item.RelativePath.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var current in EnumerateManagedConfigFiles(componentRoot, snapshot.Type))
        {
            var relative = Path.GetRelativePath(componentRoot, current).Replace('\\', '/');
            if (!wanted.Contains(relative)) File.Delete(current);
        }

        foreach (var entry in snapshot.Files)
        {
            var source = SafeCombine(filesRoot, entry.RelativePath);
            var destination = SafeCombine(componentRoot, entry.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }

    private static async Task<string> ValidateComponentAsync(
        XamppInstallation installation,
        XamppComponentType type,
        CancellationToken cancellationToken)
    {
        return type switch
        {
            XamppComponentType.Apache => await RunValidationAsync(
                Path.Combine(installation.RootPath, "apache", "bin", "httpd.exe"),
                "-t",
                Path.Combine(installation.RootPath, "apache"),
                "Apache httpd -t 설정 검증 통과",
                cancellationToken),
            XamppComponentType.Php => await RunValidationAsync(
                Path.Combine(installation.RootPath, "php", "php.exe"),
                "-v",
                Path.Combine(installation.RootPath, "php"),
                "PHP php -v 설정 검증 통과",
                cancellationToken),
            XamppComponentType.MariaDb => await ValidateMariaDbAsync(installation, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    private static async Task<string> ValidateMariaDbAsync(XamppInstallation installation, CancellationToken cancellationToken)
    {
        var mysqlRoot = Path.Combine(installation.RootPath, "mysql");
        var executable = new[]
        {
            Path.Combine(mysqlRoot, "bin", "mariadbd.exe"),
            Path.Combine(mysqlRoot, "bin", "mysqld.exe")
        }.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("MariaDB 서버 실행 파일을 찾을 수 없습니다.");

        var config = new[]
        {
            Path.Combine(mysqlRoot, "bin", "my.ini"),
            Path.Combine(mysqlRoot, "my.ini"),
            Path.Combine(mysqlRoot, "bin", "my.cnf"),
            Path.Combine(mysqlRoot, "my.cnf")
        }.FirstOrDefault(File.Exists);

        var args = config is null
            ? "--help --verbose"
            : $"--defaults-file=\"{config}\" --help --verbose";
        return await RunValidationAsync(executable, args, mysqlRoot, "MariaDB 설정 파싱 검증 통과", cancellationToken);
    }

    private static async Task<string> RunValidationAsync(
        string executable,
        string arguments,
        string workingDirectory,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("검증 실행 파일을 찾을 수 없습니다.", executable);
        var start = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("설정 검증 프로세스를 시작하지 못했습니다.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        var output = (await stdoutTask + Environment.NewLine + await stderrTask).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"설정 검증 실패 (exit={process.ExitCode}): {Compact(output)}");
        return successMessage;
    }

    private static string? ResolveServiceName(XamppInstallation installation, XamppComponentType type)
    {
        if (type == XamppComponentType.Php)
            return installation.Components.FirstOrDefault(item => item.Type == XamppComponentType.Apache)?.ServiceName;
        return installation.Components.FirstOrDefault(item => item.Type == type)?.ServiceName;
    }

    private static string GetComponentRoot(string xamppRoot, XamppComponentType type) => type switch
    {
        XamppComponentType.Apache => Path.Combine(xamppRoot, "apache"),
        XamppComponentType.Php => Path.Combine(xamppRoot, "php"),
        XamppComponentType.MariaDb => Path.Combine(xamppRoot, "mysql"),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static IEnumerable<string> EnumerateManagedConfigFiles(string componentRoot, XamppComponentType type)
    {
        if (!Directory.Exists(componentRoot)) yield break;
        if (type == XamppComponentType.Php)
        {
            var ini = Path.Combine(componentRoot, "php.ini");
            if (File.Exists(ini)) yield return ini;
            yield break;
        }
        if (type == XamppComponentType.MariaDb)
        {
            foreach (var relative in new[] { "my.ini", "my.cnf", "bin/my.ini", "bin/my.cnf" })
            {
                var path = Path.Combine(componentRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) yield return path;
            }
            yield break;
        }

        var confRoot = Path.Combine(componentRoot, "conf");
        if (!Directory.Exists(confRoot)) yield break;
        foreach (var file in Directory.EnumerateFiles(confRoot, "*.conf", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(confRoot, file).Replace('\\', '/');
            if (!relative.StartsWith("original/", StringComparison.OrdinalIgnoreCase)) yield return file;
        }
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("snapshot 상대 경로가 허용된 루트를 벗어납니다: " + relative);
        return full;
    }

    private static string Compact(string value)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 600 ? normalized : normalized[..600] + "...";
    }
}
