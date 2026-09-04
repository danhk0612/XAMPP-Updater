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
