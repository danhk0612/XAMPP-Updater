using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace XamppUpdater.App;

internal static class ExtendedLocalization
{
    private static readonly DependencyProperty WatchRegisteredProperty =
        DependencyProperty.RegisterAttached(
            "WatchRegistered",
            typeof(bool),
            typeof(ExtendedLocalization),
            new PropertyMetadata(false));

    private static readonly Dictionary<string, string> PlanTerms = new(StringComparer.Ordinal)
    {
        ["백업"] = "Backup",
        ["전체 백업"] = "Full backup",
        ["패키지 확인"] = "Package check",
        ["대상 패키지 준비"] = "Target package preparation",
        ["대상 패키지 확인"] = "Target package check",
        ["설정 비교"] = "Configuration comparison",
        ["모듈 호환성"] = "Module compatibility",
        ["설정 이식"] = "Configuration migration",
        ["충돌 확인"] = "Conflict review",
        ["검증"] = "Validation",
        ["확장 인벤토리"] = "Extension inventory",
        ["php.ini 비교"] = "php.ini comparison",
        ["설정 마이그레이션"] = "Configuration migration",
        ["확장 호환성"] = "Extension compatibility",
        ["Apache 연동 갱신"] = "Apache integration update",
        ["사용자 확인"] = "User review",
        ["업그레이드 경로 계산"] = "Upgrade path calculation",
        ["바이너리 교체"] = "Binary replacement",
        ["업그레이드 도구"] = "Upgrade tool",
        ["상태 검증"] = "Status validation",
        ["문제 항목 확인"] = "Issue review",
        ["롤백"] = "Rollback",
        ["버전 확인"] = "Version check",
        ["다운그레이드 차단"] = "Downgrade blocked"
    };

    private static readonly (string Korean, string English)[] PlanPhraseReplacements =
    {
        ("PHP Apache 모듈까지 포함해 비교·이식하는 보조 업데이트 경로입니다.", "uses an assisted update path that compares and migrates the PHP Apache module as well."),
        ("설정/모듈 비교 후 자동 교체를 준비합니다.", "prepares an automatic replacement after comparing configuration and modules."),
        ("동일 계열 패치 업데이트로 대부분 자동 처리 가능합니다.", "is a same-series patch update and can be handled mostly automatically."),
        ("메이저/마이너 변경이지만 설정·확장·Apache 연동을 비교하며 보조 업데이트로 진행합니다.", "is a major/minor change and uses an assisted update that compares configuration, extensions, and Apache integration."),
        ("동일 계열 패치 업데이트 경로입니다.", "uses a same-series patch update path."),
        ("직접 major 업그레이드를 사전 검증하고 실패 시 원본으로 롤백합니다.", "pre-validates the direct major upgrade and restores the original installation if it fails."),
        ("현재 버전을 확인할 수 없습니다.", "The current version could not be determined."),
        ("공식 패키지 위치 확인됨", "Official package location resolved"),
        ("업데이트 준비 단계에서 선택 버전에 맞는 Windows 패키지를 자동 탐색합니다.", "The preparation stage will automatically locate the Windows package for the selected version.")
    };

