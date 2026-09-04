using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace XamppUpdater.App;

/// <summary>
/// Explicit user-facing translations for surfaces that are constructed at runtime.
/// LocalizationService remains the general resource/fallback layer; this catalog
/// handles messages and controls where phrase-based translation is not sufficient.
/// </summary>
internal static class LocalizationCatalog
{
    private static readonly Dictionary<string, string> ExactEnglish = new(StringComparer.Ordinal)
    {
        ["파일 선택 복원"] = "Restore selected files",
        ["설정 항목 병합"] = "Merge configuration entries",
        ["선택한 2개 snapshot 비교"] = "Compare two selected snapshots",
        ["무결성 검사"] = "Verify integrity",
        ["메모 수정"] = "Edit note",
        ["snapshot 폴더 열기"] = "Open snapshot folder",
        ["현재 설정 snapshot 저장"] = "Save current configuration snapshot",
        ["설정 내용 비교"] = "Configuration content comparison",
        ["변경 주변만 보기"] = "Show changes with context",
        ["이전 snapshot"] = "Previous snapshot",
        ["이후 snapshot"] = "Next snapshot",
        ["◀ 이전 차이"] = "◀ Previous difference",
        ["다음 차이 ▶"] = "Next difference ▶",
        ["두 snapshot 사이에 변경된 설정 파일이 없습니다."] = "There are no changed configuration files between the two snapshots.",
        ["차이 없음"] = "No differences",
        ["설정 이력 비교"] = "Configuration history comparison",
        ["현재 설정과 비교"] = "Compare with current configuration",
        ["snapshot 무결성 검사"] = "Snapshot integrity check",
        ["설정 snapshot"] = "Configuration snapshot",
        ["snapshot 삭제"] = "Delete snapshots",
        ["선택 설정 복원"] = "Selective configuration restore",
        ["항목 병합"] = "Entry merge",
        ["설정 복원"] = "Configuration restore",
        ["전체 선택"] = "Select all",
        ["전체 해제"] = "Clear selection",
        ["선택 파일 복원"] = "Restore selected files",
        ["설정 항목 선택 병합"] = "Merge selected configuration entries",
        ["적용 가능 전체 선택"] = "Select all applicable",
        ["선택 항목 적용"] = "Apply selected entries",
        ["저장"] = "Save",
        ["복원할 파일을 하나 이상 선택하세요."] = "Select at least one file to restore.",
        ["적용할 설정 항목을 1개 이상 선택하세요."] = "Select at least one configuration entry to apply.",
        ["비교할 snapshot을 정확히 2개 선택하세요."] = "Select exactly two snapshots to compare.",
        ["서로 다른 구성요소의 snapshot은 비교할 수 없습니다."] = "Snapshots from different components cannot be compared.",
        ["현재 설정과 비교할 snapshot을 정확히 1개 선택하세요."] = "Select exactly one snapshot to compare with the current configuration.",
        ["메모 수정할 snapshot을 정확히 1개 선택하세요."] = "Select exactly one snapshot whose note you want to edit.",
        ["복원할 snapshot을 정확히 1개 선택하세요."] = "Select exactly one snapshot to restore.",
        ["폴더 열기할 snapshot을 정확히 1개 선택하세요."] = "Select exactly one snapshot whose folder you want to open.",
        ["파일 선택 복원할 snapshot을 정확히 1개 선택하세요."] = "Select exactly one snapshot for selective file restore.",
        ["설정 항목 병합할 snapshot을 정확히 1개 선택하세요."] = "Select exactly one snapshot for configuration-entry merge.",
        ["검사할 snapshot을 1개 이상 선택하세요."] = "Select at least one snapshot to verify.",
        ["삭제할 snapshot을 1개 이상 선택하세요."] = "Select at least one snapshot to delete.",
        ["자동 비교 가능한 설정 항목에서 차이를 찾지 못했습니다."] = "No differences were found among configuration entries that can be compared automatically.",
        ["선택한 snapshot과 현재 설정이 동일합니다."] = "The selected snapshot is identical to the current configuration.",
        ["직전 설정으로 자동 원복했습니다."] = "The previous configuration was restored automatically.",
        ["자동 원복도 완료되지 않았습니다."] = "Automatic restoration of the previous configuration also failed.",
        ["복원 직전 설정으로 자동 원복했습니다."] = "The configuration from immediately before the restore was restored automatically.",
        ["현재 설정은 복원 직전에 안전 snapshot으로 자동 저장하고 검증 실패 시 자동 원복합니다. 계속하시겠습니까?"] = "The current configuration will be saved automatically as a safety snapshot immediately before the restore, and it will be restored if validation fails. Do you want to continue?",
        ["snapshot 메모를 수정하세요. 비우면 메모가 제거됩니다."] = "Edit the snapshot note. Leave it empty to remove the note.",
        ["이 snapshot을 구분할 메모를 입력하세요. 비워도 저장할 수 있습니다."] = "Enter a note to identify this snapshot. You may leave it empty.",
        ["snapshot 상태로 되돌릴 파일만 선택하세요. 현재 설정과 동일한 파일은 목록에서 제외됩니다."] = "Select only the files to restore to the snapshot state. Files identical to the current configuration are omitted.",
        ["'현재에만 존재' 항목을 선택하면 해당 파일은 삭제됩니다. 적용 전에는 전체 현재 설정이 안전 snapshot으로 저장됩니다."] = "Selecting an item that exists only in the current configuration will delete that file. The complete current configuration is saved as a safety snapshot before applying changes.",
        ["자동 적용은 기존 줄의 값만 snapshot 값으로 교체하며 주석과 주변 구조는 유지합니다."] = "Automatic application replaces only existing values with snapshot values while preserving comments and surrounding structure."
    };

