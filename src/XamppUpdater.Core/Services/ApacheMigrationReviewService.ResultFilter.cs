using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed partial class ApacheMigrationReviewService
{
    async Task<ApacheMigrationReviewResult> IApacheMigrationReviewService.BuildAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        CancellationToken cancellationToken)
    {
        var result = await BuildAsync(installation, target, package, cancellationToken);
        if (!result.SyntaxValid) return result;

        var changed = false;
        var items = result.Items.Select(item =>
        {
            if (item.Kind == ApacheMigrationReviewKind.NeedsReview &&
                item.Message.StartsWith("새 Apache 패키지 기본 설정 자체가 검증에 실패했습니다:", StringComparison.Ordinal) &&
                item.Message.Contains("ServerRoot must be a valid directory", StringComparison.OrdinalIgnoreCase))
            {
                changed = true;
                return new ApacheMigrationReviewItem(
                    ApacheMigrationReviewKind.AutomaticChange,
                    "새 Apache 기본 설정 단독 검증의 ServerRoot 임시경로 오류는 보조 진단으로 처리했습니다. 실제 기존 XAMPP 설정은 새 Apache에서 Syntax OK를 통과했습니다.");
            }

            return item;
        }).ToArray();

        return changed
            ? result with { Items = items }
            : result;
    }
}
