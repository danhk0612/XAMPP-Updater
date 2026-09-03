using System.IO;
using System.Windows;
using System.Windows.Controls;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public sealed class ConfigSnapshotContentDiffWindow : Window
{
    private readonly ConfigSnapshotDiffResult _diff;
    private readonly ListBox _files = new() { MinWidth = 300 };
    private readonly TextBox _olderText = CreateViewer();
    private readonly TextBox _newerText = CreateViewer();

    public ConfigSnapshotContentDiffWindow(ConfigSnapshotDiffResult diff)
    {
        _diff = diff;
        Title = $"설정 내용 비교 - {diff.Older.Type}";
        Width = 1250;
        Height = 720;
        MinWidth = 900;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var summary = new TextBlock
        {
            Text = $"이전: {diff.Older.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} / {diff.Older.Version} / {diff.Older.Stage}\n" +
                   $"이후: {diff.Newer.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} / {diff.Newer.Version} / {diff.Newer.Stage}\n" +
                   $"변경 {diff.Changed} / 추가 {diff.Added} / 삭제 {diff.Removed} / 동일 {diff.Same}",
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetColumnSpan(summary, 3);
        root.Children.Add(summary);

        foreach (var item in diff.Items.Where(item => item.Kind != ConfigSnapshotDiffKind.Same))
            _files.Items.Add(new DiffItem(item));
        _files.SelectionChanged += (_, _) => ShowSelectedFile();
        Grid.SetRow(_files, 1);
        root.Children.Add(_files);

        var viewers = new Grid();
        viewers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        viewers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        viewers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        viewers.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        viewers.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var olderLabel = new TextBlock { Text = "이전 snapshot", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) };
        var newerLabel = new TextBlock { Text = "이후 snapshot", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) };
        Grid.SetColumn(olderLabel, 0);
        Grid.SetColumn(newerLabel, 2);
        viewers.Children.Add(olderLabel);
        viewers.Children.Add(newerLabel);
        Grid.SetRow(_olderText, 1);
        Grid.SetColumn(_olderText, 0);
        Grid.SetRow(_newerText, 1);
        Grid.SetColumn(_newerText, 2);
        viewers.Children.Add(_olderText);
        viewers.Children.Add(_newerText);

        Grid.SetRow(viewers, 1);
        Grid.SetColumn(viewers, 2);
        root.Children.Add(viewers);

        var close = new Button
        {
            Content = "닫기",
            Padding = new Thickness(18, 5, 18, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            IsCancel = true
        };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 2);
        Grid.SetColumnSpan(close, 3);
        root.Children.Add(close);

        Content = root;
        if (_files.Items.Count > 0) _files.SelectedIndex = 0;
    }

    private void ShowSelectedFile()
    {
        if (_files.SelectedItem is not DiffItem selected) return;
        _olderText.Text = ReadSnapshotFile(_diff.Older, selected.Item.RelativePath);
        _newerText.Text = ReadSnapshotFile(_diff.Newer, selected.Item.RelativePath);
    }

    private static string ReadSnapshotFile(ConfigSnapshotManifest snapshot, string relativePath)
    {
        if (!snapshot.Files.Any(item => string.Equals(item.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)))
            return "<이 snapshot에는 파일이 없습니다>";
        var filesRoot = Path.Combine(Path.GetDirectoryName(snapshot.ManifestPath)!, "files");
        var path = Path.GetFullPath(Path.Combine(filesRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(filesRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            return "<snapshot 파일을 찾을 수 없습니다>";
        try { return File.ReadAllText(path); }
        catch (Exception ex) { return "<파일 읽기 실패: " + ex.Message + ">"; }
    }

    private static TextBox CreateViewer() => new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
        FontSize = 12
    };

    private sealed class DiffItem
    {
        public DiffItem(ConfigSnapshotDiffItem item) => Item = item;
        public ConfigSnapshotDiffItem Item { get; }
        public override string ToString() => $"[{Item.Kind}] {Item.RelativePath}";
    }
}
