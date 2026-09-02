using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public static class UpdateTargetPlanner
{
    public static UpdateTargetCatalog BuildCatalog(
        XamppInstallation installation,
        OnlineVersionCatalog online,
        CandidatePackageCatalog candidates,
        SelectableVersionCatalog? selectableVersions = null)
    {
        return new UpdateTargetCatalog(
            BuildOptions(XamppComponentType.Apache, installation, online, candidates, selectableVersions),
            BuildOptions(XamppComponentType.Php, installation, online, candidates, selectableVersions),
            BuildOptions(XamppComponentType.MariaDb, installation, online, candidates, selectableVersions));
    }

    public static UpdatePlan BuildPlan(
        XamppComponentType type,
        string currentVersion,
        UpdateTargetOption target,
        InstallationCompatibilityProfile profile)
    {
        if (!Version.TryParse(currentVersion, out var current) || !Version.TryParse(target.Version, out var next))
        {
            return new UpdatePlan(type, currentVersion, target.Version, CandidateCompatibilityStatus.ManualReview,
                new[] { new UpdatePlanStep(UpdatePlanStepKind.UserConfirmation, "버전 확인", "현재/대상 버전을 정확히 판정할 수 없어 사용자가 확인해야 합니다.") },
                "버전 정보를 먼저 확인해야 합니다.");
        }

        if (next <= current)
        {
            return new UpdatePlan(type, currentVersion, target.Version, CandidateCompatibilityStatus.ManualReview,
                new[] { new UpdatePlanStep(UpdatePlanStepKind.UserConfirmation, "다운그레이드 확인", "대상 버전이 현재 버전보다 낮거나 같습니다. 일반 업데이트 경로로 처리하지 않습니다.") },
                "업데이트 대상이 현재 버전보다 높지 않습니다.");
        }

        return type switch
        {
            XamppComponentType.Apache => BuildApachePlan(currentVersion, target, profile),
            XamppComponentType.Php => BuildPhpPlan(currentVersion, target, profile),
            XamppComponentType.MariaDb => BuildMariaDbPlan(currentVersion, target, profile),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    private static IReadOnlyList<UpdateTargetOption> BuildOptions(
        XamppComponentType type,
        XamppInstallation installation,
        OnlineVersionCatalog online,
        CandidatePackageCatalog candidates,
        SelectableVersionCatalog? selectableVersions)
    {
        var installedComponent = installation.Components.First(item => item.Type == type);
        if (!installedComponent.IsInstalled || string.IsNullOrWhiteSpace(installedComponent.Version))
        {
            return Array.Empty<UpdateTargetOption>();
        }

        var installed = installedComponent.Version;
        var onlineVersion = online.Components.First(item => item.Type == type);
        var candidate = candidates.Candidates.First(item => item.Type == type);
        var options = new Dictionary<string, UpdateTargetOption>(StringComparer.OrdinalIgnoreCase);

        Add(installed, "현재 설치", UpdateTargetSource.Installed, false, true);
        Add(candidate.Version, "현재 계열 추천", UpdateTargetSource.SameSeriesCandidate, false,
            candidate.DownloadUrl is not null, candidate.DownloadUrl, candidate.FileName);
        Add(onlineVersion.XamppBundledVersion, "XAMPP 공식 기준", UpdateTargetSource.XamppBundle, false, false);
        Add(onlineVersion.UpstreamLatestVersion, "최신", UpdateTargetSource.UpstreamLatest, true,
            candidate.Version == onlineVersion.UpstreamLatestVersion && candidate.DownloadUrl is not null,
            candidate.Version == onlineVersion.UpstreamLatestVersion ? candidate.DownloadUrl : null,
            candidate.Version == onlineVersion.UpstreamLatestVersion ? candidate.FileName : null);

        if (selectableVersions is not null)
        {
            foreach (var entry in selectableVersions.Entries.Where(item => item.Type == type))
            {
                Add(entry.Version, entry.SourceLabel, UpdateTargetSource.OfficialArchive, false,
                    entry.PackageUrl is not null, entry.PackageUrl, entry.PackageFileName, entry.IsEol);
            }
        }

        return options.Values
            .OrderByDescending(item => Version.TryParse(item.Version, out var parsed) ? parsed : new Version(0, 0))
            .ToArray();

        void Add(string? version, string label, UpdateTargetSource source, bool isLatest, bool packageResolved,
            string? packageUrl = null, string? packageFileName = null, bool isEol = false)
        {
            if (string.IsNullOrWhiteSpace(version)) return;

            if (options.TryGetValue(version, out var existing))
            {
                options[version] = existing with
                {
                    Label = existing.IsLatest || !isLatest ? existing.Label : $"{existing.Label}, 최신",
                    IsLatest = existing.IsLatest || isLatest,
                    PackageResolved = existing.PackageResolved || packageResolved,
                    PackageUrl = existing.PackageUrl ?? packageUrl,
                    PackageFileName = existing.PackageFileName ?? packageFileName,
                    IsEol = existing.IsEol || isEol
                };
                return;
            }

            options[version] = new UpdateTargetOption(type, version, label, source, isLatest, packageResolved,
                packageUrl, packageFileName, isEol);
        }
    }

    private static UpdatePlan BuildApachePlan(string currentVersion, UpdateTargetOption target, InstallationCompatibilityProfile profile)
    {
        var steps = new List<UpdatePlanStep>
        {
            Auto("백업", "apache 폴더와 conf 설정을 업데이트 전 백업합니다."),
            target.PackageResolved
                ? Auto("패키지 확인", "선택 버전의 Windows Apache 패키지와 VC++ 런타임 조건을 확인합니다.")
                : Assist("패키지 확인", "자동으로 Windows 바이너리를 찾지 못하면 신뢰 가능한 공급원에서 선택 버전에 맞는 패키지를 확인합니다."),
            Auto("설정 비교", "기존 conf와 새 기본 conf를 비교하고 사용자 변경 설정을 분리합니다."),
            Assist("모듈 호환성", "기존 modules 및 PHP Apache SAPI와 새 httpd의 모듈 ABI를 비교합니다."),
            Auto("설정 이식", "자동으로 유지 가능한 VirtualHost, SSL, 포트 및 include 설정을 새 구성에 반영합니다."),
            Confirm("충돌 확인", "삭제되거나 의미가 바뀐 지시어 및 외부 Apache 모듈만 사용자에게 확인받습니다."),
            Auto("검증", "httpd -t와 서비스 시작 검증 후 실패 시 기존 백업으로 되돌립니다.")
        };
        var summary = profile.ApachePhpIntegration.IsModuleLoaded
            ? $"Apache {currentVersion} → {target.Version}: PHP Apache 모듈까지 포함해 비교·이식하는 보조 업데이트 경로입니다."
            : $"Apache {currentVersion} → {target.Version}: 설정/모듈 비교 후 자동 교체를 준비합니다.";
        return new UpdatePlan(XamppComponentType.Apache, currentVersion, target.Version, CandidateCompatibilityStatus.Assisted, steps, summary);
    }

    private static UpdatePlan BuildPhpPlan(string currentVersion, UpdateTargetOption target, InstallationCompatibilityProfile profile)
    {
        Version.TryParse(currentVersion, out var current);
        Version.TryParse(target.Version, out var next);
        var sameSeries = current is not null && next is not null && current.Major == next.Major && current.Minor == next.Minor;
        var sameMajor = current is not null && next is not null && current.Major == next.Major;
        var steps = new List<UpdatePlanStep>
        {
            Auto("백업", "php 폴더, php.ini, Apache의 PHP 연동 설정을 백업합니다."),
            target.PackageResolved
                ? Auto("대상 패키지 준비", $"선택한 PHP {target.Version} 공식 Windows 패키지를 사용합니다.")
                : Assist("대상 패키지 확인", $"PHP {target.Version} 공식 Windows 패키지를 찾아 아키텍처/TS/SAPI를 검사합니다."),
            Auto("확장 인벤토리", "현재 활성 extension과 추가 DLL을 목록화하고 대상 버전의 기본/번들 확장과 비교합니다."),
            Auto("php.ini 비교", "현재 php.ini와 대상 버전의 기본 설정을 키 단위로 비교합니다."),
            Assist("설정 마이그레이션", sameSeries
                ? "동일 계열 패치 업데이트이므로 기존 설정을 우선 유지하고 변경된 기본값만 검증합니다."
                : "버전 간 제거/변경된 지시어를 분류하고 자동 변환 가능한 설정을 새 형식으로 이식합니다."),
            Assist("확장 호환성", sameSeries
                ? "Extension Build와 DLL 의존성을 확인해 기존 확장을 최대한 유지합니다."
                : "ABI가 달라진 확장은 대상 PHP용 DLL로 교체하거나 비활성화 후보로 분류합니다."),
            Auto("Apache 연동 갱신", "php*apache2_4.dll, LoadModule, PHPIniDir를 대상 버전에 맞게 갱신합니다."),
            Confirm("사용자 확인", sameMajor
                ? "호환 DLL을 자동으로 찾지 못한 서드파티 확장과 설정 충돌만 확인받습니다."
                : "메이저 변경에서 제거된 기능/확장 및 애플리케이션 영향 가능 항목만 확인받습니다."),
            Auto("검증", "php -v, php -m, php --ini, httpd -t를 실행하고 Apache 시작까지 확인합니다. 실패하면 롤백합니다.")
        };
        var summary = sameSeries
            ? $"PHP {currentVersion} → {target.Version}: 동일 계열 패치 업데이트로 대부분 자동 처리 가능합니다."
            : $"PHP {currentVersion} → {target.Version}: 메이저/마이너 변경이지만 설정·확장·Apache 연동을 비교하며 보조 업데이트로 진행합니다.";
        return new UpdatePlan(XamppComponentType.Php, currentVersion, target.Version, CandidateCompatibilityStatus.Assisted, steps, summary);
    }

    private static UpdatePlan BuildMariaDbPlan(string currentVersion, UpdateTargetOption target, InstallationCompatibilityProfile profile)
    {
        Version.TryParse(currentVersion, out var current);
        Version.TryParse(target.Version, out var next);
        var sameSeries = current is not null && next is not null && current.Major == next.Major && current.Minor == next.Minor;
        var steps = new List<UpdatePlanStep>
        {
            Auto("전체 백업", "mysql 설정과 data 디렉터리를 백업하고 필요 시 논리 백업도 생성합니다."),
            Auto("업그레이드 경로 계산", sameSeries
                ? "동일 major.minor 패치 경로로 직접 업데이트합니다."
                : "현재 버전에서 대상 버전까지 필요한 중간 MariaDB 계열과 순서를 계산합니다."),
            target.PackageResolved
                ? Assist("단계별 패키지 준비", "선택 버전 및 필요한 중간 버전의 공식 Windows 패키지를 순서대로 준비합니다.")
                : Assist("패키지 확인", "각 단계에 필요한 공식 Windows 패키지를 자동으로 확인해 준비합니다."),
            Assist("단계별 바이너리 교체", "각 단계에서 설정/서비스 경로를 유지하며 바이너리를 순차 교체합니다."),
            Auto("업그레이드 도구", "각 단계에서 지원되는 mariadb-upgrade/mysql_upgrade 계열 도구를 실행합니다."),
            Auto("상태 검증", "서버 기동, 시스템 테이블, 사용자 DB 접근과 오류 로그를 확인합니다."),
            Confirm("문제 항목 확인", "스토리지 엔진/옵션 제거 등 자동 변환할 수 없는 변경만 사용자 확인 대상으로 남깁니다."),
            Auto("롤백 지점", "각 단계별 백업 지점을 유지해 실패 시 직전 정상 단계로 되돌립니다.")
        };
        var summary = sameSeries
            ? $"MariaDB {currentVersion} → {target.Version}: 동일 계열 패치 업데이트 경로입니다."
            : $"MariaDB {currentVersion} → {target.Version}: 필요한 중간 버전을 포함한 단계적 보조 업데이트 경로를 계산합니다.";
        return new UpdatePlan(XamppComponentType.MariaDb, currentVersion, target.Version, CandidateCompatibilityStatus.Assisted, steps, summary);
    }

    private static UpdatePlanStep Auto(string title, string detail) => new(UpdatePlanStepKind.Automatic, title, detail);
    private static UpdatePlanStep Assist(string title, string detail) => new(UpdatePlanStepKind.Assisted, title, detail);
    private static UpdatePlanStep Confirm(string title, string detail) => new(UpdatePlanStepKind.UserConfirmation, title, detail);
}
