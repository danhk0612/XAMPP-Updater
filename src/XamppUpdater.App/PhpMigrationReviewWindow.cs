using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public sealed class PhpMigrationReviewWindow : Window
{
    private readonly string _automaticProposal;
    private readonly TextBox _editor;
    private readonly TextBox _searchBox;
    private readonly TextBlock _searchStatus;

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

        var searchPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var searchLabel = new TextBlock
        {
            Text = "검색",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        DockPanel.SetDock(searchLabel, Dock.Left);
        searchPanel.Children.Add(searchLabel);

        var previousButton = new Button
        {
            Content = "이전",
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(6, 0, 0, 0)
        };
        DockPanel.SetDock(previousButton, Dock.Right);
        searchPanel.Children.Add(previousButton);

        var nextButton = new Button
        {
            Content = "다음",
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(6, 0, 0, 0)
        };
        DockPanel.SetDock(nextButton, Dock.Right);
        searchPanel.Children.Add(nextButton);

        _searchStatus = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 4, 0),
            MinWidth = 70,
            TextAlignment = TextAlignment.Right
        };
        DockPanel.SetDock(_searchStatus, Dock.Right);
        searchPanel.Children.Add(_searchStatus);

        _searchBox = new TextBox
        {
            MinWidth = 240,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        searchPanel.Children.Add(_searchBox);
        Grid.SetRow(searchPanel, 3);
        root.Children.Add(searchPanel);

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
        Grid.SetRow(_editor, 4);
        root.Children.Add(_editor);

        previousButton.Click += (_, _) => FindPrevious(focusEditor: true);
        nextButton.Click += (_, _) => FindNext(focusEditor: true);
        _searchBox.TextChanged += (_, _) =>
        {
            _searchStatus.Text = string.Empty;
            if (!string.IsNullOrWhiteSpace(_searchBox.Text))
            {
                FindNext(fromCurrentSelection: false, focusEditor: false);
            }
        };
        _searchBox.PreviewKeyDown += SearchBox_PreviewKeyDown;
        PreviewKeyDown += Window_PreviewKeyDown;

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
        reset.Click += (_, _) =>
        {
            _editor.Text = _automaticProposal;
            _searchStatus.Text = string.Empty;
        };

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
        Grid.SetRow(buttons, 5);
        root.Children.Add(buttons);

        Content = root;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) FindPrevious(focusEditor: false);
        else FindNext(focusEditor: false);
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.F3) return;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) FindPrevious(focusEditor: false);
        else FindNext(focusEditor: false);
        e.Handled = true;
    }

    private void FindNext(bool fromCurrentSelection = true, bool focusEditor = false)
    {
        var query = _searchBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            _searchStatus.Text = string.Empty;
            return;
        }

        var text = _editor.Text;
        var start = fromCurrentSelection ? _editor.SelectionStart + _editor.SelectionLength : 0;
        if (start > text.Length) start = 0;
        var index = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
        if (index < 0 && start > 0)
        {
            index = text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
        }
        SelectSearchResult(index, query.Length, focusEditor);
    }

    private void FindPrevious(bool focusEditor = false)
    {
        var query = _searchBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            _searchStatus.Text = string.Empty;
            return;
        }

        var text = _editor.Text;
        var start = Math.Max(0, _editor.SelectionStart - 1);
        var index = start < text.Length
            ? text.LastIndexOf(query, start, StringComparison.OrdinalIgnoreCase)
            : -1;
        if (index < 0 && text.Length > 0)
        {
            index = text.LastIndexOf(query, text.Length - 1, StringComparison.OrdinalIgnoreCase);
        }
        SelectSearchResult(index, query.Length, focusEditor);
    }

    private void SelectSearchResult(int index, int length, bool focusEditor)
    {
        if (index < 0)
        {
            _searchStatus.Text = "결과 없음";
            return;
        }

        _editor.Select(index, length);
        var line = _editor.GetLineIndexFromCharacterIndex(index);
        if (line >= 0) _editor.ScrollToLine(line);
        if (focusEditor) _editor.Focus();

        var total = CountOccurrences(_editor.Text, _searchBox.Text);
        var current = CountOccurrences(_editor.Text[..index], _searchBox.Text) + 1;
        _searchStatus.Text = $"{current}/{total}";
    }

    private static int CountOccurrences(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return 0;
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(query, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += Math.Max(1, query.Length);
        }
        return count;
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
