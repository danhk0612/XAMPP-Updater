# XAMPP Updater Roadmap

## 목표

Windows 11의 기존 XAMPP 설치를 대상으로 **Apache / PHP / MariaDB만** 안전하게 관리한다.

- 설치 위치: 자동 감지 + 직접 지정
- Windows 서비스명: 실제 시스템에서 감지하고, 이후 설정에서 직접 지정 가능하게 확장
- 현재 버전 확인
- 최신 버전 또는 사용자가 선택한 버전으로 업데이트
- 업데이트 전 설정/데이터 백업
- 기존 설정과 새 기본 설정 비교
- 필요한 설정 복원 또는 선택적 병합

XAMPP 전체 재설치나 다른 구성요소(Node.js, Perl, Tomcat 등) 관리는 범위 밖이다.

## Phase 1 — Foundation & Local Detection

현재 구현 단계.

- [x] .NET 8 WPF GUI 프로젝트 골격
- [x] Core와 GUI 분리
- [x] `C:\xampp` 및 각 고정 드라이브의 `\xampp` 기본 경로 감지
- [x] XAMPP 제거 정보 레지스트리 기반 설치 경로 감지
- [x] Apache/MariaDB Windows 서비스 `ImagePath` 기반 설치 경로 감지
- [x] 설치 경로 직접 입력/폴더 선택
- [x] Apache `httpd.exe -v` 버전 확인
- [x] PHP `php.exe -v` 버전 확인
- [x] MariaDB `mysqld.exe --version` 버전 확인
- [x] Apache/MariaDB 실제 서비스명 표시
- [x] 실제 MariaDB/MySQL 버전 출력 예제에 대한 parser smoke test
- [x] 임시 XAMPP 구조를 이용한 수동 경로 검사 smoke test
- [x] Phase 1은 읽기 전용으로 동작

### Phase 1 완료 조건

1. Windows 11에서 앱이 실행된다.
2. 일반적인 XAMPP 설치를 자동 감지할 수 있다.
3. 자동 감지가 실패해도 사용자가 설치 루트를 직접 지정할 수 있다.
4. Apache/PHP/MariaDB의 설치 여부와 현재 버전을 GUI에서 확인할 수 있다.
5. 등록된 Apache/MariaDB 서비스명이 고정값이 아니라 시스템에서 감지된다.
6. 파일 교체, 서비스 중지, 설정 변경 등 시스템 변경 작업을 하지 않는다.
7. Windows CI에서 빌드와 Phase 1 smoke test가 통과한다.

## Phase 2 — Version Catalog & Compatibility

- 각 구성요소의 사용 가능한 버전 목록 공급원 확정
- 최신 버전 조회
- 특정 버전 선택
- x64, VC 런타임, Thread Safe 여부 등 Windows/XAMPP 호환성 메타데이터 정의
- 다운로드 URL과 해시/서명 검증 방식 정의
- XAMPP 번들 구조와 순정 upstream ZIP 구조 차이 조사

> Apache/PHP/MariaDB는 단순히 최신 ZIP을 덮어쓰는 방식으로 처리하지 않는다. Phase 2에서 공급원과 호환성 규칙을 먼저 확정한다.

## Phase 3 — Backup & Preflight

- 실행 중 프로세스 및 Windows 서비스 상태 점검
- 업데이트 대상 파일 잠금 여부 확인
- 구성요소별 설정 파일 목록 정의
- 업데이트 전 자동 백업
- MariaDB 데이터 디렉터리 보호 정책 확정
- 롤백에 필요한 manifest 생성

## Phase 4 — Update Engine

- Apache 업데이트 전략
- PHP 업데이트 전략
- MariaDB 업데이트 전략
- 서비스 중지/재시작
- 원자적 교체가 가능한 범위 정의
- 실패 시 자동 롤백
- 최신/선택 버전 업데이트 실행

## Phase 5 — Config Compare / Restore

- 기존 설정과 신규 기본 설정 diff
- 변경된 사용자 설정 식별
- 구성요소별 복원 후보 표시
- 선택 복원/병합
- 충돌이 있는 설정은 자동 덮어쓰기하지 않음

## Phase 6 — Packaging & Release

- 단일 배포 패키지 또는 self-contained 배포 결정
- 앱 자체 업데이트
- 로그/진단 정보 내보내기
- 릴리스 빌드와 GitHub Actions 정리
