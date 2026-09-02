using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IPhpExternalExtensionInstaller
{
    Task<PhpExternalExtensionInstallResult> InstallMissingAsync(
        string currentIniPath,
        string newPhpRoot,
        string targetVersion,
        bool threadSafe,
        BinaryArchitecture architecture,
        CancellationToken cancellationToken = default);
}

public sealed partial class PhpExternalExtensionInstaller : IPhpExternalExtensionInstaller
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(2) };

    public async Task<PhpExternalExtensionInstallResult> InstallMissingAsync(
        string currentIniPath,
        string newPhpRoot,
        string targetVersion,
        bool threadSafe,
        BinaryArchitecture architecture,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(currentIniPath))
        {
            return new PhpExternalExtensionInstallResult(Array.Empty<string>(), Array.Empty<string>());
        }

        var phpSeries = GetMajorMinor(targetVersion);
        if (phpSeries is null)
        {
            return new PhpExternalExtensionInstallResult(Array.Empty<string>(), new[] { "외부 확장 복원: 대상 PHP 계열을 판정하지 못했습니다." });
        }

        var extRoot = Path.Combine(newPhpRoot, "ext");
        Directory.CreateDirectory(extRoot);
        var available = Directory.EnumerateFiles(extRoot, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var requested = EnumerateRequestedExtensions(File.ReadAllText(currentIniPath))
            .Select(PhpIniMigrationService.NormalizeExtensionName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => PhpIniMigrationService.ResolveExtensionDll(name, available) is null)
            .Take(20)
            .ToArray();

        var installed = new List<string>();
        var warnings = new List<string>();
        foreach (var packageName in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await TryInstallFromPeclAsync(
                    packageName,
                    phpSeries,
                    threadSafe,
                    architecture,
                    newPhpRoot,
                    extRoot,
                    cancellationToken);
                if (result is null)
                {
                    warnings.Add($"외부 호환 확장을 찾지 못함: {packageName}");
                    continue;
                }

                installed.Add(result);
                available.Add(result);
            }
            catch (Exception ex)
            {
                warnings.Add($"외부 확장 자동 복원 실패 ({packageName}): {ex.Message}");
            }
        }

        return new PhpExternalExtensionInstallResult(installed, warnings);
    }

    private static async Task<string?> TryInstallFromPeclAsync(
        string packageName,
        string phpSeries,
        bool threadSafe,
        BinaryArchitecture architecture,
        string phpRoot,
        string extRoot,
        CancellationToken cancellationToken)
    {
        var packageUrl = $"https://pecl.php.net/package/{Uri.EscapeDataString(packageName)}";
        var packageHtml = await GetStringAsync(packageUrl, cancellationToken);
        var release = ParseLatestStableRelease(packageHtml);
        if (release is null) return null;

        var windowsUrl = $"https://pecl.php.net/package/{Uri.EscapeDataString(packageName)}/{release}/windows";
        var windowsHtml = await GetStringAsync(windowsUrl, cancellationToken);
        var archText = architecture switch
        {
            BinaryArchitecture.X64 => "x64",
            BinaryArchitecture.X86 => "x86",
            _ => null
        };
        if (archText is null) return null;

        var safetyText = threadSafe ? "Thread Safe (TS)" : "Non Thread Safe (NTS)";
        var expectedText = $"{phpSeries} {safetyText} {archText}";
        var downloadUrl = ParseCompatibleWindowsDownload(windowsUrl, windowsHtml, expectedText);
        if (downloadUrl is null) return null;

        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XamppUpdater",
            "ExternalExtensions",
            packageName,
            release);
        Directory.CreateDirectory(cacheRoot);
        var zipPath = Path.Combine(cacheRoot, Path.GetFileName(new Uri(downloadUrl).LocalPath));
        if (!File.Exists(zipPath))
        {
            await DownloadAsync(downloadUrl, zipPath, cancellationToken);
        }

        using var archive = ZipFile.OpenRead(zipPath);
        var extensionEntry = archive.Entries.FirstOrDefault(entry =>
            Path.GetFileName(entry.FullName).Equals($"php_{packageName}.dll", StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(entry =>
                Path.GetFileName(entry.FullName).StartsWith("php_", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(entry.FullName).EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        if (extensionEntry is null) return null;

        if (!MatchesArchitecture(extensionEntry, architecture))
        {
            throw new InvalidDataException("다운로드한 PECL 확장 DLL 아키텍처가 현재 PHP와 다릅니다.");
        }

        foreach (var entry in archive.Entries.Where(entry => entry.Length > 0 && entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            var fileName = Path.GetFileName(entry.FullName);
            if (string.IsNullOrWhiteSpace(fileName)) continue;
            var destinationRoot = fileName.StartsWith("php_", StringComparison.OrdinalIgnoreCase) ? extRoot : phpRoot;
            var destination = Path.Combine(destinationRoot, fileName);
            entry.ExtractToFile(destination, overwrite: true);
        }

        return Path.GetFileName(extensionEntry.FullName);
    }

    internal static string? ParseLatestStableRelease(string html)
    {
        return StableReleaseRegex().Matches(html)
            .Select(match => match.Groups["version"].Value)
            .FirstOrDefault();
    }

    internal static string? ParseCompatibleWindowsDownload(string pageUrl, string html, string expectedText)
    {
        foreach (Match match in AnchorRegex().Matches(html))
        {
            var text = Regex.Replace(match.Groups["text"].Value, @"\s+", " ").Trim();
            if (!string.Equals(text, expectedText, StringComparison.OrdinalIgnoreCase)) continue;
            return new Uri(new Uri(pageUrl), System.Net.WebUtility.HtmlDecode(match.Groups["href"].Value)).AbsoluteUri;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateRequestedExtensions(string ini)
    {
        foreach (var raw in ini.Replace("\r\n", "\n").Split('\n'))
        {
            var match = ExtensionRegex().Match(raw);
            if (!match.Success) continue;
            yield return match.Groups["value"].Value.Trim().Trim('"', '\'');
        }
    }

    private static string? GetMajorMinor(string version)
    {
        var match = Regex.Match(version, @"^(?<major>\d+)\.(?<minor>\d+)");
        return match.Success ? $"{match.Groups["major"].Value}.{match.Groups["minor"].Value}" : null;
    }

    private static bool MatchesArchitecture(ZipArchiveEntry entry, BinaryArchitecture expected)
    {
        if (expected == BinaryArchitecture.Unknown) return true;
        try
        {
            using var input = entry.Open();
            using var memory = new MemoryStream();
            input.CopyTo(memory);
            memory.Position = 0;
            using var reader = new PEReader(memory);
            var actual = reader.PEHeaders.CoffHeader.Machine switch
            {
                Machine.I386 => BinaryArchitecture.X86,
                Machine.Amd64 => BinaryArchitecture.X64,
                Machine.Arm64 => BinaryArchitecture.Arm64,
                _ => BinaryArchitecture.Unknown
            };
            return actual == expected;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("XAMPP-Updater/0.4 (+https://github.com/danhk0612/XAMPP-Updater)");
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static async Task DownloadAsync(string url, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("XAMPP-Updater/0.4 (+https://github.com/danhk0612/XAMPP-Updater)");
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, cancellationToken);
    }

    [GeneratedRegex(@"<a[^>]+href=[\"'](?<href>[^\"']+)[\"'][^>]*>(?<text>[^<]+)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"(?<version>\d+\.\d+(?:\.\d+)?)\s*</a>\s*</td>\s*<td[^>]*>\s*stable\b", RegexOptions.IgnoreCase)]
    private static partial Regex StableReleaseRegex();

    [GeneratedRegex(@"^\s*(?:zend_)?extension\s*=\s*(?<value>[^;#]+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ExtensionRegex();
}

public sealed record PhpExternalExtensionInstallResult(
    IReadOnlyList<string> InstalledDlls,
    IReadOnlyList<string> Warnings);
