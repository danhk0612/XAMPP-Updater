using System.Net;
using System.Text.RegularExpressions;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IOnlineVersionCatalogService
{
    Task<OnlineVersionCatalog> GetLatestAsync(CancellationToken cancellationToken = default);
}

public sealed partial class OnlineVersionCatalogService : IOnlineVersionCatalogService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public async Task<OnlineVersionCatalog> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        const string apacheUrl = "https://httpd.apache.org/download.cgi";
        const string phpUrl = "https://www.php.net/downloads.php?os=windows";
        const string mariaDbUrl = "https://mariadb.com/downloads/";
        const string xamppUrl = "https://www.apachefriends.org/download.html";

        var apacheTask = GetStringAsync(apacheUrl, cancellationToken);
        var phpTask = GetStringAsync(phpUrl, cancellationToken);
        var mariaDbTask = GetStringAsync(mariaDbUrl, cancellationToken);
        var xamppTask = GetStringAsync(xamppUrl, cancellationToken);

        await Task.WhenAll(apacheTask, phpTask, mariaDbTask, xamppTask);

        var upstreamApache = ParseApacheLatest(await apacheTask);
        var upstreamPhp = ParsePhpLatest(await phpTask);
        var upstreamMariaDb = ParseMariaDbLatest(await mariaDbTask);
        var xampp = ParseXamppLatestBundle(await xamppTask);

        return new OnlineVersionCatalog(
            DateTimeOffset.Now,
            new[]
            {
                new OnlineComponentVersion(
                    XamppComponentType.Apache,
                    upstreamApache,
                    xampp.Apache,
                    apacheUrl,
                    "Apache 공식 프로젝트는 Windows 바이너리를 직접 배포하지 않습니다. 실제 적용 전 Windows 빌드 공급원과 모듈/런타임 호환성 검증이 필요합니다."),
                new OnlineComponentVersion(
                    XamppComponentType.Php,
                    upstreamPhp,
                    xampp.Php,
                    phpUrl,
                    "XAMPP의 Apache 모듈 방식에서는 Windows x64 Thread Safe 빌드를 기본 후보로 봅니다. PHP 확장 ABI와 Apache 연동 구성을 함께 확인해야 합니다."),
                new OnlineComponentVersion(
                    XamppComponentType.MariaDb,
                    upstreamMariaDb,
                    xampp.MariaDb,
                    mariaDbUrl,
                    "MariaDB는 데이터 디렉터리 형식과 업그레이드 절차가 있으므로 최신 메이저 버전으로 직접 교체하지 않습니다. 지원되는 업그레이드 경로를 별도 판정해야 합니다.")
            });
    }

    private static async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("XAMPP-Updater/0.1 (+https://github.com/danhk0612/XAMPP-Updater)");
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    internal static string? ParseApacheLatest(string html)
    {
        var text = NormalizeHtml(html);
        var matches = ApacheVersionRegex().Matches(text)
            .Select(match => match.Groups["version"].Value)
            .Where(version => Version.TryParse(version, out _));
        return MaxVersion(matches);
    }

    internal static string? ParsePhpLatest(string html)
    {
        var text = NormalizeHtml(html);
        var matches = PhpVersionRegex().Matches(text)
            .Select(match => match.Groups["version"].Value)
            .Where(version => Version.TryParse(version, out _));
        return MaxVersion(matches);
    }

    internal static string? ParseMariaDbLatest(string html)
    {
        var text = NormalizeHtml(html);
        var matches = MariaDbVersionRegex().Matches(text)
            .Select(match => match.Groups["version"].Value)
            .Where(version => Version.TryParse(version, out _));
        return MaxVersion(matches);
    }

    internal static XamppBundleVersions ParseXamppLatestBundle(string html)
    {
        var text = NormalizeHtml(html);

        var apache = MaxVersion(XamppApacheRegex().Matches(text)
            .Select(match => match.Groups["version"].Value));
        var php = MaxVersion(XamppPhpRegex().Matches(text)
            .Select(match => match.Groups["version"].Value));
        var mariaDb = MaxVersion(XamppMariaDbRegex().Matches(text)
            .Select(match => match.Groups["version"].Value));

        return new XamppBundleVersions(apache, php, mariaDb);
    }

    private static string NormalizeHtml(string html)
    {
        var withoutTags = HtmlTagRegex().Replace(html, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex().Replace(decoded, " ");
    }

    private static string? MaxVersion(IEnumerable<string> versions)
    {
        return versions
            .Select(value => new { Value = value, Parsed = Version.TryParse(value, out var version) ? version : null })
            .Where(item => item.Parsed is not null)
            .OrderByDescending(item => item.Parsed)
            .Select(item => item.Value)
            .FirstOrDefault();
    }

    internal sealed record XamppBundleVersions(string? Apache, string? Php, string? MariaDb);

    [GeneratedRegex(@"Apache HTTP Server\s+(?<version>2\.4\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ApacheVersionRegex();

    [GeneratedRegex(@"PHP\s+\d+\.\d+\s*\((?<version>\d+\.\d+\.\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex PhpVersionRegex();

    [GeneratedRegex(@"MariaDB Community Server\s+(?<version>\d+\.\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MariaDbVersionRegex();

    [GeneratedRegex(@"Apache\s+(?<version>2\.4\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex XamppApacheRegex();

    [GeneratedRegex(@"XAMPP for Windows\s+(?<version>\d+\.\d+\.\d+)|PHP\s+(?<version>\d+\.\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex XamppPhpRegex();

    [GeneratedRegex(@"MariaDB\s+(?<version>\d+\.\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex XamppMariaDbRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
