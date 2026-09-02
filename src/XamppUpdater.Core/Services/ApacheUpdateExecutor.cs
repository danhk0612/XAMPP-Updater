using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IApacheUpdateExecutor
{
    Task<UpdateExecutionResult> ExecuteAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup,
        CancellationToken cancellationToken = default);
}

public sealed partial class ApacheUpdateExecutor : IApacheUpdateExecutor
{
    private readonly IWindowsServiceController _serviceController;

    public ApacheUpdateExecutor(IWindowsServiceController? serviceController = null)
    {
        _serviceController = serviceController ?? new WindowsServiceController();
    }

    public async Task<UpdateExecutionResult> ExecuteAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup,
        CancellationToken cancellationToken = default)
    {
        if (target.Type != XamppComponentType.Apache || package.Type != XamppComponentType.Apache || backup.Manifest.Type != XamppComponentType.Apache)
            throw new ArgumentException("Apache 업데이트에 필요한 대상/패키지/백업 정보가 아닙니다.");

        var apache = installation.Components.First(item => item.Type == XamppComponentType.Apache);
        var currentVersion = apache.Version ?? backup.Manifest.CurrentVersion;
        ValidateInputs(installation, target, package, backup, currentVersion);

        var steps = new List<string>();
        var warnings = new List<string>();
        var xamppRoot = installation.RootPath;
        var apacheRoot = Path.Combine(xamppRoot, "apache");
        var serviceName = apache.ServiceName;
        var wasRunning = serviceName is not null &&
                         string.Equals(_serviceController.GetState(serviceName), "RUNNING", StringComparison.OrdinalIgnoreCase);
        var unmanagedRunning = serviceName is null && Process.GetProcessesByName("httpd").Length > 0;
        if (unmanagedRunning)
            throw new InvalidOperationException("Apache가 Windows 서비스 등록 없이 실행 중입니다. 안전한 교체를 위해 Apache를 먼저 종료해야 합니다.");

        var token = Guid.NewGuid().ToString("N");
        var stageRoot = Path.Combine(xamppRoot, $".xampp-updater-apache-stage-{token}");
        var extractRoot = Path.Combine(stageRoot, "package");
        var oldRoot = Path.Combine(xamppRoot, $".xampp-updater-apache-old-{token}");
        var swapped = false;
        var stopped = false;

        try
        {
            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(package.PackagePath, extractRoot, overwriteFiles: true);
            var payloadRoot = ResolvePayloadRoot(extractRoot, package.PayloadEntry);
            ValidateStagedApache(payloadRoot, target.Version);
            steps.Add($"Apache {target.Version} 패키지 스테이징 완료");

            if (wasRunning && serviceName is not null)
            {
                await Task.Run(() => _serviceController.Stop(serviceName, TimeSpan.FromSeconds(30)), cancellationToken);
                stopped = true;
                steps.Add($"Apache 서비스 중지: {serviceName}");
            }

            Directory.Move(apacheRoot, oldRoot);
            Directory.Move(payloadRoot, apacheRoot);
            swapped = true;
            steps.Add("Apache 디렉터리 교체 완료");

            PreserveConfiguration(oldRoot, apacheRoot);
            steps.Add("기존 Apache conf 설정 보존 완료");

            var preservedModules = PreserveReferencedModules(oldRoot, apacheRoot);
            if (preservedModules.Count > 0)
            {
                warnings.Add("새 패키지에 없어 기존 설치에서 보존한 Apache 모듈: " + string.Join(", ", preservedModules));
                steps.Add($"참조 모듈 {preservedModules.Count}개 보존");
            }

            PreserveDirectoryIfPresent(oldRoot, apacheRoot, "logs");
            steps.Add("기존 Apache logs 보존 완료");

            var httpd = Path.Combine(apacheRoot, "bin", "httpd.exe");
            var configTest = await RunAsync(httpd, new[] { "-t" }, apacheRoot, cancellationToken);
            if (configTest.ExitCode != 0 ||
                configTest.Output.Contains("Syntax error", StringComparison.OrdinalIgnoreCase) ||
                configTest.Output.Contains("Cannot load", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("새 Apache 구성 검사 실패: " + Compact(configTest.Output));
            }
            steps.Add("httpd -t 구성 검사 완료");

            if (wasRunning && serviceName is not null)
            {
                await Task.Run(() => _serviceController.Start(serviceName, TimeSpan.FromSeconds(30)), cancellationToken);
                stopped = false;
                var state = _serviceController.GetState(serviceName);
                if (!string.Equals(state, "RUNNING", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Apache 서비스 재시작 후 상태가 RUNNING이 아닙니다: {state}");
                steps.Add($"Apache 서비스 재시작 및 RUNNING 확인: {serviceName}");
            }

            var installedVersion = ReadApacheVersion(Path.Combine(apacheRoot, "bin", "httpd.exe"), apacheRoot);
            if (!installedVersion.Contains(target.Version, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("교체 후 Apache 버전 검증 실패: " + installedVersion);
            steps.Add("교체 후 Apache 버전 검증 완료");

            TryDeleteDirectory(oldRoot);
            TryDeleteDirectory(stageRoot);
            return new UpdateExecutionResult(true, false, currentVersion, target.Version, steps, warnings);
        }
        catch (Exception ex)
        {
            var rollbackErrors = new List<string>();
            try
            {
                if (serviceName is not null && string.Equals(_serviceController.GetState(serviceName), "RUNNING", StringComparison.OrdinalIgnoreCase))
                    _serviceController.Stop(serviceName, TimeSpan.FromSeconds(30));
            }
            catch (Exception stopEx)
            {
                rollbackErrors.Add("롤백 전 Apache 중지 실패: " + stopEx.Message);
            }

            if (swapped)
            {
                try
                {
                    TryDeleteDirectory(apacheRoot);
                    if (Directory.Exists(oldRoot)) Directory.Move(oldRoot, apacheRoot);
                    steps.Add("Apache 디렉터리 자동 롤백 완료");
                }
                catch (Exception rollbackEx)
                {
                    rollbackErrors.Add("Apache 디렉터리 롤백 실패: " + rollbackEx.Message);
                }
            }

            if (wasRunning && serviceName is not null)
            {
                try
                {
                    _serviceController.Start(serviceName, TimeSpan.FromSeconds(30));
                    stopped = false;
                    steps.Add("Apache 서비스 원상복구 완료");
                }
                catch (Exception startEx)
                {
                    rollbackErrors.Add("Apache 서비스 원상복구 실패: " + startEx.Message);
                }
            }

            warnings.AddRange(rollbackErrors);
            TryDeleteDirectory(stageRoot);
            return new UpdateExecutionResult(false, swapped, currentVersion, target.Version, steps, warnings, ex.Message);
        }
        finally
        {
            if (stopped && wasRunning && serviceName is not null)
            {
                try { _serviceController.Start(serviceName, TimeSpan.FromSeconds(30)); } catch { }
            }
        }
    }

    private static void ValidateInputs(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup,
        string currentVersion)
    {
        if (!string.Equals(target.Version, package.Version, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("선택 버전과 준비된 Apache 패키지 버전이 다릅니다.");
        if (!string.Equals(target.Version, backup.Manifest.TargetVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("선택 버전과 Apache 백업 manifest의 대상 버전이 다릅니다. 백업을 다시 생성하세요.");
        if (!string.Equals(currentVersion, backup.Manifest.CurrentVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("현재 Apache 버전이 백업 생성 시점과 다릅니다. 백업을 다시 생성하세요.");
        if (!PathsEqual(installation.RootPath, backup.Manifest.XamppRoot))
            throw new InvalidOperationException("백업이 현재 XAMPP 설치에 대한 것이 아닙니다.");
        if (!File.Exists(package.PackagePath))
            throw new FileNotFoundException("준비된 Apache 패키지를 찾을 수 없습니다.", package.PackagePath);
    }

    private static string ResolvePayloadRoot(string extractRoot, string payloadEntry)
    {
        var normalized = payloadEntry.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var httpdPath = Path.Combine(extractRoot, normalized);
        if (!File.Exists(httpdPath)) throw new InvalidDataException("압축 해제 후 httpd.exe를 찾을 수 없습니다.");
        var bin = Path.GetDirectoryName(httpdPath) ?? throw new InvalidDataException("Apache bin 경로를 확인할 수 없습니다.");
        return Directory.GetParent(bin)?.FullName ?? throw new InvalidDataException("Apache 패키지 루트를 확인할 수 없습니다.");
    }

    private static void ValidateStagedApache(string apacheRoot, string targetVersion)
    {
        var httpd = Path.Combine(apacheRoot, "bin", "httpd.exe");
        var output = ReadApacheVersion(httpd, apacheRoot);
        if (!output.Contains(targetVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("스테이징 Apache 버전 검증 실패: " + output);
    }

    private static string ReadApacheVersion(string httpd, string workingDirectory)
    {
        if (!File.Exists(httpd)) throw new FileNotFoundException("httpd.exe를 찾을 수 없습니다.", httpd);
        var result = RunAsync(httpd, new[] { "-v" }, workingDirectory, CancellationToken.None).GetAwaiter().GetResult();
        if (result.ExitCode != 0) throw new InvalidOperationException("httpd -v 실행 실패: " + Compact(result.Output));
        return result.Output;
    }

    private static void PreserveConfiguration(string oldRoot, string newRoot)
    {
        var oldConf = Path.Combine(oldRoot, "conf");
        var newConf = Path.Combine(newRoot, "conf");
        if (!Directory.Exists(oldConf)) return;
        if (Directory.Exists(newConf)) Directory.Delete(newConf, recursive: true);
        CopyDirectory(oldConf, newConf, overwrite: true);
    }

    private static IReadOnlyList<string> PreserveReferencedModules(string oldRoot, string newRoot)
    {
        var result = new List<string>();
        var confRoot = Path.Combine(newRoot, "conf");
        if (!Directory.Exists(confRoot)) return result;

        foreach (var conf in Directory.EnumerateFiles(confRoot, "*.conf", SearchOption.AllDirectories))
        {
            foreach (var raw in File.ReadLines(conf))
            {
                var match = LoadModuleRegex().Match(raw);
                if (!match.Success) continue;
                var configured = match.Groups["path"].Value.Trim().Trim('"', '\'').Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathFullyQualified(configured)) continue;

                var relative = configured;
                var destination = Path.GetFullPath(Path.Combine(newRoot, relative));
                if (File.Exists(destination)) continue;
                var source = Path.GetFullPath(Path.Combine(oldRoot, relative));
                if (!File.Exists(source)) continue;
                if (!IsUnderRoot(source, oldRoot) || !IsUnderRoot(destination, newRoot)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
                result.Add(Path.GetRelativePath(newRoot, destination).Replace('\\', '/'));
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void PreserveDirectoryIfPresent(string oldRoot, string newRoot, string name)
    {
        var source = Path.Combine(oldRoot, name);
        if (!Directory.Exists(source)) return;
        var destination = Path.Combine(newRoot, name);
        if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        Directory.Move(source, destination);
    }

    private static void CopyDirectory(string source, string destination, bool overwrite)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite);
        }
    }

    private static async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = string.Join(Environment.NewLine, new[] { await stdout, await stderr }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new ProcessResult(process.ExitCode, output.Trim());
    }

    private static bool PathsEqual(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private static string Compact(string value) => value.Replace("\r", " ").Replace("\n", " ").Trim();

    private sealed record ProcessResult(int ExitCode, string Output);

    [GeneratedRegex(@"^\s*LoadModule\s+\S+\s+(?<path>[^#]+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LoadModuleRegex();
}
