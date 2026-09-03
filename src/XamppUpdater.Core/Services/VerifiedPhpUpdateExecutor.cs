using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed class VerifiedPhpUpdateExecutor : IPhpUpdateExecutor
{
    private readonly IPhpUpdateExecutor _inner;

    public VerifiedPhpUpdateExecutor(IPhpUpdateExecutor? inner = null)
    {
        _inner = inner ?? new PhpUpdateExecutor();
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
