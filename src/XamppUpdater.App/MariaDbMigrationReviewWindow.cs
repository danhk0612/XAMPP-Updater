using System.Text;
using System.Windows;
using System.Windows.Controls;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public sealed class MariaDbMigrationReviewWindow : Window
{
    public MariaDbMigrationReviewWindow(MariaDbMigrationReviewResult review)
    {
        Title = $"MariaDB {review.CurrentVersion} → {review.TargetVersion} 마이그레이션 검토";
        Width = 820;
        Height = 720;
        MinWidth = 680;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(16) };
        var close = new Button
        {
            Content = "확인",
            Width = 90,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            IsDefault = true
        };
        close.Click += (_, _) => { DialogResult = true; Close(); };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var text = new TextBox
        {
            Text = BuildText(review),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 13
        };
        root.Children.Add(text);
        Content = root;
    }

    private static string BuildText(MariaDbMigrationReviewResult review)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[기본 정보]");
        sb.AppendLine($"현재 버전: {review.CurrentVersion}");
        sb.AppendLine($"대상 버전: {review.TargetVersion}");
        sb.AppendLine($"서비스: {review.ServiceName ?? "미감지"}");
        sb.AppendLine($"실행 파일: {review.ExecutablePath}");
        sb.AppendLine($"data: {review.DataPath}");
        sb.AppendLine($"설정: {(review.ConfigFiles.Count == 0 ? "별도 파일 없음" : string.Join(", ", review.ConfigFiles))}");
        sb.AppendLine();
        sb.AppendLine("[패키지/백업]");
        sb.AppendLine($"패키지: {review.PackagePath}");
        sb.AppendLine($"패키지 SHA256: {review.PackageSha256}");
        sb.AppendLine($"업그레이드 도구: {review.UpgradeTool ?? "찾지 못함"}");
        sb.AppendLine($"백업 manifest: {review.BackupManifestPath}");
        sb.AppendLine($"물리 백업: {review.BackupFiles:N0}개 / {FormatBytes(review.BackupBytes)}");
        sb.AppendLine($"논리 백업: {review.LogicalBackupPath ?? "없음"}");
        sb.AppendLine($"논리 백업 SHA256: {review.LogicalBackupSha256 ?? "없음"}");
        sb.AppendLine();
        sb.AppendLine("[사용자 확인 필요]");
        if (review.ReviewItems.Count == 0) sb.AppendLine("- 없음");
        else foreach (var item in review.ReviewItems) sb.AppendLine("- " + item);
        sb.AppendLine();
        sb.AppendLine("[자동 처리]");
        foreach (var item in review.AutomaticItems) sb.AppendLine("- " + item);
        sb.AppendLine();
        sb.AppendLine(review.CanExecute ? "검토 결과: 현재 구성으로 실행 가능" : "검토 결과: 실제 실행 전 해결이 필요한 항목이 있음");
        return sb.ToString();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:N2} {units[index]}";
    }
}
