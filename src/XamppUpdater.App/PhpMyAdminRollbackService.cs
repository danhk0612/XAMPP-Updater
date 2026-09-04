using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace XamppUpdater.App;

internal sealed record PhpMyAdminRollbackCandidate(
    string BackupPath,
    string BackupVersion,
    string CurrentVersion,
    DateTime CreatedAt,
    bool UserConfigMatches);

internal sealed record PhpMyAdminRollbackResult(
    string PreviousVersion,
    string RestoredVersion,
    string BackupPath,
    string SafetyBackupPath);

internal sealed partial class PhpMyAdminRollbackService
{
    private readonly PhpMyAdminUpdateService _updateService = new();

    public PhpMyAdminRollbackCandidate? FindLatestCandidate(string xamppRoot)
    {
        PhpMyAdminInstallationState state;
        try { state = _updateService.Inspect(xamppRoot); }
        catch { return null; }
        if (!state.IsInstalled || string.IsNullOrWhiteSpace(state.Version)) return null;

        var backupRoot = GetBackupRoot();
        if (!Directory.Exists(backupRoot)) return null;

        var candidates = new List<PhpMyAdminRollbackCandidate>();
        foreach (var path in Directory.EnumerateDirectories(backupRoot))
        {
            var match = UpdateBackupNameRegex().Match(Path.GetFileName(path));
            if (!match.Success) continue;

            var from = match.Groups["from"].Value;
            var to = match.Groups["to"].Value;
            if (!string.Equals(to, state.Version, StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryValidatePayload(path, from)) continue;

            var created = DateTime.TryParseExact(
                match.Groups["stamp"].Value,
                "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsed)
                ? parsed
                : Directory.GetCreationTime(path);

            candidates.Add(new PhpMyAdminRollbackCandidate(
                path,
                from,
                to,
                created,
                UserConfigMatches(state.DirectoryPath, path)));
        }

        if (candidates.Count == 0) return null;

        // phpMyAdmin update backups predate per-installation manifests. Prefer a backup whose
        // preserved user configuration matches the current installation; otherwise choose the
        // newest structurally valid update-created backup for the exact current version.
        var matching = candidates.Where(item => item.UserConfigMatches).ToArray();
        return (matching.Length > 0 ? matching : candidates.ToArray())
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefault();
    }

    public PhpMyAdminRollbackResult Rollback(string xamppRoot, PhpMyAdminRollbackCandidate candidate)
    {
        var state = _updateService.Inspect(xamppRoot);
        if (!state.IsInstalled || string.IsNullOrWhiteSpace(state.Version))
            throw new InvalidOperationException("현재 phpMyAdmin 설치 버전을 확인할 수 없습니다.");
        if (!string.Equals(state.Version, candidate.CurrentVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"현재 phpMyAdmin 버전이 롤백 백업의 대상 버전과 다릅니다. current={state.Version}, backup={candidate.CurrentVersion}");
        if (!TryValidatePayload(candidate.BackupPath, candidate.BackupVersion))
            throw new InvalidDataException("phpMyAdmin 롤백 백업의 구조 또는 버전 검증에 실패했습니다.");

        var localRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XAMPP-Updater", "PhpMyAdmin");
        var stagingRoot = Path.Combine(localRoot, "Staging", "rollback-" + Guid.NewGuid().ToString("N"));
        var safetyRoot = Path.Combine(localRoot, "RollbackSafety");
        var safetyPath = Path.Combine(safetyRoot, $"phpMyAdmin-{state.Version}-before-rollback-{DateTime.Now:yyyyMMdd-HHmmss}");
        var oldSwapPath = state.DirectoryPath + ".rollback-old-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(safetyRoot);

        var stagedPayload = Path.Combine(stagingRoot, "phpMyAdmin");
        var swapped = false;
        try
        {
            CopyDirectory(candidate.BackupPath, stagedPayload);
            if (!TryValidatePayload(stagedPayload, candidate.BackupVersion))
                throw new InvalidDataException("복사한 phpMyAdmin 롤백 staging 데이터 검증에 실패했습니다.");
            ValidatePhpConfigSyntax(xamppRoot, stagedPayload);

            // Rollback itself is also guarded by a separate safety copy. It is intentionally
            // stored outside Backups so it cannot be mistaken for an update-generated rollback target.
            CopyDirectory(state.DirectoryPath, safetyPath);
            if (!TryValidatePayload(safetyPath, state.Version))
                throw new InvalidDataException("phpMyAdmin 롤백 직전 안전 백업 검증에 실패했습니다.");

            Directory.Move(state.DirectoryPath, oldSwapPath);
            swapped = true;
            Directory.Move(stagedPayload, state.DirectoryPath);

            if (!TryValidatePayload(state.DirectoryPath, candidate.BackupVersion))
                throw new InvalidDataException("롤백 후 phpMyAdmin 설치 검증에 실패했습니다.");
            ValidatePhpConfigSyntax(xamppRoot, state.DirectoryPath);

            if (Directory.Exists(oldSwapPath)) Directory.Delete(oldSwapPath, recursive: true);
            swapped = false;

            return new PhpMyAdminRollbackResult(
                state.Version,
                candidate.BackupVersion,
                candidate.BackupPath,
                safetyPath);
        }
        catch
        {
            if (swapped)
            {
                try
                {
                    if (Directory.Exists(state.DirectoryPath)) Directory.Delete(state.DirectoryPath, recursive: true);
                    if (Directory.Exists(oldSwapPath)) Directory.Move(oldSwapPath, state.DirectoryPath);
                }
                catch
                {
                    // Preserve the original exception. The safety backup path remains available.
                }
            }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true); } catch { }
        }
    }

    private static string GetBackupRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XAMPP-Updater", "PhpMyAdmin", "Backups");

    private static bool TryValidatePayload(string root, string expectedVersion)
    {
        try
        {
            if (!File.Exists(Path.Combine(root, "index.php"))) return false;
            if (!Directory.Exists(Path.Combine(root, "libraries"))) return false;
            if (!Directory.Exists(Path.Combine(root, "vendor"))) return false;
            var actual = PhpMyAdminUpdateService.TryReadInstalledVersion(root);
            return string.Equals(actual, expectedVersion, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool UserConfigMatches(string currentRoot, string backupRoot)
    {
        var compared = false;
        foreach (var name in new[] { "config.inc.php", ".htaccess" })
        {
            var current = Path.Combine(currentRoot, name);
            var backup = Path.Combine(backupRoot, name);
            if (!File.Exists(current) && !File.Exists(backup)) continue;
            compared = true;
            if (!File.Exists(current) || !File.Exists(backup)) return false;
            if (!FilesEqual(current, backup)) return false;
        }
        return compared;
    }

    private static bool FilesEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length) return false;
        using var sha = SHA256.Create();
        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        return sha.ComputeHash(leftStream).SequenceEqual(sha.ComputeHash(rightStream));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void ValidatePhpConfigSyntax(string xamppRoot, string phpMyAdminRoot)
    {
        var php = Path.Combine(Path.GetFullPath(xamppRoot), "php", "php.exe");
        var config = Path.Combine(phpMyAdminRoot, "config.inc.php");
        if (!File.Exists(php) || !File.Exists(config)) return;

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = php,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Arguments = $"-n -l \"{config}\""
        }) ?? throw new InvalidOperationException("phpMyAdmin config.inc.php 구문 검증 프로세스를 시작하지 못했습니다.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(20_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("phpMyAdmin config.inc.php 구문 검증 시간이 초과되었습니다.");
        }
        if (process.ExitCode != 0)
            throw new InvalidDataException("phpMyAdmin config.inc.php 구문 검증 실패: " + (stderr + Environment.NewLine + stdout).Trim());
    }

    [GeneratedRegex(@"^phpMyAdmin-(?<from>.+?)-to-(?<to>.+?)-(?<stamp>\d{8}-\d{6})$", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateBackupNameRegex();
}
