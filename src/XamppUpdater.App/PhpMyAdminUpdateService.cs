using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XamppUpdater.App;

internal sealed record PhpMyAdminInstallationState(
    bool IsInstalled,
    string DirectoryPath,
    string? Version,
    string? Detail);

internal sealed record PhpMyAdminReleaseInfo(
    string Version,
    DateTime? ReleaseDate,
    string DownloadUrl,
    string ChecksumUrl,
    string? PhpVersionRange,
    string? DatabaseVersionRange);

internal sealed record PhpMyAdminCompatibility(
    bool CanUpdate,
    IReadOnlyList<string> Warnings,
    string Summary);

internal sealed record PhpMyAdminUpdateProgress(
    string Stage,
    int Percent,
    string Message,
    long? BytesReceived = null,
    long? TotalBytes = null);

internal sealed record PhpMyAdminUpdateResult(
    string PreviousVersion,
    string NewVersion,
    string BackupPath,
    string Sha256);

internal sealed partial class PhpMyAdminUpdateService
{
    private static readonly HttpClient Http = CreateHttpClient();
    private const string VersionEndpoint = "https://www.phpmyadmin.net/home_page/version.json";

    public PhpMyAdminInstallationState Inspect(string xamppRoot)
    {
        var directory = ResolvePhpMyAdminDirectory(xamppRoot);
        if (directory is null)
        {
            return new PhpMyAdminInstallationState(false, Path.Combine(Path.GetFullPath(xamppRoot), "phpMyAdmin"), null, "XAMPP 루트에 phpMyAdmin 폴더가 없습니다.");
        }

        var version = TryReadInstalledVersion(directory);
        return new PhpMyAdminInstallationState(true, directory, version, version is null ? "phpMyAdmin은 설치되어 있지만 버전을 판별하지 못했습니다." : null);
    }

