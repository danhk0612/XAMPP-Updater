# XAMPP Updater

Windows 11용 XAMPP 구성요소 업데이트 도구입니다.

대상은 **Apache / PHP / MariaDB** 세 가지로 제한합니다. XAMPP 전체를 재설치하지 않고 현재 설치를 감지한 뒤, 구성요소별 현재 버전 확인과 향후 안전한 버전 업데이트·설정 백업/복원/비교를 제공하는 GUI 도구를 목표로 합니다.

## 현재 개발 단계

Phase 2 — Version Catalog & Compatibility 진행 중입니다.

현재 구현된 기능:

- XAMPP 설치 경로 자동 감지 / 직접 지정
- Apache / PHP / MariaDB 현재 버전 감지
- Apache / MariaDB 실제 Windows 서비스명 감지
- Apache / PHP / MariaDB upstream 최신 버전 조회
- Apache Friends의 XAMPP 공식 번들 기준 버전 조회
- 실행 파일 PE 아키텍처(x86/x64/ARM64) 감지
- PHP Thread Safe / compiler / Extension Build / PHP API 감지
- Apache 설정의 PHP `LoadModule` 연동 방식 감지
- 현재 환경과 XAMPP 공식 버전의 1차 호환성 판정

Phase 2는 **읽기 전용**입니다. 아직 서비스 제어, 패키지 설치, 파일 교체, 설정 변경을 실행하지 않습니다.

## 빌드

Windows 11과 .NET 8 SDK 기준입니다.

```powershell
dotnet restore XamppUpdater.sln
dotnet build XamppUpdater.sln -c Release
```

실행 프로젝트:

```powershell
dotnet run --project .\src\XamppUpdater.App\XamppUpdater.App.csproj
```

## 문서

- `docs/ROADMAP.md` — 전체 단계와 Phase별 범위
- `docs/DECISIONS.md` — 확정된 범위/기술 결정
