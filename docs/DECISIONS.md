# Decisions

1.0 기준으로 확정된 프로젝트 범위와 기술 결정을 기록한다.

## D-001 관리 대상

기본 관리 대상은 선택한 XAMPP 루트의 Apache, PHP, MariaDB다. `phpMyAdmin` 폴더가 실제 존재하는 경우 phpMyAdmin을 선택적으로 추가 관리한다.

XAMPP 전체 업그레이드 도구로 확장하지 않으며 Node.js, Perl, Tomcat 등은 범위 밖이다.

## D-002 Windows 11 / .NET 8 WPF

주 지원 OS는 Windows 11이며 GUI는 .NET 8 WPF로 구현한다. 배포는 win-x64 self-contained single-file EXE를 기본으로 한다.

## D-003 설치 경로는 자동 감지와 직접 지정 모두 지원

자동 감지는 다음 신호를 조합한다.

1. 일반적인 `C:\xampp`
2. 고정 드라이브 루트의 `\xampp`
3. XAMPP 제거 정보의 `InstallLocation`
4. Apache/MariaDB Windows 서비스의 `ImagePath`

전체 드라이브 재귀 검색은 기본 동작으로 사용하지 않는다.

## D-004 서비스명 고정 금지

`Apache2.4`, `mysql` 같은 기본 서비스명을 전제로 하지 않는다. 실제 Windows 서비스의 `ImagePath`가 선택한 XAMPP 설치의 실행 파일과 연결되는지 확인한다.

## D-005 MariaDB 실행 파일 이름

XAMPP의 `mysql` 디렉터리에서 `mysqld.exe`와 `mariadbd.exe`를 모두 감지한다. 경로만으로 제품을 확정하지 않고 실행 파일 버전 정보도 사용한다.

## D-006 읽기/변경 계층 분리

설치 감지, 온라인 버전 조회, 호환성 프로파일링은 가능한 한 읽기 전용으로 수행한다. 서비스 제어와 파일 교체는 실제 업데이트/롤백 단계에서만 수행한다.

## D-007 최신 버전과 적용 가능 버전을 분리

`upstream latest`, `XAMPP official baseline`, 실제 Windows 후보 패키지와 사용자가 선택할 수 있는 계열별 버전을 서로 구분한다. 최신 버전이라는 이유만으로 바로 교체하지 않는다.

## D-008 현재 설치 환경을 먼저 프로파일링

후보 패키지 적용 전 다음을 실제 설치에서 확인한다.

- Apache/PHP/MariaDB PE 아키텍처
- PHP Thread Safety, compiler, PHP Extension Build/API
- Apache의 `LoadModule php*_module ...php*apache2_4.dll` 연동
- MariaDB 현재 release series

파일명이나 XAMPP 기본값만으로 호환성을 추정하지 않는다.

## D-009 Apache Windows 바이너리

Apache Software Foundation은 Windows용 httpd 바이너리를 직접 제공하지 않으므로 실제 Windows 패키지는 별도 공급원과 빌드 조건을 검증해야 한다. ASF 버전 정보와 실제 Windows 패키지의 compiler/module ABI/VC++ 조건을 구분한다.

## D-010 PHP의 일반 XAMPP 후보

Apache module 방식 XAMPP에서는 x64 Thread Safe PHP를 기본적인 후보로 보되, 실제 현재 설치의 Apache 연동 방식과 DLL 정보를 최종 판단 기준으로 사용한다.

## D-011 MariaDB major 업그레이드

MariaDB major 변경을 단순 파일 덮어쓰기로 처리하지 않는다. 대신 전체 논리+물리 백업, 서비스 중지, 대상 바이너리 적용, data 사본, `mariadb-upgrade`/`mysql_upgrade`, 서버 상태 검증을 포함한 검증된 경로를 사용한다.

직접 major 업그레이드는 사전 검증과 자동 원복 조건을 충족하면 허용한다. 실제로 10.4.34 → 10.6.28 → 12.3.3 경로를 검증했다.

## D-012 위험한 변경은 Assisted workflow

PHP major 변경이나 MariaDB release-series 변경처럼 단순 교체가 위험한 경우에도 무조건 기능을 막지 않는다.

- 백업
- 설정/파일/ABI 비교
- 자동 변환 가능한 항목 처리
- 필요한 경우 사용자 확인
- 실행 검증
- 실패 시 자동 원복

순서의 보조 업데이트 흐름을 제공한다.

## D-013 후보 상태는 자동화 수준

후보 상태는 다음 의미로 사용한다.

- `Automatic`: 사용자 개입 없이 필요한 검증과 적용 가능
- `Assisted`: 일부 확인이 필요하지만 앱이 전체 흐름을 수행
- `ManualReview`: 적용 전 사용자의 명시적 검토 필요
- `Unavailable`: 현재 조건에서 사용할 후보를 확보하지 못함

단순히 버전이 오래됐거나 major가 다르다는 이유만으로 `Unavailable`로 만들지 않는다.

## D-014 목표 버전 선택

