# Decisions

프로젝트 진행 중 확정된 기술/범위 결정을 기록한다.

## D-001 대상 구성요소 제한

관리 대상은 Apache, PHP, MariaDB 세 가지로 제한한다. XAMPP 전체 업그레이드 도구로 만들지 않는다.

## D-002 Windows 11 우선

초기 지원 OS는 Windows 11이다. GUI는 .NET 8 WPF로 구현한다.

## D-003 설치 경로는 자동 감지와 직접 지정 모두 지원

자동 감지는 다음 신호를 조합한다.

1. `C:\xampp`
2. 고정 드라이브 루트의 `\xampp`
3. XAMPP 제거 정보의 `InstallLocation`
4. Apache/MariaDB Windows 서비스의 `ImagePath`

전체 드라이브 재귀 검색은 Phase 1에서 하지 않는다.

## D-004 서비스명 고정 금지

`Apache2.4`, `mysql` 같은 기본 이름을 전제로 하지 않는다. Windows 서비스 레지스트리의 실제 `ImagePath`가 선택한 XAMPP 설치의 실행 파일과 일치하는지 확인해 서비스명을 찾는다.

## D-005 XAMPP의 `mysql` 디렉터리

XAMPP에서 MariaDB 서버가 `mysql\bin\mysqld.exe` 경로에 배치되는 구조를 그대로 감지한다. 단순히 경로만 보고 MariaDB라고 확정하지 않고 `mysqld.exe --version` 출력에 MariaDB 식별 문자열이 있는지도 표시한다.

## D-006 Phase 1은 읽기 전용

Phase 1에서는 설치 감지와 버전 확인만 한다. 서비스 중지/시작, 파일 수정/삭제/교체, 설정 변경은 하지 않는다.

## D-007 업데이트 전에 호환성 계층을 먼저 만든다

Apache/PHP/MariaDB의 upstream Windows 패키지는 XAMPP 번들과 구조 및 빌드 조건이 다를 수 있다. 따라서 최신 버전을 찾았다는 이유만으로 자동 교체하지 않고, Phase 2에서 공급원/아키텍처/런타임/패키지 구조 호환성을 정의한 뒤 업데이트 엔진을 구현한다.

## D-008 최신 버전과 XAMPP 적용 가능 버전을 분리

`upstream 최신 버전`과 `XAMPP 공식 번들 기준 버전`을 서로 다른 값으로 관리한다. upstream에 새 버전이 있다는 이유만으로 업데이트 가능 상태로 표시하지 않는다.

현재 메타데이터 공급원은 다음을 우선한다.

- Apache 최신 릴리스: Apache HTTP Server 공식 다운로드 페이지
- PHP 최신 Windows 릴리스: PHP 공식 Windows 다운로드 페이지
- MariaDB 최신 Community Server: MariaDB 공식 다운로드 페이지
- XAMPP 공식 번들 기준: Apache Friends 공식 Windows 다운로드 페이지

공급원 HTML 구조가 변경되어 파싱에 실패하면 해당 구성요소를 `확인 실패`로 처리하며 임의의 버전을 추정하지 않는다.

## D-009 Apache Windows 바이너리는 별도 공급원 검증 필요

Apache Software Foundation은 Windows용 httpd 바이너리를 직접 릴리스하지 않는다. 현재 공식 문서에서 Apache Lounge 등 제3자 Windows 바이너리 공급원을 안내한다.

따라서 Apache 최신 버전 메타데이터는 ASF에서 확인하되, 실제 업데이트 ZIP의 공급원/컴파일러/모듈 ABI/VC++ 런타임 호환성은 별도의 패키지 호환성 판정을 통과해야 한다.

## D-010 PHP의 XAMPP 기본 후보는 x64 Thread Safe

Apache 모듈 방식으로 PHP를 사용하는 일반적인 XAMPP 구성에서는 Windows x64 Thread Safe 빌드를 기본 후보로 본다. PHP 공식 문서도 Apache HTTP Server에서 사용하는 경우 Thread Safe 빌드를 안내한다.

단, 기존 `httpd.conf`/`httpd-xampp.conf`의 PHP 연동 방식과 확장 DLL ABI를 실제로 확인한 뒤 최종 판정한다.

## D-011 MariaDB 메이저 버전 직접 점프 금지

MariaDB는 데이터 파일과 시스템 테이블의 업그레이드 절차가 있으므로 최신 Community Server 메이저 버전을 단순 파일 교체 대상으로 취급하지 않는다.

현재 버전 → 목표 버전의 지원되는 업그레이드 경로, 백업, `mariadb-upgrade` 계열 절차를 정의하기 전에는 자동 업데이트 대상으로 표시하지 않는다.

## D-012 현재 설치 환경을 먼저 프로파일링

후보 패키지와 비교하기 전에 현재 XAMPP 설치의 실제 실행 파일과 설정을 읽어 호환성 프로파일을 만든다.

- Apache/PHP/MariaDB 실행 파일의 PE 아키텍처 확인
- `php.exe -i`에서 Thread Safety, compiler, PHP Extension Build, PHP API 확인
- `apache\conf` 아래의 실제 `LoadModule php*_module ...php*apache2_4.dll` 설정 확인
- MariaDB 현재 major.minor 계열 확인

파일 이름이나 XAMPP 기본값만으로 x64/Thread Safe/Apache module 여부를 추정하지 않는다.

## D-013 메이저 변경은 자동 업데이트 후보에서 제외

Phase 2의 1차 판정에서는 다음 변경을 자동 적용 대상으로 보지 않는다.

- PHP 메이저 버전 변경: 확장 ABI와 설정 마이그레이션 확인 필요
- MariaDB major.minor 계열 변경: 공식 업그레이드 경로 확인 필요

MariaDB가 같은 major.minor 계열이라도 실제 업데이트 전에는 데이터 백업과 업그레이드 도구 절차가 필요하다.

## D-014 Phase 2도 시스템 변경 금지

Phase 2는 온라인 메타데이터 조회와 로컬 파일/설정 읽기만 수행한다. 서비스 제어, 다운로드된 패키지 설치, 파일 교체, 설정 수정은 Phase 3/4 이전에는 실행하지 않는다.