    private static readonly (string Korean, string English)[] Replacements =
    {
        ("PHP Apache 모듈 사용", "PHP Apache module in use"),
        ("PHP Apache 모듈 미감지", "PHP Apache module not detected"),
        ("Apache 모듈 구성과 NTS가 충돌하므로 현재 구성 자체를 먼저 확인해야 합니다.", "The Apache module configuration conflicts with NTS, so the current configuration must be reviewed first."),
        ("설정 비교와 사용자 확인을 포함한 검토 후 진행 대상으로 처리합니다.", "Proceed only after a review that includes configuration comparison and user confirmation."),
        ("XAMPP 기준 버전을 확인하지 못했습니다.", "The XAMPP reference version could not be determined."),
        ("실제 후보 패키지를 기준으로 비교 후 보조 업데이트 경로를 판정합니다.", "The assisted update path will be determined after comparing against the actual candidate package."),
        ("실제 Windows 후보 패키지를 기준으로 보조 업데이트 경로를 판정합니다.", "The assisted update path will be determined against the actual Windows candidate package."),
        ("공식 후보와 업그레이드 경로를 확인해 보조 업데이트로 판정합니다.", "The official candidate and upgrade path will be checked to determine an assisted update."),
        ("자동 백업과 설정/모듈 diff 후 사용자 확인을 거치는 마이그레이션 방식으로 진행해야 합니다.", "Use a migration workflow with automatic backup, configuration/module diff, and user confirmation."),
        ("Windows 빌드/모듈 ABI를 비교해 자동 가능한 항목은 처리하고 불일치 항목만 확인받는 보조 업데이트가 적합합니다.", "An assisted update is appropriate: compare the Windows build/module ABI, handle compatible items automatically, and ask only about mismatches."),
        ("확장 ABI, php.ini, Apache SAPI 설정을 비교해 자동 변환 가능한 항목은 적용하고 충돌 항목만 사용자 선택을 받는 마이그레이션 방식으로 진행합니다.", "Use a migration workflow that compares extension ABI, php.ini, and Apache SAPI settings, applies safe conversions automatically, and asks only about conflicts."),
        ("Extension Build/Apache 모듈 DLL과 설정 차이를 비교한 뒤 보조 또는 자동 업데이트로 진행할 수 있습니다.", "After comparing the Extension Build, Apache module DLL, and configuration differences, the update can proceed automatically or in assisted mode."),
        ("데이터/설정 전체 백업 후 공식 중간 업그레이드 경로를 따라 단계별로 진행하고 각 단계에서 검증하는 마이그레이션이 필요합니다.", "A migration is required: fully back up data/configuration, follow the official intermediate upgrade path, and validate each step."),
        ("백업, 바이너리 교체, mariadb-upgrade, 상태 검증을 순차 자동화하는 보조 업데이트로 진행할 수 있습니다.", "An assisted update can automate backup, binary replacement, mariadb-upgrade, and status validation in sequence."),
        ("기존 config.inc.php와 .htaccess, upload/save 폴더를 보존하고 전체 롤백 백업 후 폴더를 교체합니다.", "The existing config.inc.php, .htaccess, and upload/save folders will be preserved, then the folder will be replaced after a full rollback backup."),
        ("현재 phpMyAdmin", "Current phpMyAdmin"),
        ("은 최신 안정판입니다.", " is the latest stable release."),
        ("설치 버전 ", "Installed version "),
        ("온라인 확인 완료:", "Online check completed:"),
        ("계열별 선택 버전", "series-specific selectable versions"),
        ("개", " items"),
        ("는 같은 Apache 2.4 계열입니다.", " is in the same Apache 2.4 series."),
        ("같은 PHP 메이저 계열입니다.", "This is in the same PHP major series."),
        ("는 메이저 변경입니다.", " is a major-version change."),
        ("는 현재 ", " compared with current "),
        ("는 같은 ", " is in the same "),
        ("와 계열이 다릅니다.", " is from a different release series."),
        ("계열의 패치 업데이트 후보입니다.", " series patch-update candidate."),
        ("공식 버전 메타데이터의 PHP 권장 범위", "the PHP range recommended by the official release metadata"),
        ("보다 새 PHP", "a newer PHP"),
        ("를 사용 중입니다. 업데이트는 허용하지만 실제 phpMyAdmin 동작 확인을 권장합니다.", " is in use. The update is allowed, but verifying phpMyAdmin operation is recommended."),
        ("현재 PHP/DB 버전에서 최신 안정판 업데이트를 진행할 수 있습니다.", "The latest stable release can be installed with the current PHP/DB versions."),
        ("업데이트 가능하지만 호환성 주의사항이 있습니다.", "The update can proceed, but there are compatibility warnings."),
        ("현재 XAMPP 구성에서는 최신 phpMyAdmin 업데이트를 진행할 수 없습니다.", "The latest phpMyAdmin update cannot be installed with the current XAMPP configuration."),
        ("PHP 버전을 확인하지 못해 phpMyAdmin 호환성을 보장할 수 없습니다.", "The PHP version could not be determined, so phpMyAdmin compatibility cannot be guaranteed."),
        ("이상 필요합니다. 현재 PHP:", " or later is required. Current PHP:"),
        ("이상 필요합니다. 현재 DB:", " or later is required. Current DB:"),
        ("파일 자체가 한쪽 snapshot에만 존재하므로 파일 단위 복원을 사용하세요.", "The file exists in only one snapshot. Use file-level restore instead."),
        ("같은 설정 항목이 여러 번 등장하여 자동 병합하지 않습니다.", "The same configuration entry appears multiple times, so it cannot be merged automatically."),
        ("항목 추가/삭제는 위치와 주석 의미가 달라질 수 있어 자동 병합하지 않습니다.", "Added/removed entries are not merged automatically because placement and comment semantics may differ."),
        ("구성요소가 다른 snapshot은 항목 비교할 수 없습니다.", "Configuration entries cannot be compared across snapshots from different components."),
        ("자동 병합할 수 없는 설정 항목입니다:", "This configuration entry cannot be merged automatically:"),
        ("[문제 snapshot]", "[Problem snapshots]"),
        ("검증 성공 파일:", "Verified files:"),
        ("선택 snapshot:", "Selected snapshots:"),
        ("정상:", "Valid:"),
        ("문제:", "Issues:"),
        ("정상", "Valid"),
        ("문제", "Issues"),
        ("XAMPP 공식", "XAMPP official"),
        ("환경:", "Environment:"),
        ("서비스:", "Service:"),
        ("경로:", "Path:"),
        ("계열 미상", "unknown series"),
        ("Thread Safety 미상", "Thread Safety unknown"),
        ("Compiler 미상", "Compiler unknown"),
        ("아키텍처 미상", "unknown architecture"),
        ("미감지", "not detected"),
        ("미등록", "not registered"),
        ("미상", "unknown")
    };

