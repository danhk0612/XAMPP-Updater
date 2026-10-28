using System.Net;
using System.Net.Sockets;
using System.Text;
using XamppUpdater.Core.Services;
using static XamppUpdater.Core.Services.ApachePhpCurlHttpProbe;

internal static class ApachePhpHttpProbeSmoke
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "xampp-probe-" + Guid.NewGuid().ToString("N"));
        var apache = Path.Combine(root, "apache");
        var conf = Path.Combine(apache, "conf", "httpd.conf");
        Directory.CreateDirectory(Path.GetDirectoryName(conf)!);
        var checks = 0;
        void Assert(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException(message); }
        string Config(string web) => $"ServerRoot \"{apache.Replace('\\', '/')}\"\nDocumentRoot \"{web.Replace('\\', '/')}\"\nListen 127.0.0.1:8080 http\n";
        void NoFiles(string web) => Assert(!Directory.EnumerateFiles(web, ".xampp-updater-curl-*.php").Any(), "Diagnostic endpoint leaked");
        var token = new string('a', 32);
        var web = Path.Combine(root, "external Web 고객사이트");
        Directory.CreateDirectory(web);
        ProbeOptions Options(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply,
            Action<string>? delete = null, TimeSpan? timeout = null) => new()
            {
                NewToken = () => token, HandlerFactory = () => new Handler(reply),
                DeleteFile = delete ?? File.Delete, Timeout = timeout ?? TimeSpan.FromSeconds(5)
            };
        ProbeOptions Reply(HttpStatusCode status, string body) => Options((request, _) =>
        {
            Assert(request.RequestUri!.Host == "127.0.0.1" && request.RequestUri.Port == 8080, "Non-loopback target");
            Assert(request.Headers.Host == "localhost", "Unexpected Host");
            Assert(File.Exists(Path.Combine(web, Path.GetFileName(request.RequestUri.AbsolutePath))), "Endpoint not written in configured root");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        });
        try
        {
            foreach (var folder in new[] { "htdocs", "custom", "Web Sites", "고객사이트", "external Web 고객사이트" })
            {
                web = Path.Combine(root, folder); Directory.CreateDirectory(web);
                var config = Config(web); File.WriteAllText(conf, config);
                var result = await ApachePhpCurlHttpProbe.RunAsync(root, Reply(HttpStatusCode.OK, token + "|OK"));
                Assert(result.Success && result.Status == ProbeStatus.Pass && result.DocumentRoot == web, "Custom root not verified");
                Assert(File.ReadAllText(conf) == config, "Apache config modified"); NoFiles(web);
            }
            foreach (var entry in new[]
            {
                (HttpStatusCode.OK, token + "|CURL_MISSING", ProbeStatus.Fail, "CURL_UNAVAILABLE"),
                (HttpStatusCode.OK, "<?php echo '" + token + "';", ProbeStatus.Fail, "PHP_SOURCE_EXPOSED"),
                (HttpStatusCode.OK, "another-site", ProbeStatus.Skip, "HTTP_UNVERIFIED"),
                (HttpStatusCode.OK, token + "|OK diagnostic warning", ProbeStatus.Skip, "HTTP_UNVERIFIED"),
                (HttpStatusCode.Found, "redirect", ProbeStatus.Skip, "HTTP_UNVERIFIED"),
                (HttpStatusCode.Unauthorized, "auth", ProbeStatus.Skip, "HTTP_UNVERIFIED"),
                (HttpStatusCode.Forbidden, "denied", ProbeStatus.Skip, "HTTP_UNVERIFIED"),
                (HttpStatusCode.NotFound, "missing", ProbeStatus.Skip, "HTTP_UNVERIFIED"),
                (HttpStatusCode.InternalServerError, "ambiguous", ProbeStatus.Skip, "HTTP_UNVERIFIED"),
                (HttpStatusCode.OK, new string('x', 20000), ProbeStatus.Skip, "RESPONSE_TOO_LARGE")
            })
            {
                var result = await ApachePhpCurlHttpProbe.RunAsync(root, Reply(entry.Item1, entry.Item2));
                Assert(result.Status == entry.Item3 && result.ReasonCode == entry.Item4, "Wrong classification: " + entry.Item4); NoFiles(web);
            }
            var collision = Path.Combine(web, ".xampp-updater-curl-" + token + ".php");
            File.WriteAllText(collision, "USER FILE");
            var collided = await ApachePhpCurlHttpProbe.RunAsync(root, Reply(HttpStatusCode.OK, token + "|OK"));
            Assert(collided.ReasonCode == "FILE_NOT_CREATED" && File.ReadAllText(collision) == "USER FILE", "Collision destroyed existing file");
            File.Delete(collision);
            using (var cancel = new CancellationTokenSource())
            {
                try
                {
                    await ApachePhpCurlHttpProbe.RunAsync(root, Options((_, ct) => { cancel.Cancel(); ct.ThrowIfCancellationRequested(); throw new Exception(); }), cancel.Token);
                    throw new InvalidOperationException("Cancellation swallowed");
                }
                catch (OperationCanceledException) { Assert(true, "cancel"); }
                NoFiles(web);
            }
            var timed = await ApachePhpCurlHttpProbe.RunAsync(root, Options(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); throw new Exception(); }, timeout: TimeSpan.FromMilliseconds(50)));
            Assert(timed.ReasonCode == "TIMEOUT", "Timeout not classified"); NoFiles(web);
            var network = await ApachePhpCurlHttpProbe.RunAsync(root, Options((_, _) => throw new HttpRequestException("refused")));
            Assert(network.Status == ProbeStatus.Skip, "Connection failure triggered rollback"); NoFiles(web);
            var cleanup = await ApachePhpCurlHttpProbe.RunAsync(root, Options((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(token + "|OK") }), _ => throw new IOException("locked")));
            Assert(cleanup.Status == ProbeStatus.Skip && cleanup.ReasonCode == "CLEANUP_FAILED" && cleanup.Detail.Contains(collision), "Cleanup failure hidden");
            File.Delete(collision);
            foreach (var config in new[]
            {
                Config(web) + "Include conf/extra/*.conf\n", Config(web) + "<VirtualHost *:80>\nDocumentRoot x\n</VirtualHost>",
                Config(web) + "<IfModule foo>\nDocumentRoot x\n</IfModule>",
                Config(web) + "DocumentRoot \"" + web + "\"\n", Config(web).Replace("127.0.0.1:8080 http", "203.0.113.1:8080"),
                Config(web).Replace("8080 http", "8443 https"), Config(web) + "SSLEngine on\n",
                Config(web).Replace(web.Replace('\\', '/'), "${UNKNOWN}/www"), Config(web).Replace(web.Replace('\\', '/'), "../escape"),
                Config(web).Replace(web.Replace('\\', '/'), Path.Combine(root, "missing").Replace('\\', '/'))
            })
            {
                File.WriteAllText(conf, config);
                var result = await ApachePhpCurlHttpProbe.RunAsync(root, Options((_, _) => throw new InvalidOperationException("Unsafe config made request")));
                Assert(result.Status == ProbeStatus.Skip, "Unsafe config accepted"); NoFiles(web);
            }
            File.WriteAllText(conf, Config(web));
            var mismatch = await ApachePhpCurlHttpProbe.RunAsync(root, new ProbeOptions
            {
                ExpectedDocumentRoot = Path.Combine(root, "other-service-root"),
                HandlerFactory = () => throw new Exception("Mismatched service root made request")
            });
            Assert(mismatch.ReasonCode == "RUNTIME_ROOT_MISMATCH", "Service config not cross-checked"); NoFiles(web);
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(web);
                try
                {
                    File.SetUnixFileMode(web, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                    var denied = await ApachePhpCurlHttpProbe.RunAsync(root, Reply(HttpStatusCode.OK, token + "|OK"));
                    Assert(denied.Status == ProbeStatus.Skip && denied.Phase == ProbePhase.FilePreparation, "Read-only root not skipped");
                }
                finally { File.SetUnixFileMode(web, mode); }
                NoFiles(web);
            }
            File.WriteAllText(conf, Config(web));
            var htaccess = Path.Combine(web, ".htaccess");
            File.WriteAllText(htaccess, "RewriteRule ^ proxy [P]");
            var overridden = await ApachePhpCurlHttpProbe.RunAsync(root, Options((_, _) => throw new Exception("htaccess was probed")));
            Assert(overridden.Status == ProbeStatus.Skip, "htaccess not skipped"); File.Delete(htaccess);
            foreach (var behavior in new[] { "execute", "redirect", "source" })
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                File.WriteAllText(conf, Config(web).Replace("8080", port.ToString()));
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var serving = Task.Run(async () =>
                {
                    using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
                    using var stream = socket.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var first = await reader.ReadLineAsync(deadline.Token);
                    string? line;
                    var hostSeen = false;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(deadline.Token)))
                        if (line.Equals("Host: localhost", StringComparison.OrdinalIgnoreCase)) hostSeen = true;
                    Assert(hostSeen && first!.StartsWith("GET /.xampp-updater-curl-"), "Unexpected loopback HTTP request");
                    var fileName = first!.Split(' ')[1].TrimStart('/');
                    var source = File.ReadAllText(Path.Combine(web, fileName));
                    var marker = fileName[".xampp-updater-curl-".Length..^4];
                    var body = behavior == "source" ? source : marker + "|OK";
                    var payload = Encoding.UTF8.GetBytes(body);
                    var header = behavior == "redirect"
                        ? $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{port}/redirect\r\n"
                        : "HTTP/1.1 200 OK\r\n";
                    header += $"Content-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header), deadline.Token);
                    await stream.WriteAsync(payload, deadline.Token);
                });
                var live = await TryRunAsync(root, deadline.Token);
                await serving;
                Assert(live.Status == (behavior == "execute" ? ProbeStatus.Pass : behavior == "source" ? ProbeStatus.Fail : ProbeStatus.Skip), "Loopback transport classification failed");
                Assert(behavior != "redirect" || live.ReasonCode == "HTTP_UNVERIFIED", "Redirect followed");
                NoFiles(web);
            }
            Assert(FindDocumentRoot(Config(web), apache) == web, "DocumentRoot helper failed on full configuration");
            var relative = ApachePhpCurlHttpProbe.FindDocumentRoot("ServerRoot \"" + apache + "\"\nDocumentRoot \"web root\"", apache);
            Assert(relative == Path.Combine(apache, "web root"), "ServerRoot relative path failed");
            var link = Path.Combine(root, "linked-web");
            if (OperatingSystem.IsWindows())
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    ArgumentList = { "/c", "mklink", "/J", link, web }
                })!;
                process.WaitForExit();
                Assert(process.ExitCode == 0, "Could not create test junction");
            }
            else Directory.CreateSymbolicLink(link, web);
            File.WriteAllText(conf, Config(link));
            var linked = await ApachePhpCurlHttpProbe.RunAsync(root, Options((_, _) => throw new Exception("Reparse point accepted")));
            Assert(linked.ReasonCode == "WEB_ROOT_UNSAFE", "Reparse point not rejected");
            Directory.Delete(link);
            Console.WriteLine($"HTTP probe fixtures: {checks} assertions passed (fixture responses and real loopback HTTP transport; no real Apache/PHP).");
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
}
