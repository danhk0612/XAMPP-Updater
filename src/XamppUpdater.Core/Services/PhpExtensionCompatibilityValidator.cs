using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed record PhpExtensionCompatibilityResult(
    bool Compatible,
    IReadOnlyList<string> Diagnostics);

public static partial class PhpExtensionCompatibilityValidator
{
    public static PhpExtensionCompatibilityResult Validate(
        string extensionPath,
        string phpRoot,
        string targetVersion,
        bool threadSafe,
        BinaryArchitecture expectedArchitecture)
    {
        var diagnostics = new List<string>();
        if (!File.Exists(extensionPath))
            return new PhpExtensionCompatibilityResult(false, new[] { "확장 DLL 파일이 없습니다: " + extensionPath });

        var actualArchitecture = ReadArchitecture(extensionPath);
        if (expectedArchitecture != BinaryArchitecture.Unknown && actualArchitecture != expectedArchitecture)
        {
            diagnostics.Add($"PE 아키텍처 불일치: 기대 {expectedArchitecture}, 실제 {actualArchitecture}");
            return new PhpExtensionCompatibilityResult(false, diagnostics);
        }
        diagnostics.Add($"PE 아키텍처 확인: {actualArchitecture}");

        var major = ParseMajor(targetVersion);
        if (major is null)
        {
            diagnostics.Add("대상 PHP major 버전을 판정하지 못했습니다.");
            return new PhpExtensionCompatibilityResult(false, diagnostics);
        }

        var expectedRuntime = $"php{major}{(threadSafe ? "ts" : string.Empty)}.dll";
        var phpRuntimeImports = PeDependencyInspector.ReadImports(extensionPath)
            .Where(name => PhpRuntimeDllRegex().IsMatch(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (phpRuntimeImports.Length == 0)
        {
            diagnostics.Add($"PHP runtime import를 찾지 못했습니다. 기대: {expectedRuntime}");
            return new PhpExtensionCompatibilityResult(false, diagnostics);
        }
        if (!phpRuntimeImports.Contains(expectedRuntime, StringComparer.OrdinalIgnoreCase))
        {
            diagnostics.Add($"TS/NTS 또는 PHP major ABI 불일치: 기대 {expectedRuntime}, 실제 {string.Join(", ", phpRuntimeImports)}");
            return new PhpExtensionCompatibilityResult(false, diagnostics);
        }
        diagnostics.Add($"PHP runtime import 확인: {expectedRuntime}");

        var searchDirectories = GetSearchDirectories(phpRoot, extensionPath);
        var missing = PeDependencyInspector.FindMissingDependencies(extensionPath, searchDirectories);
        if (missing.Count > 0)
        {
            foreach (var item in missing)
                diagnostics.Add($"종속성 확인 실패: {Path.GetFileName(item.BinaryPath)} → {item.DependencyName}");
            return new PhpExtensionCompatibilityResult(false, diagnostics);
        }
        diagnostics.Add("PE 종속성 그래프 확인 완료");

        if (OperatingSystem.IsWindows())
        {
            var loader = WindowsLoaderProbe.TryLoad(extensionPath, searchDirectories);
            if (!loader.Success)
            {
                diagnostics.Add($"Windows loader probe 실패: Win32 {loader.ErrorCode} / {loader.Message}");
                return new PhpExtensionCompatibilityResult(false, diagnostics);
            }
            diagnostics.Add("Windows loader probe 성공");
        }

        diagnostics.Add("PHP module API 최종 호환성은 이어지는 php -v / php -m 실제 로드 검사에서 확인합니다.");
        return new PhpExtensionCompatibilityResult(true, diagnostics);
    }

    private static BinaryArchitecture ReadArchitecture(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
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

    private static int? ParseMajor(string version)
    {
        var match = Regex.Match(version, @"^(?<major>\d+)");
        return match.Success && int.TryParse(match.Groups["major"].Value, out var major) ? major : null;
    }

    private static IReadOnlyList<string> GetSearchDirectories(string phpRoot, string extensionPath)
    {
        var result = new List<string>
        {
            Path.GetDirectoryName(extensionPath) ?? phpRoot,
            phpRoot,
            Path.Combine(phpRoot, "ext"),
            Environment.SystemDirectory
        };
        if (Environment.Is64BitOperatingSystem)
            result.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"));
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
            result.AddRange(pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return result.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    [GeneratedRegex(@"^php\d+(?:ts)?\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex PhpRuntimeDllRegex();
}
