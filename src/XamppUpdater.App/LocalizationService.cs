using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace XamppUpdater.App;

internal enum AppLanguageMode
{
    System,
    Korean,
    English
}

internal static partial class LocalizationService
{
    private static readonly ResourceManager Resources =
        new("XamppUpdater.App.Resources.Strings", Assembly.GetExecutingAssembly());

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XAMPP-Updater",
        "settings.json");

    private static readonly DependencyProperty LocalizationWatchRegisteredProperty =
        DependencyProperty.RegisterAttached(
            "LocalizationWatchRegistered",
            typeof(bool),
            typeof(LocalizationService),
            new PropertyMetadata(false));

    private static readonly Dictionary<string, string> SourceKeys = new(StringComparer.Ordinal)
    {
        ["Apache, PHP, MariaDB를 안전하게 업데이트하고 실패 시 자동 복원합니다."] = "Main_Subtitle",
        ["Apache, PHP, MariaDB, phpMyAdmin을 안전하게 업데이트하고 실패 시 자동 복원합니다."] = "Main_Subtitle",
        ["XAMPP 경로"] = "Main_XamppPath",
        ["찾아보기"] = "Common_Browse",
        ["XAMPP 설치 경로를 확인하는 중..."] = "Status_InspectingXampp",
        ["구성요소"] = "Nav_Components",
        ["관리"] = "Nav_Management",
        ["설정 이력"] = "Nav_ConfigHistory",
        ["저장 데이터 정리"] = "Nav_Cleanup",
        ["탐색기로 열기"] = "Nav_OpenExplorer",
        ["롤백 백업"] = "Nav_RollbackBackups",
        ["설정 이력 폴더"] = "Nav_ConfigHistoryFolder",
        ["캐시/임시 데이터"] = "Nav_CacheTemp",
        ["현재 버전"] = "Common_CurrentVersion",
        ["업데이트 버전"] = "Common_TargetVersion",
        ["업데이트 정보를 확인하는 중입니다."] = "Status_CheckingUpdateInfo",
        ["Apache 업데이트"] = "Action_UpdateApache",
        ["PHP 업데이트"] = "Action_UpdatePhp",
        ["MariaDB 업데이트"] = "Action_UpdateMariaDb",
        ["phpMyAdmin 업데이트"] = "Action_UpdatePhpMyAdmin",
        ["고급 정보"] = "Common_AdvancedInfo",
        ["작업 로그"] = "Common_ActivityLog",
        ["최근 작업 로그 열기"] = "Action_OpenRecentLog",
        ["관리자 권한"] = "Privilege_Admin",
        ["일반 권한"] = "Privilege_Standard",
        ["온라인 확인 필요"] = "Status_OnlineCheckNeeded",
        ["온라인 확인 중..."] = "Status_OnlineChecking",
        ["온라인 확인 실패"] = "Status_OnlineCheckFailed",
        ["언어"] = "Language_Label",
        ["시스템 기본값"] = "Language_System",
        ["한국어"] = "Language_Korean",
        ["English"] = "Language_English",
        ["언어 변경"] = "Language_ChangeTitle",
        ["언어 설정을 변경했습니다. 프로그램을 다시 실행하면 적용됩니다."] = "Language_RestartNotice",
        ["업데이트 완료"] = "Common_UpdateCompleted",
        ["업데이트 실패"] = "Common_UpdateFailed",
        ["취소"] = "Common_Cancel",
        ["확인"] = "Common_Ok",
        ["예"] = "Common_Yes",
        ["아니요"] = "Common_No",
        ["앱 업데이트 확인"] = "Action_CheckAppUpdate",
        ["진단 정보 내보내기"] = "Action_ExportDiagnostics",
        ["새 업데이트가 없습니다. 고급 정보는 계속 확인할 수 있습니다."] = "Status_NoNewUpdate"
    };

    private static readonly Dictionary<string, string> ExactEnglish = new(StringComparer.Ordinal)
    {
        ["설정 이력 및 복원"] = "Configuration history and restore",
        ["설정 이력"] = "Configuration history",
        ["업데이트 전·후 설정을 비교하고 문제가 있을 때 안전하게 복원합니다."] = "Compare configuration before and after updates and restore it safely when needed.",
        ["현재와 비교"] = "Compare with current",
        ["복원"] = "Restore",
        ["삭제"] = "Delete",
        ["더보기 ▼"] = "More ▼",
        ["닫기"] = "Close",
        ["파일 선택 복원"] = "Restore selected files",
        ["설정 항목 병합"] = "Merge configuration entries",
        ["선택한 2개 snapshot 비교"] = "Compare two selected snapshots",
        ["무결성 검사"] = "Verify integrity",
        ["메모 수정"] = "Edit note",
        ["snapshot 폴더 열기"] = "Open snapshot folder",
        ["현재 설정 snapshot 저장"] = "Save current configuration snapshot",
        ["구성요소"] = "Component",
        ["시각"] = "Time",
        ["단계"] = "Stage",
        ["버전"] = "Version",
        ["파일"] = "Files",
        ["메모"] = "Note",
        ["전체"] = "All",
        ["고급 복원, 비교, 무결성 검사 등 자주 사용하지 않는 작업을 엽니다."] = "Open less frequently used actions such as advanced restore, comparison, and integrity checks.",
        ["삭제할 롤백 백업 또는 다운로드 캐시가 없습니다."] = "There are no rollback backups or download caches to delete.",
        ["설정 이력은 삭제하지 않습니다. 삭제한 롤백 백업은 복구할 수 없습니다. 계속하시겠습니까?"] = "Configuration history will not be deleted. Deleted rollback backups cannot be recovered. Do you want to continue?",
        ["롤백 백업과 캐시를 정리하는 중..."] = "Cleaning rollback backups and caches...",
        ["저장 데이터 정리 후 준비 상태가 초기화되었습니다."] = "Prepared update state was reset after stored-data cleanup.",
        ["XAMPP에 포함된 phpMyAdmin을 확인하는 중입니다."] = "Inspecting the phpMyAdmin installation included with XAMPP.",
        ["최신 phpMyAdmin 안정판을 확인하는 중입니다."] = "Checking the latest stable phpMyAdmin release.",
        ["phpMyAdmin 백업 생성"] = "Create phpMyAdmin backup",
        ["백업 생성"] = "Create backup",
        ["현재 버전 / 업데이트 없음"] = "Current version / no update",
        ["업데이트 없음"] = "No update"
    };

    private static readonly (string Korean, string English)[] PhraseReplacements =
    {
        ("저장 데이터 용량 확인 실패", "Failed to inspect stored data size"),
        ("XAMPP Updater가 생성한 롤백 백업과 다운로드/확장/런타임 캐시를 삭제합니다.", "Delete rollback backups and download/extension/runtime caches created by XAMPP Updater."),
        ("일부 파일을 삭제하지 못했습니다", "Some files could not be deleted"),
        ("일부 정리 완료", "Cleanup partially completed"),
        ("정리 완료", "Cleanup completed"),
        ("확보", "reclaimed"),
        ("업데이트를 진행할 수 없습니다", "The update cannot be started"),
        ("업데이트를 진행합니다", "The update will now start"),
        ("업데이트가 완료되었습니다", "The update completed successfully"),
        ("업데이트 준비", "Update preparation"),
        ("업데이트 완료", "Update completed"),
        ("업데이트 실패", "Update failed"),
        ("업데이트 중", "Updating"),
        ("새 업데이트가 없습니다", "No new update is available"),
        ("고급 정보는 계속 확인할 수 있습니다", "Advanced information remains available"),
        ("온라인 확인 완료", "Online check completed"),
        ("계열별 선택 버전", "release-line options"),
        ("현재 계열 추천", "Recommended for current series"),
        ("XAMPP 공식 기준", "XAMPP official baseline"),
        ("자동 다운로드 주소 추가 탐색 필요", "additional package URL discovery required"),
        ("준비 점검과 패키지 준비를 실행할 수 있습니다", "Preflight and package preparation are available"),
        ("준비 점검", "Preflight"),
        ("자동 복원", "automatic restore"),
        ("자동 롤백", "automatic rollback"),
        ("롤백 백업", "rollback backup"),
        ("백업 예상", "Estimated backup"),
        ("백업 위치", "Backup location"),
        ("백업 생성", "Create backup"),
        ("백업 완료", "Backup completed"),
        ("백업 실패", "Backup failed"),
        ("패키지 다운로드", "Package download"),
        ("패키지 검증", "Package verification"),
        ("패키지 준비", "Package preparation"),
        ("다운로드 중", "Downloading"),
        ("검증 중", "Verifying"),
        ("검증 완료", "Verification completed"),
        ("서비스 중지", "Stop service"),
        ("서비스 시작", "Start service"),
        ("서비스", "Service"),
        ("설정 이력", "Configuration history"),
        ("설정 비교", "Configuration comparison"),
        ("설정 파일", "Configuration files"),
        ("설정 manifest", "Configuration manifest"),
        ("현재 설정", "Current configuration"),
        ("설정", "Configuration"),
        ("현재 버전", "Current version"),
        ("대상 버전", "Target version"),
        ("업데이트 버전", "Update version"),
        ("최신 버전", "Latest version"),
        ("최신 안정판", "latest stable release"),
        ("최신", "Latest"),
        ("현재", "Current"),
        ("설치 버전", "Installed version"),
        ("버전 확인 실패", "version detection failed"),
        ("버전 미상", "unknown version"),
        ("버전", "version"),
        ("관리자 권한", "administrator privileges"),
        ("일반 권한", "standard privileges"),
        ("프로세스: 실행 중", "Process: running"),
        ("프로세스: 중지", "Process: stopped"),
        ("상태 미상", "unknown state"),
        ("주의사항 없음", "No warnings"),
        ("주의사항", "Warnings"),
        ("필요합니다", "is required"),
        ("필요", "required"),
        ("실패했습니다", "failed"),
        ("완료했습니다", "completed"),
        ("완료", "completed"),
        ("실패", "failed"),
        ("확인 중", "Checking"),
        ("확인", "Check"),
        ("감지되지 않음", "Not detected"),
        ("미등록", "Not registered"),
        ("미상", "Unknown"),
        ("설치됨", "Installed"),
        ("설치", "installation"),
        ("환경", "Environment"),
        ("경로", "Path"),
        ("폴더", "folder"),
        ("용량", "Size"),
        ("위치", "Location"),
        ("파일", "Files"),
        ("주의", "Warning"),
        ("오류", "Error"),
        ("취소했습니다", "was canceled"),
        ("취소", "Cancel"),
        ("다시 실행", "restart"),
        ("계속하시겠습니까?", "Do you want to continue?"),
        ("예", "Yes"),
        ("아니요", "No")
    };

    private static AppLanguageMode _mode;
    private static CultureInfo _culture = CultureInfo.GetCultureInfo("en-US");

    public static AppLanguageMode Mode => _mode;
    public static CultureInfo Culture => _culture;
    public static bool IsEnglish => _culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);

    public static void Initialize()
    {
        _mode = LoadMode();
        _culture = ResolveCulture(_mode);
        CultureInfo.CurrentUICulture = _culture;
        CultureInfo.CurrentCulture = _culture;
    }

    public static string Get(string key)
    {
        try { return Resources.GetString(key, _culture) ?? key; }
        catch { return key; }
    }

    public static string Translate(string? text)
    {
        if (string.IsNullOrEmpty(text) || !IsEnglish) return text ?? string.Empty;
        if (SourceKeys.TryGetValue(text, out var key)) return Get(key);
        if (ExactEnglish.TryGetValue(text, out var exact)) return exact;
        if (!ContainsHangul(text)) return text;

        var translated = PhpMyAdminCurrentRegex().Replace(
            text,
            match => $"Current phpMyAdmin {match.Groups["version"].Value} is the latest stable release.");
        translated = InstalledVersionRegex().Replace(
            translated,
            match => $"Installed version {match.Groups["version"].Value}");
        translated = RollbackVersionRegex().Replace(
            translated,
            match => $"Rollback to {match.Groups["version"].Value}");

        foreach (var (korean, english) in PhraseReplacements)
            translated = translated.Replace(korean, english, StringComparison.Ordinal);

        translated = CountSuffixRegex().Replace(translated, "${count} items");
        translated = NormalizeEnglishPunctuationRegex().Replace(translated, ": ");
        return translated;
    }

    public static void ApplyToElement(FrameworkElement element)
    {
        if (!IsEnglish) return;

        if (element.ToolTip is string toolTip)
        {
            element.ToolTip = Translate(toolTip);
        }

        if (element is DataGrid grid)
        {
            foreach (var column in grid.Columns)
            {
                if (column.Header is string header) column.Header = Translate(header);
            }
        }

        switch (element)
        {
            case TextBlock textBlock:
                TranslateProperty(textBlock, TextBlock.TextProperty);
                Watch(textBlock, TextBlock.TextProperty);
                break;
            case TextBox textBox when textBox.IsReadOnly:
                TranslateProperty(textBox, TextBox.TextProperty);
                Watch(textBox, TextBox.TextProperty);
                break;
            case HeaderedContentControl headered when headered.Header is string:
                TranslateProperty(headered, HeaderedContentControl.HeaderProperty);
                Watch(headered, HeaderedContentControl.HeaderProperty);
                break;
            case ContentControl contentControl when contentControl.Content is string:
                TranslateProperty(contentControl, ContentControl.ContentProperty);
                Watch(contentControl, ContentControl.ContentProperty);
                break;
            case Window window:
                TranslateProperty(window, Window.TitleProperty);
                Watch(window, Window.TitleProperty);
                break;
        }
    }

    public static void ApplyToTree(DependencyObject root)
    {
        if (!IsEnglish) return;
        ApplyToTreeCore(root, new HashSet<DependencyObject>());
    }

    public static void SaveMode(AppLanguageMode mode)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        Dictionary<string, object?> settings;
        try
        {
            settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(SettingsPath)) ?? new()
                : new();
        }
        catch { settings = new(); }
        settings["language"] = mode.ToString();
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string GetLanguageDisplayName(AppLanguageMode mode) => mode switch
    {
        AppLanguageMode.System => Get("Language_System"),
        AppLanguageMode.Korean => Get("Language_Korean"),
        AppLanguageMode.English => Get("Language_English"),
        _ => mode.ToString()
    };

    private static AppLanguageMode LoadMode()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return AppLanguageMode.System;
            using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (document.RootElement.TryGetProperty("language", out var language) &&
                Enum.TryParse<AppLanguageMode>(language.GetString(), true, out var mode)) return mode;
        }
        catch { }
        return AppLanguageMode.System;
    }

    private static CultureInfo ResolveCulture(AppLanguageMode mode) => mode switch
    {
        AppLanguageMode.Korean => CultureInfo.GetCultureInfo("ko-KR"),
        AppLanguageMode.English => CultureInfo.GetCultureInfo("en-US"),
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ko", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("ko-KR")
            : CultureInfo.GetCultureInfo("en-US")
    };

    private static void ApplyToTreeCore(DependencyObject current, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(current)) return;
        if (current is FrameworkElement element) ApplyToElement(element);

        foreach (var child in LogicalTreeHelper.GetChildren(current))
        {
            if (child is DependencyObject dependencyObject) ApplyToTreeCore(dependencyObject, visited);
        }
    }

    private static bool ContainsHangul(string text) => text.Any(ch => ch is >= '\uAC00' and <= '\uD7A3');

    private static void TranslateProperty(DependencyObject target, DependencyProperty property)
    {
        if (target.GetValue(property) is not string value) return;
        var translated = Translate(value);
        if (!string.Equals(value, translated, StringComparison.Ordinal)) target.SetValue(property, translated);
    }

    private static void Watch(DependencyObject target, DependencyProperty property)
    {
        if ((bool)target.GetValue(LocalizationWatchRegisteredProperty)) return;
        var descriptor = DependencyPropertyDescriptor.FromProperty(property, target.GetType());
        if (descriptor is null) return;
        descriptor.AddValueChanged(target, (_, _) => TranslateProperty(target, property));
        target.SetValue(LocalizationWatchRegisteredProperty, true);
    }

    [GeneratedRegex(@"현재\s+phpMyAdmin\s+(?<version>\d+(?:\.\d+){1,3})은\s+최신\s+안정판입니다\.")]
    private static partial Regex PhpMyAdminCurrentRegex();

    [GeneratedRegex(@"설치\s+버전\s+(?<version>\d+(?:\.\d+){1,3})")]
    private static partial Regex InstalledVersionRegex();

    [GeneratedRegex(@"(?<version>\d+(?:\.\d+){1,3})로\s*롤백")]
    private static partial Regex RollbackVersionRegex();

    [GeneratedRegex(@"(?<count>\d+)개")]
    private static partial Regex CountSuffixRegex();

    [GeneratedRegex(@"\s*:\s*")]
    private static partial Regex NormalizeEnglishPunctuationRegex();
}
