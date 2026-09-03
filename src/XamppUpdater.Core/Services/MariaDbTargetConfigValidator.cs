using System.Diagnostics;
using System.IO.Compression;

namespace XamppUpdater.Core.Services;

public sealed record MariaDbTargetConfigValidationResult(
    bool Valid,
    string? ConfigPath,
    string? ServerExecutable,
    string Output,
    string? Error = null);

public static class MariaDbTargetConfigValidator
{
    public static MariaDbTargetConfigValidationResult Validate(
        string packagePath,
        string? currentConfigPath,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(currentConfigPath) || !File.Exists(currentConfigPath))
            return new MariaDbTargetConfigValidationResult(true, currentConfigPath, null, "설정 파일 없음 - 대상 바이너리 파싱 검사 생략");
        if (!File.Exists(packagePath))
            return new MariaDbTargetConfigValidationResult(false, currentConfigPath, null, string.Empty, "MariaDB 대상 패키지를 찾을 수 없습니다.");

        var root = Path.Combine(Path.GetTempPath(), "xampp-updater-mariadb-config-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            ZipFile.ExtractToDirectory(packagePath, root, overwriteFiles: true);
            var server = Directory.EnumerateFiles(root, "mariadbd.exe", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(root, "mysqld.exe", SearchOption.AllDirectories))
                .FirstOrDefault();
            if (server is null)
                return new MariaDbTargetConfigValidationResult(false, currentConfigPath, null, string.Empty, "대상 패키지에서 mariadbd.exe/mysqld.exe를 찾지 못했습니다.");

            var workingDirectory = Directory.GetParent(Path.GetDirectoryName(server)!)?.FullName ?? Path.GetDirectoryName(server)!;
            var result = Run(
                server,
                new[] { $"--defaults-file={Path.GetFullPath(currentConfigPath)}", "--help", "--verbose" },
                workingDirectory,
                timeout ?? TimeSpan.FromSeconds(45));

            var output = result.Output;
            var invalidOption =
                output.Contains("unknown variable", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("unknown option", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("unknown argument", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("unknown suffix", StringComparison.OrdinalIgnoreCase);
            var valid = result.ExitCode == 0 && !invalidOption;
            return new MariaDbTargetConfigValidationResult(
                valid,
                currentConfigPath,
                server,
                output,
                valid ? null : $"대상 MariaDB가 현재 설정을 정상 파싱하지 못했습니다. exit={result.ExitCode}");
        }
        catch (TimeoutException ex)
        {
            return new MariaDbTargetConfigValidationResult(false, currentConfigPath, null, string.Empty, ex.Message);
        }
        catch (Exception ex)
        {
            return new MariaDbTargetConfigValidationResult(false, currentConfigPath, null, string.Empty, "대상 MariaDB 설정 사전검사 실패: " + ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

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
        var bin = Path.GetDirectoryName(executable);
        if (!string.IsNullOrWhiteSpace(bin))
        {
            var path = start.Environment["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            start.Environment["PATH"] = string.IsNullOrWhiteSpace(path) ? bin : bin + Path.PathSeparator + path;
        }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue)))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"MariaDB 설정 사전검사 시간 초과 ({timeout.TotalSeconds:N0}초)");
        }
        Task.WaitAll(stdout, stderr);
        var output = string.Join(Environment.NewLine, new[] { stdout.Result, stderr.Result }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new ProcessResult(process.ExitCode, output.Trim());
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
