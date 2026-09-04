# Deferred Hardening Work

기준일: 2026-09-04

이 문서는 현재 안정 동작을 유지하기 위해 **당장 구현하지 않고 후속 후보로 보존하는 작업**을 정리한다.
현재 릴리스 경로를 바꾸는 작업이 아니며, 아래 항목은 실제 필요 사례가 생기기 전까지 실행 차단 조건으로 승격하지 않는다.

## 1. Apache module / PHP extension ABI 정적 메타데이터 보강

현재 이미 사용하는 판정:

- PE 아키텍처
- PHP TS/NTS
- compiler/runtime 단서
- PE import dependency
- Windows loader probe
- PHP `php -v` / `php -m`
- Apache `httpd -t`
- 실제 서비스 재기동 결과

추가 후보:

- PE version resource의 ProductVersion/FileVersion
- module/compiler 문자열
- PHP module API/build ID를 읽을 수 있는 안전한 정적 단서
- Apache 모듈의 알려진 배포 메타데이터
- 외부 extension 배포 파일명 규칙과 명시적 지원 PHP 범위

### 적용 원칙

처음에는 `Compatible / Suspicious / Unknown` 같은 **진단 정보**로만 사용한다.

새 정적 메타데이터만으로 기존에 실제 loader/runtime 검증을 통과하는 모듈을 차단하지 않는다.
충분한 실제 사례가 쌓여 명백한 불일치 조건이 확인된 경우에만 hard block 후보로 올린다.

## 2. PGP/GPG 서명 검증

공급처가 안정적으로 서명 파일과 공식 공개키를 제공하는 패키지에 한해 추가할 수 있다.

권장 정책:

- 서명 제공 + 신뢰 가능한 공식 키 + 검증 성공 → `Verified`
- 서명 미제공 → 기존 HTTPS/SHA256/패키지 구조/실행 검증 계속
- 서명 제공됐지만 검증 실패 → hard block
- 키 교체/만료/공급처 구조 변경 → 무조건 실패 처리하지 말고 공급처 정책 재확인

서명 미제공 자체를 업데이트 불가 조건으로 만들지 않는다.

## 3. 공급처 메타데이터 신뢰도 강화

후속 후보:

- 공식 checksum 목록의 출처와 버전 연결 강화
- release manifest 또는 공식 API가 생기면 HTML 정규식 파싱을 우선 대체
- 공급처 URL/파일명/컴파일러 표기 규칙 변경 감지
- 키/체크섬/패키지 URL의 출처를 진단 정보에 기록

### 적용 원칙

새 메타데이터는 기존 실제 검증을 보완한다.
정적 메타데이터가 실제 `php -m`, `httpd -t`, MariaDB 기동/upgrade 결과보다 높은 신뢰도를 가진다고 가정하지 않는다.

## 다시 착수할 조건

다음 중 하나가 생기면 우선순위를 재평가한다.

- 실제 사용자 환경에서 현재 검사로 잡지 못한 ABI 충돌 사례
- 공급처가 안정적인 공식 서명/manifest 제공을 시작
- 패키지 공급망 검증 수준을 높여야 하는 배포 요구
- HTML 파싱 변경으로 버전/패키지 조회가 반복적으로 깨짐
