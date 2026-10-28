# XAMPP Updater v1.1.4

Phase A expands the optional Apache HTTP PHP cURL probe to a single unambiguous, literal DocumentRoot. Supported roots can be outside XAMPP and contain spaces or non-ASCII characters. The final integration gate compares the candidate with the selected Apache service's runtime DocumentRoot before creating an endpoint.

The probe now exposes structured PASS/FAIL/SKIP reasons, detects PHP source disclosure, bounds response size and execution time, preserves colliding files and reports cleanup failures with the residual path. HTTP requests remain loopback-only, without proxies or redirect following. The PHP endpoint checks only cURL initialization and cleanup, not external HTTPS transfers.

DLL synchronization/audit, SHA256 guards, backup/rollback and the other component update flows are unchanged.

## Support limits and validation

Include/IncludeOptional, variables/conditional settings, VirtualHost, HTTPS, multiple listeners, rewrite/proxy arrangements, `.htaccess` and reparse-point web roots conservatively SKIP. Typical XAMPP configurations with Includes therefore need Phase B before gaining HTTP probe coverage. Other existing integration checks continue to run.

The implementation passed Linux cross-build and 128 HTTP assertions, existing DLL regression fixtures, and full Windows CI including smoke tests, NativeAOT bootstrap publishing and executable self-test. HTTP tests use fixture responses and real loopback transport; they do not run Apache or PHP. Real Windows Apache/PHP updates, service configuration behavior and JCMS24 operation have not been validated by this release work.

Requires Windows 11 x64 and Microsoft .NET 10 Desktop Runtime x64. Distribution: `XAMPP-Updater.exe`, accompanied by `XAMPP-Updater.exe.sha256`.
