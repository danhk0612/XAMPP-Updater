using System.Diagnostics;

namespace XamppUpdater.Core.Services;

public sealed record PhpTargetRuntimeValidationResult(
    bool Valid,
    string Output,
    string? Error = null);

public static class PhpTargetRuntimeValidator
{
    public static PhpTargetRuntimeValidationResult Validate(
        string phpRoot,
        string iniPath,
        TimeSpan? timeout = null)
    {
        var php = Path.Combine(phpRoot, "php.exe");
        if (!File.Exists(php))
            return new PhpTargetRuntimeValidationResult(false, string.Empty, "스테이징 PHP 실행 파일을 찾지 못했습니다.");
        if (!File.Exists(iniPath))
            return new PhpTargetRuntimeValidationResult(false, string.Empty, "마이그레이션된 php.ini를 찾지 못했습니다.");

        var outputs = new List<string>();
        foreach (var command in new[] { "-v", "-m" })
        {
            try
            {
                var result = Run(
                    php,
                    new[] { "-c", Path.GetFullPath(iniPath), command },
                    phpRoot,
                    timeout ?? TimeSpan.FromSeconds(45));
                outputs.Add($"[{command}]" + Environment.NewLine + result.Output);

                if (result.ExitCode != 0 || HasExtensionLoadFailure(result.Output))
                {
                    return new PhpTargetRuntimeValidationResult(
                        false,
                        string.Join(Environment.NewLine, outputs),
                        $"스테이징 PHP {command} 검증에 실패했습니다. exit={result.ExitCode}");
                }
            }
            catch (Exception ex)
            {
                return new PhpTargetRuntimeValidationResult(
                    false,
                    string.Join(Environment.NewLine, outputs),
                    "스테이징 PHP 실행 검증 실패: " + ex.Message);
            }
        }

        return new PhpTargetRuntimeValidationResult(true, string.Join(Environment.NewLine, outputs));
    }

    internal static bool HasExtensionLoadFailure(string output) =>
        output.Contains("Unable to load dynamic library", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("Module compiled with module API=", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("PHP compiled with module API=", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("Module compiled with build ID=", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("PHP compiled with build ID=", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("These options need to match", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("The specified module could not be found", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("지정된 모듈을 찾을 수 없습니다", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("procedure could not be found", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("프로시저를 찾을 수 없습니다", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("%1 is not a valid Win32 application", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("올바른 Win32 응용 프로그램이 아닙니다", StringComparison.OrdinalIgnoreCase);

    private static ProcessResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        var currentPath = start.Environment["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        start.Environment["PATH"] = string.IsNullOrWhiteSpace(currentPath)
            ? phpBin(workingDirectory)
            : phpBin(workingDirectory) + Path.PathSeparator + currentPath;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue)))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"PHP 검증 시간 초과 ({timeout.TotalSeconds:N0}초): {string.Join(' ', arguments)}");
        }
        Task.WaitAll(stdout, stderr);
        var output = string.Join(Environment.NewLine, new[] { stdout.Result, stderr.Result }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new ProcessResult(process.ExitCode, output.Trim());
    }

    private static string phpBin(string workingDirectory) => Path.GetFullPath(workingDirectory);

    private sealed record ProcessResult(int ExitCode, string Output);
}
