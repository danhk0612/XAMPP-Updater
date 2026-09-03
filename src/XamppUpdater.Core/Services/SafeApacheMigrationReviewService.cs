using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed class SafeApacheMigrationReviewService : IApacheMigrationReviewService
{
    private readonly IApacheMigrationReviewService _inner;

    public SafeApacheMigrationReviewService(IApacheMigrationReviewService? inner = null)
    {
        _inner = inner ?? new ApacheCompatibilityPreparedReviewService();
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
            "검토/실행 공통 호환 패키지 적용 후에도 검토 단계에서만 추가 종속 DLL 복구가 발생했습니다. 실제 실행과 결과가 달라질 수 있으므로 안전을 위해 업데이트 실행을 차단합니다."));

        return result with
        {
            SyntaxValid = false,
            Items = items,
            ValidationOutput = string.IsNullOrWhiteSpace(result.ValidationOutput)
                ? "Review-only dependency repair detected after shared compatibility preparation."
                : result.ValidationOutput + Environment.NewLine + "Review-only dependency repair detected after shared compatibility preparation; execution blocked."
        };
    }
}
