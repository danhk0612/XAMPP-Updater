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
- 완전 자동화가 어려운 경우에도 비교/선택/확인 단계를 포함한 보조 업데이트 제공

XAMPP 전체 재설치나 다른 구성요소(Node.js, Perl, Tomcat 등) 관리는 범위 밖이다.

## Phase 1 — Foundation & Local Detection

완료.

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
- [x] 실제 Windows 11 + XAMPP 환경 검증
- [x] Phase 1은 읽기 전용으로 동작

## Phase 2 — Version Catalog & Compatibility

진행 중.

### 2A — 공급원과 최신 버전 조회

- [x] Apache 최신 릴리스 공급원: Apache HTTP Server 공식 다운로드
- [x] PHP 최신 Windows 릴리스 공급원: PHP 공식 Windows 다운로드
- [x] MariaDB 최신 Community Server 공급원: MariaDB 공식 다운로드
- [x] XAMPP 공식 번들 기준 공급원: Apache Friends Windows 다운로드
- [x] upstream 최신 버전과 XAMPP 공식 번들 버전을 별도 모델로 관리
- [x] GUI 온라인 확인 기능
- [x] 공급원 parser smoke test
- [x] 공급원 파싱 실패 시 임의 추정 금지
- [x] 실제 Windows 11 환경에서 온라인 조회 검증

### 2B — 패키지 호환성 메타데이터

현재 설치 환경 감지:

- [x] Apache/PHP/MariaDB PE 아키텍처(x86/x64/ARM64) 감지
- [x] PHP Thread Safe / compiler / Extension Build / PHP API 감지
- [x] Apache 설정에서 실제 PHP `LoadModule` 연동 방식 감지
- [x] MariaDB 현재 major.minor 계열 감지
- [x] 설치 환경과 XAMPP 공식 버전을 비교한 1차 호환성 판정
- [x] 실제 Windows 11 환경에서 로컬 호환성 프로파일 검증

후보 패키지 판정:

- [x] Apache Lounge Windows ZIP 후보 탐색 및 version/arch/compiler 메타데이터 추출
- [x] PHP Windows archive에서 현재 major.minor / arch / TS / compiler 일치 패치 후보 탐색
- [x] MariaDB 공식 다운로드에서 현재 major.minor 계열 최신 winx64 후보 탐색
- [x] 후보 상태를 `자동 가능 / 보조 업데이트 / 검토 후 진행 / 후보 없음`으로 모델링
- [x] 해시가 없는 PHP archive도 보조 업데이트 후보로 유지
- [x] GUI에서 실제 후보 버전과 자동화 수준 표시
- [ ] Apache 후보 ZIP 내부 모듈/의존 DLL 기준 ABI 세부 검증
- [ ] PHP 후보 ZIP의 Extension Build / Apache SAPI DLL을 실제 압축 내용 기준으로 검증
- [ ] MariaDB SHA256 manifest 실제 값 확보 및 ZIP 해시와 연결
- [ ] MariaDB `mariadb-upgrade` 실행 조건/절차 확정

### 2C — 선택 가능한 버전 카탈로그

- [x] 현재 설치 / 현재 계열 추천 / XAMPP 공식 / upstream 최신을 목표 버전 선택지로 통합
- [x] 최신 버전을 실제 선택 가능한 대상으로 노출
- [x] Apache/PHP/MariaDB별 목표 버전 선택 UI
- [x] 현재 버전 → 선택 버전 업데이트 경로 계산
- [x] 작업을 자동 / 보조 / 사용자 확인 단계로 분류
- [x] PHP 메이저/마이너 변경 시 php.ini/확장/Apache SAPI 마이그레이션 계획 생성
- [x] MariaDB 계열 변경 시 중간 업그레이드 단계가 필요한 경로로 계획 생성
- [x] 선택 목록을 각 major.minor 계열의 최신 패치 1개로 축약
- [x] 최신 전체 버전과 계열별 최신 버전의 중복 제거
- [x] PHP 계열별 최신 Windows 패키지 정보 연결
- [x] MariaDB 계열별 최신 공식 winx64 패키지 페이지 연결
- [x] Apache ASF 릴리스에서 계열별 최신 버전 선택지 생성
- [x] 계열별 최신 필터 parser smoke test
- [x] 별도 ZIP 직접 지정 UI 제거
- [ ] 선택한 Apache 버전의 Windows ZIP URL을 가능한 공급원에서 추가 자동 탐색
- [ ] MariaDB 선택 버전 패키지 페이지에서 실제 ZIP/manifest/PGP 링크 해석
- [ ] SHA256/PGP 등 검증 메타데이터 확보
- [ ] 상세 변경사항 비교 화면

### Phase 2 완료 조건

1. 최신 버전과 XAMPP 공식 번들 기준 버전을 구분하여 표시한다.
2. 사용자가 최신 및 각 major.minor 계열의 최신 버전을 선택할 수 있다.
3. 현재 버전에서 선택 버전으로 이동하기 위해 필요한 작업을 자동 계산한다.
4. 각 작업을 자동 처리 / 보조 처리 / 사용자 확인으로 분류한다.
5. 실제 다운로드 전에 아키텍처, 런타임, Thread Safe/ABI, MariaDB 업그레이드 경로를 확인한다.

> Apache/PHP/MariaDB는 단순히 최신 ZIP을 덮어쓰는 방식으로 처리하지 않는다. 자동화가 어려운 경우에도 백업 → 비교 → 사용자 확인 → 적용 → 검증 순서의 보조 업데이트를 제공한다.

## Phase 3 — Backup, Compare & Preflight

- 실행 중 프로세스 및 Windows 서비스 상태 점검
- 업데이트 대상 파일 잠금 여부 확인
- 구성요소별 설정/확장/모듈 파일 manifest 생성
- 업데이트 전 자동 백업
- 신규 패키지와 기존 설치의 파일 구조 비교
- 기존 설정과 신규 기본 설정 diff 생성
- 자동 병합 가능 / 사용자 확인 필요 / 폐기 후보 분류
- MariaDB 데이터 디렉터리 보호 정책 확정
- 롤백에 필요한 manifest 생성

## Phase 4 — Assisted Update Engine

- 업데이트 단계별 실행 계획 화면
- Apache 서비스 중지 → 백업 → 패키지 교체 → 설정/모듈 복원 → 구성 검사 → 재시작
- PHP 백업 → 패키지 교체 → php.ini/확장 설정 비교 및 선택 병합 → Apache SAPI 재검증
- MariaDB 서비스 중지 → 전체 데이터/설정 백업 → 바이너리 교체 → `mariadb-upgrade` → 상태 검증 → 재시작
- 자동 처리 불가능한 충돌은 해당 단계에서만 사용자 선택 요청
- 실패 시 자동 롤백
- 최신/선택 버전 업데이트 실행

## Phase 5 — Config Compare / Restore

- 기존 설정과 신규 기본 설정 diff
- 변경된 사용자 설정 식별
- 구성요소별 복원 후보 표시
- 선택 복원/병합
- 충돌이 있는 설정은 자동 덮어쓰기하지 않음
- 업데이트 전/후 설정 비교 이력 제공

## Phase 6 — Packaging & Release

- 단일 배포 패키지 또는 self-contained 배포 결정
- 앱 자체 업데이트
- 로그/진단 정보 내보내기
- 릴리스 빌드와 GitHub Actions 정리
