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
