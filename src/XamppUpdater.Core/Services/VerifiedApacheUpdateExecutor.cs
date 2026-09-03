using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed class VerifiedApacheUpdateExecutor : IApacheUpdateExecutor
{
    private readonly IApacheUpdateExecutor _inner;
    private readonly IConfigSnapshotService _snapshots;

    public VerifiedApacheUpdateExecutor(IApacheUpdateExecutor? inner = null, IConfigSnapshotService? snapshots = null)
    {
        _inner = inner ?? new ApacheCompatibilityPreparedUpdateExecutor();
        _snapshots = snapshots ?? new ConfigSnapshotService();
    }

    public async Task<UpdateExecutionResult> ExecuteAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup,
        CancellationToken cancellationToken = default)
    {
        BackupIntegrityVerifier.Verify(backup);
        var snapshotWarnings = new List<string>();
        try
        {
            var before = _snapshots.Capture(installation.RootPath, XamppComponentType.Apache, backup.Manifest.CurrentVersion, "BeforeUpdate");
            snapshotWarnings.Add("업데이트 전 설정 snapshot: " + before.ManifestPath);
        }
        catch (Exception ex) { snapshotWarnings.Add("업데이트 전 설정 snapshot 저장 실패: " + ex.Message); }

        var result = await UpdateExecutionWatchdog.ExecuteAsync(
            token => _inner.ExecuteAsync(installation, target, package, backup, token),
            TimeSpan.FromMinutes(10),
            cancellationToken);
        if (result.Success)
        {
            try
            {
                var after = _snapshots.Capture(installation.RootPath, XamppComponentType.Apache, target.Version, "AfterUpdate");
                snapshotWarnings.Add("업데이트 후 설정 snapshot: " + after.ManifestPath);
            }
            catch (Exception ex) { snapshotWarnings.Add("업데이트 후 설정 snapshot 저장 실패: " + ex.Message); }
        }

        return result with { Warnings = result.Warnings.Concat(snapshotWarnings).ToArray() };
    }
}
