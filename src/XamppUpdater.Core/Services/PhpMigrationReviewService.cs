using System.IO.Compression;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public enum PhpMigrationReviewKind
{
    Preserved,
    AutomaticChange,
    NeedsReview
}

public sealed record PhpMigrationReviewItem(
    PhpMigrationReviewKind Kind,
    string Message);

public sealed record PhpMigrationReviewResult(
    string CurrentVersion,
    string TargetVersion,
    string ProposedIni,
    IReadOnlyList<PhpMigrationReviewItem> Items,
    IReadOnlyList<string> InstalledExternalExtensions)
{
    public int PreservedCount => Items.Count(item => item.Kind == PhpMigrationReviewKind.Preserved);
    public int AutomaticChangeCount => Items.Count(item => item.Kind == PhpMigrationReviewKind.AutomaticChange);
    public int NeedsReviewCount => Items.Count(item => item.Kind == PhpMigrationReviewKind.NeedsReview);
}

public interface IPhpMigrationReviewService
{
    Task<PhpMigrationReviewResult> BuildAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        CancellationToken cancellationToken = default);
}

public sealed class PhpMigrationReviewService : IPhpMigrationReviewService
{
    private readonly IPhpIniMigrationService _iniMigrationService;
    private readonly IPhpExternalExtensionInstaller _externalExtensionInstaller;

    public PhpMigrationReviewService(
        IPhpIniMigrationService? iniMigrationService = null,
        IPhpExternalExtensionInstaller? externalExtensionInstaller = null)
    {
        _iniMigrationService = iniMigrationService ?? new PhpIniMigrationService(new IgnorePhpMigrationOverrideStore());
        _externalExtensionInstaller = externalExtensionInstaller ?? new PhpExternalExtensionInstaller();
    }

