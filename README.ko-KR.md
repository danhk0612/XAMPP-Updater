# XAMPP Updater

기존 Windows 11 XAMPP 설치의 **Apache / PHP / MariaDB**를 안전하게 업데이트하고, 선택한 XAMPP 루트에 `phpMyAdmin` 폴더가 있는 경우 **phpMyAdmin**까지 관리하는 .NET 8 WPF 도구입니다.

[English README](README.md)

XAMPP 전체를 다시 설치하지 않고 설치 경로와 실제 Windows 서비스를 감지한 뒤, 구성요소별 버전 선택, 롤백 백업, 설정 마이그레이션, 실제 교체, 실행 검증, 실패 시 자동 원복까지 하나의 흐름으로 처리합니다.

## 1.0 관리 범위

관리 대상:

- Apache
- PHP
- MariaDB
- phpMyAdmin — 선택한 XAMPP 루트에 `phpMyAdmin` 폴더가 실제 존재하는 경우에만 관리

범위 밖:

- XAMPP 전체 재설치 또는 전체 버전 업그레이드
- 선택한 XAMPP 루트 밖에 별도 설치된 Apache/PHP/MariaDB/phpMyAdmin
- Node.js, Perl, Tomcat 등 기타 XAMPP 구성요소
- 표준 `apache`, `php`, `mysql`, `phpMyAdmin` 디렉터리를 임의로 재배치한 재패키징

지원 환경과 조건은 [호환성 문서](docs/COMPATIBILITY.md)를 참고하세요.

## 주요 기능

- XAMPP 설치 경로 자동 감지 / 직접 지정
- Apache / PHP / MariaDB / phpMyAdmin 현재 버전 감지
- 고정 서비스명이 아닌 실제 실행 파일 `ImagePath` 기준 Windows 서비스 감지
- `mysqld.exe`와 `mariadbd.exe` 모두 지원하는 MariaDB 감지
- upstream 및 XAMPP 기준 버전 조회
- 최신 버전 및 major.minor 계열별 선택 버전 제공
- 현재 설치 환경 호환성 프로파일링
  - PE 아키텍처
  - PHP TS/NTS, compiler/API 정보
  - Apache PHP 모듈 연동 방식
- 업데이트 전 준비 점검
- manifest / 크기 / SHA256 검증을 포함한 롤백 백업
- 정식 롤백 백업과 롤백 직전 Safety 백업 구분
- 현재 설치 버전과 직접 연결되는 정상 백업만 롤백 후보로 노출
- Apache/PHP 설정 마이그레이션 및 실제 연동 검증
- MariaDB 논리 + 물리 백업 및 업그레이드 절차
- 서비스 중지/시작과 변경 후 실행 검증
- 업데이트 또는 롤백 검증 실패 시 자동 원복
- 업데이트 전/후 설정 snapshot
- 설정 이력, 비교, 무결성 검사, 선택 복원, 안전한 항목 단위 병합
- 영구 작업 로그와 진단 정보 ZIP 내보내기
- 한국어 / 영어 UI
- GitHub Release 기반 자체 업데이트와 SHA256 검증
- win-x64 self-contained 단일 EXE

## Apache / PHP 연동 정책

Apache와 PHP는 각각 독립적으로 업데이트/롤백하지만, 일반적인 XAMPP에서는 Apache가 PHP를 모듈로 직접 로드합니다. 따라서 둘 중 하나만 변경해도 Apache 시작 가능 여부에 영향을 줄 수 있습니다.

XAMPP Updater는 상대 구성요소를 자동으로 업데이트하거나 롤백하지 않습니다. 대신:

1. 사용자가 선택한 구성요소만 변경합니다.
2. 필요한 경우 현재 PHP에 맞게 `LoadFile`, `LoadModule`, `PHPIniDir` 등의 Apache PHP SAPI 설정을 보정합니다.
3. `php -v`, `php -m`, PHP module DLL 로딩, `httpd -t`, 그리고 작업 전 Apache가 실행 중이었다면 서비스 상태까지 검증합니다.
4. 현재 Apache/PHP 조합을 검증할 수 없으면 방금 변경한 구성요소만 원복합니다.

따라서 Apache와 PHP 사이에 전역적인 업데이트/롤백 역순 제한을 두지 않습니다.

## 백업 / 롤백 정책

업데이트 직전에 생성한 정식 백업은 롤백 지점으로 유지합니다. 롤백 직전 현재 상태를 보호하기 위해 생성하는 Safety 백업은 별도로 분류하며 일반 롤백 대상으로 표시하지 않습니다.

