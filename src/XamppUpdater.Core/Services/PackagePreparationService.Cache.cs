using System.Security.Cryptography;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed partial class PackagePreparationService
{
    async Task<PackagePreparationResult> IPackagePreparationService.PrepareAsync(
        UpdateTargetOption target,
        InstallationCompatibilityProfile profile,
        CancellationToken cancellationToken)
    {
        var cached = TryPrepareCachedPackage(target, profile);
        if (cached is not null)
            return cached;

        // Call the existing public implementation directly. This bypasses this explicit
        // interface implementation and performs the normal online resolution/download.
        return await PrepareAsync(target, profile, cancellationToken);
    }

    private static PackagePreparationResult? TryPrepareCachedPackage(
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
                var inspection = InspectArchive(
                    packagePath,
                    target.Type,
                    expectedArchitecture,
                    requirePhpApacheModule);
                var sha256 = ComputeCachedSha256(packagePath);
                var inventory = PackageInventoryService.Compare(
                    profile.RootPath,
                    packagePath,
                    target.Type,
                    inspection.PayloadEntry);

                var warnings = inspection.Warnings.ToList();
                warnings.Insert(0, "기존 패키지 캐시 재사용: 네트워크 버전 조회/다운로드를 생략했습니다.");
                warnings.Add("캐시 ZIP의 SHA256, ZIP 구조 및 실행 파일 아키텍처를 다시 검증했습니다.");
                warnings.Add(
                    $"파일 인벤토리: 현재 {inventory.CurrentFiles:N0} / 패키지 {inventory.PackageFiles:N0} / 공통 {inventory.CommonFiles:N0} / 기존만 {inventory.CurrentOnlyFiles:N0} / 신규만 {inventory.PackageOnlyFiles:N0}");
                if (inventory.CompatibilityItems.Count > 0)
                {
                    var label = target.Type == XamppComponentType.Apache
                        ? "패키지에 없는 기존 Apache 모듈"
                        : "패키지에 없는 기존 PHP 확장";
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
                // Ignore corrupt/incompatible cache entries. The normal online path will
                // download and validate a replacement package.
            }
        }

        return null;
    }

    private static string ComputeCachedSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
