using System.Net;
using System.Text.RegularExpressions;

namespace XamppUpdater.Core.Services;

/// <summary>
/// Executes a short-lived, unpredictable PHP endpoint through the actual Apache service.
/// Never changes Apache configuration or starts a stopped service. The endpoint only exercises
/// extension_loaded/curl_init/curl_close; it makes no outbound HTTP requests.
/// </summary>
public static class ApachePhpCurlHttpProbe
{
    public sealed record Result(bool Executed, bool Success, string Detail);

    public static async Task<Result> TryRunAsync(string xamppRoot, CancellationToken cancellationToken = default)
    {
        var apacheRoot = Path.Combine(xamppRoot, "apache");
        var config = Path.Combine(apacheRoot, "conf", "httpd.conf");
        var documentRoot = Path.Combine(xamppRoot, "htdocs");
        if (!File.Exists(config) || !Directory.Exists(documentRoot))
            return new Result(false, false, "Apache 설정 또는 기본 htdocs 폴더가 없어 HTTP cURL 검증을 건너뜁니다.");

        var confText = File.ReadAllText(config);
        var configuredRoot = FindDocumentRoot(confText, apacheRoot);
        if (configuredRoot is null || !SamePath(configuredRoot, documentRoot))
            return new Result(false, false,
                "Apache DocumentRoot가 기본 XAMPP htdocs인지 확인되지 않아 HTTP cURL 검증을 건너뜁니다.");

        var port = FindHttpListenPort(confText);
        if (port is null)
            return new Result(false, false,
                "Apache의 로컬 HTTP Listen 포트를 확인하지 못해 HTTP cURL 검증을 건너뜁니다.");

        var token = Guid.NewGuid().ToString("N");
        var file = Path.Combine(documentRoot, ".xampp-updater-curl-" + token + ".php");
        var script = "<?php header('Content-Type: text/plain; charset=utf-8'); " +
            "$ok = extension_loaded('curl') && function_exists('curl_init'); " +
            "if ($ok) { $h = curl_init(); $ok = ($h !== false); if ($ok) curl_close($h); } " +
            "echo '" + token + "|' . ($ok ? 'OK' : 'CURL_MISSING');";
        var created = false;
        try
        {
            // CreateNew prevents overwriting user content even if the random name collides.
            using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using var writer = new StreamWriter(output);
                writer.Write(script);
            }

            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri($"http://127.0.0.1:{port}/" + Path.GetFileName(file)));
            request.Headers.Host = "localhost";
            using var response = await client.SendAsync(request, cancellationToken);
            var content = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();

            if (content.Equals(token + "|OK", StringComparison.Ordinal) && response.StatusCode == HttpStatusCode.OK)
                return new Result(true, true, $"Apache HTTP PHP cURL 실행 검증 성공 (loopback:{port})");
            if (content.Equals(token + "|CURL_MISSING", StringComparison.Ordinal))
                return new Result(true, false, "Apache HTTP PHP cURL 실행 검증 실패: 웹 SAPI에서 curl_init을 사용할 수 없습니다.");
            if (response.IsSuccessStatusCode && content.Contains(token + "|", StringComparison.Ordinal))
                return new Result(true, false, "Apache HTTP PHP cURL 응답에 예기치 않은 오류 또는 출력이 있습니다.");

            // A virtual host, rewrite rule, or access policy may prevent reaching this file.
            return new Result(false, false,
                $"HTTP 요청 상태 {(int)response.StatusCode}: 진단 PHP 파일이 실행됐는지 확인할 수 없어 건너뜁니다.");
        }
        catch (UnauthorizedAccessException ex) when (!created)
        {
            return new Result(false, false, "Apache DocumentRoot에 테스트 파일을 생성할 수 없어 HTTP 검증을 건너뜁니다: " + ex.Message);
        }
        catch (IOException ex) when (!created)
        {
            return new Result(false, false, "Apache DocumentRoot 테스트 파일을 만들 수 없어 HTTP 검증을 건너뜁니다: " + ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new Result(false, false, "Apache 루프백 접속이 불가능해 HTTP cURL 검증을 건너뜁니다: " + ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new Result(false, false, "Apache 루프백 HTTP 검증이 제한 시간(5초)을 초과해 건너뜁니다.");
        }
        finally
        {
            // Never leave an executable test endpoint accessible from the web.
            if (created && File.Exists(file))
            {
                try { File.Delete(file); }
                catch (Exception ex)
                {
                    throw new IOException("임시 Apache PHP 진단 파일을 삭제하지 못했습니다: " + file, ex);
                }
            }
        }
    }

    public static string? FindDocumentRoot(string config, string apacheRoot)
    {
        var match = Regex.Match(config, @"(?im)^\s*DocumentRoot\s+(?:""(?<path>[^""]+)""|(?<path>\S+))\s*(?:#.*)?$");
        if (!match.Success) return null;
        var value = match.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar);
        // Only literal absolute paths, never expand environment variables or aliases.
        if (value.Contains('$') || !Path.IsPathFullyQualified(value)) return null;
        return Path.GetFullPath(value);
    }

    public static int? FindHttpListenPort(string config)
    {
        foreach (Match match in Regex.Matches(config, @"(?im)^\s*Listen\s+(?<value>\S+)\s*(?:#.*)?$"))
        {
            var raw = match.Groups["value"].Value;
            var portText = raw.Contains(':') ? raw[(raw.LastIndexOf(':') + 1)..] : raw;
            if (int.TryParse(portText, out var port) && port is >= 1 and <= 65535 && port != 443)
                return port;
        }
        return null;
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
}
