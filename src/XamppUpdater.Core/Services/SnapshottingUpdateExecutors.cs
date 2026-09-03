using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed class SnapshottingMariaDbUpdateExecutor : IMariaDbUpdateExecutor
{
    private readonly IMariaDbUpdateExecutor _inner;
    private readonly IConfigSnapshotService _snapshots;

    public SnapshottingMariaDbUpdateExecutor(IMariaDbUpdateExecutor? inner = null, IConfigSnapshotService? snapshots = null)
    {
        _inner = inner ?? new MariaDbUpdateExecutor();
        _snapshots = snapshots ?? new ConfigSnapshotService();
    }

    public async Task<UpdateExecutionResult> ExecuteAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup,
        MariaDbCredentials? credentials = null,
        CancellationToken cancellationToken = default)
    {
        UpdateProgressReporter.Report(XamppComponentType.MariaDb, "BackupVerify", "논리/물리 롤백 백업 무결성을 확인하는 중...", 10);
        BackupIntegrityVerifier.Verify(backup, requireLogicalBackup: true);
        UpdateProgressReporter.Report(XamppComponentType.MariaDb, "BackupVerify", "MariaDB 롤백 백업 무결성 확인 완료", 20);

        var warnings = new List<string>();
        UpdateProgressReporter.Report(XamppComponentType.MariaDb, "BeforeSnapshot", "업데이트 전 MariaDB 설정 snapshot을 저장하는 중...", 25);
        try
        {
            var before = _snapshots.Capture(installation.RootPath, XamppComponentType.MariaDb, backup.Manifest.CurrentVersion, "BeforeUpdate");
            warnings.Add("업데이트 전 설정 snapshot: " + before.ManifestPath);
        }
        catch (Exception ex) { warnings.Add("업데이트 전 설정 snapshot 저장 실패: " + ex.Message); }

        UpdateProgressReporter.Report(XamppComponentType.MariaDb, "Execute", "MariaDB 서비스 중지·바이너리 교체·데이터 복사·upgrade 검증을 진행하는 중...", 35);
        var result = await UpdateExecutionWatchdog.ExecuteAsync(
            token => _inner.ExecuteAsync(installation, target, package, backup, credentials, token),
            TimeSpan.FromMinutes(30),
            cancellationToken);

        if (!result.Success)
        {
            UpdateProgressReporter.Report(
                XamppComponentType.MariaDb,
                result.RolledBack ? "Rollback" : "Failed",
                result.RolledBack ? "업데이트 실패 후 기존 MariaDB로 자동 롤백했습니다." : "MariaDB 업데이트가 중단되었습니다.",
                result.RolledBack ? 90 : null,
                result.RolledBack);
        }
        else
        {
            UpdateProgressReporter.Report(XamppComponentType.MariaDb, "AfterSnapshot", "업데이트 후 MariaDB 설정 snapshot을 저장하는 중...", 90);
            try
            {
                var after = _snapshots.Capture(installation.RootPath, XamppComponentType.MariaDb, target.Version, "AfterUpdate");
                warnings.Add("업데이트 후 설정 snapshot: " + after.ManifestPath);
            }
            catch (Exception ex) { warnings.Add("업데이트 후 설정 snapshot 저장 실패: " + ex.Message); }
            UpdateProgressReporter.Report(XamppComponentType.MariaDb, "Completed", $"MariaDB {target.Version} 업데이트 완료", 100);
        }

        return result with { Warnings = result.Warnings.Concat(warnings).ToArray() };
    }
}
