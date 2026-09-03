using System.IO.Compression;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IMariaDbMigrationReviewService
{
    MariaDbMigrationReviewResult Build(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup);
}

public sealed class MariaDbMigrationReviewService : IMariaDbMigrationReviewService
{
    public MariaDbMigrationReviewResult Build(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        BackupResult backup)
    {
        var component = installation.Components.First(item => item.Type == XamppComponentType.MariaDb);
        var current = component.Version ?? backup.Manifest.CurrentVersion;
        var mysqlRoot = Path.Combine(installation.RootPath, "mysql");
        var dataRoot = Path.Combine(mysqlRoot, "data");
        var configs = new[]
        {
            Path.Combine(mysqlRoot, "bin", "my.ini"),
            Path.Combine(mysqlRoot, "my.ini"),
            Path.Combine(mysqlRoot, "bin", "my.cnf"),
            Path.Combine(mysqlRoot, "my.cnf")
        }.Where(File.Exists).Select(path => Path.GetRelativePath(mysqlRoot, path)).ToArray();

        var automatic = new List<string>();
        var review = new List<string>();

        automatic.Add($"기존 data 디렉터리를 새 MariaDB에 복사하고 롤백 원본은 그대로 유지: {dataRoot}");
        automatic.Add(configs.Length == 0
            ? "별도 my.ini/my.cnf가 감지되지 않았습니다."
            : "기존 설정 보존: " + string.Join(", ", configs));
        automatic.Add("논리/물리 백업의 크기와 SHA256을 실제 교체 직전에 다시 검증합니다.");
        automatic.Add("업데이트 실패 시 기존 mysql 디렉터리 전체를 자동 롤백합니다.");

        var sameSeries = IsSameSeries(current, target.Version);
        automatic.Add(sameSeries
            ? $"동일 계열 패치 업데이트: {current} → {target.Version}"
            : $"직접 major 업그레이드: {current} → {target.Version}. MariaDB 공식 정책상 이전 버전에서 최신 버전으로 직접 업그레이드가 가능하며, 새 data 사본에서만 기동/업그레이드를 수행합니다.");

        var logical = backup.Manifest.LogicalBackup;
        if (logical is null)
            review.Add("전체 논리 백업 SQL이 없습니다. 실제 업데이트를 실행할 수 없습니다.");

        if (string.IsNullOrWhiteSpace(component.ServiceName))
            review.Add("XAMPP mysql을 가리키는 Windows 서비스를 찾지 못했습니다.");

        var upgradeTool = FindUpgradeToolInPackage(package.PackagePath);
        if (upgradeTool is null)
            review.Add("대상 패키지에서 mariadb-upgrade/mysql_upgrade를 찾지 못했습니다.");
        else
            automatic.Add($"업그레이드 도구 사용: {upgradeTool}");

        automatic.Add("업그레이드 DB 인증정보는 실행 직전에 입력하며 디스크에 영구 저장하지 않습니다.");

        return new MariaDbMigrationReviewResult(
            current,
            target.Version,
            component.ServiceName,
            component.ExecutablePath,
            dataRoot,
            configs,
            package.PackagePath,
            package.Sha256,
            upgradeTool,
            backup.ManifestPath,
            logical?.RelativePath,
            logical?.Sha256,
            backup.CopiedFiles,
            backup.CopiedBytes,
            automatic,
            review,
            review.Count == 0);
    }

    private static string? FindUpgradeToolInPackage(string packagePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            return archive.Entries
                .Select(entry => entry.FullName.Replace('\\', '/'))
                .FirstOrDefault(path =>
                    path.EndsWith("/bin/mariadb-upgrade.exe", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("/bin/mysql_upgrade.exe", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSameSeries(string currentVersion, string targetVersion) =>
        Version.TryParse(currentVersion, out var current) &&
        Version.TryParse(targetVersion, out var target) &&
        current.Major == target.Major && current.Minor == target.Minor;
}

public sealed record MariaDbMigrationReviewResult(
    string CurrentVersion,
    string TargetVersion,
    string? ServiceName,
    string ExecutablePath,
    string DataPath,
    IReadOnlyList<string> ConfigFiles,
    string PackagePath,
    string PackageSha256,
    string? UpgradeTool,
    string BackupManifestPath,
    string? LogicalBackupPath,
    string? LogicalBackupSha256,
    int BackupFiles,
    long BackupBytes,
    IReadOnlyList<string> AutomaticItems,
    IReadOnlyList<string> ReviewItems,
    bool CanExecute);