롤백 후보는 현재 설치 버전과 대상 버전이 직접 연결되고, manifest와 실제 파일이 정상인 경우에만 표시됩니다. manifest 경로, 파일 크기, SHA256, MariaDB 논리 백업 존재 여부 등을 확인합니다.

Safety 백업은 현재 정책상 **7일 초과 또는 구성요소별 최근 3개 초과** 시 자동 정리합니다. 기존 schema 1/2 백업과의 호환성도 유지합니다.

자세한 내용은 [백업/롤백 정책](docs/BACKUP_ROLLBACK_POLICY.md)을 참고하세요.

## phpMyAdmin

선택한 XAMPP 루트에 `phpMyAdmin` 디렉터리가 있는 경우에만 관리 UI가 표시됩니다.

업데이트 과정:

- 설치 버전 감지
- 공식 latest metadata 조회
- 공식 `all-languages.zip` 다운로드
- 공식 SHA256 검증
- PHP / DB 호환성 점검
- 전체 폴더 롤백 백업
- `config.inc.php`, `.htaccess`, `upload`, `save` 보존
- staging 구조/버전 검증
- `config.inc.php` PHP 구문 검사
- 폴더 교체 및 실패 시 자동 원복
- 업데이트 과정에서 생성된 유효 백업으로 롤백

실제 XAMPP 설치에서 phpMyAdmin 업데이트, 롤백, 브라우저 로그인, DB 조회까지 검증했습니다.

## 실제 환경 검증

Windows 11 XAMPP 환경에서 다음 경로를 실제로 검증했습니다.

- Apache `2.4.41 → 2.4.68`
- PHP `7.3.11 → 8.5.10`
- PHP `8.2.12 → 8.5.10`
- MariaDB `10.4.8 → 10.4.34`
- MariaDB `10.4.34 → 10.6.28`
- MariaDB `10.6.28 → 12.3.3`
- Apache 설정 snapshot 실제 복원
- phpMyAdmin 업데이트/롤백 후 브라우저 로그인 및 DB 조회
- Apache → PHP → MariaDB → phpMyAdmin 순서 업데이트 후 MariaDB → Apache → PHP → phpMyAdmin 순서 롤백
- 앱 자체 업데이트에서 EXE 교체 및 재시작

이는 실제 검증된 경로이며 모든 사용자 커스텀 XAMPP 구성을 보장한다는 의미는 아닙니다.

## 언어 설정

지원 모드:

- 시스템 기본값
- 한국어
- English

선택값은 다음 위치에 저장됩니다.

```text
%LOCALAPPDATA%\XAMPP-Updater\settings.json
```

언어 변경 시 프로그램이 자동 재시작됩니다. 내부 stage ID는 영어 고정이며 사용자 UI와 대화상자만 현지화됩니다.

## 진단 정보

**진단 정보 내보내기**에서 문제 분석용 ZIP 파일을 만들 수 있습니다.

포함:

- 앱 / OS / 권한 / XAMPP 감지 정보
- 현재 세션 작업 로그
- 영구 실행 로그
- 자체 업데이트 로그(존재하는 경우)

제외:

- 설정 파일 원문
- DB 내용
- 롤백 백업
- 다운로드한 구성요소 패키지
- 인증정보

## 개발 빌드

필요 환경:

- Windows 11
- .NET 8 SDK

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

주 실행 파일은 `XAMPP-Updater.exe`입니다.

GitHub Actions는 restore, build, smoke tests, self-contained publish, EXE 검증과 artifact 업로드를 수행합니다. `release/v*` 릴리스 브랜치에서는 `XAMPP-Updater.exe.sha256`도 생성하고 EXE와 함께 GitHub Release에 게시합니다.

## 문서

- [Roadmap](docs/ROADMAP.md) — 구현 단계와 완료 이력
- [Compatibility](docs/COMPATIBILITY.md) — 지원 XAMPP 구조와 회귀 테스트 기준
- [Backup and Rollback Policy](docs/BACKUP_ROLLBACK_POLICY.md) — 롤백 카탈로그와 보존 정책
- [Apache/PHP Integration](docs/APACHE_PHP_INTEGRATION.md) — Apache/PHP 연동 검증 정책
- [Deferred Hardening](docs/DEFERRED_HARDENING.md) — 차후 선택적으로 강화할 ABI/서명/메타데이터 항목
- [Decisions](docs/DECISIONS.md) — 프로젝트 범위와 기술 결정
