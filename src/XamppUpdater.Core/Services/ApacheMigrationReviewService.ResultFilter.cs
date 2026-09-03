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

        var changed = false;
        var items = result.Items.Select(item =>
        {
            if (result.SyntaxValid &&
                item.Kind == ApacheMigrationReviewKind.NeedsReview &&
                item.Message.StartsWith("새 Apache 패키지 기본 설정 자체가 검증에 실패했습니다:", StringComparison.Ordinal) &&
                item.Message.Contains("ServerRoot must be a valid directory", StringComparison.OrdinalIgnoreCase))
            {
                changed = true;
                return new ApacheMigrationReviewItem(
                    ApacheMigrationReviewKind.AutomaticChange,
                    "새 Apache 기본 설정 단독 검증의 ServerRoot 임시경로 오류는 보조 진단으로 처리했습니다. 실제 기존 XAMPP 설정은 새 Apache에서 Syntax OK를 통과했습니다.");
            }

            return item;
        }).ToList();

        var apacheRoot = Path.Combine(installation.RootPath, "apache");
        var confRoot = Path.Combine(apacheRoot, "conf");
        var sslIssues = ApacheSslCompatibilityService.InspectAndRepair(apacheRoot, confRoot, repairWeakSelfSigned: false);
        var sslBlocksUpdate = false;

        foreach (var issue in sslIssues)
        {
            changed = true;
            if (issue.SelfSigned && issue.KeySize is < 2048 && issue.KeyPath is not null)
            {
                items.Add(new ApacheMigrationReviewItem(
                    ApacheMigrationReviewKind.AutomaticChange,
                    $"Apache/OpenSSL 호환성: 약한 자체서명 SSL 인증서를 실제 업데이트 직전에 RSA 2048/SHA-256으로 자동 재생성합니다. {Path.GetRelativePath(apacheRoot, issue.CertificatePath).Replace('\\', '/')} / 기존 RSA {issue.KeySize}"));
            }
            else
            {
                sslBlocksUpdate = true;
                items.Add(new ApacheMigrationReviewItem(
                    ApacheMigrationReviewKind.NeedsReview,
                    "Apache/OpenSSL SSL 인증서 호환성 확인 필요: " + issue.Message));
            }
        }

        if (sslBlocksUpdate)
        {
            return result with
            {
                SyntaxValid = false,
                Items = items.ToArray(),
                ValidationOutput = result.ValidationOutput + Environment.NewLine +
                                   "SSL certificate compatibility check failed. Replace or renew the reported certificate before updating Apache."
            };
        }

        return changed
            ? result with { Items = items.ToArray() }
            : result;
    }
}
