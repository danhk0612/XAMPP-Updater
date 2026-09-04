# XAMPP Updater Roadmap

## 목표

Windows 11의 기존 XAMPP 설치를 대상으로 **Apache / PHP / MariaDB만** 안전하게 관리한다.

핵심 우선순위는 **자동화된 안전한 업데이트 → 실패 시 자동 롤백 → 업데이트 후 문제 발생 시 설정 복원**이다.
XAMPP 전체 재설치나 Node.js, Perl, Tomcat, phpMyAdmin 등 다른 구성요소 관리는 범위 밖이다.

## Phase 1 — Foundation & Local Detection

완료.

- [x] .NET 8 WPF GUI
- [x] Core / GUI 분리
- [x] XAMPP 경로 자동 감지 + 직접 지정
- [x] Apache / PHP / MariaDB 현재 버전 확인
- [x] Apache / MariaDB Windows 서비스명 및 ImagePath 감지
- [x] 실제 Windows 11 + XAMPP 환경 검증
- [x] MariaDB `mysqld.exe` / `mariadbd.exe` 감지

## Phase 2 — Version Catalog & Compatibility

완료.

- [x] Apache / PHP / MariaDB upstream 버전 조회
- [x] Apache Friends XAMPP 공식 번들 기준 버전 조회
- [x] PE 아키텍처 감지
- [x] PHP TS/NTS/compiler/API 정보 감지
- [x] Apache PHP LoadModule 연동 방식 감지
- [x] latest 및 major.minor 계열별 최신 패치 선택
- [x] major/minor 변경 대상 선택
- [x] PHP 설정/확장/SAPI 마이그레이션 계획
- [x] MariaDB major 업그레이드 경로 모델링

## Phase 3 — Backup, Compare & Preflight

핵심 기능 완료.

- [x] 구성요소별 준비 점검
- [x] 프로세스/서비스 상태 확인
- [x] 설정 manifest + SHA256
- [x] Apache/PHP 폴더 롤백 백업
- [x] MariaDB 전체 논리 + 물리 백업
- [x] 실제 교체 직전 백업 무결성 재검증
- [x] 패키지 URL 해석/다운로드/캐시
- [x] MariaDB 공식 SHA256 검증
- [x] ZIP 내부 PE 아키텍처/필수 실행 파일 검사
- [x] 기존/신규 파일 및 설정 diff
- [x] PHP 외부 extension PE/TS-NTS/loader 판정

추가 PGP 검증과 정적 ABI/메타데이터 강화는 현재 동작을 바꾸지 않고 `DEFERRED_HARDENING.md`의 후속 후보로 이동했다.

## Phase 4 — Assisted Update Engine

핵심 업데이트 엔진과 실제 환경 검증 완료.

### PHP

- [x] staging → 교체 → 자동 롤백
- [x] Apache 자동 중지/재시작
- [x] php.ini 마이그레이션
- [x] Apache PHP SAPI 자동 갱신
- [x] extension legacy alias 변환 및 외부 extension 보존
- [x] VC++ Runtime 확인/설치
- [x] `php -v`, `php -m`, `httpd -t` 검증
- [x] PHP 7.3.11 → 8.5.10 실제 업데이트
- [x] PHP 8.2.12 → 8.5.10 실제 업데이트

### Apache

- [x] staging → 교체 → 자동 롤백
- [x] 기존 conf 보존 및 마이그레이션 검토
- [x] 대상 `httpd -t` 사전검증
- [x] 외부 모듈/종속 DLL 보존 및 loader 진단
- [x] VC++ Runtime 확인
- [x] 기존 logs 보존
- [x] 약한 XAMPP 자체서명 인증서 자동 재생성
- [x] 서비스 시작 실패 상세 진단
- [x] Apache 2.4.41 → 2.4.68 실제 업데이트

### MariaDB

