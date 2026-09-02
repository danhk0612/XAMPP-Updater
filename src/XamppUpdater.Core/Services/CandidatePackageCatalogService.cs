using System.Net;
using System.Text.Json;
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

        var candidates = ApacheLoungeZipRegex().Matches(html)
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

        if (candidates is null)
        {
            return Unavailable(XamppComponentType.Apache, architecture, "Apache Lounge에서 현재 아키텍처의 Windows ZIP을 찾지 못했습니다.");
        }

        var sameSeries = SameMajorMinor(installedVersion, candidates.VersionText);
        var downloadUrl = new Uri(new Uri("https://www.apachelounge.com/download/"), candidates.Href).ToString();

        return new PackageCandidate(
            XamppComponentType.Apache,
            candidates.VersionText,
            candidates.FileName,
            downloadUrl,
            architecture,
            candidates.Compiler.ToUpperInvariant(),
            null,
            null,
            "https://www.apachelounge.com/download/",
            sameSeries ? CandidateCompatibilityStatus.Conditional : CandidateCompatibilityStatus.Blocked,
            sameSeries
                ? "같은 Apache 2.4 계열의 Windows 바이너리입니다. Apache Lounge는 구형 VS 모듈의 상위 VS 빌드 호환성을 안내하지만, XAMPP의 PHP 모듈/추가 모듈 ABI와 VC++ 런타임을 실제 ZIP 기준으로 확인해야 합니다."
                : "현재 Apache와 계열이 달라 자동 적용하지 않습니다.");
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

        var candidates = PhpArchiveZipRegex().Matches(html)
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

        if (candidates is null)
        {
            return Unavailable(XamppComponentType.Php, profile.PhpArchitecture, $"PHP Windows archive에서 {series} 계열의 현재 환경과 일치하는 ZIP을 찾지 못했습니다.");
        }

        var downloadUrl = $"https://windows.php.net/downloads/releases/archives/{candidates.FileName}";
        return new PackageCandidate(
            XamppComponentType.Php,
            candidates.VersionText,
            candidates.FileName,
            downloadUrl,
            profile.PhpArchitecture,
            candidates.Compiler.ToUpperInvariant(),
            !candidates.IsNts,
            null,
            null,
            CandidateCompatibilityStatus.Blocked,
            "현재 PHP와 같은 major.minor, 아키텍처, Thread Safe, compiler 조건의 패치 후보입니다. 그러나 과거 archive 목록에서는 이 후보의 SHA256을 확보하지 못했으므로 자동 다운로드/적용 대상에서는 차단합니다.");
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
            CandidateCompatibilityStatus.Conditional,
            $"현재와 같은 MariaDB {series} 계열의 최신 공식 winx64 ZIP 후보입니다. 공식 패키지 페이지에 SHA256 manifest와 PGP 서명이 제공되므로 실제 다운로드 단계에서 검증할 수 있습니다. 백업과 mariadb-upgrade 검증은 여전히 필요합니다.");
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
