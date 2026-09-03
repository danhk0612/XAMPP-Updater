using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed class VerifiedApacheUpdateExecutor : IApacheUpdateExecutor
{
    private readonly IApacheUpdateExecutor _inner;

    public VerifiedApacheUpdateExecutor(IApacheUpdateExecutor? inner = null)
    {
        _inner = inner ?? new ApacheUpdateExecutor();
    }

    public Task<UpdateExecutionResult> ExecuteAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup,
        CancellationToken cancellationToken = default)
    {
        BackupIntegrityVerifier.Verify(backup);
        return _inner.ExecuteAsync(installation, target, package, backup, cancellationToken);
    }
}