- [x] 전체 논리 + 물리 백업 필수
- [x] 기존 mysql 전체를 롤백 원본으로 유지
- [x] 새 MariaDB에 data 사본 적용
- [x] my.ini/my.cnf 보존
- [x] 서비스 기동 후 `mariadb-upgrade` / `mysql_upgrade`
- [x] 일회성 인증 option 파일
- [x] 동일 계열 패치 및 직접 major 업그레이드
- [x] MariaDB 10.4.8 → 10.4.34
- [x] MariaDB 10.4.34 → 10.6.28
- [x] MariaDB 10.6.28 → 12.3.3

### 공통 하드닝

- [x] 영구 실행 로그
- [x] 패키지 캐시
- [x] UAC 필요 시점 승격
- [x] 자식 프로세스 취소/watchdog
- [x] Windows 서비스 pending 상태 안전 처리
- [x] Apache/PHP/MariaDB 진행 callback 공통화
  - 공통 stage ID: BackupVerify / BeforeSnapshot / Execute / AfterSnapshot / Rollback / Failed / Completed
  - 기존 executor 순서/롤백 로직은 변경하지 않고 보고 계층만 통일

## Phase 5 — Config Compare / Restore

완료.

- [x] 업데이트 전/후 설정 snapshot 자동 저장
- [x] 수동 snapshot + 사용자 메모
- [x] SHA256/크기 manifest와 무결성 검사
- [x] DiffPlex 좌우 diff
- [x] 현재 설정과 과거 snapshot 비교
- [x] 전체/파일/안전한 항목 단위 복원
- [x] 복원 직전 안전 snapshot 및 실패 시 자동 원복
- [x] Apache/PHP/MariaDB 설정 검증
- [x] Apache 설정 snapshot 실제 복원 검증

## Phase 6 — Packaging & Release

완료.

- [x] win-x64 .NET 8 self-contained single-file EXE
- [x] GitHub Actions restore/build/smoke/publish
- [x] GitHub Release EXE + SHA256 게시
- [x] 공개 `v0.1.0` Release
- [x] 공개 `v0.1.1` Release
- [x] `releases/latest` → `v0.1.1`
- [x] 앱 자체 업데이트
  - GitHub latest 조회
  - 실제 다운로드 진행률/용량
  - 다운로드 중 취소
  - SHA256 검증
  - 검증 후 취소 차단
  - 5초 자동/즉시 재시작
  - EXE 교체 및 `.update-backup` 실패 복원
- [x] 실제 배포 EXE `v0.1.0` → `v0.1.1` 자체 업데이트 및 재실행 확인
- [x] 진단 정보 ZIP 내보내기
- [x] 공통 애플리케이션 아이콘을 EXE와 WPF 창에 적용

## 현재 안정화 — XAMPP 범용성

정적 코드/구조 검토를 완료하고 지원 범위를 문서화했다.

- [x] XAMPP 루트를 `C:\xampp`로 고정하지 않음
- [x] 임의 드라이브 및 수동 지정
- [x] 서비스명을 고정하지 않고 ImagePath 기준으로 연결
- [x] 여러 XAMPP 후보 감지
- [x] `mysqld.exe` / `mariadbd.exe` 감지
- [x] 공백/비 ASCII/보호 경로에 대한 설계 검토
- [x] 지원/조건부 지원/범위 밖 기준 문서화
- [x] 실제 환경 회귀 테스트 매트릭스 정의
- [ ] 추가 VM/사용자 환경이 확보될 때 경로·서비스·설정 조합별 회귀 테스트 확대

상세 기준: `docs/COMPATIBILITY.md`

## 차후 후보 — 현재는 구현하지 않음

아래는 현재 안정 동작을 건드리지 않고 후속 후보로만 보존한다.

- Apache module / PHP extension ABI 정적 메타데이터 추가
- 공급처가 제공하는 경우 PGP/GPG 서명 검증
- 공식 manifest/API 기반 공급처 메타데이터 신뢰도 강화

적용 정책과 다시 착수할 조건은 `docs/DEFERRED_HARDENING.md`에 정리한다.
