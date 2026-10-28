using System.Diagnostics;
using System.Net;
using System.Text;

namespace XamppUpdater.Core.Services;

/// <summary>Web-SAPI/cURL initialization only; no outbound cURL transfer or service/config changes.</summary>
public static class ApachePhpCurlHttpProbe
{
    public enum ProbeStatus { Pass, Fail, Skip }
    public enum ProbePhase { ConfigDiscovery, TargetSelection, FilePreparation, HttpRequest, PhpExecution, CurlCheck, Cleanup }
    public sealed record Result(bool Executed, bool Success, string Detail)
    {
        public ProbeStatus Status { get; init; } = Success ? ProbeStatus.Pass : Executed ? ProbeStatus.Fail : ProbeStatus.Skip;
        public ProbePhase Phase { get; init; }
        public string ReasonCode { get; init; } = "UNKNOWN";
        public string Protocol { get; init; } = "Unknown";
        public string? Host { get; init; }
        public int? Port { get; init; }
        public string? VirtualHost { get; init; }
        public string? DocumentRoot { get; init; }
        public long ElapsedMilliseconds { get; init; }
    }
    internal sealed class ProbeOptions
    {
        internal Func<HttpMessageHandler>? HandlerFactory { get; init; }
        internal Func<string> NewToken { get; init; } = () => Guid.NewGuid().ToString("N");
        internal Action<string> DeleteFile { get; init; } = File.Delete;
        internal TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
        internal string? ExpectedDocumentRoot { get; init; }
    }
    public static Task<Result> TryRunAsync(string xamppRoot, CancellationToken cancellationToken = default) =>
        RunAsync(xamppRoot, new ProbeOptions(), cancellationToken);

