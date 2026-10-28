# Apache/PHP HTTP probe expansion

## Phase A implementation (candidate for v1.1.4)

Base: `main` / v1.1.3, commit `2c4390b951c053eadabebab9fefb1f62fd490082`.
Branch: `phase-a-custom-document-root`. No version bump or release until Windows CI passes.

The shared final integration gate used by verified Apache/PHP updates and integration-checked rollbacks still owns the rollback decision. HTTP PASS is a step; SKIP is a warning; confirmed PHP-source exposure or executed PHP cURL failure is FAIL and uses the existing rollback contract. DLL synchronization, SHA256 checks, dependency ownership protection, backups, migration and MariaDB/phpMyAdmin logic are unchanged.

### Implemented

- Read the literal single main-server DocumentRoot, including existing roots outside XAMPP, spaces and non-ASCII names. Resolve relative paths against a literal ServerRoot (or the known Apache root). Normalize full paths and trailing separators; compare runtime paths case-insensitively on Windows.
- Reject unresolved variables, parent traversal, UNC/device paths, missing directories and junctions/symlinks in the web-root ancestry. Never alter ACLs. Conservatively skip `.htaccess`, proxy and rewrite configurations.
- Select one literal HTTP listener bound to wildcard/loopback, including IPv6 loopback. Never resolve a remote hostname or connect to a non-loopback address. Reject ambiguous listeners and TLS declarations.
- Before the production integration gate writes a file, run `httpd.exe -n <detected service> -t -D DUMP_RUN_CFG` and compare its `Main DocumentRoot` with the static candidate. Unavailable/mismatched runtime evidence produces SKIP, not a guessed root. Different Apache output formats remain SKIP.
- Generate a random 128-bit endpoint/token, use CreateNew, and preserve colliding user files. Execute only extension/function checks and curl_init/curl_close. No external cURL request, configuration edit, service action, credential collection or environment dump.
- Disable HTTP proxies, cookies, credentials and redirects. Limit response reading to 16 KiB and cover headers/body with the same 5-second deadline. Support caller cancellation.
- Compare the complete token response; HTTP 200 alone cannot pass. PHP source containing the unpredictable token is FAIL. Unknown HTML/errors, 401/403/404/500, redirects and unreachable listeners remain SKIP unless executed PHP reports a definite cURL failure.
- Delete only files created by this probe on success, failure, exception and cancellation. Retry cleanup once; record an exact residual path and withhold PASS if deletion still fails. Preserve an already-observed PHP FAIL if cleanup also fails. Cancellation plus cleanup failure throws an IOException containing the path.
- Add structured status, phase, reason, host/port, protocol, DocumentRoot and timing while retaining `Result(Executed, Success, Detail)` and the existing public probe entry point.

### Deliberate Phase A limits

This parser is not an Apache interpreter. Include/IncludeOptional, Define, IfModule/IfDefine/If/Else, VirtualHost, HTTPS, multiple listeners and unknown dynamic routing are SKIP. This includes typical XAMPP configurations with active Includes; Phase B is required before those configurations gain HTTP coverage. Other passive validation gates remain available. This narrower conservative policy prevents a nested or inactive DocumentRoot from becoming a writable probe target.

PHP-FPM/FastCGI and arbitrary Alias/rewrite/auth/proxy arrangements are not newly supported. No existing production-site file is changed. Reparse checks do not provide atomic protection against an administrator concurrently replacing directories after the final check; the selected machine and configuration must be trusted. A pre-update HTTP baseline is not implemented.

### Validation on Linux, 2026-10-28

SDK 10.0.403; .NET 10.0.14; Release cross-build of Core, WPF App and SmokeTests: **passed, 0 warnings / 0 errors**.

```bash
export DOTNET_ROOT=/workspace/.tools/dotnet
export DOTNET_CLI_HOME=/workspace/.tools/dotnet-home
export NUGET_PACKAGES=/workspace/.tools/nuget
export PATH="$DOTNET_ROOT:$PATH"
dotnet restore XamppUpdater.sln -p:EnableWindowsTargeting=true -p:PublishAot=false
dotnet build XamppUpdater.sln -c Release --no-restore -p:EnableWindowsTargeting=true -p:PublishAot=false -m:1 -nr:false
dotnet run --project tests/XamppUpdater.Core.SmokeTests/XamppUpdater.Core.SmokeTests.csproj -c Release --no-build -- --http-probe-only
```

