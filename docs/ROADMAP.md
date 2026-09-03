# XAMPP Updater Roadmap

## 목표

Windows 11의 기존 XAMPP 설치를 대상으로 **Apache / PHP / MariaDB만** 안전하게 관리한다.

- 설치 위치: 자동 감지 + 직접 지정
- Windows 서비스명: 실제 시스템에서 감지
- 현재 버전 확인
- 최신 버전 또는 사용자가 선택한 버전으로 업데이트
- 업데이트 전 설정/데이터 백업
- 기존 설정과 새 기본 설정 비교
- 필요한 설정 복원 또는 선택적 병합
- 완전 자동화가 어려운 경우 비교/선택/확인 단계를 포함한 보조 업데이트
- 별도로 설치된 MariaDB는 관리하지 않고 선택한 XAMPP 루트 내부 MariaDB만 관리

XAMPP 전체 재설치나 다른 구성요소(Node.js, Perl, Tomcat, phpMyAdmin 등) 관리는 범위 밖이다.

## Phase 1 — Foundation & Local Detection

완료.

- [x] .NET 8 WPF GUI 프로젝트 골격
- [x] Core와 GUI 분리
- [x] XAMPP 경로 자동 감지 + 직접 지정
- [x] Apache / PHP / MariaDB 현재 버전 확인
- [x] Apache/MariaDB Windows 서비스명 및 ImagePath 감지
- [x] 실제 Windows 11 + XAMPP 환경 검증

## Phase 2 — Version Catalog & Compatibility

완료.

- [x] Apache / PHP / MariaDB upstream 버전 조회
- [x] Apache Friends XAMPP 공식 번들 기준 버전 조회
- [x] PE 아키텍처 감지
- [x] PHP TS/NTS/compiler/API 정보 감지
- [x] Apache PHP LoadModule 연동 방식 감지
- [x] 최신 버전 및 각 major.minor 계열 최신 패치 선택
- [x] major/minor 변경을 포함한 업데이트 대상 선택
- [x] PHP 설정/확장/SAPI 마이그레이션 계획
- [x] MariaDB major 업그레이드 경로 모델링

## Phase 3 — Backup, Compare & Preflight

핵심 기능 완료. 일부 추가 검증은 하드닝 항목으로 유지한다.

### 3A — 현재 설치 Preflight

- [x] 구성요소별 업데이트 준비 점검
- [x] 프로세스/서비스 상태 확인
- [x] 백업 예상 파일 수/용량
- [x] 설정 파일 manifest + SHA256
- [x] 백업 저장 위치 산출
- [x] MariaDB data/논리 백업 필요성 표시

### 3B — 실제 백업과 롤백 manifest

- [x] 서비스/프로세스 상태 스냅샷
- [x] Apache/PHP 폴더 백업
- [x] MariaDB 전체 논리 백업
- [x] MariaDB 서비스 중지 후 물리 백업
- [x] MariaDB 인증정보는 메모리/임시 option 파일에만 사용
- [x] 파일별 크기/SHA256 manifest
- [x] 실제 업데이트 직전 백업 무결성 재검증
- [x] Apache/PHP/MariaDB 모두 파괴적 교체 전에 백업 무결성 확인

### 3C — 패키지 준비와 비교

- [x] 선택 버전 실제 패키지 URL 해석 및 다운로드
- [x] MariaDB 공식 SHA256 검증
- [x] 다운로드 패키지 SHA256 기록
- [x] ZIP 내부 PE 아키텍처/필수 실행 파일 검사
- [x] 기존/신규 파일 인벤토리 비교
- [x] 기존 설정/신규 기본 설정 diff
- [x] 다운로드 패키지 캐시 재사용
- [ ] 제공되는 경우 PGP 서명 신뢰 검증
- [ ] 외부 Apache 모듈 ABI 판정 강화
- [ ] PECL 외부 PHP extension의 의존 DLL 판정 강화

## Phase 4 — Assisted Update Engine

핵심 업데이트 엔진 구현 및 실제 환경 검증 완료. 하드닝 진행 중.

### PHP

- [x] staging → 디렉터리 교체 → 자동 롤백
- [x] Apache 자동 중지/재시작
- [x] php.ini 마이그레이션 검토/편집/확정
- [x] PHP Apache SAPI 설정 자동 갱신
- [x] PHP extension 이름/legacy alias 변환
- [x] PECL 외부 확장 자동 복원 시도
- [x] browscap 파일 보존 및 최종 정합성 검사
- [x] deprecated 설정 자동 비활성화
- [x] VC++ Runtime 자동 확인/설치
- [x] php -v / php -m / httpd -t 검증
- [x] PHP 7.3.11 → 8.5.10 실제 환경 업데이트 성공
- [x] PHP 8.2.12 → 8.5.10 실제 환경 업데이트 성공

