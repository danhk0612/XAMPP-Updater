# Apache/PHP HTTP probe expansion

## Phase A implementation (v1.1.4)

Base: `main` / v1.1.3, commit `2c4390b951c053eadabebab9fefb1f62fd490082`.
Implementation branch: `phase-a-custom-document-root`. Both implementation commits passed Windows CI; release preparation uses `release/v1.1.4`.

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

Targeted run: **128 HTTP probe assertions passed**, followed by existing Apache cURL DLL sync and dependency-audit fixtures. Existing module-initialized fixtures also executed (backup/checksum, PHP migration/staging/owned paths, rollback SAPI, package/catalog, preflight and config diff). Windows-only branches of those fixtures were not exercised on Linux.

The HTTP cases use controlled responses plus a real TcpListener/production HttpClient loopback transport. They cover base/custom/spaced/Korean/external root paths, runtime root mismatch, no configuration edits, file collision, read-only directory, reparse points, cancellation, timeout, cleanup failure, response limits, source exposure, cURL failure, wrong-site responses, redirects/access errors and unsupported configuration. These tests do **not** execute Apache or PHP.

The full original smoke runner was tried during onboarding: **blocked by Windows Registry PlatformNotSupportedException on Linux**. That is a diagnosed platform limitation, not a passing suite. The new targeted argument preserves the full Windows runner's assertions; the default Windows CI run also executes the new fixtures.

Windows CI for implementation commit `4eaf4f38f92a3af44b022e70ec4847be3ecd2500`: **passed**. [Run 40754147145](https://github.com/danhk0612/XAMPP-Updater/actions/runs/40754147145) completed restore, Release build, full smoke runner, app publish, NativeAOT bootstrap publish, executable version/layout/bootstrap self-test and win-x64 artifact upload successfully. Release tag/checksum/asset steps were correctly skipped on this feature branch. Step outcomes were read from GitHub's public Actions job page; authenticated logs/artifact download were unavailable. This evidence applies to that exact implementation commit; inspect later branch runs for follow-up changes.

Not verified: WPF GUI behavior, actual Windows service `-n`/DUMP_RUN_CFG behavior against Apache, real Windows Apache/PHP HTTP execution, actual PHP 8.2→8.2 or 8.2→8.3 upgrades, service restart/rollback, JCMS24, Do not describe CI or fixture results as production validation. The published EXE/checksum were subsequently verified as recorded below.

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

## Deployment result: v1.1.4

GitHub API access remained blocked, so no PR was created. Automatic review initially rejected direct main changes because the original instructions requested a PR/merge gate. The user then explicitly approved applying commit `9c370d0` directly to main and running release/v1.1.4 deployment. That authorization was followed; no CI verification was disabled.

- Published release: https://github.com/danhk0612/XAMPP-Updater/releases/tag/v1.1.4
- Release source/tag/branch commit: `9c370d0c2e18160f2ea402bbfc841019c2ae73a5`.
- Release CI: https://github.com/danhk0612/XAMPP-Updater/actions/runs/40757438368 — successful Windows build, full smoke, app payload, NativeAOT bootstrap, EXE version/layout/self-test, checksum generation, artifact upload and GitHub Release asset publication.
- EXE: https://github.com/danhk0612/XAMPP-Updater/releases/download/v1.1.4/XAMPP-Updater.exe
- Checksum: https://github.com/danhk0612/XAMPP-Updater/releases/download/v1.1.4/XAMPP-Updater.exe.sha256
- Published EXE size: 3,501,056 bytes.
- SHA256: `b056ac25bb4ac6ec1b436c31559aabb45fdd1a61a9f3038f536b2c447feacfc6`.

Both public assets were downloaded after publication. The computed EXE SHA256 matched the published checksum, its MZ header was verified, and Git readback confirmed main/release branch/tag all pointed at the release commit before this documentation-only follow-up. The release tag and executable remain at that exact commit. Future main commits do not change the release provenance.

This release contains Phase A only. Actual Windows Apache/PHP service integration, real update/rollback paths and JCMS24 operations remain unverified. Include/VirtualHost/HTTPS support and v1.2.0 remain follow-up work, with Phase D limited to the backlog above.
