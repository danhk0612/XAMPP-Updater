using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public enum ApacheMigrationReviewKind
{
    Preserved,
    AutomaticChange,
    NeedsReview
}

public sealed record ApacheMigrationReviewItem(ApacheMigrationReviewKind Kind, string Message);

public sealed record ApacheMigrationReviewResult(
    string CurrentVersion,
    string TargetVersion,
    bool SyntaxValid,
    string ValidationOutput,
    IReadOnlyList<ApacheMigrationReviewItem> Items,
    IReadOnlyList<string> ConfigurationFiles,
    IReadOnlyDictionary<string, string> ProposedFiles)
{
    public int PreservedCount => Items.Count(item => item.Kind == ApacheMigrationReviewKind.Preserved);
    public int AutomaticChangeCount => Items.Count(item => item.Kind == ApacheMigrationReviewKind.AutomaticChange);
    public int NeedsReviewCount => Items.Count(item => item.Kind == ApacheMigrationReviewKind.NeedsReview);
}

public interface IApacheMigrationReviewService
{
    Task<ApacheMigrationReviewResult> BuildAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        CancellationToken cancellationToken = default);
}

public sealed partial class ApacheMigrationReviewService : IApacheMigrationReviewService
{
    private readonly IApacheMigrationOverrideStore _overrideStore;

    public ApacheMigrationReviewService(IApacheMigrationOverrideStore? overrideStore = null)
    {
        _overrideStore = overrideStore ?? new ApacheMigrationOverrideStore();
    }

    public async Task<ApacheMigrationReviewResult> BuildAsync(
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package,
        CancellationToken cancellationToken = default)
    {
        if (target.Type != XamppComponentType.Apache || package.Type != XamppComponentType.Apache)
            throw new ArgumentException("Apache 마이그레이션 검토에는 Apache 대상과 패키지가 필요합니다.");

        var apache = installation.Components.First(item => item.Type == XamppComponentType.Apache);
        var currentVersion = apache.Version ?? "Unknown";
        var currentRoot = Path.Combine(installation.RootPath, "apache");
        var currentConf = Path.Combine(currentRoot, "conf");
        if (!Directory.Exists(currentConf))
            throw new DirectoryNotFoundException("현재 Apache conf 디렉터리를 찾을 수 없습니다: " + currentConf);
        if (!File.Exists(package.PackagePath))
            throw new FileNotFoundException("준비된 Apache 패키지를 찾을 수 없습니다.", package.PackagePath);

        var reviewRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XamppUpdater", "Review", $"Apache-{Guid.NewGuid():N}");
        var extractRoot = Path.Combine(reviewRoot, "package");

        try
        {
            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(package.PackagePath, extractRoot, overwriteFiles: true);
            var stagedRoot = ResolvePayloadRoot(extractRoot, package.PayloadEntry);
            var stagedConf = Path.Combine(stagedRoot, "conf");

            if (Directory.Exists(stagedConf)) Directory.Delete(stagedConf, recursive: true);
            CopyDirectory(currentConf, stagedConf);

            var items = new List<ApacheMigrationReviewItem>();
            var saved = _overrideStore.TryLoad(installation.RootPath, target.Version, currentConf);
            if (saved is not null)
            {
                ApplyFiles(stagedConf, saved.Files);
                items.Add(new ApacheMigrationReviewItem(
                    ApacheMigrationReviewKind.AutomaticChange,
                    $"사용자가 확정한 Apache 설정 적용안 사용: {saved.ConfirmedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}"));
            }

            var proposedFiles = ReadFiles(stagedConf);
            var configFiles = proposedFiles.Keys
                .Select(path => "conf/" + path.Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var config in configFiles)
                items.Add(new ApacheMigrationReviewItem(ApacheMigrationReviewKind.Preserved, config));

            var copiedModules = PreserveReferencedModules(currentRoot, stagedRoot, stagedConf);
            foreach (var module in copiedModules)
            {
                items.Add(new ApacheMigrationReviewItem(
                    ApacheMigrationReviewKind.AutomaticChange,
                    "새 패키지에 없어 기존 설치에서 참조 모듈을 임시 보존: " + module));
            }

            var mainConf = Path.Combine(stagedConf, "httpd.conf");
            if (!File.Exists(mainConf))
                throw new FileNotFoundException("기존 Apache httpd.conf를 찾을 수 없습니다.", mainConf);

            RewriteServerRootForValidation(mainConf, stagedRoot, items);

            var httpd = Path.Combine(stagedRoot, "bin", "httpd.exe");
            var validation = await RunAsync(httpd, new[] { "-t", "-f", mainConf }, stagedRoot, cancellationToken);
            if (validation.Output.Contains("Cannot load", StringComparison.OrdinalIgnoreCase) &&
                TryRepairMissingModuleDependencies(stagedRoot, validation.Output, items))
            {
                items.Add(new ApacheMigrationReviewItem(
                    ApacheMigrationReviewKind.AutomaticChange,
                    "누락 종속 DLL 자동 배치 후 httpd -t 재검증"));
                validation = await RunAsync(httpd, new[] { "-t", "-f", mainConf }, stagedRoot, cancellationToken);
            }

            if (validation.Output.Contains("Cannot load", StringComparison.OrdinalIgnoreCase))
                AddModuleDependencyDiagnostics(stagedRoot, validation.Output, items);

            var syntaxValid = validation.ExitCode == 0 &&
                              !validation.Output.Contains("Syntax error", StringComparison.OrdinalIgnoreCase) &&
                              !validation.Output.Contains("Cannot load", StringComparison.OrdinalIgnoreCase);

            items.Add(new ApacheMigrationReviewItem(
                syntaxValid ? ApacheMigrationReviewKind.AutomaticChange : ApacheMigrationReviewKind.NeedsReview,
                syntaxValid
                    ? $"Apache {target.Version} 바이너리로 기존 설정 사전 검증 통과: httpd -t"
                    : "새 Apache에서 기존 설정 사전 검증 실패: " + Compact(validation.Output)));

            return new ApacheMigrationReviewResult(
                currentVersion,
                target.Version,
                syntaxValid,
                validation.Output,
                items,
                configFiles,
                proposedFiles);
        }
        finally
        {
            TryDeleteDirectory(reviewRoot);
        }
    }

