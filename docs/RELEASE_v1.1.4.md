# XAMPP Updater v1.1.4

Phase A expands the optional Apache HTTP PHP cURL probe to a single unambiguous, literal DocumentRoot. Supported roots can be outside XAMPP and contain spaces or non-ASCII characters. The final integration gate compares the candidate with the selected Apache service's runtime DocumentRoot before creating an endpoint.

The probe now exposes structured PASS/FAIL/SKIP reasons, detects PHP source disclosure, bounds response size and execution time, preserves colliding files and reports cleanup failures with the residual path. HTTP requests remain loopback-only, without proxies or redirect following. The PHP endpoint checks only cURL initialization and cleanup, not external HTTPS transfers.

DLL synchronization/audit, SHA256 guards, backup/rollback and the other component update flows are unchanged.

## Support limits and validation

Include/IncludeOptional, variables/conditional settings, VirtualHost, HTTPS, multiple listeners, rewrite/proxy arrangements, `.htaccess` and reparse-point web roots conservatively SKIP. Typical XAMPP configurations with Includes therefore need Phase B before gaining HTTP probe coverage. Other existing integration checks continue to run.

The implementation passed Linux cross-build and 128 HTTP assertions, existing DLL regression fixtures, and full Windows CI including smoke tests, NativeAOT bootstrap publishing and executable self-test. HTTP tests use fixture responses and real loopback transport; they do not run Apache or PHP. Real Windows Apache/PHP updates, service configuration behavior and JCMS24 operation have not been validated by this release work.

Requires Windows 11 x64 and Microsoft .NET 10 Desktop Runtime x64. Distribution: `XAMPP-Updater.exe`, accompanied by `XAMPP-Updater.exe.sha256`.

## Published assets and verification

[GitHub Release](https://github.com/danhk0612/XAMPP-Updater/releases/tag/v1.1.4) · [Windows CI](https://github.com/danhk0612/XAMPP-Updater/actions/runs/40757438368)

- Source/tag: `9c370d0c2e18160f2ea402bbfc841019c2ae73a5`.
- [XAMPP-Updater.exe](https://github.com/danhk0612/XAMPP-Updater/releases/download/v1.1.4/XAMPP-Updater.exe), 3,501,056 bytes.
- [XAMPP-Updater.exe.sha256](https://github.com/danhk0612/XAMPP-Updater/releases/download/v1.1.4/XAMPP-Updater.exe.sha256).
- SHA256: `b056ac25bb4ac6ec1b436c31559aabb45fdd1a61a9f3038f536b2c447feacfc6`.

The release workflow passed all Windows build, smoke, bootstrap and executable verification gates, then published both assets. The published files were independently downloaded and their SHA256 values matched. The user explicitly approved direct main integration and release workflow deployment after GitHub API access prevented PR creation. Real Apache/PHP and JCMS24 runtime validation remains outstanding.
