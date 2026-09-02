using System.Diagnostics;
using System.Text.RegularExpressions;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public sealed partial class ComponentVersionDetector : IComponentVersionDetector
{
    public ComponentVersionResult Detect(XamppComponentType type, string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            return new ComponentVersionResult(null, string.Empty, "실행 파일 없음");
        }

        var arguments = type switch
        {
            XamppComponentType.Apache => "-v",
            XamppComponentType.Php => "-v",
            XamppComponentType.MariaDb => "--version",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

        var output = Run(executablePath, arguments);
        var version = type switch
        {
            XamppComponentType.Apache => MatchVersion(ApacheVersionRegex(), output),
            XamppComponentType.Php => MatchVersion(PhpVersionRegex(), output),
            XamppComponentType.MariaDb => MatchVersion(MariaDbVersionRegex(), output),
            _ => null
        };

        string? detail = null;
        if (type == XamppComponentType.MariaDb && !output.Contains("MariaDB", StringComparison.OrdinalIgnoreCase))
        {
            detail = "mysql\\bin\\mysqld.exe는 존재하지만 MariaDB 식별 문자열을 확인하지 못했습니다.";
        }

        return new ComponentVersionResult(version, output.Trim(), detail);
    }

    private static string Run(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(fileName) ?? Environment.CurrentDirectory
            }
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(5000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"버전 확인 시간 초과: {fileName}");
        }

        var standardOutput = stdout.GetAwaiter().GetResult();
        var standardError = stderr.GetAwaiter().GetResult();
        return string.Join(Environment.NewLine, new[] { standardOutput, standardError }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string? MatchVersion(Regex regex, string output)
    {
        var match = regex.Match(output);
        return match.Success ? match.Groups["version"].Value : null;
    }

    [GeneratedRegex(@"Apache/(?<version>\d+(?:\.\d+)+)", RegexOptions.IgnoreCase)]
    private static partial Regex ApacheVersionRegex();

    [GeneratedRegex(@"PHP\s+(?<version>\d+\.\d+\.\d+(?:[-+A-Za-z0-9.]*)?)", RegexOptions.IgnoreCase)]
    private static partial Regex PhpVersionRegex();

    [GeneratedRegex(@"Ver\s+(?<version>\d+\.\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MariaDbVersionRegex();
}
