using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IPackagePreparationService
{
    Task<PackagePreparationResult> PrepareAsync(
        UpdateTargetOption target,
        InstallationCompatibilityProfile profile,
        CancellationToken cancellationToken = default);
}

public sealed partial class PackagePreparationService : IPackagePreparationService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    private static readonly SemaphoreSlim PackagePreparationGate = new(1, 1);

    public async Task<PackagePreparationResult> PrepareAsync(
        UpdateTargetOption target,
        InstallationCompatibilityProfile profile,
        CancellationToken cancellationToken = default)
    {
        await PackagePreparationGate.WaitAsync(cancellationToken);
        try
        {
            return await PrepareCoreAsync(target, profile, cancellationToken);
        }
        finally
        {
            PackagePreparationGate.Release();
        }
    }

    private async Task<PackagePreparationResult> PrepareCoreAsync(
        UpdateTargetOption target,
        InstallationCompatibilityProfile profile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target.PackageUrl))
        {
            throw new InvalidOperationException(
                $"{target.Type} {target.Version}의 Windows 패키지 위치를 아직 자동으로 확인하지 못했습니다.");
        }

        var sourceUrl = target.PackageUrl;
        if (IsDeferredApacheResolver(sourceUrl))
        {
            var candidate = await new CandidatePackageCatalogService()
                .ResolveApacheVersionAsync(target.Version, profile, cancellationToken);
            if (candidate.DownloadUrl is null)
            {
                throw new InvalidOperationException(candidate.Reason);
            }
            sourceUrl = candidate.DownloadUrl;
        }

        var downloadUrl = await ResolveDownloadUrlAsync(target, sourceUrl, cancellationToken);
        var fileName = GetFileName(downloadUrl, target);
        var packageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XamppUpdater",
            "Packages",
            target.Type.ToString(),
            target.Version);
        Directory.CreateDirectory(packageDirectory);

        var packagePath = Path.Combine(packageDirectory, fileName);
        var temporaryPath = packagePath + ".part";

        await DownloadAsync(downloadUrl, temporaryPath, cancellationToken);
        File.Move(temporaryPath, packagePath, overwrite: true);

        var actualSha256 = ComputeSha256(packagePath);
        var officialSha256 = await TryGetOfficialSha256Async(
            target,
            sourceUrl,
            fileName,
            cancellationToken);
        if (officialSha256 is not null &&
            !string.Equals(actualSha256, officialSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"공식 SHA256과 다운로드 파일이 일치하지 않습니다. 공식: {officialSha256}, 실제: {actualSha256}");
        }

        var expectedArchitecture = target.Type switch
        {
            XamppComponentType.Apache => profile.ApacheArchitecture,
            XamppComponentType.Php => profile.PhpArchitecture,
            XamppComponentType.MariaDb => profile.MariaDbArchitecture,
            _ => BinaryArchitecture.Unknown
        };
        var requirePhpApacheModule = target.Type == XamppComponentType.Php && profile.ApachePhpIntegration.IsModuleLoaded;
        var inspection = InspectArchive(packagePath, target.Type, expectedArchitecture, requirePhpApacheModule);
        var warnings = inspection.Warnings.ToList();
        warnings.Add(officialSha256 is null
            ? "공식 SHA256 manifest를 자동 확보하지 못해 다운로드 파일의 로컬 SHA256만 기록했습니다."
            : "공식 SHA256 manifest와 다운로드 파일의 해시가 일치합니다.");

        var inventory = PackageInventoryService.Compare(
            profile.RootPath,
            packagePath,
            target.Type,
            inspection.PayloadEntry);
        warnings.Add(
            $"파일 인벤토리: 현재 {inventory.CurrentFiles:N0} / 패키지 {inventory.PackageFiles:N0} / 공통 {inventory.CommonFiles:N0} / 기존만 {inventory.CurrentOnlyFiles:N0} / 신규만 {inventory.PackageOnlyFiles:N0}");
        if (inventory.CompatibilityItems.Count > 0)
        {
            var label = target.Type == XamppComponentType.Apache ? "패키지에 없는 기존 Apache 모듈" : "패키지에 없는 기존 PHP 확장";
            warnings.Add($"{label}: {string.Join(", ", inventory.CompatibilityItems.Take(12))}" +
                         (inventory.CompatibilityItems.Count > 12 ? $" 외 {inventory.CompatibilityItems.Count - 12}개" : string.Empty));
        }

        var info = new FileInfo(packagePath);
        return new PackagePreparationResult(
            target.Type,
            target.Version,
            sourceUrl,
            downloadUrl,
            packagePath,
            fileName,
            info.Length,
            actualSha256,
            inspection.Architecture,
            inspection.PayloadEntry,
            inspection.EntryCount,
            inspection.PhpApacheModulePresent,
            warnings);
    }

    internal static bool IsDeferredApacheResolver(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme.Equals("xampp-updater-resolve", StringComparison.OrdinalIgnoreCase) &&
        uri.Host.Equals("apache", StringComparison.OrdinalIgnoreCase);

    internal static PackageArchiveInspection InspectArchive(
        string packagePath,
        XamppComponentType type,
        BinaryArchitecture expectedArchitecture,
        bool requirePhpApacheModule)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var payload = FindPayloadEntry(archive, type)
            ?? throw new InvalidDataException($"{type} 핵심 실행 파일을 ZIP에서 찾을 수 없습니다.");

        var architecture = ReadArchitecture(payload);
        if (expectedArchitecture != BinaryArchitecture.Unknown &&
            architecture != BinaryArchitecture.Unknown &&
            architecture != expectedArchitecture)
        {
            throw new InvalidDataException(
                $"패키지 아키텍처가 현재 환경과 다릅니다. 현재: {expectedArchitecture}, 패키지: {architecture}");
        }

        var phpApacheModulePresent = type != XamppComponentType.Php || archive.Entries.Any(entry =>
            PhpApacheModuleRegex().IsMatch(Path.GetFileName(entry.FullName)));
        if (type == XamppComponentType.Php && requirePhpApacheModule && !phpApacheModulePresent)
        {
            throw new InvalidDataException("Apache 모듈 방식에 필요한 php*apache2_4.dll이 패키지에 없습니다.");
        }

        var warnings = new List<string>();
        if (architecture == BinaryArchitecture.Unknown)
        {
            warnings.Add("패키지 실행 파일의 PE 아키텍처를 판정하지 못했습니다.");
        }

        return new PackageArchiveInspection(
            architecture,
            payload.FullName,
            archive.Entries.Count,
            phpApacheModulePresent,
            warnings);
    }

    internal static string? ResolveMariaDbZipUrl(string pageUrl, string html, string version)
    {
        var expected = $"mariadb-{version}-winx64.zip";
        var match = MariaDbZipLinkRegex().Matches(html)
            .Select(item => item.Groups["href"].Value)
            .FirstOrDefault(href => href.Contains(expected, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(match))
        {
            return null;
        }

        return new Uri(new Uri(pageUrl), match).AbsoluteUri;
    }

    internal static string? ResolveSha256ManifestUrl(string pageUrl, string html)
    {
        var href = Sha256ManifestLinkRegex().Matches(html)
            .Select(match => match.Groups["href"].Value)
            .FirstOrDefault(value => value.Contains("sha256sums.txt", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(href) ? null : new Uri(new Uri(pageUrl), href).AbsoluteUri;
    }

    internal static string? ParseSha256Sum(string text, string fileName)
    {
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var match = Sha256LineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var listedFile = match.Groups["file"].Value.TrimStart('*');
            if (string.Equals(Path.GetFileName(listedFile), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups["hash"].Value.ToUpperInvariant();
            }
        }

        return null;
    }

    private static async Task<string?> TryGetOfficialSha256Async(
        UpdateTargetOption target,
        string sourceUrl,
        string fileName,
        CancellationToken cancellationToken)
    {
        if (target.Type != XamppComponentType.MariaDb ||
            (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) && uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        try
        {
            var pageHtml = await GetStringAsync(sourceUrl, cancellationToken);
            var manifestUrl = ResolveSha256ManifestUrl(sourceUrl, pageHtml);
            if (manifestUrl is null)
            {
                return null;
            }

            var sums = await GetStringAsync(manifestUrl, cancellationToken);
            return ParseSha256Sum(sums, fileName);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> ResolveDownloadUrlAsync(
        UpdateTargetOption target,
        string sourceUrl,
        CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) &&
            uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return sourceUrl;
        }

        if (target.Type != XamppComponentType.MariaDb)
        {
            throw new InvalidOperationException(
                $"{target.Type} {target.Version}의 직접 ZIP 주소를 자동으로 확인하지 못했습니다.");
        }

        var html = await GetStringAsync(sourceUrl, cancellationToken);
        return ResolveMariaDbZipUrl(sourceUrl, html, target.Version)
               ?? throw new InvalidOperationException($"MariaDB {target.Version} winx64 ZIP을 공식 패키지 페이지에서 찾지 못했습니다.");
    }

    private static ZipArchiveEntry? FindPayloadEntry(ZipArchive archive, XamppComponentType type)
    {
        return type switch
        {
            XamppComponentType.Apache => archive.Entries.FirstOrDefault(entry =>
                NormalizeEntry(entry.FullName).EndsWith("bin/httpd.exe", StringComparison.OrdinalIgnoreCase)),
            XamppComponentType.Php => archive.Entries.FirstOrDefault(entry =>
                NormalizeEntry(entry.FullName).Equals("php.exe", StringComparison.OrdinalIgnoreCase) ||
                NormalizeEntry(entry.FullName).EndsWith("/php.exe", StringComparison.OrdinalIgnoreCase)),
            XamppComponentType.MariaDb => archive.Entries.FirstOrDefault(entry =>
                NormalizeEntry(entry.FullName).EndsWith("bin/mariadbd.exe", StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(entry =>
                    NormalizeEntry(entry.FullName).EndsWith("bin/mysqld.exe", StringComparison.OrdinalIgnoreCase)),
            _ => null
        };
    }

    private static BinaryArchitecture ReadArchitecture(ZipArchiveEntry entry)
    {
        try
        {
            using var source = entry.Open();
            using var seekable = entry.Length <= int.MaxValue
                ? new MemoryStream((int)entry.Length)
                : new MemoryStream();
            source.CopyTo(seekable);
            seekable.Position = 0;

            using var reader = new PEReader(seekable, PEStreamOptions.LeaveOpen);
            return reader.PEHeaders.CoffHeader.Machine switch
            {
                Machine.I386 => BinaryArchitecture.X86,
                Machine.Amd64 => BinaryArchitecture.X64,
                Machine.Arm64 => BinaryArchitecture.Arm64,
                _ => BinaryArchitecture.Unknown
            };
        }
        catch
        {
            return BinaryArchitecture.Unknown;
        }
    }

    private static async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("XAMPP-Updater/0.3 (+https://github.com/danhk0612/XAMPP-Updater)");
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("XAMPP-Updater/0.3 (+https://github.com/danhk0612/XAMPP-Updater)");
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string GetFileName(string url, UpdateTargetOption target)
    {
        var name = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? Path.GetFileName(uri.LocalPath)
            : null;
        return string.IsNullOrWhiteSpace(name)
            ? target.PackageFileName ?? $"{target.Type}-{target.Version}.zip"
            : name;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string NormalizeEntry(string value) => value.Replace('\\', '/').TrimStart('/');

    [GeneratedRegex(@"php\d*apache2_4\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex PhpApacheModuleRegex();

    [GeneratedRegex(@"href\s*=\s*[""'](?<href>[^""']+\.zip)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex MariaDbZipLinkRegex();

    [GeneratedRegex(@"href\s*=\s*[""'](?<href>[^""']*sha256sums\.txt[^""']*)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex Sha256ManifestLinkRegex();

    [GeneratedRegex(@"^(?<hash>[A-Fa-f0-9]{64})\s+\*?(?<file>.+)$")]
    private static partial Regex Sha256LineRegex();
}

public sealed record PackageArchiveInspection(
    BinaryArchitecture Architecture,
    string PayloadEntry,
    int EntryCount,
    bool PhpApacheModulePresent,
    IReadOnlyList<string> Warnings);
