using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed class ApacheCompatibilityPreparedReviewService : IApacheMigrationReviewService
{
    private readonly IApacheMigrationReviewService _inner;
    private readonly IApacheCompatibilityPackageService _compatibility;

    public ApacheCompatibilityPreparedReviewService(
        IApacheMigrationReviewService? inner = null,
        IApacheCompatibilityPackageService? compatibility = null)
    {
        _inner = inner ?? new ApacheMigrationReviewService();
        _compatibility = compatibility ?? new ApacheCompatibilityPackageService();
    }

    public async Task<ApacheMigrationReviewResult> BuildAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        CancellationToken cancellationToken = default)
    {
        using var prepared = _compatibility.Prepare(installation, target, package);
        var result = await _inner.BuildAsync(installation, target, prepared.Package, cancellationToken);
        if (prepared.Compatibility.PreservedModules.Count == 0 && prepared.Compatibility.PreservedDependencies.Count == 0)
            return result;

        var items = result.Items.ToList();
        foreach (var module in prepared.Compatibility.PreservedModules)
            items.Add(new ApacheMigrationReviewItem(ApacheMigrationReviewKind.AutomaticChange, "검토/실행 공통 규칙으로 외부 Apache 모듈 보존: " + module));
        foreach (var dependency in prepared.Compatibility.PreservedDependencies)
            items.Add(new ApacheMigrationReviewItem(ApacheMigrationReviewKind.AutomaticChange, "검토/실행 공통 규칙으로 모듈 종속 DLL 보존: " + dependency));
        return result with { Items = items };
    }
}

public sealed class ApacheCompatibilityPreparedUpdateExecutor : IApacheUpdateExecutor
{
    private readonly IApacheUpdateExecutor _inner;
    private readonly IApacheCompatibilityPackageService _compatibility;

    public ApacheCompatibilityPreparedUpdateExecutor(
        IApacheUpdateExecutor? inner = null,
        IApacheCompatibilityPackageService? compatibility = null)
    {
        _inner = inner ?? new ApacheUpdateExecutor();
        _compatibility = compatibility ?? new ApacheCompatibilityPackageService();
    }

    public async Task<UpdateExecutionResult> ExecuteAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup,
        CancellationToken cancellationToken = default)
    {
        using var prepared = _compatibility.Prepare(installation, target, package);
        var result = await _inner.ExecuteAsync(installation, target, prepared.Package, backup, cancellationToken);

        var compatibilityWarnings = prepared.Compatibility.PreservedModules
            .Select(value => "외부 Apache 모듈 공통 호환 보존: " + value)
            .Concat(prepared.Compatibility.PreservedDependencies.Select(value => "Apache 모듈 종속 DLL 공통 호환 보존: " + value))
            .ToArray();
        if (compatibilityWarnings.Length == 0) return result;
        return result with { Warnings = result.Warnings.Concat(compatibilityWarnings).ToArray() };
    }
}
