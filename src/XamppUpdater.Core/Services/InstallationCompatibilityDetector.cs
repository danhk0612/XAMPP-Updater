using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IInstallationCompatibilityDetector
{
    InstallationCompatibilityProfile Detect(string rootPath, XamppInstallation installation);
}

public sealed partial class InstallationCompatibilityDetector : IInstallationCompatibilityDetector
{
    public InstallationCompatibilityProfile Detect(string rootPath, XamppInstallation installation)
    {
        var apache = installation.Components.First(component => component.Type == XamppComponentType.Apache);
        var php = installation.Components.First(component => component.Type == XamppComponentType.Php);
        var mariaDb = installation.Components.First(component => component.Type == XamppComponentType.MariaDb);

        var phpInfo = php.IsInstalled ? Run(php.ExecutablePath, "-i") : string.Empty;
        var phpProfile = ParsePhpInfo(phpInfo);
        var integration = DetectApachePhpIntegration(rootPath);

        return new InstallationCompatibilityProfile(
            rootPath,
            DetectArchitecture(apache.ExecutablePath),
            DetectArchitecture(php.ExecutablePath),
            DetectArchitecture(mariaDb.ExecutablePath),
            phpProfile,
            integration,
            ParseMajorMinor(mariaDb.Version));
    }

    internal static PhpRuntimeProfile ParsePhpInfo(string output)
    {
        return new PhpRuntimeProfile(
            ParseEnabledDisabled(output, "Thread Safety"),
            MatchValue(output, "Compiler"),
            MatchValue(output, "PHP Extension Build"),
            MatchValue(output, "PHP API"));
    }

    internal static ApachePhpIntegration ParseApachePhpIntegration(string configPath, string content)
    {
        foreach (Match match in PhpLoadModuleRegex().Matches(content))
        {
            var moduleName = match.Groups["module"].Value;
            var modulePath = match.Groups["path"].Value;
            return new ApachePhpIntegration(true, moduleName, modulePath, configPath);
        }

        return new ApachePhpIntegration(false, null, null, null);
    }

    private static ApachePhpIntegration DetectApachePhpIntegration(string rootPath)
    {
        var confRoot = Path.Combine(rootPath, "apache", "conf");
        if (!Directory.Exists(confRoot))
        {
            return new ApachePhpIntegration(false, null, null, null);
        }

        foreach (var configPath in Directory.EnumerateFiles(confRoot, "*.conf", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var content = File.ReadAllText(configPath);
                var result = ParseApachePhpIntegration(configPath, content);
                if (result.IsModuleLoaded)
                {
                    return result;
                }
            }
            catch (IOException)
            {
                // 읽을 수 없는 개별 설정 파일은 건너뛴다.
            }
            catch (UnauthorizedAccessException)
            {
                // 읽을 수 없는 개별 설정 파일은 건너뛴다.
            }
        }

        return new ApachePhpIntegration(false, null, null, null);
    }

    internal static BinaryArchitecture DetectArchitecture(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            return BinaryArchitecture.Unknown;
        }

        try
        {
            using var stream = File.OpenRead(executablePath);
            using var peReader = new PEReader(stream);
            return peReader.PEHeaders.CoffHeader.Machine switch
            {
                System.Reflection.PortableExecutable.Machine.I386 => BinaryArchitecture.X86,
                System.Reflection.PortableExecutable.Machine.Amd64 => BinaryArchitecture.X64,
                System.Reflection.PortableExecutable.Machine.Arm64 => BinaryArchitecture.Arm64,
                _ => BinaryArchitecture.Unknown
            };
        }
        catch (BadImageFormatException)
        {
            return BinaryArchitecture.Unknown;
        }
        catch (IOException)
        {
            return BinaryArchitecture.Unknown;
        }
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

        if (!process.WaitForExit(8000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"PHP 환경 확인 시간 초과: {fileName}");
        }

        return string.Join(Environment.NewLine,
            new[] { stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult() }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static bool? ParseEnabledDisabled(string output, string key)
    {
        var value = MatchValue(output, key);
        if (value is null)
        {
            return null;
        }

        if (value.Equals("enabled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static string? MatchValue(string output, string key)
    {
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var separator = line.IndexOf("=>", StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var candidateKey = line[..separator].Trim();
            if (!candidateKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(separator + 2)..].Trim();
            var secondSeparator = value.IndexOf("=>", StringComparison.Ordinal);
            return (secondSeparator >= 0 ? value[..secondSeparator] : value).Trim();
        }

        return null;
    }

    private static string? ParseMajorMinor(string? version)
    {
        if (version is null || !Version.TryParse(version, out var parsed))
        {
            return null;
        }

        return $"{parsed.Major}.{parsed.Minor}";
    }

    [GeneratedRegex(@"(?im)^\s*LoadModule\s+(?<module>php\w*_module)\s+[\"'](?<path>[^\"']*php[^\"']*apache2_4\.dll)[\"']")]
    private static partial Regex PhpLoadModuleRegex();
}
