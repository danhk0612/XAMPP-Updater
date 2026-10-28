using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace XamppUpdater.Core.Services;

public enum DllAuditSeverity { Info, Warning, Block }

public sealed record DllAuditFinding(
    DllAuditSeverity Severity,
    string Code,
    string Name,
    string Detail);

public sealed record DllDependencyAuditReport(
    int PhpRootCount,
    int ApacheRootCount,
    IReadOnlyList<DllAuditFinding> Findings,
    IReadOnlySet<string> ApacheOwnedDependencies)
{
    public IReadOnlyList<string> Summarize(string phase, int maxDetails = 24)
    {
        var messages = new List<string>
        {
            $"DLL 의존성 진단 [{phase}]: 활성 PHP 모듈 {PhpRootCount}, Apache 모듈 {ApacheRootCount}, " +
            $"주의 {Findings.Count(item => item.Severity == DllAuditSeverity.Warning)}, " +
            $"DLL 이름 충돌 {Findings.Count(item => item.Code.Contains("COLLISION", StringComparison.Ordinal))}"
        };
        messages.AddRange(Findings.Take(maxDetails)
            .Select(item => $"DLL [{item.Severity}/{item.Code}] {item.Name}: {item.Detail}"));
        if (Findings.Count > maxDetails)
            messages.Add($"DLL 진단 추가 항목 {Findings.Count - maxDetails}개 생략");
        return messages;
    }

    public void EnsureSafeCurlCopy(IReadOnlyList<ApachePhpDllSyncService.Change> changes)
    {
        var unsafeFiles = changes
            .Where(change => ApacheOwnedDependencies.Contains(change.Name))
            .Select(change => change.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unsafeFiles.Length > 0)
            throw new InvalidOperationException(
                "PHP cURL DLL 자동 동기화 차단: Apache 실행 파일이나 활성 Apache 모듈도 해당 DLL에 의존합니다: " +
                string.Join(", ", unsafeFiles) +
                ". Apache 모듈 호환성을 별도로 확인해야 합니다. DLL을 무조건 덮어쓰지 않습니다.");
    }
}

