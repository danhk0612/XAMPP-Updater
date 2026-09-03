using System.Diagnostics;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed record ComponentRollbackResult(
    bool Success,
    bool RestoredOriginalState,
    string? Error,
    IReadOnlyList<string> Steps);

public interface IComponentRollbackService
{
    Task<ComponentRollbackResult> RollbackAsync(
        XamppInstallation installation,
        BackupResult rollbackBackup,
        CancellationToken cancellationToken = default);
}

public sealed class ComponentRollbackService : IComponentRollbackService
{
    private readonly IWindowsServiceController _services;

    public ComponentRollbackService(IWindowsServiceController? services = null)
    {
        _services = services ?? new WindowsServiceController();
    }

    public async Task<ComponentRollbackResult> RollbackAsync(
        XamppInstallation installation,
        BackupResult rollbackBackup,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<string>();
        var manifest = rollbackBackup.Manifest;
        if (!PathsEqual(installation.RootPath, manifest.XamppRoot))
            throw new InvalidOperationException("선택한 롤백 백업이 현재 XAMPP 설치의 백업이 아닙니다.");

        var installed = installation.Components.FirstOrDefault(item => item.Type == manifest.Type)
            ?? throw new InvalidOperationException("현재 구성요소 정보를 찾을 수 없습니다.");
        if (!string.Equals(installed.Version, manifest.TargetVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"현재 {manifest.Type} 버전({installed.Version ?? "Unknown"})과 백업의 업데이트 대상 버전({manifest.TargetVersion})이 일치하지 않습니다.");

        BackupIntegrityVerifier.Verify(rollbackBackup, requireLogicalBackup: manifest.Type == XamppComponentType.MariaDb);
        steps.Add("롤백 백업 manifest/크기/SHA256 검증 완료");

        var componentRoot = manifest.ComponentRoot;
        var parent = Path.GetDirectoryName(componentRoot)
            ?? throw new InvalidOperationException("구성요소 상위 폴더를 확인할 수 없습니다.");
        var displacedRoot = Path.Combine(parent, $".{Path.GetFileName(componentRoot)}-before-rollback-{Guid.NewGuid():N}");
        var serviceName = ResolveServiceName(installation, manifest.Type, manifest.ServiceName);
        var serviceWasRunning = serviceName is not null &&
            string.Equals(_services.GetState(serviceName), "RUNNING", StringComparison.OrdinalIgnoreCase);
        var displaced = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (serviceWasRunning && serviceName is not null)
            {
                _services.Stop(serviceName, TimeSpan.FromSeconds(45));
                steps.Add($"서비스 중지: {serviceName}");
            }

            if (Directory.Exists(displacedRoot)) Directory.Delete(displacedRoot, true);
            if (Directory.Exists(componentRoot))
            {
                Directory.Move(componentRoot, displacedRoot);
                displaced = true;
                steps.Add("현재 프로그램 폴더를 임시 안전 위치로 이동");
            }

            Directory.CreateDirectory(componentRoot);
            RestoreFiles(rollbackBackup, componentRoot, cancellationToken);
            if (manifest.Type == XamppComponentType.Apache && displaced)
                PreserveApacheLogs(displacedRoot, componentRoot);
            steps.Add($"{manifest.Type} {manifest.CurrentVersion} 프로그램 전체 복원 완료");

            await ValidateAsync(manifest.Type, componentRoot, cancellationToken);
            steps.Add("복원된 프로그램 실행/설정 검증 완료");

            if (serviceWasRunning && serviceName is not null)
            {
                _services.Start(serviceName, TimeSpan.FromSeconds(45));
                steps.Add($"서비스 재시작 확인: {serviceName}");
            }

            if (displaced && Directory.Exists(displacedRoot))
                Directory.Delete(displacedRoot, true);
            return new ComponentRollbackResult(true, false, null, steps);
        }
        catch (Exception ex)
        {
            steps.Add("롤백 실패: " + ex.Message);
            var restored = false;
            try
            {
                if (serviceName is not null && string.Equals(_services.GetState(serviceName), "RUNNING", StringComparison.OrdinalIgnoreCase))
                    _services.Stop(serviceName, TimeSpan.FromSeconds(30));
            }
            catch { }

            try
            {
                if (displaced && Directory.Exists(displacedRoot))
                {
                    if (Directory.Exists(componentRoot)) Directory.Delete(componentRoot, true);
                    Directory.Move(displacedRoot, componentRoot);
                    restored = true;
                    steps.Add("롤백 직전 프로그램 폴더로 자동 원복 완료");
                }
                if (serviceWasRunning && serviceName is not null)
                    _services.Start(serviceName, TimeSpan.FromSeconds(45));
            }
            catch (Exception restoreEx)
            {
                steps.Add("롤백 직전 상태 원복 실패: " + restoreEx.Message);
            }
            return new ComponentRollbackResult(false, restored, ex.Message, steps);
        }
    }

    private static void RestoreFiles(BackupResult backup, string destinationRoot, CancellationToken cancellationToken)
    {
        var filesRoot = Path.Combine(backup.Manifest.BackupRoot, "files");
        foreach (var item in backup.Manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = SafeCombine(filesRoot, item.RelativePath);
            var destination = SafeCombine(destinationRoot, item.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
        }
    }

    private static void PreserveApacheLogs(string displacedRoot, string componentRoot)
    {
        var source = Path.Combine(displacedRoot, "logs");
        if (!Directory.Exists(source)) return;
        var target = Path.Combine(componentRoot, "logs");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            try { File.Copy(file, destination, true); } catch { }
        }
    }

    private static async Task ValidateAsync(XamppComponentType type, string componentRoot, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case XamppComponentType.Apache:
                await RunAsync(Path.Combine(componentRoot, "bin", "httpd.exe"), "-t", componentRoot, cancellationToken);
                break;
            case XamppComponentType.Php:
                await RunAsync(Path.Combine(componentRoot, "php.exe"), "-v", componentRoot, cancellationToken);
                break;
            case XamppComponentType.MariaDb:
            {
                var exe = new[] { Path.Combine(componentRoot, "bin", "mariadbd.exe"), Path.Combine(componentRoot, "bin", "mysqld.exe") }
                    .FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("복원된 MariaDB 서버 실행 파일을 찾을 수 없습니다.");
                var config = new[] { Path.Combine(componentRoot, "bin", "my.ini"), Path.Combine(componentRoot, "my.ini"), Path.Combine(componentRoot, "bin", "my.cnf"), Path.Combine(componentRoot, "my.cnf") }
                    .FirstOrDefault(File.Exists);
                var args = config is null ? "--help --verbose" : $"--defaults-file=\"{config}\" --help --verbose";
                await RunAsync(exe, args, Path.GetDirectoryName(exe)!, cancellationToken);
                break;
            }
        }
    }

    private static async Task RunAsync(string executable, string arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("복원 검증 실행 파일을 찾을 수 없습니다.", executable);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            throw;
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"복원 검증 실패 (exit {process.ExitCode}): {stderr}\n{stdout}".Trim());
    }

    private static string? ResolveServiceName(XamppInstallation installation, XamppComponentType type, string? manifestService)
    {
        if (type == XamppComponentType.Php)
            return installation.Components.FirstOrDefault(item => item.Type == XamppComponentType.Apache)?.ServiceName;
        return installation.Components.FirstOrDefault(item => item.Type == type)?.ServiceName ?? manifestService;
    }

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("백업 상대 경로가 허용된 루트를 벗어납니다: " + relative);
        return full;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
