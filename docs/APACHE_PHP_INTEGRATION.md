# Apache/PHP integration policy

Apache와 PHP는 서로의 버전을 자동으로 변경하지 않는다. 사용자가 선택한 구성요소만 업데이트/롤백하고, 작업 직후 현재 Apache/PHP 조합의 실제 연동 상태를 검증한다.

## 공통 검증

Apache 업데이트, PHP 업데이트, Apache 롤백, PHP 롤백 모두 동일한 `ApachePhpIntegrationValidator`를 최종 게이트로 사용한다.

검증 항목:

1. 현재 PHP `php -v`
2. 현재 PHP `php -m` 및 startup extension load 오류
3. Apache 설정의 활성 `LoadModule php*_module ...php*apache2_4.dll` 탐지
4. Windows `LoadLibraryEx` 기반 PHP Apache module DLL 로더 검증
5. Apache `httpd -t`
6. Apache가 작업 전에 실행 중이었던 경우 서비스 상태 `RUNNING` 확인

Apache가 원래 중지 상태였다면 검증을 위해 임의로 서비스를 시작하지 않는다.

## 실패 처리

- 상대 구성요소를 자동 업데이트하거나 자동 롤백하지 않는다.
- Apache 작업 후 연동 검증 실패 시 방금 변경한 Apache만 기존 백업으로 원복한다.
- PHP 작업 후 연동 검증 실패 시 방금 변경한 PHP와 해당 작업에서 변경된 Apache PHP SAPI 설정만 원복한다.
- 롤백 작업도 최종 연동 검증이 성공하기 전까지 롤백 직전 구성요소 폴더를 안전 위치에 보관한다.
- 롤백 후 연동 검증 실패 시 롤백 직전 상태로 자동 복귀한다.

## 범위 밖

단순한 공급자 지원 버전 차이를 이유로 상대 구성요소의 버전을 자동 변경하지 않는다. 자동 개입은 실제 파일/설정/런타임 연동 검증 실패에 한정한다.

## PHP cURL / Apache bin DLL collision handling

When PHP is updated, XAMPP Updater reads the PE import dependency graph reachable from `php/ext/php_curl.dll`. For DLLs supplied at the PHP root, it synchronizes only *different same-named files already present* in `apache/bin`. This handles the observed `libssh2.dll`, `nghttp2.dll`, `brotlicommon.dll`, and `brotlidec.dll` collisions without forcing a PHP-version-specific copy list. Apache-owned OpenSSL, APR, PHP SAPI, and libhttpd DLLs are excluded from automatic replacement.

Changes are applied with Apache stopped and verified with SHA256. The original Apache DLLs and their applied hashes are retained in the selected PHP rollback backup, separately from the PHP folder files. A PHP rollback restores these DLLs unless another Apache update has subsequently changed them (in which case the DLL is preserved and a warning is issued). Apache updates and Apache rollbacks re-evaluate cURL dependency collisions against the installed PHP version. Existing PHP backups without this metadata remain supported.

The integration gate probes the cURL extension through the Windows loader when it is enabled, but does not send an HTTP request or create a temporary web-accessible PHP script. Therefore live HTTP cURL behavior is not guaranteed by this passive check; a real Apache/PHP endpoint test remains useful before production deployment.

## v1.1.2 post-restart cURL guard

For every DLL reachable from PHP's `ext/php_curl.dll` dependency graph (excluding Apache-owned SSL/APR/SAPI libraries), the updater now copies missing Apache-bin files as well as mismatching files. It backs up both the original existence flag and SHA256 so rollback can remove newly introduced files or restore old ones, even if a previously synchronized file has disappeared.

The PHP update records per-DLL state and abbreviated SHA256 before starting Apache and again immediately after service restart. The final Apache/PHP integration validator independently re-checks every selected dependency; a missing or mismatching DLL fails validation and triggers the existing PHP rollback path rather than reporting success.

When Apache was running and has a verifiable standard XAMPP `htdocs` DocumentRoot and local HTTP Listen port, the final validator creates a short-lived, unpredictable PHP file and requests it over `127.0.0.1` (without using a proxy). The PHP file only checks `extension_loaded('curl')` and exercises `curl_init` / `curl_close`; it performs no outbound cURL request and is removed in `finally`. Actual web-SAPI cURL failure causes validation to fail. An unreachable or non-standard document root/virtual-host configuration is reported as an explicit skipped-check warning rather than claiming HTTP validation passed. Do not assume this test validates remote TLS/cURL transfer behavior.

## v1.1.3: full active-module DLL audit (read-only)

A new `DllDependencyAuditService` inspects the PE import graph of installed PHP executables/SAPI, PHP extensions enabled in `php.ini`, Apache `httpd.exe`, and modules referenced by non-commented `LoadModule` directives. It audits transitive imports, missing locally resolvable DLLs, and same-name binaries with differing SHA256 values in `php/` vs `apache/bin/`. The update log includes an abbreviated findings summary; non-fatal warnings are also surfaced in the result.

**Automatic repair remains limited to the existing cURL dependency closure.** Before that repair, the audit checks whether the destination DLL is also imported by the Apache executable or a configured Apache module. If yes, the updater **blocks the overwrite and rolls back rather than replacing Apache-owned binaries**. Other collisions, including Apache's OpenSSL, APR and zlib runtime differences, are warnings rather than unqualified failures: hash mismatches alone do not establish ABI breakage. The existing CLI, Windows loader, Apache configuration and conditional real HTTP tests remain the functional validation gates.

The audit is necessarily conservative: Windows DLL search order, forwarded exports, delay-load imports, runtime `LoadLibrary` calls, PATH-provided dependencies, conditional Apache directives and virtual-host-specific PHP behavior cannot all be proven statically. Missing import warnings should be investigated using actual runtime logs and tests. No mass DLL copying occurs.
