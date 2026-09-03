using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using DiffPlex.Wpf.Controls;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public sealed class ConfigSnapshotContentDiffWindow : Window
{
    private readonly ConfigSnapshotDiff _diff;
    private readonly ListBox _files = new() { MinWidth = 330 };
    private readonly SideBySideDiffViewer _viewer = new()
    {
        HideLineNumbers = false,
        IgnoreUnchanged = true,
        LinesContext = 3,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12
    };
    private readonly TextBlock _fileSummary = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _changePosition = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    private readonly CheckBox _collapseUnchanged = new()
    {
        Content = "변경 주변만 보기",
        IsChecked = true,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 12, 0)
    };

    private IReadOnlyList<int> _changeRows = Array.Empty<int>();
    private int _changeCursor = -1;

    public ConfigSnapshotContentDiffWindow(ConfigSnapshotDiff diff)
    {
        _diff = diff;
        Title = $"설정 내용 비교 - {diff.Older.Type}";
        Width = 1380;
        Height = 780;
        MinWidth = 980;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var summary = new TextBlock
        {
            Text = $"이전: {diff.Older.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} / {diff.Older.Version} / {diff.Older.Stage}\n" +
                   $"이후: {diff.Newer.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} / {diff.Newer.Version} / {diff.Newer.Stage}\n" +
                   $"파일 변경 {diff.Changed} / 추가 {diff.Added} / 삭제 {diff.Removed} / 동일 {diff.Same}",
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetColumnSpan(summary, 3);
        root.Children.Add(summary);

        foreach (var item in diff.Items.Where(item => item.Kind != ConfigSnapshotDiffKind.Same))
        {
            var olderText = ReadSnapshotFile(diff.Older, item.RelativePath);
            var newerText = ReadSnapshotFile(diff.Newer, item.RelativePath);
            var model = SideBySideDiffBuilder.Diff(olderText, newerText, ignoreWhiteSpace: false, ignoreCase: false);
            _files.Items.Add(new DiffItem(item, GetChangedRows(model).Count));
        }
        _files.SelectionChanged += (_, _) => ShowSelectedFile();
        Grid.SetRow(_files, 1);
        root.Children.Add(_files);

        var viewerArea = new Grid();
        viewerArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        viewerArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        viewerArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        viewerArea.Children.Add(_fileSummary);

        var headers = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        headers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        headers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var olderLabel = new TextBlock { Text = "이전 snapshot", FontWeight = FontWeights.SemiBold };
        var newerLabel = new TextBlock { Text = "이후 snapshot", FontWeight = FontWeights.SemiBold };
        Grid.SetColumn(olderLabel, 0);
        Grid.SetColumn(newerLabel, 2);
        headers.Children.Add(olderLabel);
        headers.Children.Add(newerLabel);
        Grid.SetRow(headers, 1);
        viewerArea.Children.Add(headers);

        Grid.SetRow(_viewer, 2);
        viewerArea.Children.Add(_viewer);

        Grid.SetRow(viewerArea, 1);
        Grid.SetColumn(viewerArea, 2);
        root.Children.Add(viewerArea);

        var controls = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        _collapseUnchanged.Checked += (_, _) => ApplyCollapsedMode();
        _collapseUnchanged.Unchecked += (_, _) => ApplyCollapsedMode();
        controls.Children.Add(_collapseUnchanged);

        var previous = new Button { Content = "◀ 이전 차이", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
        previous.Click += (_, _) => NavigateChange(-1);
        controls.Children.Add(previous);
        controls.Children.Add(_changePosition);

        var next = new Button { Content = "다음 차이 ▶", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 12, 0) };
        next.Click += (_, _) => NavigateChange(1);
        controls.Children.Add(next);

        var close = new Button
        {
            Content = "닫기",
            Padding = new Thickness(18, 5, 18, 5),
            IsCancel = true
        };
        close.Click += (_, _) => Close();
        controls.Children.Add(close);
        Grid.SetRow(controls, 2);
        Grid.SetColumnSpan(controls, 3);
        root.Children.Add(controls);

        Content = root;
        if (_files.Items.Count > 0)
        {
            _files.SelectedIndex = 0;
        }
        else
        {
            _fileSummary.Text = "두 snapshot 사이에 변경된 설정 파일이 없습니다.";
            _changePosition.Text = "차이 없음";
        }
    }

    private void ShowSelectedFile()
    {
        if (_files.SelectedItem is not DiffItem selected) return;

        var olderText = ReadSnapshotFile(_diff.Older, selected.Item.RelativePath);
        var newerText = ReadSnapshotFile(_diff.Newer, selected.Item.RelativePath);
        var model = SideBySideDiffBuilder.Diff(olderText, newerText, ignoreWhiteSpace: false, ignoreCase: false);
        _viewer.DiffModel = model;
        ApplyCollapsedMode();

        _changeRows = GetChangedRows(model);
        _changeCursor = _changeRows.Count > 0 ? 0 : -1;
        UpdateChangePosition();

        var olderExists = SnapshotContains(_diff.Older, selected.Item.RelativePath);
        var newerExists = SnapshotContains(_diff.Newer, selected.Item.RelativePath);
        _fileSummary.Text =
            $"[{selected.Item.Kind}] {selected.Item.RelativePath}  /  변경 행 {selected.ChangedRows:N0}개" +
            (!olderExists ? "  /  이전 snapshot에 없음" : string.Empty) +
            (!newerExists ? "  /  이후 snapshot에 없음" : string.Empty);

        if (_changeCursor >= 0)
        {
            _viewer.GoTo(_changeRows[_changeCursor], isLeftLine: false);
        }
    }

    private void ApplyCollapsedMode()
    {
        _viewer.IgnoreUnchanged = _collapseUnchanged.IsChecked == true;
        _viewer.LinesContext = 3;
        _viewer.Refresh();
    }

    private void NavigateChange(int direction)
    {
        if (_changeRows.Count == 0)
        {
            _changePosition.Text = "차이 없음";
            return;
        }

        _changeCursor = (_changeCursor + direction + _changeRows.Count) % _changeRows.Count;
        _viewer.GoTo(_changeRows[_changeCursor], isLeftLine: false);
        UpdateChangePosition();
    }

    private void UpdateChangePosition()
    {
        _changePosition.Text = _changeRows.Count == 0
            ? "차이 없음"
            : $"{_changeCursor + 1:N0} / {_changeRows.Count:N0}";
    }

    private static IReadOnlyList<int> GetChangedRows(SideBySideDiffModel model)
    {
        var count = Math.Max(model.OldText.Lines.Count, model.NewText.Lines.Count);
        var rows = new List<int>();
        for (var index = 0; index < count; index++)
        {
            var oldType = index < model.OldText.Lines.Count ? model.OldText.Lines[index].Type : ChangeType.Imaginary;
            var newType = index < model.NewText.Lines.Count ? model.NewText.Lines[index].Type : ChangeType.Imaginary;
            if (oldType != ChangeType.Unchanged || newType != ChangeType.Unchanged)
                rows.Add(index);
        }
        return rows;
    }

    private static bool SnapshotContains(ConfigSnapshotManifest snapshot, string relativePath) =>
        snapshot.Files.Any(item => string.Equals(item.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

    private static string ReadSnapshotFile(ConfigSnapshotManifest snapshot, string relativePath)
    {
        if (!SnapshotContains(snapshot, relativePath)) return string.Empty;

        var filesRoot = Path.Combine(Path.GetDirectoryName(snapshot.ManifestPath)!, "files");
        var path = Path.GetFullPath(Path.Combine(filesRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(filesRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return string.Empty;

        try { return File.ReadAllText(path); }
        catch { return string.Empty; }
    }

    private sealed class DiffItem
    {
        public DiffItem(ConfigSnapshotDiffItem item, int changedRows)
        {
            Item = item;
            ChangedRows = changedRows;
        }

        public ConfigSnapshotDiffItem Item { get; }
        public int ChangedRows { get; }
        public override string ToString() => $"[{Item.Kind}] {Item.RelativePath}  ({ChangedRows:N0}행)";
    }
}