    public async Task<PhpMyAdminReleaseInfo> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(VersionEndpoint, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = document.RootElement;
        var version = root.GetProperty("version").GetString();
        if (string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("phpMyAdmin 최신 버전 정보를 읽지 못했습니다.");

        DateTime? releaseDate = null;
        if (root.TryGetProperty("date", out var dateElement) && DateTime.TryParse(dateElement.GetString(), out var parsedDate)) releaseDate = parsedDate.Date;

        string? phpRange = null;
        string? databaseRange = null;
        if (root.TryGetProperty("releases", out var releases) && releases.ValueKind == JsonValueKind.Array)
        {
            foreach (var release in releases.EnumerateArray())
            {
                if (!string.Equals(release.GetProperty("version").GetString(), version, StringComparison.OrdinalIgnoreCase)) continue;
                if (release.TryGetProperty("php_versions", out var phpElement)) phpRange = phpElement.GetString();
                if (release.TryGetProperty("mysql_versions", out var dbElement)) databaseRange = dbElement.GetString();
                break;
            }
        }

        var fileName = $"phpMyAdmin-{version}-all-languages.zip";
        var baseUrl = $"https://files.phpmyadmin.net/phpMyAdmin/{version}/{fileName}";
        return new PhpMyAdminReleaseInfo(version, releaseDate, baseUrl, baseUrl + ".sha256", phpRange, databaseRange);
    }

    public PhpMyAdminCompatibility EvaluateCompatibility(PhpMyAdminReleaseInfo release, string? phpVersion, string? databaseVersion)
    {
        var warnings = new List<string>();
        var canUpdate = true;

        if (!TryParseVersion(phpVersion, out var php))
        {
            canUpdate = false;
            warnings.Add("PHP 버전을 확인하지 못해 phpMyAdmin 호환성을 보장할 수 없습니다.");
        }
        else if (php < new Version(7, 2))
        {
            canUpdate = false;
            warnings.Add($"phpMyAdmin {release.Version}은 PHP 7.2 이상이 필요합니다. 현재 PHP: {phpVersion}");
        }

        if (TryParseVersion(databaseVersion, out var database) && database < new Version(5, 5))
        {
            canUpdate = false;
            warnings.Add($"phpMyAdmin {release.Version}은 MySQL/MariaDB 5.5 이상이 필요합니다. 현재 DB: {databaseVersion}");
        }

        if (release.PhpVersionRange?.Contains('<') == true && TryGetExclusiveUpperBound(release.PhpVersionRange, out var upper) && TryParseVersion(phpVersion, out php) && php >= upper)
        {
            warnings.Add($"공식 버전 메타데이터의 PHP 권장 범위({release.PhpVersionRange})보다 새 PHP {phpVersion}를 사용 중입니다. 업데이트는 허용하지만 실제 phpMyAdmin 동작 확인을 권장합니다.");
        }

        var summary = canUpdate
            ? warnings.Count == 0 ? "현재 PHP/DB 버전에서 최신 안정판 업데이트를 진행할 수 있습니다." : "업데이트 가능하지만 호환성 주의사항이 있습니다."
            : "현재 XAMPP 구성에서는 최신 phpMyAdmin 업데이트를 진행할 수 없습니다.";
        return new PhpMyAdminCompatibility(canUpdate, warnings, summary);
    }

    public async Task<PhpMyAdminUpdateResult> UpdateAsync(
        string xamppRoot,
        PhpMyAdminReleaseInfo release,
        string? phpVersion,
        string? databaseVersion,
        IProgress<PhpMyAdminUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var state = Inspect(xamppRoot);
        if (!state.IsInstalled) throw new InvalidOperationException("현재 XAMPP에 phpMyAdmin이 설치되어 있지 않습니다.");
        if (string.IsNullOrWhiteSpace(state.Version)) throw new InvalidOperationException("현재 phpMyAdmin 버전을 확인할 수 없어 안전한 업데이트를 진행할 수 없습니다.");

        var compatibility = EvaluateCompatibility(release, phpVersion, databaseVersion);
        if (!compatibility.CanUpdate) throw new InvalidOperationException(string.Join(Environment.NewLine, compatibility.Warnings));
        if (CompareVersions(state.Version, release.Version) >= 0) throw new InvalidOperationException($"현재 phpMyAdmin {state.Version}은 최신 안정판 {release.Version} 이상입니다.");

        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XAMPP-Updater", "PhpMyAdmin");
        var packageRoot = Path.Combine(localRoot, "Packages");
        var stagingRoot = Path.Combine(localRoot, "Staging", Guid.NewGuid().ToString("N"));
        var backupRoot = Path.Combine(localRoot, "Backups");
        Directory.CreateDirectory(packageRoot);
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(backupRoot);

        var fileName = Path.GetFileName(new Uri(release.DownloadUrl).LocalPath);
        var packagePath = Path.Combine(packageRoot, fileName);
        var backupPath = Path.Combine(backupRoot, $"phpMyAdmin-{state.Version}-to-{release.Version}-{DateTime.Now:yyyyMMdd-HHmmss}");
        var oldSwapPath = state.DirectoryPath + ".update-old-" + Guid.NewGuid().ToString("N");
        string? actualSha = null;
        var swapped = false;

        try
        {
            Report(progress, "Execute", 5, $"phpMyAdmin {release.Version} 패키지를 다운로드하는 중...");
            await DownloadFileAsync(release.DownloadUrl, packagePath, progress, cancellationToken);

            Report(progress, "BackupVerify", 30, "공식 SHA256 값을 확인하는 중...");
            var expectedSha = await DownloadChecksumAsync(release.ChecksumUrl, cancellationToken);
            actualSha = await ComputeSha256Async(packagePath, cancellationToken);
            if (!string.Equals(expectedSha, actualSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"phpMyAdmin 패키지 SHA256 불일치: expected={expectedSha}, actual={actualSha}");

            Report(progress, "Execute", 38, "검증된 phpMyAdmin 패키지를 임시 폴더에 푸는 중...");
            ZipFile.ExtractToDirectory(packagePath, stagingRoot, overwriteFiles: false);
            var payloadRoot = FindPayloadRoot(stagingRoot);
            ValidatePayload(payloadRoot, release.Version);

            Report(progress, "BeforeSnapshot", 48, "기존 XAMPP phpMyAdmin 사용자 설정을 새 패키지에 이관하는 중...");
            PreserveUserFiles(state.DirectoryPath, payloadRoot);
            ValidatePhpConfigSyntax(xamppRoot, payloadRoot);

            Report(progress, "BeforeSnapshot", 58, "기존 phpMyAdmin 전체 롤백 백업을 생성하는 중...");
            CopyDirectory(state.DirectoryPath, backupPath);
            var backupVersion = TryReadInstalledVersion(backupPath);
            if (!string.Equals(backupVersion, state.Version, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("phpMyAdmin 롤백 백업의 버전 검증에 실패했습니다.");

            Report(progress, "Execute", 75, "기존 phpMyAdmin 폴더를 검증된 새 버전으로 교체하는 중...");
            Directory.Move(state.DirectoryPath, oldSwapPath);
            swapped = true;
            Directory.Move(payloadRoot, state.DirectoryPath);

            Report(progress, "AfterSnapshot", 90, "교체된 phpMyAdmin 설치를 검증하는 중...");
            ValidatePayload(state.DirectoryPath, release.Version);
            ValidatePhpConfigSyntax(xamppRoot, state.DirectoryPath);

            if (Directory.Exists(oldSwapPath)) Directory.Delete(oldSwapPath, recursive: true);
            swapped = false;
            Report(progress, "Completed", 100, $"phpMyAdmin {state.Version} → {release.Version} 업데이트 완료");
            return new PhpMyAdminUpdateResult(state.Version, release.Version, backupPath, actualSha ?? string.Empty);
        }
        catch
        {
            Report(progress, "Rollback", 0, "phpMyAdmin 업데이트 실패. 기존 설치를 자동 복원하는 중...");
            if (swapped)
            {
                try
                {
                    if (Directory.Exists(state.DirectoryPath)) Directory.Delete(state.DirectoryPath, recursive: true);
                    if (Directory.Exists(oldSwapPath)) Directory.Move(oldSwapPath, state.DirectoryPath);
                }
                catch { }
            }
            Report(progress, "Failed", 0, "phpMyAdmin 업데이트가 실패했습니다.");
            throw;
        }
        finally
        {
            try { if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true); } catch { }
        }
    }

    internal static string? TryReadInstalledVersion(string directory)
    {
        var candidates = new[]
        {
            Path.Combine(directory, "libraries", "classes", "Version.php"),
            Path.Combine(directory, "libraries", "VersionInformation.php"),
            Path.Combine(directory, "README"),
            Path.Combine(directory, "README.md"),
            Path.Combine(directory, "ChangeLog")
        };

        foreach (var path in candidates.Where(File.Exists))
        {
            try
            {
                var text = File.ReadAllText(path);
                var match = VersionConstantRegex().Match(text);
                if (match.Success) return match.Groups["version"].Value;
                match = PhpMyAdminTextVersionRegex().Match(text);
                if (match.Success) return match.Groups["version"].Value;
            }
            catch { }
        }
        return null;
    }

    internal static int CompareVersions(string left, string right)
    {
        if (TryParseVersion(left, out var leftVersion) && TryParseVersion(right, out var rightVersion)) return leftVersion.CompareTo(rightVersion);
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolvePhpMyAdminDirectory(string xamppRoot)
    {
        var root = Path.GetFullPath(xamppRoot);
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateDirectories(root).FirstOrDefault(path => string.Equals(Path.GetFileName(path), "phpMyAdmin", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task DownloadFileAsync(string url, string destination, IProgress<PhpMyAdminUpdateProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
        var buffer = new byte[128 * 1024];
        long received = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            var percent = total is > 0 ? 5 + (int)Math.Min(24, received * 24 / total.Value) : 15;
            progress?.Report(new PhpMyAdminUpdateProgress("Execute", percent, "phpMyAdmin 패키지 다운로드 중...", received, total));
        }
    }

    private static async Task<string> DownloadChecksumAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var match = Sha256Regex().Match(text);
        if (!match.Success) throw new InvalidDataException("phpMyAdmin SHA256 응답에서 해시를 찾지 못했습니다.");
        return match.Groups["sha"].Value.ToLowerInvariant();
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FindPayloadRoot(string stagingRoot)
    {
        if (File.Exists(Path.Combine(stagingRoot, "index.php"))) return stagingRoot;
        var candidates = Directory.EnumerateDirectories(stagingRoot).Where(path => File.Exists(Path.Combine(path, "index.php"))).ToArray();
        if (candidates.Length != 1) throw new InvalidDataException("phpMyAdmin ZIP 내부의 설치 루트를 하나로 확정하지 못했습니다.");
        return candidates[0];
    }

    private static void ValidatePayload(string payloadRoot, string expectedVersion)
    {
        var required = new[] { Path.Combine(payloadRoot, "index.php"), Path.Combine(payloadRoot, "libraries"), Path.Combine(payloadRoot, "vendor") };
        foreach (var path in required)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) throw new InvalidDataException($"phpMyAdmin 필수 파일/폴더가 없습니다: {Path.GetFileName(path)}");
        }
        var actualVersion = TryReadInstalledVersion(payloadRoot);
        if (!string.Equals(actualVersion, expectedVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"phpMyAdmin 패키지 버전 검증 실패: expected={expectedVersion}, actual={actualVersion ?? "unknown"}");
    }

    private static void PreserveUserFiles(string currentRoot, string newRoot)
    {
        foreach (var fileName in new[] { "config.inc.php", ".htaccess" })
        {
            var source = Path.Combine(currentRoot, fileName);
            if (File.Exists(source)) File.Copy(source, Path.Combine(newRoot, fileName), overwrite: true);
        }
        foreach (var directoryName in new[] { "upload", "save" })
        {
            var source = Path.Combine(currentRoot, directoryName);
            if (Directory.Exists(source)) CopyDirectory(source, Path.Combine(newRoot, directoryName));
        }
    }

    private static void ValidatePhpConfigSyntax(string xamppRoot, string phpMyAdminRoot)
    {
        var configPath = Path.Combine(phpMyAdminRoot, "config.inc.php");
        if (!File.Exists(configPath)) return;
        var phpExe = Path.Combine(Path.GetFullPath(xamppRoot), "php", "php.exe");
        if (!File.Exists(phpExe)) return;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = phpExe,
                Arguments = $"-l \"{configPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = phpMyAdminRoot
            }
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("phpMyAdmin config.inc.php PHP 구문 검사가 시간 초과되었습니다.");
        }
        if (process.ExitCode != 0) throw new InvalidDataException("기존 phpMyAdmin config.inc.php가 현재 PHP 구문 검사를 통과하지 못했습니다.\n" + stdout + stderr);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(value)) return false;
        var match = NumericVersionRegex().Match(value);
        if (!match.Success || !Version.TryParse(match.Value, out var parsed) || parsed is null) return false;
        version = parsed;
        return true;
    }

    private static bool TryGetExclusiveUpperBound(string range, out Version version)
    {
        version = new Version(0, 0);
        var match = ExclusiveUpperBoundRegex().Match(range);
        if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var parsed) || parsed is null) return false;
        version = parsed;
        return true;
    }

    private static void Report(IProgress<PhpMyAdminUpdateProgress>? progress, string stage, int percent, string message) => progress?.Report(new PhpMyAdminUpdateProgress(stage, percent, message));

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XAMPP-Updater/0.1 (+https://github.com/danhk0612/XAMPP-Updater)");
        return client;
    }

    [GeneratedRegex(@"(?:const\s+VERSION\s*=|VERSION\s*=)\s*['""](?<version>\d+\.\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionConstantRegex();

    [GeneratedRegex(@"phpMyAdmin[^\r\n]{0,80}?(?<version>\d+\.\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PhpMyAdminTextVersionRegex();

    [GeneratedRegex(@"(?<sha>[a-fA-F0-9]{64})")]
    private static partial Regex Sha256Regex();

    [GeneratedRegex(@"\d+\.\d+(?:\.\d+)?")]
    private static partial Regex NumericVersionRegex();

    [GeneratedRegex(@"<\s*(?<version>\d+\.\d+(?:\.\d+)?)")]
    private static partial Regex ExclusiveUpperBoundRegex();
}
