using System.IO.Compression;
using System.Security.Cryptography;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed class ApacheCompatibilityPreparedPackage : IDisposable
{
    public ApacheCompatibilityPreparedPackage(
        PackagePreparationResult package,
        ApacheModuleCompatibilityResult compatibility,
        string? temporaryRoot)
    {
        Package = package;
        Compatibility = compatibility;
        TemporaryRoot = temporaryRoot;
    }

    public PackagePreparationResult Package { get; }
    public ApacheModuleCompatibilityResult Compatibility { get; }
    public string? TemporaryRoot { get; }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(TemporaryRoot) || !Directory.Exists(TemporaryRoot)) return;
        try { Directory.Delete(TemporaryRoot, recursive: true); } catch { }
    }
}

public interface IApacheCompatibilityPackageService
{
    ApacheCompatibilityPreparedPackage Prepare(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package);
}

public sealed class ApacheCompatibilityPackageService : IApacheCompatibilityPackageService
{
    private readonly IApacheModuleCompatibilityService _modules;
    private readonly IApacheMigrationOverrideStore _overrides;

    public ApacheCompatibilityPackageService(
        IApacheModuleCompatibilityService? modules = null,
        IApacheMigrationOverrideStore? overrides = null)
    {
        _modules = modules ?? new ApacheModuleCompatibilityService();
        _overrides = overrides ?? new ApacheMigrationOverrideStore();
    }

    public ApacheCompatibilityPreparedPackage Prepare(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package)
    {
        if (target.Type != XamppComponentType.Apache || package.Type != XamppComponentType.Apache)
            throw new ArgumentException("Apache 호환 패키지 준비에는 Apache 대상/패키지가 필요합니다.");
        if (!File.Exists(package.PackagePath))
            throw new FileNotFoundException("Apache 원본 패키지를 찾을 수 없습니다.", package.PackagePath);

        var currentRoot = Path.Combine(installation.RootPath, "apache");
        var currentConf = Path.Combine(currentRoot, "conf");
        if (!Directory.Exists(currentConf))
            throw new DirectoryNotFoundException("현재 Apache conf 디렉터리를 찾을 수 없습니다: " + currentConf);

        var tempRoot = Path.Combine(Path.GetTempPath(), $"xampp-updater-apache-compat-{Guid.NewGuid():N}");
        var extractRoot = Path.Combine(tempRoot, "package");
        var planningConf = Path.Combine(tempRoot, "planning-conf");
        var patchedZip = Path.Combine(tempRoot, "apache-compatible.zip");

        try
        {
            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(package.PackagePath, extractRoot, overwriteFiles: true);
            var payloadRoot = ResolvePayloadRoot(extractRoot, package.PayloadEntry);

            CopyDirectory(currentConf, planningConf);
            var reviewed = _overrides.TryLoad(installation.RootPath, target.Version, currentConf);
            if (reviewed is not null) ApplyReviewedConfiguration(planningConf, reviewed.Files);

            var compatibility = _modules.Prepare(currentRoot, payloadRoot, planningConf);
            if (!compatibility.Success)
            {
                throw new InvalidOperationException(
                    "Apache 외부 모듈/종속 DLL 호환 준비 실패: " +
                    string.Join("; ", compatibility.UnresolvedDependencies.Take(8)));
            }

            if (compatibility.PreservedModules.Count == 0 && compatibility.PreservedDependencies.Count == 0)
            {
                TryDeleteDirectory(tempRoot);
                return new ApacheCompatibilityPreparedPackage(package, compatibility, null);
            }

            ZipFile.CreateFromDirectory(extractRoot, patchedZip, CompressionLevel.Fastest, includeBaseDirectory: false);
            var info = new FileInfo(patchedZip);
            var warnings = package.Warnings
                .Concat(compatibility.PreservedModules.Select(value => "외부 Apache 모듈 호환 보존: " + value))
                .Concat(compatibility.PreservedDependencies.Select(value => "Apache 모듈 종속 DLL 호환 보존: " + value))
                .ToArray();
            var patched = package with
            {
                PackagePath = patchedZip,
                FileName = Path.GetFileName(patchedZip),
                Size = info.Length,
                Sha256 = ComputeSha256(patchedZip),
                ArchiveEntries = Directory.EnumerateFiles(extractRoot, "*", SearchOption.AllDirectories).Count(),
                Warnings = warnings
            };
            return new ApacheCompatibilityPreparedPackage(patched, compatibility, tempRoot);
        }
        catch
        {
            TryDeleteDirectory(tempRoot);
            throw;
        }
    }

    private static string ResolvePayloadRoot(string extractRoot, string payloadEntry)
    {
        var normalized = payloadEntry.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var httpd = Path.Combine(extractRoot, normalized);
        if (!File.Exists(httpd)) throw new InvalidDataException("Apache 호환 패키지에서 httpd.exe를 찾을 수 없습니다.");
        var bin = Path.GetDirectoryName(httpd) ?? throw new InvalidDataException("Apache bin 경로를 확인할 수 없습니다.");
        return Directory.GetParent(bin)?.FullName ?? throw new InvalidDataException("Apache 패키지 루트를 확인할 수 없습니다.");
    }

    private static void ApplyReviewedConfiguration(string confRoot, IReadOnlyDictionary<string, string> files)
    {
        foreach (var pair in files)
        {
            var destination = SafeCombine(confRoot, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            if (destination is null) throw new InvalidDataException("Apache 검토 설정 경로가 conf 밖을 가리킵니다: " + pair.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, pair.Value);
        }
    }

    private static string? SafeCombine(string root, string relative)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        return full.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? full
            : null;
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

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
