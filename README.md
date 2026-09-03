# XAMPP Updater

Windows 11의 기존 XAMPP 설치에서 **Apache / PHP / MariaDB**만 안전하게 업데이트하기 위한 .NET 8 WPF GUI 도구입니다.

XAMPP 전체를 재설치하지 않고 설치 경로와 실제 Windows 서비스를 감지한 뒤, 구성요소별 버전 선택, 백업, 설정 마이그레이션, 실제 교체, 검증, 실패 시 자동 롤백까지 하나의 흐름으로 처리하는 것을 목표로 합니다.

## 현재 개발 단계

**Phase 6 — Packaging & Release 최종 검증 단계**입니다.

핵심 업데이트 엔진과 설정 복원 기능은 구현되어 있으며 Windows 11 테스트 환경에서 다음 실제 업데이트를 확인했습니다.

- Apache 2.4.41 → 2.4.68
- PHP 7.3.11 → 8.5.10
- PHP 8.2.12 → 8.5.10
- MariaDB 10.4.8 → 10.4.34
- MariaDB 10.4.34 → 10.6.28
- MariaDB 10.6.28 → 12.3.3

릴리스 파이프라인에서는 `v0.1.0`과 `v0.1.1` 공개 GitHub Release를 실제 생성했고, 두 버전 모두 `XAMPP-Updater.exe`와 `XAMPP-Updater.exe.sha256` 게시를 확인했습니다. 현재 GitHub `latest release`는 `v0.1.1`입니다.

## 주요 기능

- XAMPP 설치 경로 자동 감지 / 직접 지정
- Apache / PHP / MariaDB 현재 버전 감지
- Apache / MariaDB 실제 Windows 서비스명 감지
- upstream 및 XAMPP 기준 버전 조회
- 최신 버전 또는 major.minor 계열별 최신 패치 선택
- 현재 설치 환경의 PE 아키텍처, PHP TS/NTS/API, Apache PHP 연동 방식 검사
- 업데이트 전 사전 점검과 롤백 백업
- 다운로드 패키지 구조·아키텍처·SHA256 검증
- Apache/PHP 설정 마이그레이션 검토 및 자동 변환
- MariaDB 논리 + 물리 백업 후 업그레이드
- 서비스 중지/재시작과 실제 실행 검증
- 실패 시 자동 롤백
- 업데이트 전/후 설정 snapshot, 비교, 선택 복원
- 영구 작업 로그 및 최근 로그 열기
- 진단 정보 ZIP 내보내기
- win-x64 self-contained 단일 EXE publish
- GitHub Release 기반 앱 자체 업데이트 구현
  - 새 EXE와 SHA256 검증 파일 다운로드
  - 다운로드 중 진행률/용량 표시 및 취소
  - SHA256 검증 후 현재 EXE 교체 및 자동 재시작
  - 교체 실패 시 `.update-backup` 복원

남은 Phase 6 최종 검증은 Windows에서 실제 `v0.1.0` 배포 EXE를 실행해 `v0.1.1`로 자체 업데이트하는 왕복 테스트입니다.

## 범위

관리 대상은 선택한 XAMPP 루트 내부의 다음 세 구성요소로 제한합니다.

- Apache
- PHP
- MariaDB

XAMPP 전체 재설치, 별도 설치된 MariaDB, Node.js, Perl, Tomcat, phpMyAdmin 등은 관리하지 않습니다.

## 개발 빌드

Windows 11과 .NET 8 SDK 기준입니다.

```powershell
dotnet restore XamppUpdater.sln
dotnet build XamppUpdater.sln -c Release
```

개발 실행:

```powershell
dotnet run --project .\src\XamppUpdater.App\XamppUpdater.App.csproj
```

## 배포 빌드

win-x64 self-contained 단일 EXE를 생성합니다.

```powershell
dotnet publish .\src\XamppUpdater.App\XamppUpdater.App.csproj `
  -c Release `
  -p:PublishProfile=win-x64 `
  -o .\artifacts\win-x64
```

생성되는 주 실행 파일은 `XAMPP-Updater.exe`입니다.

GitHub Actions는 branch/PR 빌드에서 restore, build, smoke tests, self-contained publish, EXE 검증과 artifact 업로드를 수행합니다. `v*` 태그 또는 `release/v*` 릴리스 브랜치에서는 추가로 `XAMPP-Updater.exe.sha256`을 생성하고 GitHub Release에 EXE와 SHA256 파일을 게시하도록 구성되어 있습니다.

`release/v0.1.0`과 `release/v0.1.1` 경로로 실제 공개 Release 게시를 검증했습니다. GitHub Actions의 `GITHUB_TOKEN`이 생성한 태그는 재귀 workflow 실행을 발생시키지 않으므로, 해당 릴리스 브랜치가 생성한 태그에서 별도의 tag-trigger run은 생성되지 않습니다.

## 진단 정보 내보내기

GUI의 **진단 정보 내보내기**에서 ZIP 파일을 저장할 수 있습니다.

포함:

- 앱/OS/권한/XAMPP 감지 정보
- 현재 세션 작업 로그
- 영구 실행 로그
- 자체 업데이트 로그(존재하는 경우)

제외:

- 설정 파일 원문
- 데이터베이스 내용
- 롤백 백업
- 다운로드한 구성요소 패키지
- 인증정보

## 문서

- `docs/ROADMAP.md` — 전체 단계와 남은 작업
- `docs/DECISIONS.md` — 확정된 범위/기술 결정
