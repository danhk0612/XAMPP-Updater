using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed partial class ApacheUpdateExecutor
{
    async Task<UpdateExecutionResult> IApacheUpdateExecutor.ExecuteAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup,
        CancellationToken cancellationToken)
    {
        var apache = installation.Components.First(item => item.Type == XamppComponentType.Apache);
        var currentVersion = apache.Version ?? backup.Manifest.CurrentVersion;
        var apacheRoot = Path.Combine(installation.RootPath, "apache");
        var confRoot = Path.Combine(apacheRoot, "conf");

        var initialIssues = ApacheSslCompatibilityService.InspectAndRepair(
            apacheRoot,
            confRoot,
            repairWeakSelfSigned: false);

        var blockers = initialIssues
            .Where(issue => !(issue.SelfSigned && issue.KeySize is < 2048 && issue.KeyPath is not null))
            .ToArray();

        if (blockers.Length > 0)
        {
            return new UpdateExecutionResult(
                false,
                false,
                currentVersion,
                target.Version,
                Array.Empty<string>(),
                blockers.Select(issue => issue.Message).ToArray(),
                "Apache SSL 인증서가 대상 OpenSSL 보안 정책과 호환되지 않습니다. 사용자/공인 인증서는 자동 교체하지 않습니다.");
        }

        var weakSelfSigned = initialIssues
            .Where(issue => issue.SelfSigned && issue.KeySize is < 2048 && issue.KeyPath is not null)
            .ToArray();

        if (weakSelfSigned.Length == 0)
            return await ExecuteAsync(installation, target, package, backup, cancellationToken);

        var originalFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in weakSelfSigned)
        {
            if (File.Exists(issue.CertificatePath) && !originalFiles.ContainsKey(issue.CertificatePath))
                originalFiles[issue.CertificatePath] = await File.ReadAllBytesAsync(issue.CertificatePath, cancellationToken);
            if (issue.KeyPath is not null && File.Exists(issue.KeyPath) && !originalFiles.ContainsKey(issue.KeyPath))
                originalFiles[issue.KeyPath] = await File.ReadAllBytesAsync(issue.KeyPath, cancellationToken);
        }

        try
        {
            var repaired = ApacheSslCompatibilityService.InspectAndRepair(
                apacheRoot,
                confRoot,
                repairWeakSelfSigned: true);

            var failedRepair = repaired.FirstOrDefault(issue => issue.SelfSigned && !issue.Repaired && issue.KeySize is < 2048);
            if (failedRepair is not null)
            {
                RestoreFiles(originalFiles);
                return new UpdateExecutionResult(
                    false,
                    false,
                    currentVersion,
                    target.Version,
                    Array.Empty<string>(),
                    new[] { failedRepair.Message },
                    "약한 자체서명 SSL 인증서를 안전한 키로 자동 재생성하지 못했습니다.");
            }

            var result = await ExecuteAsync(installation, target, package, backup, cancellationToken);
            if (!result.Success)
            {
                RestoreFiles(originalFiles);
                return result with
                {
                    Warnings = result.Warnings.Concat(new[]
                    {
                        "Apache 업데이트 실패/롤백에 따라 자동 재생성했던 SSL 인증서와 키도 원래 파일로 복구했습니다."
                    }).ToArray()
                };
            }

            return result with
            {
                Steps = result.Steps.Concat(repaired
                    .Where(issue => issue.Repaired)
                    .Select(issue => "SSL 호환성 자동 처리: " + issue.Message)).ToArray()
            };
        }
        catch
        {
            RestoreFiles(originalFiles);
            throw;
        }
    }

    private static void RestoreFiles(IReadOnlyDictionary<string, byte[]> originals)
    {
        foreach (var pair in originals)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(pair.Key)!);
                File.WriteAllBytes(pair.Key, pair.Value);
            }
            catch
            {
                // The main executor still has the component backup/rollback path. Best-effort here prevents
                // an SSL pre-processing change from surviving a failed update.
            }
        }
    }
}
