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
            return $"환경: {architecture}, {integration}. XAMPP 기준 버전을 확인하지 못해 자동 적용을 보류합니다.";
        }

        if (!SameMajorMinor(installedVersion, online.XamppBundledVersion))
        {
            return $"환경: {architecture}, {integration}. XAMPP 공식 {online.XamppBundledVersion}는 현재 {installedVersion ?? "미상"}와 계열이 달라 수동 검토가 필요합니다.";
        }

        return $"환경: {architecture}, {integration}. XAMPP 공식 {online.XamppBundledVersion}는 같은 Apache 2.4 계열이지만 Windows 빌드 공급원/모듈 ABI 확인 전 자동 적용하지 않습니다.";
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
            return $"환경: {architecture}, {threadSafe}, {compiler}, {integration}. Apache 모듈 구성인데 NTS로 감지되어 자동 업데이트를 차단해야 합니다.";
        }

        if (online.XamppBundledVersion is null)
        {
            return $"환경: {architecture}, {threadSafe}, {compiler}, {integration}. XAMPP 기준 버전을 확인하지 못해 자동 적용을 보류합니다.";
        }

        if (!SameMajor(installedVersion, online.XamppBundledVersion))
        {
            return $"환경: {architecture}, {threadSafe}, {compiler}, {integration}. PHP {installedVersion ?? "미상"} → {online.XamppBundledVersion}는 메이저 변경입니다. 확장 ABI와 설정 마이그레이션 검증이 필요해 자동 적용 대상이 아닙니다.";
        }

        return $"환경: {architecture}, {threadSafe}, {compiler}, {integration}. 같은 PHP 메이저 계열이지만 Extension Build/Apache 모듈 DLL 호환성 확인 후 적용해야 합니다.";
    }

    private static string EvaluateMariaDb(string? installedVersion, OnlineComponentVersion online, InstallationCompatibilityProfile profile)
    {
        var architecture = FormatArchitecture(profile.MariaDbArchitecture);
        var series = profile.MariaDbSeries ?? "계열 미상";

        if (online.XamppBundledVersion is null)
        {
            return $"환경: {architecture}, MariaDB {series}. XAMPP 기준 버전을 확인하지 못해 자동 적용을 보류합니다.";
        }

        if (!SameMajorMinor(installedVersion, online.XamppBundledVersion))
        {
            return $"환경: {architecture}, MariaDB {series}. XAMPP 공식 {online.XamppBundledVersion}와 계열이 달라 데이터 디렉터리 직접 교체를 금지합니다.";
        }

        return $"환경: {architecture}, MariaDB {series}. XAMPP 공식 {online.XamppBundledVersion}는 같은 {series} 계열입니다. 패치 업데이트 후보지만 백업과 mariadb-upgrade 절차 검증이 선행되어야 합니다.";
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