    public static string TranslateText(string? text)
    {
        if (string.IsNullOrEmpty(text) || !LocalizationService.IsEnglish)
            return text ?? string.Empty;

        var structured = TranslateStructuredRuntimeText(text);
        if (!ReferenceEquals(structured, text) && !string.Equals(structured, text, StringComparison.Ordinal))
            return structured;

        var translated = UserMessageLocalization.PreTranslate(text);
        foreach (var (korean, english) in Replacements)
            translated = translated.Replace(korean, english, StringComparison.Ordinal);

        translated = LocalizationCatalog.TranslateUserText(translated);

        foreach (var (korean, english) in Replacements)
            translated = translated.Replace(korean, english, StringComparison.Ordinal);
        return translated;
    }

    private static string TranslateStructuredRuntimeText(string text)
    {
        var planMatch = Regex.Match(
            text,
            @"^업데이트 경로: (?<summary>[^\r\n]+)\r?\n자동 (?<automatic>\d+) / 보조 (?<assisted>\d+) / 확인 (?<confirm>\d+)\r?\n(?<steps>[^\r\n]+)(?:\r?\n패키지: (?<package>.+))?$",
            RegexOptions.CultureInvariant);
        if (planMatch.Success)
        {
            var summary = TranslatePlanPhrase(planMatch.Groups["summary"].Value);
            var steps = string.Join(
                " → ",
                planMatch.Groups["steps"].Value.Split(" → ", StringSplitOptions.None)
                    .Select(step => PlanTerms.TryGetValue(step, out var translatedStep) ? translatedStep : TranslatePlanPhrase(step)));
            var result =
                $"Update path: {summary}\nAutomatic {planMatch.Groups["automatic"].Value} / Assisted {planMatch.Groups["assisted"].Value} / Review {planMatch.Groups["confirm"].Value}\n{steps}";
            if (planMatch.Groups["package"].Success)
                result += "\nPackage: " + TranslatePlanPhrase(planMatch.Groups["package"].Value);
            return result;
        }

        var onlineMatch = Regex.Match(
            text,
            @"^온라인 확인 완료: (?<time>.+?) / 계열별 선택 버전 (?<count>\d+)개$",
            RegexOptions.CultureInvariant);
        if (onlineMatch.Success)
            return $"Online check completed: {onlineMatch.Groups["time"].Value} / {onlineMatch.Groups["count"].Value} series-specific selectable versions";

        var phpMyAdminPlan = Regex.Match(
            text,
            @"^phpMyAdmin (?<current>[^ ]+) → (?<target>[^. ]+)\. 기존 config\.inc\.php와 \.htaccess, upload/save 폴더를 보존하고 전체 롤백 백업 후 폴더를 교체합니다\.$",
            RegexOptions.CultureInvariant);
        if (phpMyAdminPlan.Success)
            return $"phpMyAdmin {phpMyAdminPlan.Groups["current"].Value} → {phpMyAdminPlan.Groups["target"].Value}. The existing config.inc.php, .htaccess, and upload/save folders will be preserved, then the folder will be replaced after a full rollback backup.";

        return text;
    }

    private static string TranslatePlanPhrase(string value)
    {
        var translated = value;
        foreach (var (korean, english) in PlanPhraseReplacements)
            translated = translated.Replace(korean, english, StringComparison.Ordinal);
        if (PlanTerms.TryGetValue(translated, out var exact)) return exact;
        return translated;
    }

    public static void ApplyToElement(FrameworkElement element)
    {
        if (!LocalizationService.IsEnglish) return;

        if (element is TextBox { IsReadOnly: true } textBox && Window.GetWindow(textBox) is ConfigHistoryWindow)
        {
            ApplyTextBox(textBox);
            if ((bool)textBox.GetValue(WatchRegisteredProperty)) return;
            var descriptor = DependencyPropertyDescriptor.FromProperty(TextBox.TextProperty, typeof(TextBox));
            descriptor?.AddValueChanged(textBox, (_, _) => ApplyTextBox(textBox));
            textBox.SetValue(WatchRegisteredProperty, true);
        }
    }

    private static void ApplyTextBox(TextBox textBox)
    {
        var translated = TranslateText(textBox.Text);
        if (!string.Equals(textBox.Text, translated, StringComparison.Ordinal))
            textBox.Text = translated;
    }
}