    public async Task<PhpMigrationReviewResult> BuildAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        CancellationToken cancellationToken = default)
    {
        if (target.Type != XamppComponentType.Php || package.Type != XamppComponentType.Php)
        {
            throw new ArgumentException("PHP 마이그레이션 검토에는 PHP 대상과 패키지가 필요합니다.");
        }

        var php = installation.Components.First(item => item.Type == XamppComponentType.Php);
        var currentVersion = php.Version ?? "Unknown";
        var finalPhpRoot = Path.Combine(installation.RootPath, "php");
        var currentIni = Path.Combine(finalPhpRoot, "php.ini");
        if (!File.Exists(currentIni))
        {
            throw new FileNotFoundException("현재 php.ini를 찾을 수 없습니다.", currentIni);
        }

        if (!File.Exists(package.PackagePath))
        {
            throw new FileNotFoundException("준비된 PHP 패키지를 찾을 수 없습니다.", package.PackagePath);
        }

        var reviewRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XamppUpdater",
            "Review",
            $"PHP-{Guid.NewGuid():N}");
        var extractedRoot = Path.Combine(reviewRoot, "package");

        try
        {
            Directory.CreateDirectory(extractedRoot);
            ZipFile.ExtractToDirectory(package.PackagePath, extractedRoot, overwriteFiles: true);
            var phpRoot = ResolvePayloadRoot(extractedRoot, package.PayloadEntry);
            var threadSafe = Directory.EnumerateFiles(phpRoot, "php*apache2_4.dll", SearchOption.TopDirectoryOnly).Any();

            var extensionResult = await _externalExtensionInstaller.InstallMissingAsync(
                currentIni,
                phpRoot,
                target.Version,
                threadSafe,
                package.Architecture,
                cancellationToken);

            var migration = _iniMigrationService.Migrate(currentIni, phpRoot, target.Version);
            if (!migration.Migrated || migration.IniPath is null || !File.Exists(migration.IniPath))
            {
                throw new InvalidOperationException("PHP 설정 마이그레이션 제안안을 만들지 못했습니다.");
            }

            // 검토용 스테이징 경로는 검토 종료 시 삭제된다. 사용자가 확정하는 php.ini와
            // 검토 메시지에는 실제 업데이트 완료 후의 XAMPP PHP 경로만 노출/저장한다.
            var proposedIni = NormalizeReviewPath(File.ReadAllText(migration.IniPath), phpRoot, finalPhpRoot);
            var items = BuildPreservedItems(File.ReadAllText(currentIni), proposedIni);

            foreach (var dll in extensionResult.InstalledDlls)
            {
                items.Add(new PhpMigrationReviewItem(
                    PhpMigrationReviewKind.AutomaticChange,
                    $"외부 호환 확장 자동 복원: {dll}"));
            }

            foreach (var warning in extensionResult.Warnings)
            {
                var normalizedWarning = NormalizeReviewPath(warning, phpRoot, finalPhpRoot);
                items.Add(new PhpMigrationReviewItem(
                    IsExtensionWarningReviewRequired(normalizedWarning) ? PhpMigrationReviewKind.NeedsReview : PhpMigrationReviewKind.AutomaticChange,
                    normalizedWarning));
            }

            foreach (var warning in migration.Warnings)
            {
                var normalizedWarning = NormalizeReviewPath(warning, phpRoot, finalPhpRoot);
                items.Add(new PhpMigrationReviewItem(
                    IsMigrationWarningReviewRequired(normalizedWarning) ? PhpMigrationReviewKind.NeedsReview : PhpMigrationReviewKind.AutomaticChange,
                    normalizedWarning));
            }

            return new PhpMigrationReviewResult(
                currentVersion,
                target.Version,
                proposedIni,
                items,
                extensionResult.InstalledDlls);
        }
        finally
        {
            TryDeleteDirectory(reviewRoot);
        }
    }

    private static string NormalizeReviewPath(string text, string reviewPhpRoot, string finalPhpRoot)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Replace(reviewPhpRoot, finalPhpRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static List<PhpMigrationReviewItem> BuildPreservedItems(string currentIni, string proposedIni)
    {
        var current = currentIni.Replace("\r\n", "\n").Split('\n');
        var proposed = proposedIni.Replace("\r\n", "\n").Split('\n');
        var result = new List<PhpMigrationReviewItem>();
        var count = Math.Min(current.Length, proposed.Length);
        for (var index = 0; index < count; index++)
        {
            var line = current[index].Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#') || !line.Contains('=')) continue;
            if (!string.Equals(current[index], proposed[index], StringComparison.Ordinal)) continue;
            result.Add(new PhpMigrationReviewItem(PhpMigrationReviewKind.Preserved, line));
        }
        return result;
    }

    private static bool IsExtensionWarningReviewRequired(string warning) =>
        warning.Contains("찾지 못함", StringComparison.OrdinalIgnoreCase) ||
        warning.Contains("실패", StringComparison.OrdinalIgnoreCase);

    private static bool IsMigrationWarningReviewRequired(string warning) =>
        warning.Contains("찾지 못해 비활성화", StringComparison.OrdinalIgnoreCase) ||
        warning.Contains("없어 비활성화", StringComparison.OrdinalIgnoreCase);

    private static string ResolvePayloadRoot(string extractedRoot, string payloadEntry)
    {
        var normalized = payloadEntry.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var payloadFile = Path.Combine(extractedRoot, normalized);
        if (!File.Exists(payloadFile))
        {
            throw new InvalidDataException("압축 해제 후 php.exe를 찾을 수 없습니다.");
        }

        return Path.GetDirectoryName(payloadFile) ?? extractedRoot;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private sealed class IgnorePhpMigrationOverrideStore : IPhpMigrationOverrideStore
    {
        public string Save(string xamppRoot, string targetVersion, string sourceIniPath, string iniText) => string.Empty;
        public PhpMigrationOverride? TryLoad(string xamppRoot, string targetVersion, string sourceIniPath) => null;
    }
}
