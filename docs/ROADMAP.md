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

### 2B — 패키지 호환성 메타데이터

- [ ] Apache Windows 바이너리 공급원 확정 및 다운로드 파일 메타데이터 추출
- [ ] Apache 아키텍처/VC 런타임/모듈 ABI 판정
- [ ] PHP x64 / Thread Safe / compiler / extension ABI 판정
- [ ] MariaDB x64 ZIP 패키지와 업그레이드 경로 판정
- [ ] 현재 설치 환경의 x86/x64 및 빌드 정보 추가 감지
- [ ] 호환 / 조건부 호환 / 비호환 상태 모델 정의

### 2C — 선택 가능한 버전 카탈로그

- [ ] 지원되는 과거 버전 목록 조회
- [ ] 특정 버전 선택 UI
- [ ] 선택 버전별 패키지 URL 결정
- [ ] SHA256/PGP 등 검증 메타데이터 확보
- [ ] 다운로드 전 호환성 판정 결과 표시

### Phase 2 완료 조건

1. 최신 버전과 XAMPP 공식 번들 기준 버전을 구분하여 표시한다.
2. 사용자가 선택 가능한 버전 목록을 제공한다.
3. 각 후보가 현재 설치에 적용 가능한지 자동 판정한다.
4. 실제 다운로드 전에 아키텍처, 런타임, Thread Safe/ABI, MariaDB 업그레이드 경로를 확인한다.
5. 해시 또는 서명 검증 정보를 확보하지 못한 패키지는 자동 업데이트 대상으로 허용하지 않는다.

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