    private static Dictionary<string, string> ReadFiles(string confRoot)
    {
        return Directory.EnumerateFiles(confRoot, "*.conf", SearchOption.AllDirectories)
            .Where(path => IsActiveConfig(confRoot, path))
            .ToDictionary(
                path => Path.GetRelativePath(confRoot, path).Replace('\\', '/'),
                File.ReadAllText,
                StringComparer.OrdinalIgnoreCase);
    }

    private static void ApplyFiles(string confRoot, IReadOnlyDictionary<string, string> files)
    {
        foreach (var pair in files)
        {
            var relative = pair.Key.Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(confRoot, relative));
            if (!IsUnderRoot(destination, confRoot) || !IsActiveConfig(confRoot, destination)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, pair.Value);
        }
    }

    private static bool IsActiveConfig(string confRoot, string path)
    {
        var relative = Path.GetRelativePath(confRoot, path).Replace('\\', '/');
        return !relative.StartsWith("original/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePayloadRoot(string extractRoot, string payloadEntry)
    {
        var normalized = payloadEntry.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var httpd = Path.Combine(extractRoot, normalized);
        if (!File.Exists(httpd)) throw new InvalidDataException("압축 해제 후 httpd.exe를 찾을 수 없습니다.");
        var bin = Path.GetDirectoryName(httpd) ?? throw new InvalidDataException("Apache bin 경로를 확인할 수 없습니다.");
        return Directory.GetParent(bin)?.FullName ?? throw new InvalidDataException("Apache 패키지 루트를 확인할 수 없습니다.");
    }

    private static void RewriteServerRootForValidation(string mainConf, string stagedRoot, ICollection<ApacheMigrationReviewItem> items)
    {
        var original = File.ReadAllText(mainConf);
        var apachePath = stagedRoot.Replace('\\', '/');

        // XAMPP Apache configurations can contain both directives independently.
        // Rewriting only Define SRVROOT can leave a literal ServerRoot pointing at the live Apache,
        // causing the new httpd.exe to load old modules during the staging validation.
        var updated = DefineSrvRootRegex().Replace(original, $"Define SRVROOT \"{apachePath}\"");
        updated = ServerRootRegex().Replace(updated, $"ServerRoot \"{apachePath}\"");

        if (!string.Equals(original, updated, StringComparison.Ordinal))
        {
            File.WriteAllText(mainConf, updated);
            items.Add(new ApacheMigrationReviewItem(
                ApacheMigrationReviewKind.AutomaticChange,
                "검토용 Define SRVROOT/ServerRoot를 임시 Apache 경로로 변경하여 사전 검증"));
        }
    }

    private static IReadOnlyList<string> PreserveReferencedModules(string currentRoot, string stagedRoot, string stagedConf)
    {
        var result = new List<string>();
        foreach (var conf in Directory.EnumerateFiles(stagedConf, "*.conf", SearchOption.AllDirectories)
                     .Where(path => IsActiveConfig(stagedConf, path)))
        {
            foreach (var raw in File.ReadLines(conf))
            {
                var match = LoadModuleRegex().Match(raw);
                if (!match.Success) continue;
                var configured = match.Groups["path"].Value.Trim().Trim('"', '\'').Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathFullyQualified(configured)) continue;
                var destination = Path.GetFullPath(Path.Combine(stagedRoot, configured));
                if (File.Exists(destination)) continue;
                var source = Path.GetFullPath(Path.Combine(currentRoot, configured));
                if (!File.Exists(source) || !IsUnderRoot(source, currentRoot) || !IsUnderRoot(destination, stagedRoot)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
                result.Add(Path.GetRelativePath(stagedRoot, destination).Replace('\\', '/'));
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool TryRepairMissingModuleDependencies(
        string stagedRoot,
        string validationOutput,
        ICollection<ApacheMigrationReviewItem> items)
    {
        var module = ResolveFailedModule(stagedRoot, validationOutput);
        if (module is null || !File.Exists(module)) return false;

        var searchDirectories = GetDependencySearchDirectories(stagedRoot, module);
        var missing = PeDependencyInspector.FindMissingDependencies(module, searchDirectories);
        var copied = false;
        foreach (var item in missing)
        {
            var source = PeDependencyInspector.FindAnywhere(stagedRoot, item.DependencyName);
            if (source is null) continue;
            var destination = Path.Combine(stagedRoot, "bin", item.DependencyName);
            if (File.Exists(destination) || string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            items.Add(new ApacheMigrationReviewItem(
                ApacheMigrationReviewKind.AutomaticChange,
                $"모듈 종속 DLL 자동 배치: {item.DependencyName} → bin/{item.DependencyName}"));
            copied = true;
        }
        return copied;
    }

    private static void AddModuleDependencyDiagnostics(
        string stagedRoot,
        string validationOutput,
        ICollection<ApacheMigrationReviewItem> items)
    {
        var module = ResolveFailedModule(stagedRoot, validationOutput);
        if (module is null)
        {
            items.Add(new ApacheMigrationReviewItem(
                ApacheMigrationReviewKind.NeedsReview,
                "로드 실패한 Apache 모듈 경로를 오류 출력에서 해석하지 못했습니다."));
            return;
        }

        var relativeModule = Path.GetRelativePath(stagedRoot, module).Replace('\\', '/');
        if (!File.Exists(module))
        {
            items.Add(new ApacheMigrationReviewItem(
                ApacheMigrationReviewKind.NeedsReview,
                $"로드 대상 모듈 파일이 새 패키지에 없습니다: {relativeModule}"));
            return;
        }

        var imports = PeDependencyInspector.ReadImports(module);
        if (imports.Count > 0)
        {
            items.Add(new ApacheMigrationReviewItem(
                ApacheMigrationReviewKind.AutomaticChange,
                $"{relativeModule} 직접 종속 DLL: {string.Join(", ", imports)}"));
        }

        var searchDirectories = GetDependencySearchDirectories(stagedRoot, module);
        var missing = PeDependencyInspector.FindMissingDependencies(module, searchDirectories);
        if (missing.Count == 0)
        {
            var probe = WindowsLoaderProbe.TryLoad(module, searchDirectories);
            if (probe.Success)
            {
                items.Add(new ApacheMigrationReviewItem(
                    ApacheMigrationReviewKind.NeedsReview,
                    $"{relativeModule} Windows LoadLibraryEx 직접 로드는 성공했습니다. httpd.exe 내부 로딩 경로/Apache 모듈 ABI 문제를 추가 확인해야 합니다."));
            }
            else
            {
                items.Add(new ApacheMigrationReviewItem(
                    ApacheMigrationReviewKind.NeedsReview,
                    $"{relativeModule} Windows 로더 직접 진단 실패: Win32 오류 {probe.ErrorCode} / {probe.Message}"));
            }
            return;
        }

        foreach (var dependency in missing)
        {
            var owner = Path.GetRelativePath(stagedRoot, dependency.BinaryPath).Replace('\\', '/');
            items.Add(new ApacheMigrationReviewItem(
                ApacheMigrationReviewKind.NeedsReview,
                $"누락 종속 DLL: {dependency.DependencyName} (요청 파일: {owner})"));
        }
    }

    private static string? ResolveFailedModule(string stagedRoot, string validationOutput)
    {
        var match = CannotLoadModuleRegex().Match(validationOutput);
        if (!match.Success) return null;
        var configured = match.Groups["path"].Value.Trim().Trim('"', '\'').Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathFullyQualified(configured)) return configured;
        var path = Path.GetFullPath(Path.Combine(stagedRoot, configured));
        return IsUnderRoot(path, stagedRoot) ? path : null;
    }

    private static IReadOnlyList<string> GetDependencySearchDirectories(string stagedRoot, string module)
    {
        var result = new List<string>
        {
            Path.GetDirectoryName(module) ?? stagedRoot,
            Path.Combine(stagedRoot, "bin"),
            stagedRoot,
            Environment.SystemDirectory
        };
        if (Environment.Is64BitOperatingSystem)
            result.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"));
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
            result.AddRange(pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return result.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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

    private static async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("httpd.exe를 찾을 수 없습니다.", executable);
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        var binDirectory = Path.GetDirectoryName(executable);
        if (!string.IsNullOrWhiteSpace(binDirectory))
        {
            var currentPath = start.Environment["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            start.Environment["PATH"] = string.IsNullOrWhiteSpace(currentPath)
                ? binDirectory
                : binDirectory + Path.PathSeparator + currentPath;
        }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = string.Join(Environment.NewLine, new[] { await stdout, await stderr }.Where(text => !string.IsNullOrWhiteSpace(text)));
        return new ProcessResult(process.ExitCode, output.Trim());
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private static string Compact(string value) => value.Replace("\r", " ").Replace("\n", " ").Trim();
    private sealed record ProcessResult(int ExitCode, string Output);

    [GeneratedRegex(@"(?im)^\s*Define\s+SRVROOT\s+[^\r\n]+$")]
    private static partial Regex DefineSrvRootRegex();

    [GeneratedRegex(@"(?im)^\s*ServerRoot\s+[^\r\n]+$")]
    private static partial Regex ServerRootRegex();

    [GeneratedRegex(@"^\s*LoadModule\s+\S+\s+(?<path>[^#]+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LoadModuleRegex();

    [GeneratedRegex(@"Cannot load\s+(?<path>[^\s]+)\s+into server", RegexOptions.IgnoreCase)]
    private static partial Regex CannotLoadModuleRegex();
}