Targeted run: **127 HTTP probe assertions passed**, followed by existing Apache cURL DLL sync and dependency-audit fixtures. Existing module-initialized fixtures also executed (backup/checksum, PHP migration/staging/owned paths, rollback SAPI, package/catalog, preflight and config diff). Windows-only branches of those fixtures were not exercised on Linux.

The HTTP cases use controlled responses plus a real TcpListener/production HttpClient loopback transport. They cover base/custom/spaced/Korean/external root paths, runtime root mismatch, no configuration edits, file collision, read-only directory, reparse points, cancellation, timeout, cleanup failure, response limits, source exposure, cURL failure, wrong-site responses, redirects/access errors and unsupported configuration. These tests do **not** execute Apache or PHP.

The full original smoke runner was tried during onboarding: **blocked by Windows Registry PlatformNotSupportedException on Linux**. That is a diagnosed platform limitation, not a passing suite. The new targeted argument preserves the full Windows runner's assertions; the default Windows CI run also executes the new fixtures.

Not verified: Windows build/smoke/publish CI, WPF, Windows service `-n`/DUMP_RUN_CFG behavior, native bootstrap, real Windows Apache/PHP HTTP execution, actual PHP 8.2→8.2 or 8.2→8.3 upgrades, service restart/rollback, JCMS24, release EXE/checksum. Do not describe this candidate as released or production-validated.

## Phase B dependency and design

Separate branch/PR after the Phase A review and release gate. Introduce read-only runtime discovery; reuse Phase A path normalization and the probe/result contract rather than another probe implementation.

1. Resolve the selected service's effective -f/-d/define arguments and compare httpd -t, -S, DUMP_RUN_CFG and (when supported) DUMP_INCLUDES evidence.
2. Follow only active Include/IncludeOptional files in Apache order; bound recursion/file counts, detect cycles/duplicate loads and expand wildcard paths deterministically. Conditional module/define blocks need runtime evidence; never treat every conf file as active.
3. Keep each VirtualHost's address, port, ServerName/aliases, DocumentRoot, PHP handler, SSL state and source locations together. Avoid inheriting fields across unrelated sites.
4. Select only an unambiguous safe representative site. Ambiguous sites remain SKIP (or require explicit future user selection). Report the precise site and scope, never claim all VirtualHosts passed.
5. Add fixture matrices plus real Windows Apache routing tests before release claims.

## Phase C dependency and design

Separate PR based on Phase B. Keep TLS verification enabled. Use an HttpClient SocketsHttpHandler ConnectCallback to pin the TCP connection to a confirmed loopback listener while preserving the URI hostname for HTTP Host, SNI and certificate validation. No DNS-based remote connection, proxy, redirect or certificate validation override. Treat untrusted/self-signed/name-mismatched certificates as explicit SKIP.

Test HTTP 80/8080, HTTPS 443/8443, IPv4/IPv6, mixed listeners, named SNI, trusted and untrusted certificates, timeouts, redirects and wrong-host routing. Real Windows Apache/PHP integration and successful Windows CI are prerequisites for the combined v1.2.0 release.

## Phase D backlog only

Handle reproducible cases for Rewrite/.htaccess, Alias, Basic/IP access control, proxying and PHP-FPM/FastCGI as separate work. Do not disable security or gather credentials to force a probe to pass. Keep configuration recognition separate from proof of execution.

## External gates and continuation

GitHub Git reads work through platform authentication. GitHub API access initially returned Forbidden; `api.github.com` has been added to the environment draft's custom allowlist, preserving package-manager presets. The draft must be saved/applied in environment settings before PR/CI/API operations can be retried. API authentication/write permission must then be verified separately; Git access alone does not prove it.

A reviewable PR body is in `docs/HTTP_PROBE_PHASE_A_PR.md`. After access is available: open Phase A PR, examine actual Windows CI, fix failures, verify real Apache/PHP cases, merge only with those gates met, then bump to 1.1.4 following Directory.Build.props and the release/v* workflow. Check the published EXE and SHA256. Do not create release branches to bypass blocked checks. Phase B/C implementation and release must not be marked complete from this Phase A candidate.
