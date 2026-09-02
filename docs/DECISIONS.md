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

현재 버전 → 목표 버전의 지원되는 업그레이드 경로, 백업, `mariadb-upgrade` 계열 절차를 정의하기 전에는 무인 자동 업데이트 대상으로 표시하지 않는다.

## D-012 현재 설치 환경을 먼저 프로파일링

후보 패키지와 비교하기 전에 현재 XAMPP 설치의 실제 실행 파일과 설정을 읽어 호환성 프로파일을 만든다.

- Apache/PHP/MariaDB 실행 파일의 PE 아키텍처 확인
- `php.exe -i`에서 Thread Safety, compiler, PHP Extension Build, PHP API 확인
- `apache\conf` 아래의 실제 `LoadModule php*_module ...php*apache2_4.dll` 설정 확인
- MariaDB 현재 major.minor 계열 확인

파일 이름이나 XAMPP 기본값만으로 x64/Thread Safe/Apache module 여부를 추정하지 않는다.

## D-013 메이저 변경은 무인 자동 적용 대신 보조 업데이트

PHP 메이저 변경이나 MariaDB major.minor 계열 변경처럼 단순 덮어쓰기가 위험한 경우에도 업데이트 기능 자체를 막지 않는다.

- 자동으로 백업한다.
- 기존/신규 설정과 파일 구조를 비교한다.
- 자동 변환 가능한 항목은 변환한다.
- 충돌/삭제/ABI 불일치 항목만 사용자에게 보여 선택을 받는다.
- 필요한 경우 사용자가 공식 패키지를 직접 지정할 수 있게 한다.
- 검증 및 사전 점검을 통과한 뒤 나머지 교체/재시작/후처리는 앱이 수행한다.

즉 `무인 자동 불가`와 `업데이트 불가`를 구분한다.

## D-014 Phase 2도 시스템 변경 금지

Phase 2는 온라인 메타데이터 조회와 로컬 파일/설정 읽기만 수행한다. 서비스 제어, 다운로드된 패키지 설치, 파일 교체, 설정 수정은 Phase 3/4 이전에는 실행하지 않는다.

## D-015 후보 상태는 자동화 수준을 나타낸다

후보 패키지 상태는 위험 여부를 이유로 단순 `차단`하지 않고 다음 네 단계로 관리한다.

- `Automatic`: 필요한 검증과 마이그레이션이 모두 자동화되어 사용자 개입 없이 실행 가능
- `Assisted`: 패키지 확인, 설정 diff, ABI 확인, 업그레이드 절차 등 일부 확인이 필요하지만 앱이 작업 흐름을 안내하고 나머지를 자동 수행
- `ManualReview`: 계열 변경 등으로 사용자가 변경 내용을 검토/승인해야 하지만 앱에서 비교·백업·적용 절차를 제공
- `Unavailable`: 현재 공급원이나 아키텍처에서 사용할 후보 패키지를 찾지 못함

해시가 없다는 이유만으로 업데이트 경로를 없애지 않는다. 이 경우 공식 출처에서 사용자가 패키지를 직접 확인/지정하게 하고, 앱이 내부 메타데이터와 현재 환경을 다시 검증한 뒤 `Assisted` 흐름으로 진행한다.