모든 패치 릴리스를 나열하지 않고 major.minor 계열별 최신 패치를 중심으로 제공한다. 최신/upstream/XAMPP 기준과 공식 archive 후보가 겹치면 하나의 버전 선택지로 합친다.

## D-015 상대 구성요소 자동 연쇄 변경 금지

한 구성요소 업데이트/롤백 때문에 다른 구성요소의 버전을 자동으로 업데이트하거나 롤백하지 않는다.

지원 버전 차이만 있는 경우에는 호환성 정보 또는 경고만 제공하며 상대 구성요소를 연쇄 변경하지 않는다.

## D-016 Apache ↔ PHP는 실제 연동 검증

Apache와 PHP는 일반적인 XAMPP에서 직접 module/SAPI로 연동되므로 예외적으로 실제 런타임 연동을 검증한다.

Apache 또는 PHP를 변경한 뒤:

- 현재 PHP에 맞는 `LoadFile`/`LoadModule`/`PHPIniDir` 보정
- `php -v`
- `php -m`
- Apache module DLL loader 확인
- `httpd -t`
- 작업 전 Apache가 실행 중이었다면 최종 서비스 `RUNNING`

을 확인한다.

실패하면 상대 구성요소는 건드리지 않고 방금 변경한 구성요소만 직전 상태로 자동 원복한다.

## D-017 전역 롤백 순서 강제 없음

Apache/PHP/MariaDB/phpMyAdmin 사이에 업데이트 역순으로만 롤백해야 한다는 전역 제한을 두지 않는다.

실제 환경에서 Apache → PHP → MariaDB → phpMyAdmin 업데이트 후 MariaDB → Apache → PHP → phpMyAdmin 순서 롤백을 검증했다.

MariaDB는 다른 구성요소 때문이 아니라 자신의 데이터/시스템 테이블 변경 때문에 자체 롤백 검증을 더 엄격하게 유지한다.

## D-018 롤백 백업과 Safety 백업 구분

업데이트 전 생성되는 정식 백업은 사용자 롤백 지점이다. 롤백 직전 자동 생성되는 현재 상태 보호용 백업은 `Safety`로 구분하고 일반 롤백 후보에 노출하지 않는다.

현재 정책은 Safety 백업을 7일, 구성요소별 최근 3개 기준으로 정리한다.

## D-019 롤백 후보는 현재 버전과 연결된 검증된 백업만

롤백 카탈로그는 다음을 확인한다.

- 구성요소와 XAMPP 루트 일치
- 현재 버전과 백업 `TargetVersion` 연결
- 정상적인 이전 버전 방향
- manifest 경로 및 파일 존재
- 크기/SHA256
- MariaDB 논리 백업 존재

실행 직전에도 다시 무결성을 확인한다. schema 1/2 기존 백업 호환은 유지한다.

## D-020 phpMyAdmin은 독립 폴더 교체 방식

phpMyAdmin은 Apache/PHP/MariaDB executor에 억지로 통합하지 않는다.

- XAMPP 루트에 폴더가 있을 때만 UI 제공
- 공식 latest metadata / ZIP / SHA256 사용
- `config.inc.php`, `.htaccess`, `upload`, `save` 보존
- 전체 폴더 rollback backup
- staging/버전/PHP syntax 검증
- 실패 시 폴더 원복

실제 업데이트/롤백/브라우저 로그인/DB 조회까지 검증했다.

## D-021 설정 이력은 프로그램 데이터로 별도 관리

업데이트 전/후 및 수동 설정 snapshot은 프로그램 데이터 영역에 저장한다. 실제 XAMPP 설정과 별개로 SHA256/크기 manifest, 비교, 무결성 검사, 전체/파일/안전한 항목 단위 복원을 제공한다.

복원 직전에는 Safety snapshot을 생성하고 검증 실패 시 자동 원복한다.

## D-022 다국어 정책

UI는 System / Korean / English를 지원한다. 사용자 선택은 `%LOCALAPPDATA%\XAMPP-Updater\settings.json`에 저장하고 언어 변경 시 자동 재시작한다.

내부 stage ID와 프로그램 내부 식별자는 영어 고정으로 유지하고 사용자에게 표시되는 UI/대화상자/상태 문구만 현지화한다.

## D-023 자체 업데이트

GitHub Releases의 최신 버전을 확인하고 `XAMPP-Updater.exe`와 SHA256 파일을 내려받아 검증한 뒤 현재 EXE를 교체한다. 검증 이후에는 취소를 제한하고 교체 실패 시 `.update-backup`을 복원한다.

## D-024 1.0 이후 하드닝

다음은 1.0 필수 범위가 아니라 선택적 후속 하드닝으로 유지한다.

- Apache module / PHP extension ABI 정적 메타데이터 강화
- 공급처 PGP/GPG 서명 검증
- 공식 manifest/API 기반 공급처 메타데이터 신뢰도 강화

자세한 착수 조건은 `docs/DEFERRED_HARDENING.md`에 둔다.
