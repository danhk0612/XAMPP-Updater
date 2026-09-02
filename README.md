# XAMPP Updater

Windows 11용 XAMPP 구성요소 업데이트 도구입니다.

대상은 **Apache / PHP / MariaDB** 세 가지로 제한합니다. XAMPP 전체를 재설치하지 않고 현재 설치를 감지한 뒤, 구성요소별 현재 버전 확인과 안전한 버전 업데이트·설정 백업/복원/비교를 제공하는 GUI 도구를 목표로 합니다.

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
- 현재 환경에 맞는 실제 Windows 패키지 후보 탐색
- 후보별 `자동 가능 / 보조 업데이트 / 검토 후 진행 / 후보 없음` 판정
- 현재 버전보다 높은 공식 릴리스 목록을 업데이트 버전 선택지로 제공
- PHP Windows 공식 archive의 버전별 ZIP URL 연결
- MariaDB 공식 DLM의 버전별 winx64 패키지 페이지 연결
- Apache ASF 2.4 릴리스 목록 제공
- 현재 → 선택 버전 업데이트 작업 계획 자동 계산
- 자동 / 보조 / 사용자 확인 단계 분류
- 자동 패키지 탐색이 안 되는 경우 ZIP 직접 지정

완전 자동 업데이트가 어려운 경우에도 기능을 단순 차단하지 않습니다. 자동 백업, 기존/신규 설정 비교, 패키지 직접 지정, 충돌 항목 선택, 적용 후 검증과 롤백을 조합한 **보조 업데이트 흐름**을 목표로 합니다.

PHP의 경우 현재 7.x 설치에서도 8.x 최신 버전을 선택할 수 있으며, php.ini/확장 ABI/Apache SAPI 변경을 업데이트 계획에 포함합니다. MariaDB도 다른 major.minor 계열을 목표로 선택할 수 있고 필요한 중간 업그레이드 단계를 계산하는 방향으로 처리합니다.

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
