using XamppUpdater.Core.Models;

namespace XamppUpdater.Core.Services;

public static class CompatibilityEvaluator
{
    public static string Evaluate(XamppComponentType type, string? installedVersion, OnlineComponentVersion online, InstallationCompatibilityProfile profile)
    {
        return type switch
        {
            XamppComponentType.Apache => EvaluateApache(installedVersion, online, profile),
            XamppComponentType.Php => EvaluatePhp(installedVersion, online, profile),
            XamppComponentType.MariaDb => EvaluateMariaDb(installedVersion, online, profile),
            _ => "판정 불가"
        };
    }

    private static string EvaluateApache(string? installedVersion, OnlineComponentVersion online, InstallationCompatibilityProfile profile)
    {
        var architecture = FormatArchitecture(profile.ApacheArchitecture);
        var integration = profile.ApachePhpIntegration.IsModuleLoaded ? "PHP Apache 모듈 사용" : "PHP Apache 모듈 미감지";

        if (online.XamppBundledVersion is null)
        {
            return $"환경: {architecture}, {integration}. XAMPP 기준 버전을 확인하지 못했습니다. 실제 후보 패키지를 기준으로 비교 후 보조 업데이트 경로를 판정합니다.";
        }

        if (!SameMajorMinor(installedVersion, online.XamppBundledVersion))
        {
            return $"환경: {architecture}, {integration}. XAMPP 공식 {online.XamppBundledVersion}는 현재 {installedVersion ?? "미상"}와 계열이 다릅니다. 자동 백업과 설정/모듈 diff 후 사용자 확인을 거치는 마이그레이션 방식으로 진행해야 합니다.";
        }

        return $"환경: {architecture}, {integration}. XAMPP 공식 {online.XamppBundledVersion}는 같은 Apache 2.4 계열입니다. Windows 빌드/모듈 ABI를 비교해 자동 가능한 항목은 처리하고 불일치 항목만 확인받는 보조 업데이트가 적합합니다.";
    }

    private static string EvaluatePhp(string? installedVersion, OnlineComponentVersion online, InstallationCompatibilityProfile profile)
    {
        var architecture = FormatArchitecture(profile.PhpArchitecture);
        var threadSafe = profile.Php.ThreadSafe switch
        {
            true => "Thread Safe",
            false => "Non Thread Safe",
            null => "Thread Safety 미상"
        };
        var integration = profile.ApachePhpIntegration.IsModuleLoaded ? "Apache module" : "Apache module 미감지";
        var compiler = profile.Php.Compiler ?? "Compiler 미상";

        if (profile.ApachePhpIntegration.IsModuleLoaded && profile.Php.ThreadSafe == false)
        {
            return $"환경: {architecture}, {threadSafe}, {compiler}, {integration}. Apache 모듈 구성과 NTS가 충돌하므로 현재 구성 자체를 먼저 확인해야 합니다. 설정 비교와 사용자 확인을 포함한 검토 후 진행 대상으로 처리합니다.";
        }

        if (online.XamppBundledVersion is null)
        {
            return $"환경: {architecture}, {threadSafe}, {compiler}, {integration}. XAMPP 기준 버전을 확인하지 못했습니다. 실제 Windows 후보 패키지를 기준으로 보조 업데이트 경로를 판정합니다.";
        }

        if (!SameMajor(installedVersion, online.XamppBundledVersion))
        {
            return $"환경: {architecture}, {threadSafe}, {compiler}, {integration}. PHP {installedVersion ?? "미상"} → {online.XamppBundledVersion}는 메이저 변경입니다. 확장 ABI, php.ini, Apache SAPI 설정을 비교해 자동 변환 가능한 항목은 적용하고 충돌 항목만 사용자 선택을 받는 마이그레이션 방식으로 진행합니다.";
        }

        return $"환경: {architecture}, {threadSafe}, {compiler}, {integration}. 같은 PHP 메이저 계열입니다. Extension Build/Apache 모듈 DLL과 설정 차이를 비교한 뒤 보조 또는 자동 업데이트로 진행할 수 있습니다.";
    }

    private static string EvaluateMariaDb(string? installedVersion, OnlineComponentVersion online, InstallationCompatibilityProfile profile)
    {
        var architecture = FormatArchitecture(profile.MariaDbArchitecture);
        var series = profile.MariaDbSeries ?? "계열 미상";

        if (online.XamppBundledVersion is null)
        {
            return $"환경: {architecture}, MariaDB {series}. XAMPP 기준 버전을 확인하지 못했습니다. 공식 후보와 업그레이드 경로를 확인해 보조 업데이트로 판정합니다.";
        }

        if (!SameMajorMinor(installedVersion, online.XamppBundledVersion))
        {
            return $"환경: {architecture}, MariaDB {series}. XAMPP 공식 {online.XamppBundledVersion}와 계열이 다릅니다. 데이터/설정 전체 백업 후 공식 중간 업그레이드 경로를 따라 단계별로 진행하고 각 단계에서 검증하는 마이그레이션이 필요합니다.";
        }

        return $"환경: {architecture}, MariaDB {series}. XAMPP 공식 {online.XamppBundledVersion}는 같은 {series} 계열입니다. 백업, 바이너리 교체, mariadb-upgrade, 상태 검증을 순차 자동화하는 보조 업데이트 후보입니다.";
    }

    private static bool SameMajor(string? left, string? right)
    {
        return Version.TryParse(left, out var l) && Version.TryParse(right, out var r) && l.Major == r.Major;
    }

    private static bool SameMajorMinor(string? left, string? right)
    {
        return Version.TryParse(left, out var l) && Version.TryParse(right, out var r) && l.Major == r.Major && l.Minor == r.Minor;
    }

    public static string FormatArchitecture(BinaryArchitecture architecture)
    {
        return architecture switch
        {
            BinaryArchitecture.X86 => "x86",
            BinaryArchitecture.X64 => "x64",
            BinaryArchitecture.Arm64 => "ARM64",
            _ => "아키텍처 미상"
        };
    }
}