    private static readonly (string Korean, string English)[] PhraseEnglish =
    {
        ("현재 버전 / 업데이트 없음", "Current version / no update"),
        ("업데이트 없음", "No update"),
        ("현재 계열 추천", "Recommended for current series"),
        ("XAMPP 공식 기준", "XAMPP official baseline"),
        ("설정 이력 비교", "Configuration history comparison"),
        ("현재 설정과 비교", "Compare with current configuration"),
        ("snapshot 무결성 검사", "Snapshot integrity check"),
        ("파일 선택 복원", "Selective file restore"),
        ("설정 항목 병합", "Configuration entry merge"),
        ("설정 복원", "Configuration restore"),
        ("설정 snapshot", "Configuration snapshot"),
        ("snapshot 삭제", "Snapshot deletion"),
        ("메모 수정", "Edit note"),
        ("수동 snapshot", "Manual snapshot"),
        ("snapshot 메모", "snapshot note"),
        ("변경 주변만 보기", "Show changes with context"),
        ("이전 snapshot", "Previous snapshot"),
        ("이후 snapshot", "Next snapshot"),
        ("변경 행", "Changed lines"),
        ("이전 snapshot에 없음", "Not present in the previous snapshot"),
        ("이후 snapshot에 없음", "Not present in the next snapshot"),
        ("선택 snapshot", "Selected snapshots"),
        ("검증 성공 파일", "Verified files"),
        ("문제 snapshot", "Problem snapshots"),
        ("정상", "Valid"),
        ("문제", "Issues"),
        ("선택한 snapshot", "Selected snapshot"),
        ("삭제 완료", "Deletion completed"),
        ("삭제 실패", "Deletion failed"),
        ("선택한 설정 항목", "Selected configuration entries"),
        ("검증 실패 시 직전 설정으로 자동 원복합니다", "If validation fails, the previous configuration will be restored automatically"),
        ("선택한 설정 파일", "Selected configuration files"),
        ("복원이 완료되었습니다", "Restore completed successfully"),
        ("파일 선택 복원에 실패했습니다", "Selective file restore failed"),
        ("설정을 선택한 snapshot 상태로 전체 복원합니다", "Restore the complete configuration to the selected snapshot state"),
        ("설정 복원이 완료되었습니다", "Configuration restore completed successfully"),
        ("설정 복원에 실패했습니다", "Configuration restore failed"),
        ("당시 버전", "Version at capture"),
        ("단계", "Stage"),
        ("메모", "Note"),
        ("현재에만 존재", "Exists only in current configuration"),
        ("snapshot에만 존재", "Exists only in snapshot"),
        ("선택 시 삭제", "delete when selected"),
        ("선택 시 복원", "restore when selected"),
        ("변경됨", "Changed"),
        ("snapshot 파일로 덮어쓰기", "overwrite with snapshot file"),
        ("변경 파일", "Changed files"),
        ("복원 선택", "Selected for restore"),
        ("변경 항목", "Changed entries"),
        ("자동 적용 가능", "Auto-applicable"),
        ("수동 확인", "Manual review"),
        ("선택", "Selected"),
        ("공식 메타데이터", "Official metadata"),
        ("호환성 주의사항", "compatibility warnings"),
        ("현재 PHP/DB 버전에서", "With the current PHP/DB versions"),
        ("업데이트를 진행할 수 있습니다", "the update can proceed"),
        ("업데이트 가능하지만", "The update can proceed, but there are"),
        ("현재 XAMPP 구성에서는", "With the current XAMPP configuration"),
        ("진행할 수 없습니다", "cannot proceed"),
        ("PHP 버전을 확인하지 못해", "The PHP version could not be determined, so"),
        ("호환성을 보장할 수 없습니다", "compatibility cannot be guaranteed"),
        ("권장 범위", "recommended range"),
        ("보다 새 PHP", "a newer PHP version"),
        ("사용 중입니다", "is in use"),
        ("실제 phpMyAdmin 동작 확인을 권장합니다", "Verifying phpMyAdmin operation is recommended"),
        ("계열", "series"),
        ("환경", "Environment"),
        ("경로", "Path"),
        ("서비스", "Service"),
        ("미감지", "not detected"),
        ("미상", "unknown"),
        ("미등록", "not registered")
    };

    public static string Text(string korean, string english) =>
        LocalizationService.IsEnglish ? english : korean;

    public static string TranslateUserText(string? text)
    {
        if (string.IsNullOrEmpty(text) || !LocalizationService.IsEnglish) return text ?? string.Empty;
        if (ExactEnglish.TryGetValue(text, out var exact)) return exact;

        var translated = LocalizationService.Translate(text);
        if (ExactEnglish.TryGetValue(translated, out exact)) return exact;

        foreach (var (korean, english) in PhraseEnglish)
            translated = translated.Replace(korean, english, StringComparison.Ordinal);

        return translated;
    }

    public static string TranslateDisplayValue(object? value)
    {
        if (value is null) return string.Empty;
        var display = value.GetType().GetProperty("DisplayText", BindingFlags.Public | BindingFlags.Instance)?.GetValue(value)?.ToString()
                      ?? value.ToString()
                      ?? string.Empty;
        return TranslateUserText(display);
    }

    public static void ApplyToElement(FrameworkElement element)
    {
        if (!LocalizationService.IsEnglish) return;

        if (element.ToolTip is string toolTip)
            element.ToolTip = TranslateUserText(toolTip);

        if (element is HeaderedItemsControl headered && headered.Header is string header)
            headered.Header = TranslateUserText(header);
    }
}