### Apache

- [x] staging → 디렉터리 교체 → 자동 롤백
- [x] 기존 conf 보존 및 편집 가능한 마이그레이션 검토
- [x] 대상 httpd.exe로 교체 전 httpd -t 검증
- [x] 절대 Apache 경로의 staging 검증용 임시 치환
- [x] 참조 모듈 보존
- [x] PE 정적 의존성 검사 및 Windows loader probe 진단
- [x] VS18 VC++ Runtime 확인
- [x] 기존 logs는 복사 보존하여 롤백 원본 유지
- [x] 약한 XAMPP 자체서명 RSA 인증서 자동 재생성
- [x] 서비스 시작 실패 시 상태 코드/error.log 진단
- [x] Apache 2.4.41 → 2.4.68 실제 환경 업데이트 성공
- [x] 검토에서만 가능한 의존 DLL 임시 복구가 발생하면 실제 실행 가능으로 오판하지 않도록 차단
- [ ] 외부 모듈 자동 보존 경로와 실행 단계의 active conf 범위를 완전히 일치시키기
- [ ] 검토 단계의 의존 DLL 자동 복구가 필요한 경우 실행 단계에도 동일 복구 적용

### MariaDB

- [x] 전체 논리 백업 + 물리 백업 필수화
- [x] 기존 mysql 전체를 롤백 원본으로 유지
- [x] 새 MariaDB에는 data 사본만 복사
- [x] my.ini/my.cnf 보존
- [x] 서비스 기동 후 mariadb-upgrade/mysql_upgrade
- [x] 업그레이드 인증정보 일회성 option 파일 처리
- [x] 마이그레이션 검토 UI
- [x] 동일 계열 패치 업데이트
- [x] 직접 major 업그레이드
- [x] MariaDB 10.4.8 → 10.4.34 실제 환경 성공
- [x] MariaDB 10.4.34 → 10.6.28 직접 major 업그레이드 성공
- [x] MariaDB 10.6.28 → 12.3.3 직접 major 업그레이드 성공

### 공통 하드닝

- [x] 영구 실행 로그
- [x] 최근 로그 열기
- [x] 창 타이틀에 빌드 시각 표시
- [x] 패키지 캐시 재사용
- [x] 실제 교체 직전 백업 파일 크기/SHA256 재검증
- [x] 일반 권한에서는 검사/비교 가능, 서비스 제어가 필요한 실제 작업에서만 UAC 승격 재실행
- [x] 승격 재실행 시 현재 XAMPP 경로 전달
- [ ] 장시간 외부 프로세스 실행에 일관된 timeout 적용
- [ ] 단계별 실시간 진행 callback 통일
- [ ] 외부 모듈/extension ABI 및 의존성 판정 강화

## Phase 5 — Config Compare / Restore

핵심 snapshot 비교/복원 기능 구현 및 실제 Apache 복원 검증 완료. 선택 병합 기능을 남겨둔다.

- [x] 업데이트 전/후 설정 snapshot 자동 저장
- [x] 수동 현재 설정 snapshot + 사용자 메모
- [x] 파일별 SHA256/크기 manifest
- [x] snapshot 무결성 검사
- [x] 업데이트/복원 이력별 설정 비교
- [x] DiffPlex 기반 좌우 내용 diff, 변경 강조/줄 번호/차이 이동
- [x] 현재 설정과 과거 snapshot 즉시 비교
- [x] snapshot 메모 수정/삭제/폴더 열기
- [x] 이전 snapshot 전체 설정 복원
- [x] 복원 직전 BeforeRestore 안전 snapshot
- [x] 복원 성공 후 AfterRestore 이력 snapshot
- [x] Apache httpd -t / PHP php -v / MariaDB 설정 파싱 검증
- [x] 실행 중이던 서비스의 중지/재시작 및 실패 시 자동 원복
- [x] Apache 설정 snapshot 실제 복원 성공 확인
- [x] 기존 사용자 설정과 신규 기본값 분리 표시(업데이트 마이그레이션 검토)
- [x] 변경/추가/삭제 설정 파일 식별
- [ ] 파일 단위 선택 복원/병합
- [ ] ini/conf 항목 단위 선택 병합
- [ ] 충돌 항목 사용자 선택 후 적용

## Phase 6 — Packaging & Release

- [ ] self-contained 배포 방식 결정
- [ ] 앱 자체 업데이트
- [ ] 로그/진단 정보 내보내기
- [ ] 릴리스 빌드 및 GitHub Actions 정리
