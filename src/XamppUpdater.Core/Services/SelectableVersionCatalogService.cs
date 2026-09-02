using System.Net;
using System.Text.RegularExpressions;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface ISelectableVersionCatalogService
{
    Task<SelectableVersionCatalog> GetAsync(
        XamppInstallation installation,
        InstallationCompatibilityProfile profile,
        CancellationToken cancellationToken = default);
}

public sealed partial class SelectableVersionCatalogService : ISelectableVersionCatalogService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    public async Task<SelectableVersionCatalog> GetAsync(
        XamppInstallation installation,
        InstallationCompatibilityProfile profile,
        CancellationToken cancellationToken = default)
    {
        var apacheCurrent = installation.Components.First(item => item.Type == XamppComponentType.Apache).Version;
        var phpCurrent = installation.Components.First(item => item.Type == XamppComponentType.Php).Version;
        var mariaDbCurrent = installation.Components.First(item => item.Type == XamppComponentType.MariaDb).Version;

        var apacheTask = GetApacheEntriesAsync(apacheCurrent, cancellationToken);
        var phpTask = GetPhpEntriesAsync(phpCurrent, profile, cancellationToken);
        var mariaDbTask = GetMariaDbEntriesAsync(mariaDbCurrent, profile.MariaDbArchitecture, cancellationToken);

        await Task.WhenAll(apacheTask, phpTask, mariaDbTask);

        return new SelectableVersionCatalog(
            DateTimeOffset.Now,
            (await apacheTask)
                .Concat(await phpTask)
                .Concat(await mariaDbTask)
                .ToArray());
    }

    internal static IReadOnlyList<SelectableVersionEntry> ParseApacheArchiveVersions(string html, string? currentVersion)
    {
        var current = TryVersion(currentVersion);
        return ApacheArchiveVersionRegex().Matches(WebUtility.HtmlDecode(html))
            .Select(match => match.Groups["version"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new { Value = value, Parsed = TryVersion(value) })
            .Where(item => item.Parsed is not null && (current is null || item.Parsed > current))
            .OrderByDescending(item => item.Parsed)
            .Select(item => new SelectableVersionEntry(
                XamppComponentType.Apache,
                item.Value,
                "ASF 공식 릴리스",
                null,
                null))
            .ToArray();
    }

    internal static IReadOnlyList<SelectableVersionEntry> ParsePhpArchiveVersions(
        string html,
        string? currentVersion,
        BinaryArchitecture architecture,
        bool requireThreadSafe)
    {
        var current = TryVersion(currentVersion);
        var expectedArch = architecture switch
        {
            BinaryArchitecture.X64 => "x64",
            BinaryArchitecture.X86 => "x86",
            _ => null
        };

        if (expectedArch is null)
        {
            return Array.Empty<SelectableVersionEntry>();
        }

        return PhpArchiveZipRegex().Matches(WebUtility.HtmlDecode(html))
            .Select(match => new
            {
                FileName = match.Groups["file"].Value,
                VersionText = match.Groups["version"].Value,
                IsNts = match.Groups["nts"].Success,
                Architecture = match.Groups["arch"].Value,
                Parsed = TryVersion(match.Groups["version"].Value)
            })
            .Where(item => item.Parsed is not null && (current is null || item.Parsed > current))
            .Where(item => item.Architecture.Equals(expectedArch, StringComparison.OrdinalIgnoreCase))
            .Where(item => !requireThreadSafe || !item.IsNts)
            .GroupBy(item => item.VersionText, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(item => item.IsNts)
                .ThenByDescending(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(item => item.Parsed)
            .Select(item => new SelectableVersionEntry(
                XamppComponentType.Php,
                item.VersionText,
                "PHP Windows 공식 archive",
                $"https://downloads.php.net/~windows/releases/archives/{item.FileName}",
                item.FileName))
            .ToArray();
    }

    internal static IReadOnlyList<MariaDbSeriesEntry> ParseMariaDbSeries(string html, string? currentVersion)
    {
        var current = TryVersion(currentVersion);
        return MariaDbSeriesRegex().Matches(WebUtility.HtmlDecode(html))
            .Select(match => new MariaDbSeriesEntry(
                match.Groups["series"].Value,
                match.Groups["eol"].Success))
            .DistinctBy(item => item.Series, StringComparer.OrdinalIgnoreCase)
            .Where(item =>
            {
                var parsed = TryVersion(item.Series + ".0");
                if (parsed is null)
                {
                    return false;
                }

                return current is null || parsed.Major > current.Major ||
                       (parsed.Major == current.Major && parsed.Minor >= current.Minor);
            })
            .OrderBy(item => TryVersion(item.Series + ".0"))
            .ToArray();
    }

    internal static IReadOnlyList<SelectableVersionEntry> ParseMariaDbSeriesVersions(
        string html,
        MariaDbSeriesEntry series,
        string? currentVersion,
        BinaryArchitecture architecture)
    {
        if (architecture != BinaryArchitecture.X64)
        {
            return Array.Empty<SelectableVersionEntry>();
        }

        var current = TryVersion(currentVersion);
        return MariaDbVersionRegex().Matches(WebUtility.HtmlDecode(html))
            .Select(match => match.Groups["version"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new { Value = value, Parsed = TryVersion(value) })
            .Where(item => item.Parsed is not null && (current is null || item.Parsed > current))
            .OrderByDescending(item => item.Parsed)
            .Select(item => new SelectableVersionEntry(
                XamppComponentType.MariaDb,
                item.Value,
                series.IsEol ? "MariaDB 공식 (EOL 계열)" : "MariaDB 공식",
                $"https://dlm.mariadb.com/browse/mariadb_server/{item.Value}/winx64-packages/",
                $"mariadb-{item.Value}-winx64.zip",
                series.IsEol))
            .ToArray();
    }

    private async Task<IReadOnlyList<SelectableVersionEntry>> GetApacheEntriesAsync(
        string? currentVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var html = await GetStringAsync("https://archive.apache.org/dist/httpd/", cancellationToken);
            return ParseApacheArchiveVersions(html, currentVersion);
        }
        catch
        {
            return Array.Empty<SelectableVersionEntry>();
        }
    }

    private async Task<IReadOnlyList<SelectableVersionEntry>> GetPhpEntriesAsync(
        string? currentVersion,
        InstallationCompatibilityProfile profile,
        CancellationToken cancellationToken)
    {
        try
        {
            var html = await GetStringAsync("https://downloads.php.net/~windows/releases/archives/", cancellationToken);
            return ParsePhpArchiveVersions(
                html,
                currentVersion,
                profile.PhpArchitecture,
                profile.ApachePhpIntegration.IsModuleLoaded || profile.Php.ThreadSafe == true);
        }
        catch
        {
            return Array.Empty<SelectableVersionEntry>();
        }
    }

    private async Task<IReadOnlyList<SelectableVersionEntry>> GetMariaDbEntriesAsync(
        string? currentVersion,
        BinaryArchitecture architecture,
        CancellationToken cancellationToken)
    {
        if (architecture != BinaryArchitecture.X64)
        {
            return Array.Empty<SelectableVersionEntry>();
        }

        try
        {
            var rootHtml = await GetStringAsync("https://dlm.mariadb.com/browse/mariadb_server/", cancellationToken);
            var series = ParseMariaDbSeries(rootHtml, currentVersion);
            var tasks = series.Select(async item =>
            {
                try
                {
                    var html = await GetStringAsync($"https://dlm.mariadb.com/browse/mariadb_server/{item.Series}/", cancellationToken);
                    return ParseMariaDbSeriesVersions(html, item, currentVersion, architecture);
                }
                catch
                {
                    return (IReadOnlyList<SelectableVersionEntry>)Array.Empty<SelectableVersionEntry>();
                }
            }).ToArray();

            var results = await Task.WhenAll(tasks);
            return results
                .SelectMany(item => item)
                .GroupBy(item => item.Version, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(item => TryVersion(item.Version))
                .ToArray();
        }
        catch
        {
            return Array.Empty<SelectableVersionEntry>();
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

    private static Version? TryVersion(string? value) => Version.TryParse(value, out var parsed) ? parsed : null;

    internal sealed record MariaDbSeriesEntry(string Series, bool IsEol);

    [GeneratedRegex(@"(?:httpd-|CHANGES_)(?<version>2\.4\.\d+)(?:\.tar\.(?:bz2|gz|xz))?", RegexOptions.IgnoreCase)]
    private static partial Regex ApacheArchiveVersionRegex();

    [GeneratedRegex(@"(?<file>php-(?<version>\d+\.\d+\.\d+)(?:-[0-9]+)?-(?<nts>nts-)?Win32-(?:vc\d+|vs\d+)-(?<arch>x64|x86)\.zip)", RegexOptions.IgnoreCase)]
    private static partial Regex PhpArchiveZipRegex();

    [GeneratedRegex(@"Community Server\s+(?<series>\d+\.\d+)(?<eol>\s*\(EOL\))?", RegexOptions.IgnoreCase)]
    private static partial Regex MariaDbSeriesRegex();

    [GeneratedRegex(@"Community Server\s+(?<version>\d+\.\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MariaDbVersionRegex();
}
