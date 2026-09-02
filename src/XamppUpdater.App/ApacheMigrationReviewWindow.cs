using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public sealed class ApacheMigrationReviewWindow : Window
{
    private readonly Dictionary<string, string> _automaticFiles;
    private readonly Dictionary<string, string> _workingFiles;
    private readonly ListBox _fileList;
    private readonly TextBox _editor;
    private readonly TextBox _searchBox;
    private readonly TextBlock _searchStatus;
    private string? _currentFile;

    public IReadOnlyDictionary<string, string>? FinalFiles { get; private set; }

    public ApacheMigrationReviewWindow(ApacheMigrationReviewResult review, string currentConfRoot)
    {
        _automaticFiles = new Dictionary<string, string>(review.ProposedFiles, StringComparer.OrdinalIgnoreCase);
        _workingFiles = new Dictionary<string, string>(review.ProposedFiles, StringComparer.OrdinalIgnoreCase);

        Title = $"Apache 설정 마이그레이션 검토 - {review.CurrentVersion} → {review.TargetVersion}";
        Width = 1100;
        Height = 780;
        MinWidth = 820;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var summary = new TextBlock
        {
            Text = $"설정 파일 {review.ConfigurationFiles.Count}개 / 자동 처리 {review.AutomaticChangeCount} / 사용자 확인 {review.NeedsReviewCount}\n" +
                   (review.SyntaxValid
                       ? "현재 제안은 새 Apache 바이너리의 httpd -t 사전 검증을 통과했습니다. 필요하면 설정을 편집한 뒤 확정할 수 있습니다."
                       : "사전 검증에 실패했습니다. 오류 내용을 참고해 설정을 편집한 뒤 적용안을 확정하세요. 실제 업데이트 직전에 다시 검증합니다."),
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(summary, 0);
        root.Children.Add(summary);

        var issueBox = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = BuildReviewText(review),
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(issueBox, 1);
        root.Children.Add(issueBox);

        var workGrid = new Grid();
        workGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        workGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _fileList = new ListBox
        {
            Margin = new Thickness(0, 0, 10, 0),
            FontFamily = new FontFamily("Consolas")
        };
        foreach (var file in _workingFiles.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            _fileList.Items.Add(file);
        _fileList.SelectionChanged += FileList_SelectionChanged;
        Grid.SetColumn(_fileList, 0);
        workGrid.Children.Add(_fileList);

        var editorGrid = new Grid();
        editorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editorGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var searchPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        searchPanel.Children.Add(new TextBlock
        {
            Text = "검색",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        var previousButton = new Button { Content = "이전", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(previousButton, Dock.Right);
        searchPanel.Children.Add(previousButton);
        var nextButton = new Button { Content = "다음", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(nextButton, Dock.Right);
        searchPanel.Children.Add(nextButton);
        _searchStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0), MinWidth = 70, TextAlignment = TextAlignment.Right };
        DockPanel.SetDock(_searchStatus, Dock.Right);
        searchPanel.Children.Add(_searchStatus);
        _searchBox = new TextBox { MinWidth = 220, VerticalContentAlignment = VerticalAlignment.Center };
        searchPanel.Children.Add(_searchBox);
        Grid.SetRow(searchPanel, 0);
        editorGrid.Children.Add(searchPanel);

        _editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(_editor, 1);
        editorGrid.Children.Add(_editor);
        Grid.SetColumn(editorGrid, 1);
        workGrid.Children.Add(editorGrid);

        previousButton.Click += (_, _) => FindPrevious(true);
        nextButton.Click += (_, _) => FindNext(true, true);
        _searchBox.TextChanged += (_, _) =>
        {
            _searchStatus.Text = string.Empty;
            if (!string.IsNullOrWhiteSpace(_searchBox.Text)) FindNext(false, false);
        };
        _searchBox.PreviewKeyDown += SearchBox_PreviewKeyDown;
        PreviewKeyDown += Window_PreviewKeyDown;

        Grid.SetRow(workGrid, 2);
        root.Children.Add(workGrid);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var openFolder = new Button { Content = "현재 conf 폴더 열기", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        openFolder.Click += (_, _) =>
        {
            if (Directory.Exists(currentConfRoot))
                Process.Start(new ProcessStartInfo { FileName = currentConfRoot, UseShellExecute = true });
        };

        var resetFile = new Button { Content = "현재 파일 자동 제안으로", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        resetFile.Click += (_, _) =>
        {
            if (_currentFile is null || !_automaticFiles.TryGetValue(_currentFile, out var value)) return;
            _workingFiles[_currentFile] = value;
            _editor.Text = value;
            _searchStatus.Text = string.Empty;
        };

        var cancel = new Button { Content = "취소", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };

        var confirm = new Button { Content = "적용안 확정", Padding = new Thickness(14, 5, 14, 5), IsDefault = true };
        confirm.Click += (_, _) =>
        {
            SaveCurrentFile();
            if (_workingFiles.Count == 0)
            {
                MessageBox.Show(this, "확정할 Apache 설정 파일이 없습니다.", "Apache 설정 검토", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            FinalFiles = new Dictionary<string, string>(_workingFiles, StringComparer.OrdinalIgnoreCase);
            DialogResult = true;
            Close();
        };

        buttons.Children.Add(openFolder);
        buttons.Children.Add(resetFile);
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
        if (_fileList.Items.Count > 0) _fileList.SelectedIndex = 0;
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SaveCurrentFile();
        _currentFile = _fileList.SelectedItem as string;
        if (_currentFile is not null && _workingFiles.TryGetValue(_currentFile, out var text))
            _editor.Text = text;
        else
            _editor.Clear();
        _searchStatus.Text = string.Empty;
    }

    private void SaveCurrentFile()
    {
        if (_currentFile is not null) _workingFiles[_currentFile] = _editor.Text;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) FindPrevious(false);
        else FindNext(true, false);
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
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) FindPrevious(true);
        else FindNext(true, true);
        e.Handled = true;
    }

    private void FindNext(bool fromCurrentSelection, bool focusEditor)
    {
        var query = _searchBox.Text;
        if (string.IsNullOrWhiteSpace(query)) { _searchStatus.Text = string.Empty; return; }
        var text = _editor.Text;
        var start = fromCurrentSelection ? _editor.SelectionStart + _editor.SelectionLength : 0;
        if (start > text.Length) start = 0;
        var index = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
        if (index < 0 && start > 0) index = text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
        SelectSearchResult(index, query.Length, focusEditor);
    }

    private void FindPrevious(bool focusEditor)
    {
        var query = _searchBox.Text;
        if (string.IsNullOrWhiteSpace(query)) { _searchStatus.Text = string.Empty; return; }
        var text = _editor.Text;
        var start = Math.Max(0, _editor.SelectionStart - 1);
        var index = start < text.Length ? text.LastIndexOf(query, start, StringComparison.OrdinalIgnoreCase) : -1;
        if (index < 0 && text.Length > 0) index = text.LastIndexOf(query, text.Length - 1, StringComparison.OrdinalIgnoreCase);
        SelectSearchResult(index, query.Length, focusEditor);
    }

    private void SelectSearchResult(int index, int length, bool focusEditor)
    {
        if (index < 0) { _searchStatus.Text = "결과 없음"; return; }
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

        if (!string.IsNullOrWhiteSpace(review.ValidationOutput))
        {
            lines.Add(string.Empty);
            lines.Add("[httpd -t 출력]");
            lines.Add(review.ValidationOutput);
        }
        return string.Join(Environment.NewLine, lines);
    }
}