    internal static async Task<Result> RunAsync(string xamppRoot, ProbeOptions options, CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        ApachePhpProbeTargetSelector.Target? target = null;
        Result Outcome(ProbeStatus status, ProbePhase phase, string code, string detail) =>
            new(status != ProbeStatus.Skip, status == ProbeStatus.Pass,
                $"Apache PHP HTTP cURL [{status.ToString().ToUpperInvariant()}:{code}] {detail}")
            {
                Status = status, Phase = phase, ReasonCode = code,
                Protocol = target is null ? "Unknown" : "HTTP", Host = target?.Address.ToString(),
                Port = target?.Port, DocumentRoot = target?.DocumentRoot,
                ElapsedMilliseconds = clock.ElapsedMilliseconds
            };
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var apacheRoot = Path.Combine(xamppRoot, "apache");
            var config = Path.Combine(apacheRoot, "conf", "httpd.conf");
            if (!File.Exists(config))
                return Outcome(ProbeStatus.Skip, ProbePhase.ConfigDiscovery, "CONFIG_MISSING", "Apache 설정 파일이 없습니다.");
            target = ApachePhpProbeTargetSelector.Select(await File.ReadAllTextAsync(config, cancellationToken), apacheRoot, out var reason);
            if (target is null)
                return Outcome(ProbeStatus.Skip, ProbePhase.TargetSelection, reason, "단일 활성 DocumentRoot/로컬 HTTP Listen을 확정할 수 없습니다. Include/조건부 설정/VirtualHost는 후속 단계에서 지원합니다.");
            if (options.ExpectedDocumentRoot is not null && !string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.ExpectedDocumentRoot)),
                target.DocumentRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return Outcome(ProbeStatus.Skip, ProbePhase.TargetSelection, "RUNTIME_ROOT_MISMATCH", "Apache 서비스의 활성 DocumentRoot가 설정 후보와 다릅니다.");
            if (!ApachePhpProbeTargetSelector.IsSafeWebRoot(target.DocumentRoot))
                return Outcome(ProbeStatus.Skip, ProbePhase.TargetSelection, "WEB_ROOT_UNSAFE", "DocumentRoot가 없거나 경로에 재분석 지점/.htaccess가 있습니다: " + target.DocumentRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Outcome(ProbeStatus.Skip, ProbePhase.ConfigDiscovery, "CONFIG_UNREADABLE", ex.Message);
        }
        var token = options.NewToken();
        if (token.Length != 32 || token.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("Probe token must contain 32 hexadecimal characters.");
        var file = Path.Combine(target.DocumentRoot, ".xampp-updater-curl-" + token + ".php");
        var script = "<?php header('Content-Type: text/plain; charset=utf-8'); header('Cache-Control: no-store'); " +
            "$ok = extension_loaded('curl') && function_exists('curl_init') && function_exists('curl_close'); " +
            "try { if ($ok) { $h = curl_init(); $ok = ($h !== false); if ($ok) curl_close($h); } } " +
            "catch (Throwable $e) { $ok = false; } " +
            "echo '" + token + "' . '|' . ($ok ? 'OK' : 'CURL_MISSING');";
        var created = false;
        Result? result = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        try
        {
            if (!ApachePhpProbeTargetSelector.IsSafeWebRoot(target.DocumentRoot))
                return Outcome(ProbeStatus.Skip, ProbePhase.FilePreparation, "WEB_ROOT_CHANGED", target.DocumentRoot);
            using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using var writer = new StreamWriter(output, new UTF8Encoding(false));
                await writer.WriteAsync(script.AsMemory(), timeout.Token);
            }
            using var handler = options.HandlerFactory?.Invoke() ?? new HttpClientHandler
            {
                AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
                Credentials = null, PreAuthenticate = false
            };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var uri = new UriBuilder("http", target.Address.ToString(), target.Port, Path.GetFileName(file)).Uri;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Host = "localhost";
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            const int maxBytes = 16384;
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var bytes = new byte[maxBytes + 1];
            var length = 0;
            while (length < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(length), timeout.Token);
                if (read == 0) break;
                length += read;
            }
            var content = Encoding.UTF8.GetString(bytes, 0, length).Trim();
            if (content.Contains("<?php", StringComparison.OrdinalIgnoreCase) && content.Contains(token, StringComparison.Ordinal))
                result = Outcome(ProbeStatus.Fail, ProbePhase.PhpExecution, "PHP_SOURCE_EXPOSED", "웹 서버가 진단 PHP 소스를 노출했습니다.");
            else if (length > maxBytes)
                result = Outcome(ProbeStatus.Skip, ProbePhase.HttpRequest, "RESPONSE_TOO_LARGE", "응답이 16 KiB 제한을 초과했습니다.");
            else if (content == token + "|CURL_MISSING")
                result = Outcome(ProbeStatus.Fail, ProbePhase.CurlCheck, "CURL_UNAVAILABLE", "웹 SAPI에서 cURL 초기화/정리가 실패했습니다.");
            else if (response.StatusCode == HttpStatusCode.OK && content == token + "|OK")
                result = Outcome(ProbeStatus.Pass, ProbePhase.CurlCheck, "CURL_INITIALIZED", $"로컬 HTTP PHP 실행 및 cURL 초기화/정리 확인 ({target.Address}:{target.Port}, DocumentRoot={target.DocumentRoot}). 외부 HTTPS 통신은 검사하지 않았습니다.");
            else
                result = Outcome(ProbeStatus.Skip, ProbePhase.HttpRequest, "HTTP_UNVERIFIED", $"HTTP {(int)response.StatusCode}: 인증/접근 제한/리디렉션/다른 사이트 또는 PHP 실행 여부를 확인할 수 없는 응답입니다.");
        }
        catch (Exception ex) when (!created && ex is IOException or UnauthorizedAccessException)
        {
            result = Outcome(ProbeStatus.Skip, ProbePhase.FilePreparation, "FILE_NOT_CREATED", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            result = Outcome(ProbeStatus.Skip, ProbePhase.HttpRequest, "PROBE_UNAVAILABLE", ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = Outcome(ProbeStatus.Skip, ProbePhase.HttpRequest, "TIMEOUT", "로컬 HTTP 검증이 제한 시간을 초과했습니다.");
        }
        finally
        {
            if (created)
            {
                Exception? error = null;
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    try { options.DeleteFile(file); error = null; break; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = ex; }
                }
                if (error is not null)
                {
                    var cleanupDetail = "임시 PHP 파일 삭제 실패. 직접 삭제해야 합니다: " + file + " / " + error.Message;
                    result = result is { Status: ProbeStatus.Fail }
                        ? result with { Phase = ProbePhase.Cleanup, Detail = result.Detail + " / " + cleanupDetail }
                        : Outcome(ProbeStatus.Skip, ProbePhase.Cleanup, "CLEANUP_FAILED", cleanupDetail);
                    if (cancellationToken.IsCancellationRequested) throw new IOException(result.Detail, error);
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result! with { ElapsedMilliseconds = clock.ElapsedMilliseconds };
    }
    public static string? FindDocumentRoot(string config, string apacheRoot) =>
        ApachePhpProbeTargetSelector.Select(config + "\nListen 127.0.0.1:80 http\n", apacheRoot, out _)?.DocumentRoot;

    public static int? FindHttpListenPort(string config)
    {
        foreach (var line in config.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Listen ", StringComparison.OrdinalIgnoreCase) &&
                ApachePhpProbeTargetSelector.TryListener(trimmed[7..].Trim(), out _, out var port)) return port;
        }
        return null;
    }
}
