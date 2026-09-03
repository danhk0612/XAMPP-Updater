using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IVisualCppRuntimeInstaller
{
    Task<VisualCppRuntimeInstallResult> EnsureMinimumAsync(
        BinaryArchitecture architecture,
        Version minimumVersion,
        CancellationToken cancellationToken = default);
}

public sealed class VisualCppRuntimeInstaller : IVisualCppRuntimeInstaller
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    public async Task<VisualCppRuntimeInstallResult> EnsureMinimumAsync(
        BinaryArchitecture architecture,
        Version minimumVersion,
        CancellationToken cancellationToken = default)
    {
        var before = GetInstalledVersion(architecture);
        if (before is not null && before >= minimumVersion)
        {
            return new VisualCppRuntimeInstallResult(
                true, false, false, 0, before, before, null, null);
        }

        var (url, fileName) = architecture switch
        {
            BinaryArchitecture.X86 => ("https://aka.ms/vc14/vc_redist.x86.exe", "vc_redist.x86.exe"),
            BinaryArchitecture.Arm64 => ("https://aka.ms/vc14/vc_redist.arm64.exe", "vc_redist.arm64.exe"),
            _ => ("https://aka.ms/vc14/vc_redist.x64.exe", "vc_redist.x64.exe")
        };

        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XamppUpdater",
            "Runtime");
        Directory.CreateDirectory(cacheRoot);
        var installerPath = Path.Combine(cacheRoot, fileName);
        var temporaryPath = installerPath + ".part";

        await DownloadAsync(url, temporaryPath, cancellationToken);
        File.Move(temporaryPath, installerPath, overwrite: true);
        var sha256 = ComputeSha256(installerPath);

        var start = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/install /quiet /norestart",
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = cacheRoot
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Visual C++ Redistributable 설치 프로그램을 시작하지 못했습니다.");
        await process.WaitForExitAsync(cancellationToken);

        var success = process.ExitCode is 0 or 1638 or 3010;
        var rebootRequired = process.ExitCode == 3010;
        var after = GetInstalledVersion(architecture);
        if (success && !rebootRequired && (after is null || after < minimumVersion))
        {
            success = false;
        }

        return new VisualCppRuntimeInstallResult(
            success,
            true,
            rebootRequired,
            process.ExitCode,
            before,
            after,
            installerPath,
            sha256);
    }

    internal static Version? GetInstalledVersion(BinaryArchitecture architecture)
    {
        try
        {
            string directory;
            if (architecture == BinaryArchitecture.X86 && Environment.Is64BitOperatingSystem)
            {
                directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64");
            }
            else
            {
                directory = Environment.SystemDirectory;
            }

            var path = Path.Combine(directory, "VCRUNTIME140.dll");
            if (!File.Exists(path)) return null;
            var versionText = FileVersionInfo.GetVersionInfo(path).FileVersion;
            if (string.IsNullOrWhiteSpace(versionText)) return null;
            var normalized = versionText.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            return Version.TryParse(normalized, out var version) ? version : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("XAMPP-Updater", "0.4"));
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

public sealed record VisualCppRuntimeInstallResult(
    bool Success,
    bool Installed,
    bool RebootRequired,
    int ExitCode,
    Version? BeforeVersion,
    Version? AfterVersion,
    string? InstallerPath,
    string? Sha256);
