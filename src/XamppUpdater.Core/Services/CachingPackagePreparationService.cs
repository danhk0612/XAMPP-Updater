using System.Security.Cryptography;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed class CachingPackagePreparationService : IPackagePreparationService
{
    private readonly IPackagePreparationService _inner;

    public CachingPackagePreparationService(IPackagePreparationService? inner = null)
    {
        _inner = inner ?? new PackagePreparationService();
    }

    public async Task<PackagePreparationResult> PrepareAsync(
        UpdateTargetOption target,
        InstallationCompatibilityProfile profile,
        CancellationToken cancellationToken = default)
    {
        var cached = TryPrepareFromCache(target, profile);
        if (cached is not null)
            return cached;

        return await _inner.PrepareAsync(target, profile, cancellationToken);
    }

    private static PackagePreparationResult? TryPrepareFromCache(
        UpdateTargetOption target,
        InstallationCompatibilityProfile profile)
    {
        var packageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XamppUpdater",
            "Packages",
            target.Type.ToString(),
            target.Version);
        if (!Directory.Exists(packageDirectory)) return null;

        var expectedArchitecture = target.Type switch
        {
            XamppComponentType.Apache => profile.ApacheArchitecture,
            XamppComponentType.Php => profile.PhpArchitecture,
            XamppComponentType.MariaDb => profile.MariaDbArchitecture,
            _ => BinaryArchitecture.Unknown
        };
        var requirePhpApacheModule = target.Type == XamppComponentType.Php && profile.ApachePhpIntegration.IsModuleLoaded;

        foreach (var packagePath in Directory.EnumerateFiles(packageDirectory, "*.zip", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var inspection = PackagePreparationService.InspectArchive(
                    packagePath,
                    target.Type,
                    expectedArchitecture,
                    requirePhpApacheModule);
                var sha256 = ComputeSha256(packagePath);
                var inventory = PackageInventoryService.Compare(
                    profile.RootPath,
                    packagePath,
                    target.Type,
                    inspection.PayloadEntry);
                var warnings = inspection.Warnings.ToList();
                warnings.Insert(0, "기존 패키지 캐시를 재사용했습니다. 네트워크 조회/다운로드를 생략했습니다.");
                warnings.Add("캐시 ZIP의 로컬 SHA256과 패키지 구조/아키텍처를 다시 검증했습니다.");
                warnings.Add(
                    $"파일 인벤토리: 현재 {inventory.CurrentFiles:N0} / 패키지 {inventory.PackageFiles:N0} / 공통 {inventory.CommonFiles:N0} / 기존만 {inventory.CurrentOnlyFiles:N0} / 신규만 {inventory.PackageOnlyFiles:N0}");
                if (inventory.CompatibilityItems.Count > 0)
                {
                    var label = target.Type == XamppComponentType.Apache ? "패키지에 없는 기존 Apache 모듈" : "패키지에 없는 기존 PHP 확장";
                    warnings.Add($"{label}: {string.Join(", ", inventory.CompatibilityItems.Take(12))}" +
                                 (inventory.CompatibilityItems.Count > 12 ? $" 외 {inventory.CompatibilityItems.Count - 12}개" : string.Empty));
                }

                var info = new FileInfo(packagePath);
                var source = target.PackageUrl ?? "local-cache";
                return new PackagePreparationResult(
                    target.Type,
                    target.Version,
                    source,
                    source,
                    packagePath,
                    info.Name,
                    info.Length,
                    sha256,
                    inspection.Architecture,
                    inspection.PayloadEntry,
                    inspection.EntryCount,
                    inspection.PhpApacheModulePresent,
                    warnings);
            }
            catch
            {
                // Ignore stale/broken cache entries and let the normal preparation service redownload.
            }
        }

        return null;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
