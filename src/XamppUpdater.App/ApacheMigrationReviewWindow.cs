using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public sealed class ApacheMigrationReviewWindow : Window
{
    public ApacheMigrationReviewWindow(ApacheMigrationReviewResult review, string currentConfRoot)
    {
        Title = $"Apache 설정 마이그레이션 검토 - {review.CurrentVersion} → {review.TargetVersion}";
        Width = 920;
        Height = 680;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var summary = new TextBlock
        {
            Text = $"설정 파일 {review.ConfigurationFiles.Count}개 / 자동 처리 {review.AutomaticChangeCount} / 사용자 확인 {review.NeedsReviewCount}\n" +
                   (review.SyntaxValid
                       ? "새 Apache 바이너리로 기존 설정 사전 검증을 통과했습니다."
                       : "사전 검증에 실패했습니다. 아래 오류를 확인한 뒤 현재 Apache 설정을 수정해야 합니다."),
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(summary, 0);
        root.Children.Add(summary);

        var tabs = new TabControl();

        var reviewText = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = BuildReviewText(review)
        };
        tabs.Items.Add(new TabItem { Header = "검토 결과", Content = reviewText });

        var output = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.IsNullOrWhiteSpace(review.ValidationOutput) ? "(출력 없음)" : review.ValidationOutput
        };
        tabs.Items.Add(new TabItem { Header = "httpd -t 출력", Content = output });

        var files = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.Join(Environment.NewLine, review.ConfigurationFiles)
        };
        tabs.Items.Add(new TabItem { Header = "설정 파일", Content = files });

        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var openFolder = new Button
        {
            Content = "현재 conf 폴더 열기",
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 8, 0)
        };
        openFolder.Click += (_, _) =>
        {
            if (Directory.Exists(currentConfRoot))
                Process.Start(new ProcessStartInfo { FileName = currentConfRoot, UseShellExecute = true });
        };

        var close = new Button
        {
            Content = "닫기",
            Padding = new Thickness(14, 5, 14, 5),
            IsDefault = true
        };
        close.Click += (_, _) => Close();

        buttons.Children.Add(openFolder);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
    }

    private static string BuildReviewText(ApacheMigrationReviewResult review)
    {
        var lines = new List<string>();
        var needsReview = review.Items.Where(item => item.Kind == ApacheMigrationReviewKind.NeedsReview).ToArray();
        lines.Add("[사용자 확인 필요]");
        if (needsReview.Length == 0)
            lines.Add("- 없음: 현재 설정은 대상 Apache에서 사전 검증을 통과했습니다.");
        else
            lines.AddRange(needsReview.Select(item => "- " + item.Message));

        var automatic = review.Items.Where(item => item.Kind == ApacheMigrationReviewKind.AutomaticChange).ToArray();
        if (automatic.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add("[자동 처리]");
            lines.AddRange(automatic.Select(item => "- " + item.Message));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
