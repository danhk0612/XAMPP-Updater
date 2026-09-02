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

    public async Task<PackagePreparationResult> PrepareAsync(
        UpdateTargetOption target,
        InstallationCompatibilityProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(target.PackageUrl))
        {
            throw new InvalidOperationException(
                $"{target.Type} {target.Version}의 Windows 패키지 위치를 아직 자동으로 확인하지 못했습니다.");
        }

        var sourceUrl = target.PackageUrl;
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

        var expectedArchitecture = target.Type switch
        {
            XamppComponentType.Apache => profile.ApacheArchitecture,
            XamppComponentType.Php => profile.PhpArchitecture,
            XamppComponentType.MariaDb => profile.MariaDbArchitecture,
            _ => BinaryArchitecture.Unknown
        };
        var requirePhpApacheModule = target.Type == XamppComponentType.Php && profile.ApachePhpIntegration.IsModuleLoaded;
        var inspection = InspectArchive(packagePath, target.Type, expectedArchitecture, requirePhpApacheModule);
        var info = new FileInfo(packagePath);

        return new PackagePreparationResult(
            target.Type,
            target.Version,
            sourceUrl,
            downloadUrl,
            packagePath,
            fileName,
            info.Length,
            ComputeSha256(packagePath),
            inspection.Architecture,
            inspection.PayloadEntry,
            inspection.EntryCount,
            inspection.PhpApacheModulePresent,
            inspection.Warnings);
    }

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
            using var stream = entry.Open();
            using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
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
}

public sealed record PackageArchiveInspection(
    BinaryArchitecture Architecture,
    string PayloadEntry,
    int EntryCount,
    bool PhpApacheModulePresent,
    IReadOnlyList<string> Warnings);
