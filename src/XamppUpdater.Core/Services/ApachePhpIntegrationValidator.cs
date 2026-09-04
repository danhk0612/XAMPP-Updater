using System.Diagnostics;
using System.Text.RegularExpressions;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed record ApachePhpIntegrationValidationResult(
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Warnings);

public interface IApachePhpIntegrationValidator
{
    Task<ApachePhpIntegrationValidationResult> ValidateAsync(
        XamppInstallation installation,
        bool requireApacheRunning,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Final integration gate shared by Apache/PHP update and rollback paths.
/// It never changes component versions. It only validates the currently installed pair.
/// </summary>
public sealed class ApachePhpIntegrationValidator : IApachePhpIntegrationValidator
{
    private readonly IWindowsServiceController _services;

    public ApachePhpIntegrationValidator(IWindowsServiceController? services = null)
    {
        _services = services ?? new WindowsServiceController();
    }

    public async Task<ApachePhpIntegrationValidationResult> ValidateAsync(
        XamppInstallation installation,
        bool requireApacheRunning,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<string>();
        var warnings = new List<string>();
        var xamppRoot = installation.RootPath;
        var apacheRoot = Path.Combine(xamppRoot, "apache");
        var phpRoot = Path.Combine(xamppRoot, "php");
        var httpd = Path.Combine(apacheRoot, "bin", "httpd.exe");
        var php = Path.Combine(phpRoot, "php.exe");

        if (!Directory.Exists(apacheRoot) || !File.Exists(httpd))
            throw new InvalidOperationException("Apache/PHP 연동 검증을 위한 Apache 설치를 찾을 수 없습니다.");
        if (!Directory.Exists(phpRoot) || !File.Exists(php))
            throw new InvalidOperationException("Apache/PHP 연동 검증을 위한 PHP 설치를 찾을 수 없습니다.");

        var phpVersion = await RunAsync(php, ["-v"], phpRoot, cancellationToken);
        EnsureSuccess(phpVersion, "PHP -v");
        EnsureNoPhpStartupFailure(phpVersion.Output);
        steps.Add("PHP -v 연동 전 검증 완료");

        var phpModules = await RunAsync(php, ["-m"], phpRoot, cancellationToken);
        EnsureSuccess(phpModules, "PHP -m");
        EnsureNoPhpStartupFailure(phpModules.Output);
        steps.Add("PHP -m 연동 전 검증 완료");

        var configuredModule = FindConfiguredPhpModule(apacheRoot);
        if (configuredModule is not null)
        {
            if (!File.Exists(configuredModule))
                throw new InvalidOperationException("Apache가 참조하는 PHP module DLL이 없습니다: " + configuredModule);

            if (OperatingSystem.IsWindows())
            {
                var loader = WindowsLoaderProbe.TryLoad(
                    configuredModule,
                    new[]
                    {
                        phpRoot,
                        Path.Combine(apacheRoot, "bin"),
                        Path.Combine(apacheRoot, "modules")
                    });
                if (!loader.Success)
                {
                    throw new InvalidOperationException(
                        $"Apache PHP module DLL 로더 검증 실패 (Windows error {loader.ErrorCode}): {loader.Message}");
                }
                steps.Add("Apache PHP module DLL Windows loader 검증 완료");
            }
            else
            {
                warnings.Add("Windows가 아닌 테스트 환경에서는 PHP module DLL loader 검증을 건너뜁니다.");
            }
        }
        else
        {
            warnings.Add("Apache 설정에서 활성 mod_php LoadModule을 찾지 못했습니다. DLL loader 검증은 생략합니다.");
        }

        var apacheTest = await RunAsync(httpd, ["-t"], apacheRoot, cancellationToken);
        EnsureSuccess(apacheTest, "Apache httpd -t");
        if (apacheTest.Output.Contains("Syntax error", StringComparison.OrdinalIgnoreCase) ||
            apacheTest.Output.Contains("Cannot load", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Apache/PHP 연동 구성 검사 실패: " + Compact(apacheTest.Output));
        }
        steps.Add("Apache httpd -t 연동 검증 완료");

        if (requireApacheRunning)
        {
            var serviceName = installation.Components
                .FirstOrDefault(component => component.Type == XamppComponentType.Apache)?.ServiceName;
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new InvalidOperationException("Apache 실행 상태 검증이 필요하지만 서비스 이름을 확인할 수 없습니다.");

            var state = _services.GetState(serviceName);
            if (!string.Equals(state, "RUNNING", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Apache/PHP 연동 후 Apache 서비스 상태가 RUNNING이 아닙니다: {state}");
            steps.Add($"Apache 서비스 RUNNING 연동 검증 완료: {serviceName}");
        }

        return new ApachePhpIntegrationValidationResult(steps, warnings);
    }

    private static string? FindConfiguredPhpModule(string apacheRoot)
    {
        var confRoot = Path.Combine(apacheRoot, "conf");
        if (!Directory.Exists(confRoot)) return null;

        foreach (var file in Directory.EnumerateFiles(confRoot, "*.conf", SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith('#')) continue;
                var match = Regex.Match(
                    line,
                    "^\\s*LoadModule\\s+php(?:\\d+)?_module\\s+(?<path>\"[^\"]+\"|\\S*php\\d*apache2_4\\.dll\\S*)\\s*$",
                    RegexOptions.IgnoreCase);
                if (!match.Success) continue;

                var raw = match.Groups["path"].Value.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathRooted(raw)) return Path.GetFullPath(raw);
                return Path.GetFullPath(Path.Combine(apacheRoot, raw));
            }
        }

        return null;
    }

    private static void EnsureSuccess(ProcessResult result, string name)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{name} 검증 실패 (exit {result.ExitCode}): {Compact(result.Output)}");
    }

    private static void EnsureNoPhpStartupFailure(string output)
    {
        if (output.Contains("PHP Startup: Unable to load dynamic library", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Unable to load dynamic library", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PHP 확장 로드 오류가 발생했습니다: " + Compact(output));
        }
    }

    private static async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("연동 검증 실행 파일을 찾을 수 없습니다.", executable);

        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            throw;
        }

        var output = string.Join(
            Environment.NewLine,
            new[] { await stdoutTask, await stderrTask }.Where(text => !string.IsNullOrWhiteSpace(text)));
        return new ProcessResult(process.ExitCode, output.Trim());
    }

    private static string Compact(string text) => text.Replace("\r", " ").Replace("\n", " ").Trim();

    private sealed record ProcessResult(int ExitCode, string Output);
}
