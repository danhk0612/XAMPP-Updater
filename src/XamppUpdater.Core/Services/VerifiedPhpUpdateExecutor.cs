using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed class VerifiedPhpUpdateExecutor : IPhpUpdateExecutor
{
    private readonly IPhpUpdateExecutor _inner;
    private readonly IConfigSnapshotService _snapshots;
    private readonly IApachePhpIntegrationValidator _integrationValidator;
    private readonly IComponentRollbackService _rollbackService;
    private readonly IWindowsServiceController _services;

    public VerifiedPhpUpdateExecutor(
        IPhpUpdateExecutor? inner = null,
        IConfigSnapshotService? snapshots = null,
        IApachePhpIntegrationValidator? integrationValidator = null,
        IComponentRollbackService? rollbackService = null,
        IWindowsServiceController? services = null)
    {
        _services = services ?? new WindowsServiceController();
        _inner = inner ?? new PhpUpdateExecutor();
        _snapshots = snapshots ?? new ConfigSnapshotService();
        _integrationValidator = integrationValidator ?? new ApachePhpIntegrationValidator(_services);
        _rollbackService = rollbackService ?? new IntegrationCheckedComponentRollbackService(services: _services);
    }

    public async Task<UpdateExecutionResult> ExecuteAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup,
        CancellationToken cancellationToken = default)
    {
        UpdateProgressReporter.Report(XamppComponentType.Php, UpdateProgressStages.BackupVerify, "롤백 백업 무결성을 확인하는 중...", 10);
        BackupIntegrityVerifier.Verify(backup);
        UpdateProgressReporter.Report(XamppComponentType.Php, UpdateProgressStages.BackupVerify, "롤백 백업 무결성 확인 완료", 20);

        var apacheServiceName = installation.Components.FirstOrDefault(item => item.Type == XamppComponentType.Apache)?.ServiceName;
        var apacheWasRunning = !string.IsNullOrWhiteSpace(apacheServiceName) &&
            string.Equals(_services.GetState(apacheServiceName), "RUNNING", StringComparison.OrdinalIgnoreCase);

        var snapshotWarnings = new List<string>();
        UpdateProgressReporter.Report(XamppComponentType.Php, UpdateProgressStages.BeforeSnapshot, "업데이트 전 설정 snapshot을 저장하는 중...", 25);
        try
        {
            var before = _snapshots.Capture(installation.RootPath, XamppComponentType.Php, backup.Manifest.CurrentVersion, "BeforeUpdate");
            snapshotWarnings.Add("업데이트 전 설정 snapshot: " + before.ManifestPath);
        }
        catch (Exception ex) { snapshotWarnings.Add("업데이트 전 설정 snapshot 저장 실패: " + ex.Message); }

        UpdateProgressReporter.Report(XamppComponentType.Php, UpdateProgressStages.Execute, "PHP 교체·설정 마이그레이션·런타임 검증을 진행하는 중...", 35);
        var result = await UpdateExecutionWatchdog.ExecuteAsync(
            token => _inner.ExecuteAsync(installation, target, package, backup, token),
            TimeSpan.FromMinutes(10),
            cancellationToken);

        if (result.Success)
        {
            try
            {
                UpdateProgressReporter.Report(XamppComponentType.Php, UpdateProgressStages.Execute, "현재 Apache와 PHP 연동을 최종 검증하는 중...", 85);
                var integration = await _integrationValidator.ValidateAsync(installation, apacheWasRunning, cancellationToken);
                result = result with
                {
                    Steps = result.Steps.Concat(integration.Steps).Concat(new[] { "Apache/PHP 공통 연동 검증 완료" }).ToArray(),
                    Warnings = result.Warnings.Concat(integration.Warnings).ToArray()
                };
            }
            catch (Exception integrationEx)
            {
                var installedState = installation with
                {
                    Components = installation.Components
                        .Select(component => component.Type == XamppComponentType.Php
                            ? component with { Version = target.Version }
                            : component)
                        .ToArray()
                };
                var rollback = await _rollbackService.RollbackAsync(installedState, backup, cancellationToken);
                result = result with
                {
                    Success = false,
                    RolledBack = rollback.Success,
                    Steps = result.Steps
                        .Concat(new[] { "Apache/PHP 연동 검증 실패: " + integrationEx.Message })
                        .Concat(rollback.Steps)
                        .ToArray(),
                    Warnings = result.Warnings
                        .Concat(new[] { "Apache/PHP 연동 검증 실패로 PHP 변경만 원복했습니다: " + integrationEx.Message })
                        .ToArray(),
                    Error = rollback.Success
                        ? integrationEx.Message
                        : integrationEx.Message + " / 자동 원복 실패: " + rollback.Error
                };
            }
        }

        if (!result.Success)
        {
            UpdateProgressReporter.Report(
                XamppComponentType.Php,
                result.RolledBack ? UpdateProgressStages.Rollback : UpdateProgressStages.Failed,
                result.RolledBack ? "업데이트 또는 Apache/PHP 연동 검증 실패 후 기존 PHP로 자동 롤백했습니다." : "PHP 업데이트가 중단되었습니다.",
                result.RolledBack ? 90 : null,
                result.RolledBack);
        }
        else
        {
            UpdateProgressReporter.Report(XamppComponentType.Php, UpdateProgressStages.AfterSnapshot, "업데이트 후 설정 snapshot을 저장하는 중...", 90);
            try
            {
                var after = _snapshots.Capture(installation.RootPath, XamppComponentType.Php, target.Version, "AfterUpdate");
                snapshotWarnings.Add("업데이트 후 설정 snapshot: " + after.ManifestPath);
            }
            catch (Exception ex) { snapshotWarnings.Add("업데이트 후 설정 snapshot 저장 실패: " + ex.Message); }
            UpdateProgressReporter.Report(XamppComponentType.Php, UpdateProgressStages.Completed, $"PHP {target.Version} 업데이트 완료", 100);
        }

        return result with { Warnings = result.Warnings.Concat(snapshotWarnings).ToArray() };
    }
}
