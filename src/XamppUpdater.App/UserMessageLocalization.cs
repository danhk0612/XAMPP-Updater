namespace XamppUpdater.App;

internal static class UserMessageLocalization
{
    private static readonly (string Korean, string English)[] PreReplacements =
    {
        ("현재 설정은 복원 직전에 안전 snapshot으로 자동 저장하고 검증 실패 시 자동 원복합니다.",
            "The current configuration will be saved automatically as a safety snapshot immediately before the restore, and restored automatically if validation fails."),
        ("선택한 설정 항목", "Selected configuration entries"),
        ("개를 snapshot 값으로 병합합니다.", " will be merged using snapshot values."),
        ("개 병합을 완료했습니다.", " were merged successfully."),
        ("선택한 설정 파일", "Selected configuration files"),
        ("개를 snapshot 상태로 복원합니다.", " will be restored to the snapshot state."),
        ("개의 복원이 완료되었습니다.", " were restored successfully."),
        ("선택한 snapshot", "Selected snapshots"),
        ("개를 삭제합니다.", " will be deleted."),
        ("개를 삭제했습니다.", " were deleted."),
        ("실제 설정에는 영향을 주지 않지만 삭제한 이력은 복구할 수 없습니다.",
            "This does not affect the live configuration, but deleted history cannot be recovered."),
        ("자동 비교 가능한 설정 항목에서 차이를 찾지 못했습니다.",
            "No differences were found among configuration entries that can be compared automatically."),
        ("선택한 snapshot과 현재 설정이 동일합니다.",
            "The selected snapshot is identical to the current configuration."),
        ("검증 실패 시 직전 설정으로 자동 원복합니다.",
            "If validation fails, the previous configuration will be restored automatically."),
        ("직전 설정으로 자동 원복했습니다.",
            "The previous configuration was restored automatically."),
        ("복원 직전 설정으로 자동 원복했습니다.",
            "The configuration from immediately before the restore was restored automatically."),
        ("자동 원복도 완료되지 않았습니다.",
            "Automatic restoration of the previous configuration also failed."),
        ("다른 XAMPP 설치 경로에서 생성한 snapshot은 복원할 수 없습니다.",
            "A snapshot created from a different XAMPP installation path cannot be restored."),
        ("서로 다른 XAMPP 설치의 설정 snapshot은 비교할 수 없습니다.",
            "Configuration snapshots from different XAMPP installations cannot be compared."),
        ("서로 다른 구성요소의 설정 snapshot은 비교할 수 없습니다.",
            "Configuration snapshots from different components cannot be compared.")
    };

    public static string PreTranslate(string text)
    {
        if (!LocalizationService.IsEnglish || string.IsNullOrEmpty(text)) return text;
        var translated = text;
        foreach (var (korean, english) in PreReplacements)
            translated = translated.Replace(korean, english, StringComparison.Ordinal);
        return translated;
    }
}
