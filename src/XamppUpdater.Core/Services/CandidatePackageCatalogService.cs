using System.Net;
using System.Text.RegularExpressions;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface ICandidatePackageCatalogService
{
    Task<CandidatePackageCatalog> GetCandidatesAsync(
        XamppInstallation installation,
        InstallationCompatibilityProfile profile,
        CancellationToken cancellationToken = default);
}

public sealed partial class CandidatePackageCatalogService : ICandidatePackageCatalogService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<CandidatePackageCatalog> GetCandidatesAsync(
        XamppInstallation installation,
        InstallationCompatibilityProfile profile,
        CancellationToken cancellationToken = default)
    {
        var apacheVersion = installation.Components.First(component => component.Type == XamppComponentType.Apache).Version;
        var phpVersion = installation.Components.First(component => component.Type == XamppComponentType.Php).Version;
        var mariaDbVersion = installation.Components.First(component => component.Type == XamppComponentType.MariaDb).Version;

        var apacheTask = ResolveApacheAsync(apacheVersion, profile, cancellationToken);
        var phpTask = ResolvePhpAsync(phpVersion, profile, cancellationToken);
        var mariaDbTask = ResolveMariaDbAsync(mariaDbVersion, profile, cancellationToken);

        await Task.WhenAll(apacheTask, phpTask, mariaDbTask);

        return new CandidatePackageCatalog(
            DateTimeOffset.Now,
            new[] { await apacheTask, await phpTask, await mariaDbTask });
    }

    internal static PackageCandidate ParseApacheLoungeCandidate(
        string html,
        BinaryArchitecture architecture,
        string? installedVersion)
    {
        var expectedArch = architecture switch
        {
            BinaryArchitecture.X64 => "Win64",
            BinaryArchitecture.X86 => "win32",
            _ => null
        };

        if (expectedArch is null)
        {
            return Unavailable(XamppComponentType.Apache, architecture, "현재 아키텍처에 맞는 Apache Lounge 후보 규칙이 없습니다.");
        }

        var candidate = ApacheLoungeZipRegex().Matches(html)
            .Select(match => new
            {
                Href = WebUtility.HtmlDecode(match.Groups["href"].Value),
                FileName = match.Groups["file"].Value,
                VersionText = match.Groups["version"].Value,
                Architecture = match.Groups["arch"].Value,
                Compiler = match.Groups["compiler"].Value,
                Parsed = Version.TryParse(match.Groups["version"].Value, out var version) ? version : null
            })
            .Where(item => item.Parsed is not null && item.Architecture.Equals(expectedArch, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Parsed)
            .FirstOrDefault();

        if (candidate is null)
        {
            return Unavailable(XamppComponentType.Apache, architecture, "Apache Lounge에서 현재 아키텍처의 Windows ZIP을 찾지 못했습니다.");
        }

        var sameSeries = SameMajorMinor(installedVersion, candidate.VersionText);
        var downloadUrl = new Uri(new Uri("https://www.apachelounge.com/download/"), candidate.Href).ToString();

        return new PackageCandidate(
            XamppComponentType.Apache,
            candidate.VersionText,
            candidate.FileName,
            downloadUrl,
            architecture,
            candidate.Compiler.ToUpperInvariant(),
            null,
            null,
            "https://www.apachelounge.com/download/",
            sameSeries ? CandidateCompatibilityStatus.Assisted : CandidateCompatibilityStatus.ManualReview,
            sameSeries
                ? "같은 Apache 2.4 계열의 Windows 바이너리입니다. 자동 백업 후 새 패키지와 기존 모듈/설정을 비교하고, PHP/추가 모듈 ABI 검사를 통과한 항목은 자동 교체하며 불일치 항목만 사용자 확인을 받는 보조 업데이트 대상으로 처리합니다."
                : "현재 Apache와 계열이 다릅니다. 패키지 구조와 설정 차이를 비교해 마이그레이션 항목을 만든 뒤 사용자 확인을 거치는 수동 검토 업데이트 대상으로 처리합니다.");
    }

    internal static PackageCandidate ParsePhpArchiveCandidate(
        string html,
        string? installedVersion,
        InstallationCompatibilityProfile profile)
    {
        if (!TryGetSeries(installedVersion, out var series))
        {
            return Unavailable(XamppComponentType.Php, profile.PhpArchitecture, "현재 PHP 계열을 확인할 수 없습니다.");
        }

        var expectedArch = profile.PhpArchitecture switch
        {
            BinaryArchitecture.X64 => "x64",
            BinaryArchitecture.X86 => "x86",
            _ => null
        };

        if (expectedArch is null)
        {
            return Unavailable(XamppComponentType.Php, profile.PhpArchitecture, "현재 PHP 아키텍처에 맞는 Windows 패키지 규칙이 없습니다.");
        }

        var expectedCompiler = NormalizePhpCompiler(profile.Php.Compiler);
        var requireThreadSafe = profile.ApachePhpIntegration.IsModuleLoaded || profile.Php.ThreadSafe == true;

        var candidate = PhpArchiveZipRegex().Matches(html)
            .Select(match => new
            {
                FileName = match.Groups["file"].Value,
                VersionText = match.Groups["version"].Value,
                IsNts = match.Groups["nts"].Success,
                Compiler = match.Groups["compiler"].Value.ToLowerInvariant(),
                Architecture = match.Groups["arch"].Value.ToLowerInvariant(),
                Parsed = Version.TryParse(match.Groups["version"].Value, out var version) ? version : null
            })
            .Where(item => item.Parsed is not null)
            .Where(item => item.VersionText.StartsWith(series + ".", StringComparison.OrdinalIgnoreCase))
            .Where(item => item.Architecture == expectedArch)
            .Where(item => requireThreadSafe ? !item.IsNts : true)
            .Where(item => expectedCompiler is null || item.Compiler.Equals(expectedCompiler, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Parsed)
            .FirstOrDefault();

        if (candidate is null)
        {
            return Unavailable(XamppComponentType.Php, profile.PhpArchitecture, $"PHP Windows archive에서 {series} 계열의 현재 환경과 일치하는 ZIP을 찾지 못했습니다.");
        }

        var downloadUrl = $"https://windows.php.net/downloads/releases/archives/{candidate.FileName}";
        return new PackageCandidate(
            XamppComponentType.Php,
            candidate.VersionText,
            candidate.FileName,
            downloadUrl,
            profile.PhpArchitecture,
            candidate.Compiler.ToUpperInvariant(),
            !candidate.IsNts,
            null,
            null,
            CandidateCompatibilityStatus.Assisted,
            "현재 PHP와 같은 major.minor, 아키텍처, Thread Safe, compiler 조건의 패치 후보입니다. 공식 archive에서 SHA256을 직접 확보할 수 없어 완전 자동 적용은 하지 않지만, 사용자가 공식 패키지를 확인하거나 직접 지정하면 앱이 파일/ABI/확장 목록과 설정 차이를 검사한 뒤 백업·교체·설정 병합을 자동화하는 보조 업데이트 대상으로 처리합니다.");
    }

    internal static PackageCandidate ParseMariaDbSeriesCandidate(
        string seriesHtml,
        string? installedVersion,
        BinaryArchitecture architecture)
    {
        if (!TryGetSeries(installedVersion, out var series))
        {
            return Unavailable(XamppComponentType.MariaDb, architecture, "현재 MariaDB 계열을 확인할 수 없습니다.");
        }

        if (architecture != BinaryArchitecture.X64)
        {
            return Unavailable(XamppComponentType.MariaDb, architecture, "현재 구현은 MariaDB 공식 winx64 ZIP 후보만 판정합니다.");
        }

        var version = MariaDbSeriesVersionRegex().Matches(WebUtility.HtmlDecode(seriesHtml))
            .Select(match => match.Groups["version"].Value)
            .Where(value => value.StartsWith(series + ".", StringComparison.OrdinalIgnoreCase))
            .Select(value => new { Value = value, Parsed = Version.TryParse(value, out var parsed) ? parsed : null })
            .Where(item => item.Parsed is not null)
            .OrderByDescending(item => item.Parsed)
            .Select(item => item.Value)
            .FirstOrDefault();

        if (version is null)
        {
            return Unavailable(XamppComponentType.MariaDb, architecture, $"MariaDB 공식 다운로드에서 {series} 계열 버전을 찾지 못했습니다.");
        }

        var fileName = $"mariadb-{version}-winx64.zip";
        var packagePage = $"https://dlm.mariadb.com/browse/mariadb_server/{version}/winx64-packages/";

        return new PackageCandidate(
            XamppComponentType.MariaDb,
            version,
            fileName,
            packagePage,
            architecture,
            null,
            null,
            null,
            packagePage,
            CandidateCompatibilityStatus.Assisted,
            $"현재와 같은 MariaDB {series} 계열의 최신 공식 winx64 ZIP 후보입니다. 다운로드 단계에서 공식 SHA256/PGP를 검증하고, 데이터 전체 백업·설정 비교·바이너리 교체·mariadb-upgrade 실행 및 결과 확인까지 순차 자동화하는 보조 업데이트 대상으로 처리합니다.");
    }

    private async Task<PackageCandidate> ResolveApacheAsync(
        string? installedVersion,
        InstallationCompatibilityProfile profile,
        CancellationToken cancellationToken)
    {
        try
        {
            var html = await GetStringAsync("https://www.apachelounge.com/download/", cancellationToken);
            return ParseApacheLoungeCandidate(html, profile.ApacheArchitecture, installedVersion);
        }
        catch (Exception ex)
        {
            return Unavailable(XamppComponentType.Apache, profile.ApacheArchitecture, $"Apache 후보 조회 실패: {ex.Message}");
        }
    }

    private async Task<PackageCandidate> ResolvePhpAsync(
        string? installedVersion,
        InstallationCompatibilityProfile profile,
        CancellationToken cancellationToken)
    {
        try
        {
            var html = await GetStringAsync("https://windows.php.net/downloads/releases/archives/", cancellationToken);
            return ParsePhpArchiveCandidate(html, installedVersion, profile);
        }
        catch (Exception ex)
        {
            return Unavailable(XamppComponentType.Php, profile.PhpArchitecture, $"PHP 후보 조회 실패: {ex.Message}");
        }
    }

    private async Task<PackageCandidate> ResolveMariaDbAsync(
        string? installedVersion,
        InstallationCompatibilityProfile profile,
        CancellationToken cancellationToken)
    {
        if (!TryGetSeries(installedVersion, out var series))
        {
            return Unavailable(XamppComponentType.MariaDb, profile.MariaDbArchitecture, "현재 MariaDB 계열을 확인할 수 없습니다.");
        }

        try
        {
            var html = await GetStringAsync($"https://dlm.mariadb.com/browse/mariadb_server/{series}/", cancellationToken);
            return ParseMariaDbSeriesCandidate(html, installedVersion, profile.MariaDbArchitecture);
        }
        catch (Exception ex)
        {
            return Unavailable(XamppComponentType.MariaDb, profile.MariaDbArchitecture, $"MariaDB 후보 조회 실패: {ex.Message}");
        }
    }

    private static async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("XAMPP-Updater/0.2 (+https://github.com/danhk0612/XAMPP-Updater)");
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static bool TryGetSeries(string? version, out string series)
    {
        if (Version.TryParse(version, out var parsed))
        {
            series = $"{parsed.Major}.{parsed.Minor}";
            return true;
        }

        series = string.Empty;
        return false;
    }

    private static bool SameMajorMinor(string? left, string? right)
    {
        return Version.TryParse(left, out var l) && Version.TryParse(right, out var r) && l.Major == r.Major && l.Minor == r.Minor;
    }

    private static string? NormalizePhpCompiler(string? compiler)
    {
        if (string.IsNullOrWhiteSpace(compiler))
        {
            return null;
        }

        var match = CompilerVersionRegex().Match(compiler);
        if (!match.Success)
        {
            return null;
        }

        var number = match.Groups["version"].Value;
        return int.TryParse(number, out var parsed) && parsed <= 15 ? $"vc{parsed}" : $"vs{number}";
    }

    private static PackageCandidate Unavailable(XamppComponentType type, BinaryArchitecture architecture, string reason)
    {
        return new PackageCandidate(
            type,
            null,
            null,
            null,
            architecture,
            null,
            null,
            null,
            null,
            CandidateCompatibilityStatus.Unavailable,
            reason);
    }

    [GeneratedRegex("href=[\"'](?<href>[^\"']*(?<file>httpd-(?<version>2\\.4\\.\\d+)(?:-[0-9]+)?-(?<arch>Win64|win32)-(?<compiler>VS\\d+)\\.zip))[^\"']*[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex ApacheLoungeZipRegex();

    [GeneratedRegex("(?<file>php-(?<version>\\d+\\.\\d+\\.\\d+)(?:-[0-9]+)?-(?<nts>nts-)?Win32-(?<compiler>vc\\d+|vs\\d+)-(?<arch>x64|x86)\\.zip)", RegexOptions.IgnoreCase)]
    private static partial Regex PhpArchiveZipRegex();

    [GeneratedRegex("Community Server\\s+(?<version>\\d+\\.\\d+\\.\\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MariaDbSeriesVersionRegex();

    [GeneratedRegex("(?:MSVC|Visual C\\+\\+|VC|VS)\\s*(?<version>\\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CompilerVersionRegex();
}
