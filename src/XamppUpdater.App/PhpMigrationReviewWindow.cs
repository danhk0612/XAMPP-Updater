using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public sealed class PhpMigrationReviewWindow : Window
{
    private readonly string _automaticProposal;
    private readonly TextBox _editor;

    public string? FinalIniText { get; private set; }

    public PhpMigrationReviewWindow(PhpMigrationReviewResult review)
    {
        _automaticProposal = review.ProposedIni;
        Title = $"PHP 설정 마이그레이션 검토 - {review.CurrentVersion} → {review.TargetVersion}";
        Width = 980;
        Height = 760;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var summary = new TextBlock
        {
            Text = $"자동 유지 {review.PreservedCount} / 자동 변경 {review.AutomaticChangeCount} / 사용자 확인 {review.NeedsReviewCount}\n" +
                   "대부분은 자동 처리됩니다. 아래 '사용자 확인' 항목과 최종 php.ini만 검토하면 됩니다.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetRow(summary, 0);
        root.Children.Add(summary);

        var issueBox = new TextBox
        {
            IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            Text = BuildReviewText(review),
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(issueBox, 1);
        root.Children.Add(issueBox);

        var editorLabel = new TextBlock
        {
            Text = "최종 php.ini — 필요하면 직접 편집할 수 있습니다.",
            Margin = new Thickness(0, 0, 0, 6),
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetRow(editorLabel, 2);
        root.Children.Add(editorLabel);

        _editor = new TextBox
        {
            Text = review.ProposedIni,
            AcceptsReturn = true,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            TextWrapping = TextWrapping.NoWrap
        };
        Grid.SetRow(_editor, 3);
        root.Children.Add(_editor);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var reset = new Button
        {
            Content = "자동 제안으로 되돌리기",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        reset.Click += (_, _) => _editor.Text = _automaticProposal;

        var cancel = new Button
        {
            Content = "취소",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };

        var confirm = new Button
        {
            Content = "적용안 확정",
            Padding = new Thickness(14, 6, 14, 6),
            IsDefault = true
        };
        confirm.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_editor.Text))
            {
                MessageBox.Show(this, "php.ini 내용이 비어 있습니다.", "PHP 설정 검토", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FinalIniText = _editor.Text;
            DialogResult = true;
            Close();
        };

        buttons.Children.Add(reset);
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        Content = root;
    }

    private static string BuildReviewText(PhpMigrationReviewResult review)
    {
        var lines = new List<string>();
        var needsReview = review.Items.Where(item => item.Kind == PhpMigrationReviewKind.NeedsReview).ToArray();
        if (needsReview.Length > 0)
        {
            lines.Add("[사용자 확인 필요]");
            lines.AddRange(needsReview.Select(item => "- " + item.Message));
        }
        else
        {
            lines.Add("[사용자 확인 필요]");
            lines.Add("- 없음: 현재 자동 제안으로 처리 가능합니다.");
        }

        var automatic = review.Items.Where(item => item.Kind == PhpMigrationReviewKind.AutomaticChange).ToArray();
        if (automatic.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add("[자동 변경/대체]");
            lines.AddRange(automatic.Select(item => "- " + item.Message));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
