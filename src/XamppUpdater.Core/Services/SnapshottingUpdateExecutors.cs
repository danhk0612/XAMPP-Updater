using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed class SnapshottingApacheUpdateExecutor : IApacheUpdateExecutor
{
    private readonly IApacheUpdateExecutor _inner;
    private readonly IConfigSnapshotService _snapshots;

    public SnapshottingApacheUpdateExecutor(IApacheUpdateExecutor? inner = null, IConfigSnapshotService? snapshots = null)
    {
        _inner = inner ?? new VerifiedApacheUpdateExecutor();
        _snapshots = snapshots ?? new ConfigSnapshotService();
    }

    public async Task<UpdateExecutionResult> ExecuteAsync(XamppInstallation installation, UpdateTargetOption target, PackagePreparationResult package, BackupResult backup, CancellationToken cancellationToken = default)
        => await ExecuteWithSnapshotsAsync(_inner.ExecuteAsync, installation, target, package, backup, XamppComponentType.Apache, _snapshots, cancellationToken);

    private static async Task<UpdateExecutionResult> ExecuteWithSnapshotsAsync(
        Func<XamppInstallation, UpdateTargetOption, PackagePreparationResult, BackupResult, CancellationToken, Task<UpdateExecutionResult>> execute,
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup,
        XamppComponentType type,
        IConfigSnapshotService snapshots,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        try
        {
            var before = snapshots.Capture(installation.RootPath, type, backup.Manifest.CurrentVersion, "BeforeUpdate");
            warnings.Add("설정 snapshot 저장: " + before.ManifestPath);
        }
        catch (Exception ex) { warnings.Add("업데이트 전 설정 snapshot 저장 실패: " + ex.Message); }

        var result = await execute(installation, target, package, backup, cancellationToken);
        if (result.Success)
        {
            try
            {
                var after = snapshots.Capture(installation.RootPath, type, target.Version, "AfterUpdate");
                warnings.Add("업데이트 후 설정 snapshot 저장: " + after.ManifestPath);
            }
            catch (Exception ex) { warnings.Add("업데이트 후 설정 snapshot 저장 실패: " + ex.Message); }
        }

        return result with { Warnings = result.Warnings.Concat(warnings).ToArray() };
    }
}

public sealed class SnapshottingPhpUpdateExecutor : IPhpUpdateExecutor
{
    private readonly IPhpUpdateExecutor _inner;
    private readonly IConfigSnapshotService _snapshots;

    public SnapshottingPhpUpdateExecutor(IPhpUpdateExecutor? inner = null, IConfigSnapshotService? snapshots = null)
    {
        _inner = inner ?? new VerifiedPhpUpdateExecutor();
        _snapshots = snapshots ?? new ConfigSnapshotService();
    }

    public async Task<UpdateExecutionResult> ExecuteAsync(XamppInstallation installation, UpdateTargetOption target, PackagePreparationResult package, BackupResult backup, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        try
        {
            var before = _snapshots.Capture(installation.RootPath, XamppComponentType.Php, backup.Manifest.CurrentVersion, "BeforeUpdate");
            warnings.Add("설정 snapshot 저장: " + before.ManifestPath);
        }
        catch (Exception ex) { warnings.Add("업데이트 전 설정 snapshot 저장 실패: " + ex.Message); }

        var result = await _inner.ExecuteAsync(installation, target, package, backup, cancellationToken);
        if (result.Success)
        {
            try
            {
                var after = _snapshots.Capture(installation.RootPath, XamppComponentType.Php, target.Version, "AfterUpdate");
                warnings.Add("업데이트 후 설정 snapshot 저장: " + after.ManifestPath);
            }
            catch (Exception ex) { warnings.Add("업데이트 후 설정 snapshot 저장 실패: " + ex.Message); }
        }

        return result with { Warnings = result.Warnings.Concat(warnings).ToArray() };
    }
}

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
            warnings.Add("설정 snapshot 저장: " + before.ManifestPath);
        }
        catch (Exception ex) { warnings.Add("업데이트 전 설정 snapshot 저장 실패: " + ex.Message); }

        var result = await _inner.ExecuteAsync(installation, target, package, backup, credentials, cancellationToken);
        if (result.Success)
        {
            try
            {
                var after = _snapshots.Capture(installation.RootPath, XamppComponentType.MariaDb, target.Version, "AfterUpdate");
                warnings.Add("업데이트 후 설정 snapshot 저장: " + after.ManifestPath);
            }
            catch (Exception ex) { warnings.Add("업데이트 후 설정 snapshot 저장 실패: " + ex.Message); }
        }

        return result with { Warnings = result.Warnings.Concat(warnings).ToArray() };
    }
}
