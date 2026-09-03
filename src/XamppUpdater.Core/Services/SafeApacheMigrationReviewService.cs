using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed class SafeApacheMigrationReviewService : IApacheMigrationReviewService
{
    private readonly IApacheMigrationReviewService _inner;

    public SafeApacheMigrationReviewService(IApacheMigrationReviewService? inner = null)
    {
        _inner = inner ?? new ApacheMigrationReviewService();
    }

    public async Task<ApacheMigrationReviewResult> BuildAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.BuildAsync(installation, target, package, cancellationToken);
        var reviewOnlyRepair = result.Items.Any(item =>
            item.Message.Contains("모듈 종속 DLL 자동 배치", StringComparison.OrdinalIgnoreCase) ||
            item.Message.Contains("누락 종속 DLL 자동 배치", StringComparison.OrdinalIgnoreCase));

        if (!reviewOnlyRepair) return result;

        var items = result.Items.ToList();
        items.Add(new ApacheMigrationReviewItem(
            ApacheMigrationReviewKind.NeedsReview,
            "검토용 staging에서만 Apache 모듈 종속 DLL 자동 복구가 발생했습니다. 실제 실행 단계와 동일한 복구 경로가 아직 보장되지 않으므로 안전을 위해 업데이트 실행을 차단합니다."));

        return result with
        {
            SyntaxValid = false,
            Items = items,
            ValidationOutput = string.IsNullOrWhiteSpace(result.ValidationOutput)
                ? "Review-only dependency repair detected."
                : result.ValidationOutput + Environment.NewLine + "Review-only dependency repair detected; execution blocked."
        };
    }
}
