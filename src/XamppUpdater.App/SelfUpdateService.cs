using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XamppUpdater.App;

internal sealed record AppUpdateInfo(
    Version Version,
    string TagName,
    Uri ExecutableUri,
    Uri ChecksumUri,
    string ReleasePageUrl);

internal sealed record AppUpdateDownloadProgress(long BytesReceived, long? TotalBytes);

internal sealed class SelfUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/danhk0612/XAMPP-Updater/releases/latest";
    private const string ExecutableAssetName = "XAMPP-Updater.exe";
    private const string ChecksumAssetName = "XAMPP-Updater.exe.sha256";
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);

    public bool IsPublishedExecutable
    {
        get
        {
            var processPath = Environment.ProcessPath;
            return !string.IsNullOrWhiteSpace(processPath) &&
                   !string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task<(AppUpdateInfo? Update, bool ReleaseExists)> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        using var response = await HttpClient.GetAsync(LatestReleaseApi, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return (null, false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var tagName = root.GetProperty("tag_name").GetString();
        if (!TryParseTagVersion(tagName, out var releaseVersion))
            throw new InvalidOperationException($"GitHub 최신 릴리스 버전을 해석할 수 없습니다: {tagName ?? "(없음)"}");

        if (releaseVersion <= CurrentVersion) return (null, true);

        Uri? executableUri = null;
        Uri? checksumUri = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var downloadUrl = asset.GetProperty("browser_download_url").GetString();
            if (string.IsNullOrWhiteSpace(downloadUrl)) continue;

            if (string.Equals(name, ExecutableAssetName, StringComparison.OrdinalIgnoreCase))
                executableUri = new Uri(downloadUrl);
            else if (string.Equals(name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase))
                checksumUri = new Uri(downloadUrl);
        }

        if (executableUri is null || checksumUri is null)
            throw new InvalidOperationException("최신 릴리스에 XAMPP-Updater.exe 또는 SHA256 검증 파일이 없습니다.");

        var releasePageUrl = root.TryGetProperty("html_url", out var htmlUrl)
            ? htmlUrl.GetString() ?? string.Empty
            : string.Empty;

        return (new AppUpdateInfo(releaseVersion, tagName!, executableUri, checksumUri, releasePageUrl), true);
    }

    public async Task<string> DownloadAsync(
        AppUpdateInfo update,
        IProgress<AppUpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var updateRoot = GetUpdateRoot();
        Directory.CreateDirectory(updateRoot);

        var tempPath = Path.Combine(updateRoot, $"XAMPP-Updater-{update.Version}.exe.download");
        if (File.Exists(tempPath)) File.Delete(tempPath);

        try
        {
            using var response = await HttpClient.GetAsync(update.ExecutableUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);
            var buffer = new byte[1024 * 128];
            long received = 0;

            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                progress?.Report(new AppUpdateDownloadProgress(received, totalBytes));
            }

            progress?.Report(new AppUpdateDownloadProgress(received, totalBytes));
            return tempPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    public async Task<string> VerifyAndStageAsync(AppUpdateInfo update, string downloadedPath)
    {
        try
        {
            var checksumText = (await HttpClient.GetStringAsync(update.ChecksumUri)).Trim();
            var expectedHash = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(expectedHash) || expectedHash.Length != 64)
                throw new InvalidOperationException("릴리스 SHA256 검증 파일 형식이 올바르지 않습니다.");

            await using (var stream = File.OpenRead(downloadedPath))
            {
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("다운로드한 앱의 SHA256이 릴리스 검증값과 일치하지 않습니다.");
            }

            var stagedPath = Path.Combine(GetUpdateRoot(), $"XAMPP-Updater-{update.Version}.exe");
            if (File.Exists(stagedPath)) File.Delete(stagedPath);
            File.Move(downloadedPath, stagedPath);
            return stagedPath;
        }
        catch
        {
            TryDelete(downloadedPath);
            throw;
        }
    }

    public void StartReplacement(string stagedExecutablePath)
    {
        var targetPath = Environment.ProcessPath ?? throw new InvalidOperationException("현재 실행 파일 경로를 확인할 수 없습니다.");
        if (!IsPublishedExecutable)
            throw new InvalidOperationException("dotnet run 개발 실행 상태에서는 앱 자체 업데이트를 적용할 수 없습니다. 배포된 XAMPP-Updater.exe에서 실행하세요.");

        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("현재 실행 파일 폴더를 확인할 수 없습니다.");
        var updateRoot = Path.GetDirectoryName(stagedExecutablePath)
            ?? throw new InvalidOperationException("업데이트 임시 폴더를 확인할 수 없습니다.");
        Directory.CreateDirectory(updateRoot);

        var scriptPath = Path.Combine(updateRoot, $"apply-{Guid.NewGuid():N}.ps1");
        var logPath = Path.Combine(updateRoot, "self-update.log");
        File.WriteAllText(scriptPath, BuildReplacementScript(), new UTF8Encoding(false));

        var requiresElevation = !CanWriteDirectory(targetDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = updateRoot,
            UseShellExecute = requiresElevation,
            CreateNoWindow = !requiresElevation
        };
        if (requiresElevation) startInfo.Verb = "runas";

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add(stagedExecutablePath);
        startInfo.ArgumentList.Add(targetPath);
        startInfo.ArgumentList.Add(logPath);

        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("앱 업데이트 적용 프로세스를 시작하지 못했습니다.");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XAMPP-Updater/0.1");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static bool TryParseTagVersion(string? tagName, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tagName)) return false;
        var normalized = tagName.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V')) normalized = normalized[1..];
        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0) normalized = normalized[..suffixIndex];
        if (!Version.TryParse(normalized, out var parsedVersion) || parsedVersion is null) return false;
        version = parsedVersion;
        return true;
    }

    private static string GetUpdateRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XAMPP-Updater",
        "SelfUpdate");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static bool CanWriteDirectory(string directory)
    {
        var probePath = Path.Combine(directory, $".xampp-updater-write-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probePath)) { }
            File.Delete(probePath);
            return true;
        }
        catch
        {
            TryDelete(probePath);
            return false;
        }
    }

    private static string BuildReplacementScript() => """
param(
    [int]$ParentPid,
    [string]$Source,
    [string]$Target,
    [string]$LogPath
)
$ErrorActionPreference = 'Stop'
function Write-UpdateLog([string]$Message) {
    Add-Content -LiteralPath $LogPath -Value (('[{0:yyyy-MM-dd HH:mm:ss}] {1}' -f (Get-Date), $Message)) -Encoding UTF8
}
$backup = $Target + '.update-backup'
try {
    Write-UpdateLog ('Waiting for PID ' + $ParentPid)
    while (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue) {
        Start-Sleep -Milliseconds 300
    }
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
    Copy-Item -LiteralPath $Target -Destination $backup -Force
    Copy-Item -LiteralPath $Source -Destination $Target -Force
    $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash
    if ($sourceHash -ne $targetHash) { throw 'Replaced executable hash mismatch.' }
    $newProcess = Start-Process -FilePath $Target -WorkingDirectory (Split-Path -Parent $Target) -PassThru
    Start-Sleep -Seconds 2
    if ($newProcess.HasExited) { throw ('Updated application exited immediately with code ' + $newProcess.ExitCode) }
    Remove-Item -LiteralPath $backup -Force
    Write-UpdateLog 'Update applied and application restarted.'
}
catch {
    Write-UpdateLog ('ERROR: ' + $_.Exception.Message)
    if (Test-Path -LiteralPath $backup) {
        Copy-Item -LiteralPath $backup -Destination $Target -Force
        Write-UpdateLog 'Previous executable restored from .update-backup.'
        try { Start-Process -FilePath $Target -WorkingDirectory (Split-Path -Parent $Target) } catch { }
    }
    exit 1
}
finally {
    Remove-Item -LiteralPath $Source -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
}
""";
}
