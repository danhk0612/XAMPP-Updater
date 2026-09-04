using System.ComponentModel;
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
        ("는 같은 Apache 2.4 계열입니다.", " is in the same Apache 2.4 series."),
        ("같은 PHP 메이저 계열입니다.", "This is in the same PHP major series."),
        ("는 메이저 변경입니다.", " is a major-version change."),
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

        var translated = UserMessageLocalization.PreTranslate(text);
        foreach (var (korean, english) in Replacements)
            translated = translated.Replace(korean, english, StringComparison.Ordinal);

        translated = LocalizationCatalog.TranslateUserText(translated);

        foreach (var (korean, english) in Replacements)
            translated = translated.Replace(korean, english, StringComparison.Ordinal);
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
