using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed class VerifiedPhpUpdateExecutor : IPhpUpdateExecutor
{
    private readonly IPhpUpdateExecutor _inner;
    private readonly IConfigSnapshotService _snapshots;

    public VerifiedPhpUpdateExecutor(IPhpUpdateExecutor? inner = null, IConfigSnapshotService? snapshots = null)
    {
        _inner = inner ?? new PhpUpdateExecutor();
        _snapshots = snapshots ?? new ConfigSnapshotService();
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

        if (!result.Success)
        {
            UpdateProgressReporter.Report(
                XamppComponentType.Php,
                result.RolledBack ? UpdateProgressStages.Rollback : UpdateProgressStages.Failed,
                result.RolledBack ? "업데이트 실패 후 기존 PHP로 자동 롤백했습니다." : "PHP 업데이트가 중단되었습니다.",
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
