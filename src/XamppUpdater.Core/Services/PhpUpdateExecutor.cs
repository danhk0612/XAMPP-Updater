using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IPhpUpdateExecutor
{
    Task<UpdateExecutionResult> ExecuteAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup,
        CancellationToken cancellationToken = default);
}

public sealed partial class PhpUpdateExecutor : IPhpUpdateExecutor
{
    private readonly IWindowsServiceController _serviceController;
    private readonly IPhpIniMigrationService _iniMigrationService;

    public PhpUpdateExecutor(
        IWindowsServiceController? serviceController = null,
        IPhpIniMigrationService? iniMigrationService = null)
    {
        _serviceController = serviceController ?? new WindowsServiceController();
        _iniMigrationService = iniMigrationService ?? new PhpIniMigrationService();
    }

    public async Task<UpdateExecutionResult> ExecuteAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup,
        CancellationToken cancellationToken = default)
    {
        if (target.Type != XamppComponentType.Php || package.Type != XamppComponentType.Php || backup.Manifest.Type != XamppComponentType.Php)
        {
            throw new ArgumentException("PHP 업데이트에 필요한 대상/패키지/백업 정보가 아닙니다.");
        }

        var php = installation.Components.First(item => item.Type == XamppComponentType.Php);
        var currentVersion = php.Version ?? backup.Manifest.CurrentVersion;
        ValidateInputs(installation, target, package, backup, currentVersion);

        var steps = new List<string>();
        var warnings = new List<string>();
        var xamppRoot = installation.RootPath;
        var phpRoot = Path.Combine(xamppRoot, "php");
        var apacheRoot = Path.Combine(xamppRoot, "apache");
        var apacheExecutable = Path.Combine(apacheRoot, "bin", "httpd.exe");
        var apache = installation.Components.FirstOrDefault(item => item.Type == XamppComponentType.Apache);
        var apacheServiceName = apache?.ServiceName;
        var apacheWasRunning = apacheServiceName is not null &&
                               string.Equals(_serviceController.GetState(apacheServiceName), "RUNNING", StringComparison.OrdinalIgnoreCase);
        var unmanagedApacheRunning = apacheServiceName is null && Process.GetProcessesByName("httpd").Length > 0;

        if (unmanagedApacheRunning)
        {
            throw new InvalidOperationException("Apache가 서비스 등록 없이 실행 중입니다. 안전한 PHP DLL 교체를 위해 Apache를 먼저 종료해야 합니다.");
        }

        if (Process.GetProcessesByName("php").Length > 0)
        {
            throw new InvalidOperationException("php.exe 프로세스가 실행 중입니다. 실행 중인 PHP CLI 작업을 종료한 뒤 다시 시도하세요.");
        }

        var token = Guid.NewGuid().ToString("N");
        var stagingRoot = Path.Combine(xamppRoot, $".xampp-updater-php-stage-{token}");
        var extractedRoot = Path.Combine(stagingRoot, "package");
        var configBackupRoot = Path.Combine(stagingRoot, "apache-conf");
        var oldPhpRoot = Path.Combine(xamppRoot, $".xampp-updater-php-old-{token}");
        var apacheSnapshots = new List<(string Source, string Snapshot)>();
        var apacheStopped = false;
        var phpSwapped = false;

        try
        {
            Directory.CreateDirectory(extractedRoot);
            ZipFile.ExtractToDirectory(package.PackagePath, extractedRoot, overwriteFiles: true);
            var payloadRoot = ResolvePayloadRoot(extractedRoot, package.PayloadEntry);
            ValidateStagedPhp(payloadRoot, target.Version);
            steps.Add($"PHP {target.Version} 패키지 스테이징 완료");

            apacheSnapshots.AddRange(SnapshotApachePhpConfigs(apacheRoot, configBackupRoot));

            if (apacheWasRunning && apacheServiceName is not null)
            {
                await Task.Run(() => _serviceController.Stop(apacheServiceName, TimeSpan.FromSeconds(30)), cancellationToken);
                apacheStopped = true;
                steps.Add($"Apache 서비스 중지: {apacheServiceName}");
            }

            Directory.Move(phpRoot, oldPhpRoot);
            Directory.Move(payloadRoot, phpRoot);
            phpSwapped = true;
            steps.Add("PHP 디렉터리 교체 완료");

            var currentIni = Path.Combine(oldPhpRoot, "php.ini");
            var iniResult = _iniMigrationService.Migrate(currentIni, phpRoot);
            warnings.AddRange(iniResult.Warnings);
            if (iniResult.Migrated)
            {
                steps.Add("php.ini 마이그레이션 완료");
            }

            var sapiWarnings = MigrateApachePhpSapi(apacheRoot, phpRoot);
            warnings.AddRange(sapiWarnings);
            steps.Add("Apache PHP SAPI 설정 갱신 완료");

            ValidateInstalledPhp(phpRoot);
            steps.Add("php -v / php -m 검증 완료");

            if (File.Exists(apacheExecutable))
            {
                var apacheTest = await RunAsync(apacheExecutable, new[] { "-t" }, apacheRoot, cancellationToken);
                if (apacheTest.ExitCode != 0 || apacheTest.Output.Contains("Syntax error", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Apache 구성 검사 실패: " + Compact(apacheTest.Output));
                }
                steps.Add("httpd -t 구성 검사 완료");
            }

            if (apacheWasRunning && apacheServiceName is not null)
            {
                await Task.Run(() => _serviceController.Start(apacheServiceName, TimeSpan.FromSeconds(30)), cancellationToken);
                apacheStopped = false;
                steps.Add($"Apache 서비스 재시작: {apacheServiceName}");
            }

            TryDeleteDirectory(oldPhpRoot);
            TryDeleteDirectory(stagingRoot);
            return new UpdateExecutionResult(true, false, currentVersion, target.Version, steps, warnings);
        }
        catch (Exception ex)
        {
            var rollbackErrors = new List<string>();
            try
            {
                if (apacheServiceName is not null && string.Equals(_serviceController.GetState(apacheServiceName), "RUNNING", StringComparison.OrdinalIgnoreCase))
                {
                    _serviceController.Stop(apacheServiceName, TimeSpan.FromSeconds(30));
                }
            }
            catch (Exception stopEx)
            {
                rollbackErrors.Add("롤백 전 Apache 중지 실패: " + stopEx.Message);
            }

            if (phpSwapped)
            {
                try
                {
                    TryDeleteDirectory(phpRoot);
                    if (Directory.Exists(oldPhpRoot)) Directory.Move(oldPhpRoot, phpRoot);
                    steps.Add("PHP 디렉터리 자동 롤백 완료");
                }
                catch (Exception rollbackEx)
                {
                    rollbackErrors.Add("PHP 디렉터리 롤백 실패: " + rollbackEx.Message);
                }
            }

            foreach (var snapshot in apacheSnapshots)
            {
                try
                {
                    File.Copy(snapshot.Snapshot, snapshot.Source, overwrite: true);
                }
                catch (Exception restoreEx)
                {
                    rollbackErrors.Add($"Apache 설정 롤백 실패 ({snapshot.Source}): {restoreEx.Message}");
                }
            }

            if (apacheWasRunning && apacheServiceName is not null)
            {
                try
                {
                    _serviceController.Start(apacheServiceName, TimeSpan.FromSeconds(30));
                    apacheStopped = false;
                    steps.Add("Apache 서비스 원상복구 완료");
                }
                catch (Exception startEx)
                {
                    rollbackErrors.Add("Apache 서비스 원상복구 실패: " + startEx.Message);
                }
            }

            warnings.AddRange(rollbackErrors);
            TryDeleteDirectory(stagingRoot);
            return new UpdateExecutionResult(false, phpSwapped, currentVersion, target.Version, steps, warnings, ex.Message);
        }
        finally
        {
            if (apacheStopped && apacheWasRunning && apacheServiceName is not null)
            {
                try { _serviceController.Start(apacheServiceName, TimeSpan.FromSeconds(30)); } catch { }
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
            throw new InvalidOperationException("선택 버전과 준비된 패키지 버전이 다릅니다.");
        if (!string.Equals(target.Version, backup.Manifest.TargetVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("선택 버전과 백업 manifest의 대상 버전이 다릅니다. 백업을 다시 생성하세요.");
        if (!string.Equals(currentVersion, backup.Manifest.CurrentVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("현재 PHP 버전이 백업 생성 시점과 다릅니다. 백업을 다시 생성하세요.");
        if (!string.Equals(Path.GetFullPath(installation.RootPath), Path.GetFullPath(backup.Manifest.XamppRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("백업이 현재 XAMPP 설치에 대한 것이 아닙니다.");
        if (!File.Exists(package.PackagePath))
            throw new FileNotFoundException("준비된 PHP 패키지를 찾을 수 없습니다.", package.PackagePath);
        if (!File.Exists(backup.ManifestPath))
            throw new FileNotFoundException("롤백 manifest를 찾을 수 없습니다.", backup.ManifestPath);
    }

    private static string ResolvePayloadRoot(string extractedRoot, string payloadEntry)
    {
        var normalized = payloadEntry.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var payloadFile = Path.Combine(extractedRoot, normalized);
        if (!File.Exists(payloadFile))
            throw new InvalidDataException("압축 해제 후 php.exe를 찾을 수 없습니다.");
        return Path.GetDirectoryName(payloadFile) ?? extractedRoot;
    }

    private static void ValidateStagedPhp(string phpRoot, string targetVersion)
    {
        var phpExe = Path.Combine(phpRoot, "php.exe");
        if (!File.Exists(phpExe)) throw new InvalidDataException("스테이징 패키지에 php.exe가 없습니다.");
        var result = Run(phpExe, new[] { "-n", "-v" }, phpRoot);
        if (result.ExitCode != 0 || !result.Output.Contains($"PHP {targetVersion}", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("스테이징 PHP 버전 검증 실패: " + Compact(result.Output));
    }

    private static void ValidateInstalledPhp(string phpRoot)
    {
        var phpExe = Path.Combine(phpRoot, "php.exe");
        foreach (var args in new[] { new[] { "-v" }, new[] { "-m" } })
        {
            var result = Run(phpExe, args, phpRoot);
            if (result.ExitCode != 0 ||
                result.Output.Contains("PHP Startup: Unable to load dynamic library", StringComparison.OrdinalIgnoreCase) ||
                result.Output.Contains("Unable to load dynamic library", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("새 PHP 실행 검증 실패: " + Compact(result.Output));
            }
        }
    }

    private static IReadOnlyList<(string Source, string Snapshot)> SnapshotApachePhpConfigs(string apacheRoot, string snapshotRoot)
    {
        var result = new List<(string, string)>();
        var confRoot = Path.Combine(apacheRoot, "conf");
        if (!Directory.Exists(confRoot)) return result;
        foreach (var file in Directory.EnumerateFiles(confRoot, "*.conf", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("php", StringComparison.OrdinalIgnoreCase)) continue;
            var relative = Path.GetRelativePath(confRoot, file);
            var snapshot = Path.Combine(snapshotRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            File.Copy(file, snapshot, overwrite: true);
            result.Add((file, snapshot));
        }
        return result;
    }

    private static IReadOnlyList<string> MigrateApachePhpSapi(string apacheRoot, string phpRoot)
    {
        var warnings = new List<string>();
        var moduleDll = Directory.EnumerateFiles(phpRoot, "php*apache2_4.dll", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (moduleDll is null)
        {
            warnings.Add("새 PHP 패키지에서 Apache 2.4 module DLL을 찾지 못했습니다.");
            return warnings;
        }

        var phpMajor = ParseMajor(Path.GetFileName(moduleDll));
        var newModule = phpMajor >= 8 ? "php_module" : $"php{phpMajor}_module";
        var phpTs = Directory.EnumerateFiles(phpRoot, "php*ts.dll", SearchOption.TopDirectoryOnly).FirstOrDefault();
        var confRoot = Path.Combine(apacheRoot, "conf");
        if (!Directory.Exists(confRoot)) return warnings;

        foreach (var file in Directory.EnumerateFiles(confRoot, "*.conf", SearchOption.AllDirectories))
        {
            var original = File.ReadAllText(file);
            if (!original.Contains("php", StringComparison.OrdinalIgnoreCase)) continue;
            var oldModules = PhpModuleTokenRegex().Matches(original).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var updated = PhpLoadModuleRegex().Replace(original, $"LoadModule {newModule} \"{ToApachePath(moduleDll)}\"");
            if (phpTs is not null)
            {
                updated = PhpLoadFileRegex().Replace(updated, $"LoadFile \"{ToApachePath(phpTs)}\"");
            }
            foreach (var old in oldModules)
            {
                updated = Regex.Replace(updated, $@"\b{Regex.Escape(old)}\b", newModule, RegexOptions.IgnoreCase);
            }
            if (!string.Equals(original, updated, StringComparison.Ordinal)) File.WriteAllText(file, updated);
        }
        return warnings;
    }

    private static int ParseMajor(string fileName)
    {
        var match = Regex.Match(fileName, @"php(?<major>\d+)apache", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["major"].Value, out var major) ? major : 8;
    }

    private static string ToApachePath(string path) => path.Replace('\\', '/');

    private static ProcessResult Run(string executable, IReadOnlyList<string> arguments, string workingDirectory) =>
        RunAsync(executable, arguments, workingDirectory, CancellationToken.None).GetAwaiter().GetResult();

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
        var output = string.Join(Environment.NewLine, new[] { await stdout, await stderr }.Where(text => !string.IsNullOrWhiteSpace(text)));
        return new ProcessResult(process.ExitCode, output.Trim());
    }

    private static string Compact(string text) => text.Replace("\r", " ").Replace("\n", " ").Trim();

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private sealed record ProcessResult(int ExitCode, string Output);

    [GeneratedRegex(@"php(?:\d+)?_module", RegexOptions.IgnoreCase)]
    private static partial Regex PhpModuleTokenRegex();

    [GeneratedRegex(@"(?im)^\s*LoadModule\s+php(?:\d+)?_module\s+[^\r\n]+php\d*apache2_4\.dll[^\r\n]*$")]
    private static partial Regex PhpLoadModuleRegex();

    [GeneratedRegex(@"(?im)^\s*LoadFile\s+[^\r\n]+php\d+ts\.dll[^\r\n]*$")]
    private static partial Regex PhpLoadFileRegex();
}
