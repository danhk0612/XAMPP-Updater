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
        var warnings = new List<string>();
        try
        {
            var before = _snapshots.Capture(installation.RootPath, XamppComponentType.MariaDb, backup.Manifest.CurrentVersion, "BeforeUpdate");
            warnings.Add("업데이트 전 설정 snapshot: " + before.ManifestPath);
        }
        catch (Exception ex) { warnings.Add("업데이트 전 설정 snapshot 저장 실패: " + ex.Message); }

        var result = await UpdateExecutionWatchdog.ExecuteAsync(
            token => _inner.ExecuteAsync(installation, target, package, backup, credentials, token),
            TimeSpan.FromMinutes(30),
            cancellationToken);
        if (result.Success)
        {
            try
            {
                var after = _snapshots.Capture(installation.RootPath, XamppComponentType.MariaDb, target.Version, "AfterUpdate");
                warnings.Add("업데이트 후 설정 snapshot: " + after.ManifestPath);
            }
            catch (Exception ex) { warnings.Add("업데이트 후 설정 snapshot 저장 실패: " + ex.Message); }
        }

        return result with { Warnings = result.Warnings.Concat(warnings).ToArray() };
    }
}
