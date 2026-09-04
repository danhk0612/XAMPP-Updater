# XAMPP Updater Roadmap

## 목표

Windows 11의 기존 XAMPP 설치에서 **Apache / PHP / MariaDB**, 그리고 XAMPP에 포함된 경우 **phpMyAdmin**을 안전하게 관리한다.

핵심 우선순위는 **자동화된 안전한 업데이트 → 실패 시 자동 롤백 → 업데이트 후 문제 발생 시 설정 복원**이다.
XAMPP 전체 재설치나 Node.js, Perl, Tomcat 등 다른 구성요소 관리는 범위 밖이다.

## 1.0 상태

핵심 기능, 실제 업데이트/롤백 검증, 설정 복원, 자체 업데이트, phpMyAdmin 관리, 한국어/영어 UI 기반까지 완료했다.

1.0 이후 남는 작업은 새 핵심 기능이 아니라 일반적인 버그 수정, 추가 환경 회귀 테스트, 번역/레이아웃 보정, 선택적 보안 하드닝으로 취급한다.

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
- [x] MariaDB major 업그레이드 경로 모델링 및 실제 검증

## Phase 3 — Backup, Compare & Preflight

완료.

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
- [x] 정식 롤백 백업과 롤백 직전 Safety 백업 구분
- [x] 현재 설치 버전에 직접 연결되는 정상 롤백 백업만 카탈로그에 노출
- [x] 롤백 후보 manifest/파일 크기/SHA256 검증 및 MariaDB 논리 백업 필수화
- [x] Safety 백업 7일/구성요소별 최근 3개 보존 정책
- [x] schema 1/2 기존 백업 호환 유지

세부 정책: `docs/BACKUP_ROLLBACK_POLICY.md`

## Phase 4 — Assisted Update Engine

완료 및 실제 환경 검증 완료.

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
- [x] 동일 계열 패치 및 검증된 직접 major 업그레이드
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
  - 각 executor의 고유 교체/롤백 로직은 유지하고 보고 계층만 통일
- [x] Apache ↔ PHP 변경 후 공통 연동 검증
- [x] Apache/PHP 롤백 후 현재 상대 구성요소와 연동 실패 시 방금 변경한 쪽만 자동 원복
- [x] Apache 롤백 시 현재 PHP에 맞춰 SAPI 설정 재보정
- [x] Apache 롤백에서 백업 제외된 logs 디렉터리 구조 보존
- [x] 실제 환경에서 Apache → PHP → MariaDB → phpMyAdmin 업데이트 후 MariaDB → Apache → PHP → phpMyAdmin 순서 롤백 검증

세부 정책: `docs/APACHE_PHP_INTEGRATION.md`

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
- [x] GitHub Release 기반 앱 자체 업데이트
- [x] 실제 EXE 교체/재시작 검증
- [x] 실패 시 `.update-backup` 복원
- [x] 진단 정보 ZIP 내보내기
- [x] 공통 애플리케이션 아이콘
- [x] 1.0.0 릴리스 준비

## Phase 7 — Optional phpMyAdmin & Localization

완료.

### phpMyAdmin

- [x] 선택한 XAMPP 루트에 `phpMyAdmin` 폴더가 있을 때만 UI 노출
- [x] 설치 버전 탐지
- [x] 공식 latest metadata 조회
- [x] all-languages ZIP 다운로드
- [x] 공식 SHA256 검증
- [x] 현재 PHP/DB 최소 호환성 검사
- [x] 공식 metadata 상 PHP 상한 초과 시 경고
- [x] 기존 phpMyAdmin 전체 롤백 백업
- [x] `config.inc.php`, `.htaccess`, `upload`, `save` 보존
- [x] 새 패키지 staging/구조/버전 검증
- [x] `config.inc.php` PHP 구문 검사
- [x] 폴더 교체 및 실패 시 자동 원복
- [x] 관리자 권한 재실행 후 자동 재개
- [x] 공통 stage ID 기반 진행률/실제 다운로드 용량 표시
- [x] 업데이트 과정에서 생성된 백업 감지 및 롤백
- [x] 실제 XAMPP 설치에서 업데이트 및 롤백 검증
- [x] 업데이트 후 브라우저 로그인/DB 조회 확인

### 한국어 / 영어 다국어화

- [x] 리소스 기반 문자열 계층
- [x] 시스템 기본값 / 한국어 / English 모드
- [x] Windows UI 언어 기반 시스템 기본 선택
- [x] `%LOCALAPPDATA%\XAMPP-Updater\settings.json`에 사용자 선택 저장
- [x] 언어 선택 UI
- [x] 언어 변경 시 자동 재시작
- [x] XAML 및 동적 WPF Text/Content/Header 현지화 계층
- [x] 자체 업데이트 UI/진행/대화상자 현지화
- [x] 영어 모드 MessageBox 버튼 현지화
- [x] 버전 셀렉터/동적 계획/온라인 상태/Advanced information 현지화 보강
- [x] 설정 이력/비교/복원/병합/무결성 검사 UI 현지화 보강
- [x] 내부 stage ID는 영어 고정 유지
- [x] 실제 Windows에서 반복 육안 검증 및 잔여 혼합 문구 수정

이후 번역 누락이나 긴 문자열 레이아웃 문제는 일반 버그 수정으로 처리한다.

## XAMPP 범용성 안정화

현재 지원 범위를 문서화하고 기본 회귀 기준을 확정했다.

- [x] XAMPP 루트를 `C:\xampp`로 고정하지 않음
- [x] 임의 드라이브 및 수동 지정
- [x] 서비스명을 고정하지 않고 ImagePath 기준으로 연결
- [x] 여러 XAMPP 후보 감지
- [x] `mysqld.exe` / `mariadbd.exe` 감지
- [x] 공백/비 ASCII/보호 경로 설계 검토
- [x] 지원/조건부 지원/범위 밖 기준 문서화
- [x] 실제 환경 회귀 테스트 매트릭스 정의

추가 VM/사용자 환경이 확보될 때의 회귀 테스트 확대는 1.0 이후 유지보수 작업으로 처리한다.

상세 기준: `docs/COMPATIBILITY.md`

## 1.0 이후 선택적 후보

현재 안정 동작을 건드리지 않고 후속 후보로만 보존한다.

- Apache module / PHP extension ABI 정적 메타데이터 강화
- 공급처가 제공하는 경우 PGP/GPG 서명 검증
- 공식 manifest/API 기반 공급처 메타데이터 신뢰도 강화

적용 정책과 다시 착수할 조건은 `docs/DEFERRED_HARDENING.md`에 정리한다.
