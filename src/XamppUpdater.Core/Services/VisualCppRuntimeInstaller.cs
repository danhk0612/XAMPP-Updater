using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public interface IVisualCppRuntimeInstaller
{
    Task<VisualCppRuntimeInstallResult> EnsureLatestAsync(
        BinaryArchitecture architecture,
        CancellationToken cancellationToken = default);
}

public sealed class VisualCppRuntimeInstaller : IVisualCppRuntimeInstaller
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    public async Task<VisualCppRuntimeInstallResult> EnsureLatestAsync(
        BinaryArchitecture architecture,
        CancellationToken cancellationToken = default)
    {
        var url = architecture switch
        {
            BinaryArchitecture.X86 => "https://aka.ms/vc14/vc_redist.x86.exe",
            BinaryArchitecture.Arm64 => "https://aka.ms/vc14/vc_redist.arm64.exe",
            _ => "https://aka.ms/vc14/vc_redist.x64.exe"
        };

        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XamppUpdater",
            "Runtime");
        Directory.CreateDirectory(cacheRoot);
        var installerPath = Path.Combine(cacheRoot, Path.GetFileName(new Uri(url).LocalPath));
        if (string.IsNullOrWhiteSpace(Path.GetFileName(installerPath)) || installerPath.EndsWith("Runtime", StringComparison.OrdinalIgnoreCase))
        {
            installerPath = Path.Combine(cacheRoot, architecture == BinaryArchitecture.X86 ? "vc_redist.x86.exe" : architecture == BinaryArchitecture.Arm64 ? "vc_redist.arm64.exe" : "vc_redist.x64.exe");
        }

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

        return process.ExitCode switch
        {
            0 => new VisualCppRuntimeInstallResult(true, false, process.ExitCode, installerPath, sha256),
            1638 => new VisualCppRuntimeInstallResult(true, false, process.ExitCode, installerPath, sha256),
            3010 => new VisualCppRuntimeInstallResult(true, true, process.ExitCode, installerPath, sha256),
            _ => new VisualCppRuntimeInstallResult(false, false, process.ExitCode, installerPath, sha256)
        };
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
    bool RebootRequired,
    int ExitCode,
    string InstallerPath,
    string Sha256);
