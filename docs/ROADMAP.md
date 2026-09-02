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
- [x] Apache / PHP / MariaDB 현재 버전 확인
- [x] Apache/MariaDB 실제 서비스명 표시
- [x] 실제 Windows 11 + XAMPP 환경 검증

## Phase 2 — Version Catalog & Compatibility

완료.

- [x] Apache / PHP / MariaDB upstream 최신 버전 조회
- [x] Apache Friends XAMPP 공식 번들 기준 버전 조회
- [x] PE 아키텍처(x86/x64/ARM64) 감지
- [x] PHP Thread Safe / compiler / Extension Build / PHP API 감지
- [x] Apache의 실제 PHP `LoadModule` 연동 방식 감지
- [x] MariaDB 현재 major.minor 계열 감지
- [x] 현재 환경에 맞는 패치 후보 탐색
- [x] 후보 상태를 `자동 가능 / 보조 업데이트 / 검토 후 진행 / 후보 없음`으로 모델링
- [x] 최신 버전 및 각 major.minor 계열의 최신 패치 1개를 목표 버전으로 선택 가능
- [x] 현재 버전 → 선택 버전 업데이트 경로 계산
- [x] PHP 메이저/마이너 변경의 php.ini/확장/Apache SAPI 마이그레이션 계획
- [x] MariaDB 계열 변경의 단계적 업그레이드 계획
- [x] 계열별 최신 버전 필터 smoke test

패키지 다운로드 시점의 SHA256/PGP, 압축 내부 구조, ABI 세부 검증은 Phase 3의 실제 업데이트 준비 과정에서 수행한다.

## Phase 3 — Backup, Compare & Preflight

진행 중.

### 3A — 현재 설치 Preflight

- [x] 구성요소별 `업데이트 준비 점검` UI
- [x] 현재 프로세스 실행 여부 확인
- [x] 등록 서비스 상태 확인
- [x] 구성요소 전체 백업 예상 파일 수/용량 산출
- [x] Apache `.conf`, PHP `.ini*`, MariaDB `my.ini/my.cnf` 설정 파일 manifest 생성
- [x] 설정 파일 SHA256 기록
- [x] 기본 백업 저장 위치 산출
- [x] MariaDB data 디렉터리 보호/논리 백업 필요성 표시
- [x] Preflight smoke test

### 3B — 실제 백업과 롤백 manifest

- [x] 업데이트 직전 서비스/프로세스 상태 스냅샷 저장
- [x] Apache/PHP 구성요소 폴더 자동 백업
- [x] MariaDB 설정 + data 물리 백업 엔진
- [x] MariaDB `mariadb-dump/mysqldump` 전체 논리 백업
- [x] MariaDB 자동 무인증/root 무암호 시도 후 인증 실패 시 사용자 계정/암호 요청
- [x] MariaDB 서비스 중지 → 물리 백업 → 원래 RUNNING 상태 복구 흐름
- [x] 백업 manifest(JSON) 저장
- [x] 논리 백업 SQL의 크기/SHA256을 manifest에 기록
- [x] 파일별 크기/SHA256/원본 경로 기록
- [x] 롤백에 필요한 서비스 상태와 대상 버전 기록
- [x] 백업/패키지/비교 작업 중 UI 동시 입력 방지

> Windows 서비스 중지/시작에 필요한 권한이 없는 경우에는 논리 백업까지 생성한 뒤 물리 백업 단계에서 관리자 권한 필요 오류를 표시한다. 앱 전체 권한 상승 정책은 Phase 4 실행 엔진에서 확정한다.

### 3C — 패키지 준비와 비교

- [x] 선택 버전 실제 패키지 URL 확정 및 다운로드 가능한 공급원 연결
- [x] MariaDB 공식 `sha256sums.txt` 자동 검증
- [x] 다운로드 패키지 로컬 SHA256 기록
- [x] ZIP 내부 PE 아키텍처/필수 실행 파일/모듈 구조 검사
- [x] 기존 설치와 신규 패키지 파일 인벤토리 비교
- [x] 기존 설정과 신규 기본 설정 diff 생성
- [ ] PGP가 제공되는 패키지 서명 신뢰 검증
- [ ] 자동 병합 가능 / 사용자 확인 필요 / 폐기 후보 상세 분류
- [ ] Apache 외부 모듈 및 PHP extension ABI/런타임 상세 판정
- [ ] MariaDB 목표 버전별 중간 업그레이드 경로를 실제 패키지 단계로 확정

### Phase 3 완료 조건

1. 실제 업데이트 전에 서비스/프로세스/파일 상태를 재현 가능한 manifest로 저장한다.
2. 구성요소별 복구 가능한 백업을 생성한다.
3. 대상 패키지를 다운로드하고 가능한 검증 수단을 자동 적용한다.
4. 기존 설정과 신규 패키지 차이를 사용자에게 적용 전에 보여준다.
5. 실제 교체를 시작하기 전에 롤백 가능한 상태임을 확인한다.

## Phase 4 — Assisted Update Engine

- 업데이트 단계별 실행 계획 화면
- Apache 서비스 중지 → 백업 → 패키지 교체 → 설정/모듈 복원 → 구성 검사 → 재시작
- PHP 백업 → 패키지 교체 → php.ini/확장 설정 비교 및 선택 병합 → Apache SAPI 재검증
- MariaDB 논리 백업 → 서비스 중지 → 전체 data/설정 물리 백업 → 바이너리 교체 → `mariadb-upgrade` → 상태 검증 → 재시작
- Windows 서비스 제어 권한/UAC 처리 정책
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
