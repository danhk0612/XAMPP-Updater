using System.Diagnostics;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

/// <summary>
/// Adds a final Apache/PHP integration gate to normal component rollback.
/// The pre-rollback component directory is kept aside until the restored pair passes validation.
/// </summary>
public sealed class IntegrationCheckedComponentRollbackService : IComponentRollbackService
{
    private readonly IComponentRollbackService _inner;
    private readonly IApachePhpIntegrationValidator _integrationValidator;
    private readonly IWindowsServiceController _services;

    public IntegrationCheckedComponentRollbackService(
        IComponentRollbackService? inner = null,
        IApachePhpIntegrationValidator? integrationValidator = null,
        IWindowsServiceController? services = null)
    {
        _services = services ?? new WindowsServiceController();
        _inner = inner ?? new ComponentRollbackService(_services);
        _integrationValidator = integrationValidator ?? new ApachePhpIntegrationValidator(_services);
    }

    public async Task<ComponentRollbackResult> RollbackAsync(
        XamppInstallation installation,
        BackupResult rollbackBackup,
        CancellationToken cancellationToken = default)
    {
        var type = rollbackBackup.Manifest.Type;
        if (type is not (XamppComponentType.Apache or XamppComponentType.Php))
            return await _inner.RollbackAsync(installation, rollbackBackup, cancellationToken);

        var steps = new List<string>();
        var componentRoot = rollbackBackup.Manifest.ComponentRoot;
        if (!Directory.Exists(componentRoot))
            return await _inner.RollbackAsync(installation, rollbackBackup, cancellationToken);

        var apache = installation.Components.FirstOrDefault(item => item.Type == XamppComponentType.Apache);
        var apacheServiceName = apache?.ServiceName;
        var apacheWasRunning = !string.IsNullOrWhiteSpace(apacheServiceName) &&
            string.Equals(_services.GetState(apacheServiceName), "RUNNING", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(apacheServiceName) && Process.GetProcessesByName("httpd").Length > 0)
            throw new InvalidOperationException("Apache가 서비스 등록 없이 실행 중입니다. 안전한 Apache/PHP 롤백을 위해 Apache를 먼저 종료해야 합니다.");
        if (type == XamppComponentType.Php && Process.GetProcessesByName("php").Length > 0)
            throw new InvalidOperationException("php.exe 프로세스가 실행 중입니다. 실행 중인 PHP CLI 작업을 종료한 뒤 다시 시도하세요.");

        var parent = Path.GetDirectoryName(componentRoot)
            ?? throw new InvalidOperationException("롤백 대상 구성요소 상위 폴더를 확인할 수 없습니다.");
        var safetyRoot = Path.Combine(parent, $".{Path.GetFileName(componentRoot)}-integration-safety-{Guid.NewGuid():N}");
        var apachePhpConfigs = type == XamppComponentType.Php
            ? SnapshotApachePhpConfigs(installation.RootPath)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var moved = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (apacheWasRunning && apacheServiceName is not null)
            {
                _services.Stop(apacheServiceName, TimeSpan.FromSeconds(45));
                steps.Add($"Apache 서비스 중지: {apacheServiceName}");
            }

            Directory.Move(componentRoot, safetyRoot);
            moved = true;
            steps.Add("연동 검증 완료 전까지 롤백 직전 프로그램 폴더를 안전 위치에 보관");

            // Apache logs are intentionally excluded from updater backups. Recreate a temporary
            // Apache root carrying the current logs so the inner rollback service can preserve
            // them into the restored Apache tree before it runs httpd -t.
            if (type == XamppComponentType.Apache)
            {
                Directory.CreateDirectory(Path.Combine(componentRoot, "logs"));
                PreserveApacheLogs(safetyRoot, componentRoot);
                steps.Add("Apache 검증 전에 logs 디렉터리와 기존 로그 보존 준비 완료");
            }

            var innerResult = await _inner.RollbackAsync(installation, rollbackBackup, cancellationToken);
            steps.AddRange(innerResult.Steps);
            if (!innerResult.Success)
            {
                RestoreSafetyState(componentRoot, safetyRoot, apachePhpConfigs, steps);
                moved = false;
                if (apacheWasRunning && apacheServiceName is not null)
                    _services.Start(apacheServiceName, TimeSpan.FromSeconds(45));
                return new ComponentRollbackResult(false, true, innerResult.Error, steps);
            }

            if (type == XamppComponentType.Apache)
                PreserveApacheLogs(safetyRoot, componentRoot);

            if (apacheWasRunning && apacheServiceName is not null)
            {
                _services.Start(apacheServiceName, TimeSpan.FromSeconds(45));
                steps.Add($"Apache 서비스 재시작: {apacheServiceName}");
            }

            var integration = await _integrationValidator.ValidateAsync(
                installation,
                requireApacheRunning: apacheWasRunning,
                cancellationToken);
            steps.AddRange(integration.Steps);
            steps.AddRange(integration.Warnings.Select(warning => "연동 검증 경고: " + warning));
            steps.Add("Apache/PHP 공통 연동 검증 완료");

            if (Directory.Exists(safetyRoot)) Directory.Delete(safetyRoot, true);
            moved = false;
            return new ComponentRollbackResult(true, false, null, steps);
        }
        catch (Exception ex)
        {
            steps.Add("Apache/PHP 연동 검증 또는 롤백 실패: " + ex.Message);
            try
            {
                if (apacheServiceName is not null &&
                    string.Equals(_services.GetState(apacheServiceName), "RUNNING", StringComparison.OrdinalIgnoreCase))
                {
                    _services.Stop(apacheServiceName, TimeSpan.FromSeconds(30));
                }
            }
            catch { }

            var restored = false;
            try
            {
                if (moved && Directory.Exists(safetyRoot))
                {
                    RestoreSafetyState(componentRoot, safetyRoot, apachePhpConfigs, steps);
                    moved = false;
                    restored = true;
                }
                if (apacheWasRunning && apacheServiceName is not null)
                    _services.Start(apacheServiceName, TimeSpan.FromSeconds(45));
            }
            catch (Exception restoreEx)
            {
                steps.Add("롤백 직전 상태 원복 실패: " + restoreEx.Message);
            }

            return new ComponentRollbackResult(false, restored, ex.Message, steps);
        }
        finally
        {
            if (moved && Directory.Exists(safetyRoot))
            {
                try { Directory.Delete(safetyRoot, true); } catch { }
            }
        }
    }

    private static Dictionary<string, string> SnapshotApachePhpConfigs(string xamppRoot)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var confRoot = Path.Combine(xamppRoot, "apache", "conf");
        if (!Directory.Exists(confRoot)) return result;
        foreach (var file in Directory.EnumerateFiles(confRoot, "*.conf", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (text.Contains("php", StringComparison.OrdinalIgnoreCase)) result[file] = text;
        }
        return result;
    }

    private static void RestoreSafetyState(
        string componentRoot,
        string safetyRoot,
        IReadOnlyDictionary<string, string> apachePhpConfigs,
        ICollection<string> steps)
    {
        if (Directory.Exists(componentRoot)) Directory.Delete(componentRoot, true);
        Directory.Move(safetyRoot, componentRoot);
        foreach (var pair in apachePhpConfigs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)!);
            File.WriteAllText(pair.Key, pair.Value);
        }
        steps.Add("Apache/PHP 연동 실패 후 롤백 직전 프로그램/설정 상태로 자동 원복 완료");
    }

    private static void PreserveApacheLogs(string safetyRoot, string restoredRoot)
    {
        var source = Path.Combine(safetyRoot, "logs");
        if (!Directory.Exists(source)) return;
        var target = Path.Combine(restoredRoot, "logs");
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relative));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            try { File.Copy(file, destination, true); } catch { }
        }
    }
}