/// <summary>
/// Read-only, conservative PE import audit for active PHP extensions and Apache modules.
/// Unlike the cURL repairer, this audits all configured modules and never copies dependencies.
/// Delay-loaded DLLs, runtime LoadLibrary calls, forwarded exports and exact Windows loader
/// search order cannot be proven statically and must not be treated as validated.
/// </summary>
public static class DllDependencyAuditService
{
    private static readonly Regex PhpExtensionDirective = new(
        @"(?im)^\s*(?:extension|zend_extension)\s*=\s*(?<value>[^;\r\n#]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ApacheLoadModule = new(
        @"(?im)^\s*LoadModule\s+(?<name>\S+)\s+(?<path>""[^""]+""|'[^']+'|\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> CommonWindowsDlls = new(StringComparer.OrdinalIgnoreCase)
    {
        "KERNEL32.dll", "KERNELBASE.dll", "USER32.dll", "ADVAPI32.dll", "WS2_32.dll",
        "MSVCRT.dll", "SHELL32.dll", "OLE32.dll", "OLEAUT32.dll", "CRYPT32.dll",
        "GDI32.dll", "NTDLL.dll", "RPCRT4.dll", "COMDLG32.dll", "COMCTL32.dll",
        "SHLWAPI.dll", "NORMALIZ.dll", "BCRYPT.dll", "SECUR32.dll", "IPHLPAPI.dll",
        "DNSAPI.dll", "WINHTTP.dll", "WININET.dll", "VERSION.dll", "WLDAP32.dll",
        "PSAPI.dll", "DBGHELP.dll", "VCRUNTIME140.dll", "VCRUNTIME140_1.dll",
        "MSVCP140.dll", "UCRTBASE.dll"
    };

    public static DllDependencyAuditReport Analyze(
        string phpRoot,
        string apacheRoot,
        Func<string, IReadOnlyList<string>>? readImports = null)
    {
        readImports ??= PeDependencyInspector.ReadImports;
        var findings = new List<DllAuditFinding>();
        var phpRoots = GetPhpRoots(phpRoot, findings);
        var apacheRoots = GetApacheRoots(phpRoot, apacheRoot, findings);
        var apacheBin = Path.Combine(apacheRoot, "bin");
        var phpExt = Path.Combine(phpRoot, "ext");
        var windowsSystem = Environment.SystemDirectory;
        var phpSearch = new[] { phpRoot, phpExt, apacheBin, windowsSystem };
        var apacheSearch = new[] { apacheBin, Path.Combine(apacheRoot, "modules"), phpRoot, phpExt, windowsSystem };

        var phpDeps = Walk(phpRoots, phpSearch, "PHP", readImports, findings);
        var apacheDeps = Walk(apacheRoots, apacheSearch, "Apache", readImports, findings);
        var usedNames = phpDeps.Keys.Concat(apacheDeps.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in usedNames.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            var phpFile = Path.Combine(phpRoot, name);
            var apacheFile = Path.Combine(apacheBin, name);
            if (!File.Exists(phpFile) || !File.Exists(apacheFile)) continue;
            if (HashesMatch(phpFile, apacheFile)) continue;

            var apacheOwner = apacheDeps.ContainsKey(name);
            var phpConsumer = phpDeps.ContainsKey(name);
            var protectedLibrary = IsProtectedApacheRuntime(name);
            // A hash collision does not by itself establish an ABI failure: Apache may
            // legitimately retain its own zlib/OpenSSL copy. Only block a specific
            // requested overwrite of a DLL already used by an Apache module.
            findings.Add(new DllAuditFinding(
                DllAuditSeverity.Warning,
                protectedLibrary && apacheOwner ? "APACHE_RUNTIME_COLLISION" :
                    apacheOwner && phpConsumer ? "SHARED_COLLISION" : "SAME_NAME_DIFFERENT_BINARY",
                name,
                $"PHP/Apache DLL SHA256 불일치; PHP 참조={phpConsumer}, Apache 참조={apacheOwner}. " +
                "실제 로딩 경로와 ABI를 확인해야 하며 자동으로 Apache 소유 DLL을 교체하지 않습니다."));
        }

        return new DllDependencyAuditReport(phpRoots.Count, apacheRoots.Count,
            findings.DistinctBy(item => $"{item.Code}|{item.Name}|{item.Detail}", StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(item => item.Severity).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            apacheDeps.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> Walk(
        IReadOnlyList<string> roots,
        IReadOnlyList<string> search,
        string owner,
        Func<string, IReadOnlyList<string>> imports,
        ICollection<DllAuditFinding> findings)
    {
        var deps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string File, int Depth)>(roots.Select(path => (path, 0)));
        while (queue.Count > 0 && visited.Count < 1024)
        {
            var (path, depth) = queue.Dequeue();
            if (depth > 10 || !File.Exists(path) || !visited.Add(Path.GetFullPath(path))) continue;
            IReadOnlyList<string> names;
            try { names = imports(path); }
            catch (Exception ex)
            {
                findings.Add(new DllAuditFinding(DllAuditSeverity.Warning, "PE_READ_FAILED",
                    Path.GetFileName(path), $"{owner} import 분석 실패: {ex.Message}"));
                continue;
            }

            foreach (var name in names)
            {
                if (!IsValidImport(name) || IsWindowsLibrary(name)) continue;
                deps.TryAdd(name, Path.GetFileName(path));
                var resolved = Resolve(name, Path.GetDirectoryName(path), search);
                if (resolved is null)
                {
                    findings.Add(new DllAuditFinding(DllAuditSeverity.Warning, "DEPENDENCY_NOT_RESOLVED",
                        name, $"{owner} {Path.GetFileName(path)}에서 참조. 지정된 모듈 경로에 없으며 PATH/동적 로딩 여부 확인 필요"));
                    continue;
                }
                // Do not recurse into system DLLs; do traverse PHP and Apache runtime dependencies.
                if (!IsWindowsSystemPath(resolved)) queue.Enqueue((resolved, depth + 1));
            }
        }
        if (visited.Count >= 1024)
            findings.Add(new DllAuditFinding(DllAuditSeverity.Warning, "AUDIT_LIMIT", owner,
                "DLL 의존성 탐색 1024파일 제한 도달; 일부 분석이 생략되었습니다."));
        return deps;
    }

    private static IReadOnlyList<string> GetPhpRoots(string phpRoot, ICollection<DllAuditFinding> findings)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in new[] { "php.exe", "php8apache2_4.dll", "php7apache2_4.dll" })
        {
            var path = Path.Combine(phpRoot, file);
            if (File.Exists(path)) roots.Add(path);
        }
        var ini = Path.Combine(phpRoot, "php.ini");
        if (!File.Exists(ini))
        {
            findings.Add(new DllAuditFinding(DllAuditSeverity.Warning, "INI_ABSENT", "php.ini",
                "설정 파일이 없어 활성 PHP 확장 검색을 생략했습니다."));
            return roots.ToArray();
        }
        foreach (Match match in PhpExtensionDirective.Matches(File.ReadAllText(ini)))
        {
            var value = match.Groups["value"].Value.Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(value)) continue;
            var name = Path.GetFileName(value.Replace('\\', '/'));
            if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                if (name.StartsWith("php_", StringComparison.OrdinalIgnoreCase)) name += ".dll";
                else name = "php_" + name + ".dll";
            }
            var path = Path.IsPathFullyQualified(value)
                ? value : Path.Combine(phpRoot, "ext", name);
            if (File.Exists(path)) roots.Add(Path.GetFullPath(path));
            else
                findings.Add(new DllAuditFinding(DllAuditSeverity.Warning, "EXTENSION_NOT_FOUND", name,
                    "php.ini에 활성화되어 있지만 분석 가능한 경로에서 파일을 찾지 못했습니다."));
        }
        return roots.ToArray();
    }

    private static IReadOnlyList<string> GetApacheRoots(
        string phpRoot, string apacheRoot, ICollection<DllAuditFinding> findings)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var httpd = Path.Combine(apacheRoot, "bin", "httpd.exe");
        if (File.Exists(httpd)) roots.Add(httpd);
        var conf = Path.Combine(apacheRoot, "conf");
        if (!Directory.Exists(conf)) return roots.ToArray();
        foreach (var file in Directory.EnumerateFiles(conf, "*.conf", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(conf, file).Replace('\\', '/');
            if (relative.StartsWith("original/", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (Match match in ApacheLoadModule.Matches(File.ReadAllText(file)))
            {
                var name = match.Groups["name"].Value;
                // mod_php is owned by the PHP distribution, not by Apache's runtime.
                if (name.StartsWith("php", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith("_module", StringComparison.OrdinalIgnoreCase))
                    continue;
                var configured = match.Groups["path"].Value.Trim('"', '\'').Replace('/', Path.DirectorySeparatorChar);
                if (configured.Contains('$')) continue;
                var path = Path.IsPathFullyQualified(configured)
                    ? configured : Path.Combine(apacheRoot, configured);
                if (File.Exists(path)) roots.Add(Path.GetFullPath(path));
                else
                    findings.Add(new DllAuditFinding(DllAuditSeverity.Warning, "APACHE_MODULE_NOT_FOUND",
                        name, "Apache LoadModule 경로가 없거나 현재 설정에서 해석되지 않음: " + configured));
            }
        }
        return roots.ToArray();
    }

    private static bool IsProtectedApacheRuntime(string name) =>
        name.StartsWith("libssl", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("libcrypto", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("libapr", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("libhttpd.dll", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("php", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidImport(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
        name == Path.GetFileName(name) &&
        !name.Contains('/') && !name.Contains('\\') && !name.Contains(':');

    private static bool IsWindowsLibrary(string name) =>
        CommonWindowsDlls.Contains(name) ||
        name.StartsWith("api-ms-win-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("ext-ms-win-", StringComparison.OrdinalIgnoreCase);

    private static string? Resolve(string name, string? currentDirectory, IReadOnlyList<string> directories)
    {
        if (currentDirectory is not null)
        {
            var candidate = Path.Combine(currentDirectory, name);
            if (File.Exists(candidate)) return candidate;
        }
        foreach (var root in directories)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var candidate = Path.Combine(root, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static bool HashesMatch(string left, string right)
    {
        using var a = File.OpenRead(left);
        using var b = File.OpenRead(right);
        return SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b));
    }

    private static bool IsWindowsSystemPath(string path)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return !string.IsNullOrWhiteSpace(windows) &&
            Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(windows)) +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
