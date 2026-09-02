# XAMPP Updater

Windows 11용 XAMPP 구성요소 업데이트 도구입니다.

대상은 **Apache / PHP / MariaDB** 세 가지로 제한합니다. XAMPP 전체를 재설치하지 않고 현재 설치를 감지한 뒤, 구성요소별 현재 버전 확인과 향후 안전한 버전 업데이트·설정 백업/복원/비교를 제공하는 GUI 도구를 목표로 합니다.

## Phase 1

현재 `phase-1-foundation` 브랜치에서 첫 단계 구현을 진행합니다.

- .NET 8 WPF GUI
- XAMPP 설치 경로 자동 감지
- 설치 경로 직접 입력/폴더 선택
- Apache / PHP / MariaDB 로컬 버전 확인
- Apache / MariaDB Windows 서비스명 감지
- 읽기 전용 동작

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
