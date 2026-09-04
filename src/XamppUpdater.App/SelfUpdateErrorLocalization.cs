namespace XamppUpdater.App;

internal static class SelfUpdateErrorLocalization
{
    private static readonly Dictionary<string, string> ExactEnglish = new(StringComparer.Ordinal)
    {
        ["최신 릴리스에 XAMPP-Updater.exe 또는 SHA256 검증 파일이 없습니다."] = "The latest release does not contain XAMPP-Updater.exe or its SHA256 checksum file.",
        ["릴리스 SHA256 검증 파일 형식이 올바르지 않습니다."] = "The release SHA256 checksum file has an invalid format.",
        ["다운로드한 앱의 SHA256이 릴리스 검증값과 일치하지 않습니다."] = "The downloaded application's SHA256 does not match the release checksum.",
        ["현재 실행 파일 경로를 확인할 수 없습니다."] = "The current executable path could not be determined.",
        ["dotnet run 개발 실행 상태에서는 앱 자체 업데이트를 적용할 수 없습니다. 배포된 XAMPP-Updater.exe에서 실행하세요."] = "Self-update cannot be applied while running with dotnet run. Run the published XAMPP-Updater.exe instead.",
        ["현재 실행 파일 폴더를 확인할 수 없습니다."] = "The current executable directory could not be determined.",
        ["업데이트 임시 폴더를 확인할 수 없습니다."] = "The temporary update directory could not be determined.",
        ["앱 업데이트 적용 프로세스를 시작하지 못했습니다."] = "The app update replacement process could not be started."
    };

    public static string Translate(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return LocalizationService.IsEnglish ? "Unknown error." : "알 수 없는 오류입니다.";

        if (!LocalizationService.IsEnglish) return message;
        if (ExactEnglish.TryGetValue(message, out var exact)) return exact;

        const string parsePrefix = "GitHub 최신 릴리스 버전을 해석할 수 없습니다:";
        if (message.StartsWith(parsePrefix, StringComparison.Ordinal))
        {
            var value = message[parsePrefix.Length..].Trim();
            if (value == "(없음)") value = "(none)";
            return $"The latest GitHub release version could not be parsed: {value}";
        }

        return ExtendedLocalization.TranslateText(message);
    }
}
