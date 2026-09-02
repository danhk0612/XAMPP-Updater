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
        _iniMigrationService = iniMigrationService ?? new PhpIniMigrationService();
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
        var currentIni = Path.Combine(installation.RootPath, "php", "php.ini");
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

            var items = new List<PhpMigrationReviewItem>();
            items.Add(new PhpMigrationReviewItem(
                PhpMigrationReviewKind.Preserved,
                "기존 php.ini를 기준으로 변경이 필요하지 않은 설정은 그대로 유지합니다."));

            foreach (var dll in extensionResult.InstalledDlls)
            {
                items.Add(new PhpMigrationReviewItem(
                    PhpMigrationReviewKind.AutomaticChange,
                    $"외부 호환 확장 자동 복원: {dll}"));
            }

            foreach (var warning in extensionResult.Warnings)
            {
                items.Add(new PhpMigrationReviewItem(
                    IsExtensionWarningReviewRequired(warning) ? PhpMigrationReviewKind.NeedsReview : PhpMigrationReviewKind.AutomaticChange,
                    warning));
            }

            foreach (var warning in migration.Warnings)
            {
                items.Add(new PhpMigrationReviewItem(
                    IsMigrationWarningReviewRequired(warning) ? PhpMigrationReviewKind.NeedsReview : PhpMigrationReviewKind.AutomaticChange,
                    warning));
            }

            return new PhpMigrationReviewResult(
                currentVersion,
                target.Version,
                File.ReadAllText(migration.IniPath),
                items,
                extensionResult.InstalledDlls);
        }
        finally
        {
            TryDeleteDirectory(reviewRoot);
        }
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
}
