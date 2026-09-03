using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public sealed class ConfigHistoryWindow : Window
{
    private readonly IReadOnlyList<ConfigSnapshotManifest> _snapshots;
    private readonly IConfigSnapshotCompareService _compare = new ConfigSnapshotCompareService();
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Extended, MinWidth = 390 };
    private readonly TextBox _details = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new System.Windows.Media.FontFamily("Consolas")
    };

    public ConfigHistoryWindow(string xamppRoot, XamppComponentType type, IConfigSnapshotService? service = null)
    {
        var snapshots = service ?? new ConfigSnapshotService();
        _snapshots = snapshots.List(xamppRoot, type);

        Title = $"{type} 설정 이력";
        Width = 980;
        Height = 620;
        MinWidth = 760;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        foreach (var snapshot in _snapshots)
        {
            _list.Items.Add(new SnapshotItem(snapshot));
        }
        _list.SelectionChanged += (_, _) => ShowSelection();
        Grid.SetColumn(_list, 0);
        root.Children.Add(_list);

        Grid.SetColumn(_details, 2);
        root.Children.Add(_details);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var open = new Button { Content = "선택 snapshot 폴더 열기", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        open.Click += (_, _) => OpenSelectedFolder();
        var compare = new Button { Content = "선택한 2개 비교", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        compare.Click += (_, _) => CompareSelected();
        var close = new Button { Content = "닫기", Padding = new Thickness(18, 5, 18, 5), IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(open);
        buttons.Children.Add(compare);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
        if (_snapshots.Count == 0)
        {
            _details.Text = "저장된 설정 snapshot이 없습니다.\r\n\r\n이 기능이 적용된 버전으로 업데이트를 실행하면 업데이트 전/후 snapshot이 자동 저장됩니다.";
        }
        else
        {
            _list.SelectedIndex = 0;
        }
    }

    private void ShowSelection()
    {
        if (_list.SelectedItems.Count != 1 || _list.SelectedItem is not SnapshotItem selected) return;
        var snapshot = selected.Manifest;
        _details.Text =
            $"캡처: {snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff}\r\n" +
            $"단계: {snapshot.Stage}\r\n" +
            $"버전: {snapshot.Version ?? "Unknown"}\r\n" +
            $"XAMPP: {snapshot.XamppRoot}\r\n" +
            $"manifest: {snapshot.ManifestPath}\r\n" +
            $"설정 파일: {snapshot.Files.Count}개\r\n\r\n" +
            string.Join("\r\n", snapshot.Files.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"{item.RelativePath}  {item.Size:N0} bytes  {item.Sha256}"));
    }

    private void CompareSelected()
    {
        var selected = _list.SelectedItems.Cast<SnapshotItem>().Select(item => item.Manifest).OrderBy(item => item.CapturedAt).ToArray();
        if (selected.Length != 2)
        {
            MessageBox.Show(this, "비교할 snapshot을 정확히 2개 선택하세요.", "설정 이력 비교", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var diff = _compare.Compare(selected[0], selected[1]);
            _details.Text =
                $"이전: {diff.Older.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} / {diff.Older.Version} / {diff.Older.Stage}\r\n" +
                $"이후: {diff.Newer.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} / {diff.Newer.Version} / {diff.Newer.Stage}\r\n\r\n" +
                $"변경 {diff.Changed} / 추가 {diff.Added} / 삭제 {diff.Removed} / 동일 {diff.Same}\r\n\r\n" +
                string.Join("\r\n", diff.Items
                    .Where(item => item.Kind != ConfigSnapshotDiffKind.Same)
                    .Select(item => $"[{item.Kind}] {item.RelativePath}"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "설정 이력 비교", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSelectedFolder()
    {
        if (_list.SelectedItem is not SnapshotItem selected) return;
        var folder = Path.GetDirectoryName(selected.Manifest.ManifestPath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    private sealed class SnapshotItem
    {
        public SnapshotItem(ConfigSnapshotManifest manifest) => Manifest = manifest;
        public ConfigSnapshotManifest Manifest { get; }
        public override string ToString() => $"{Manifest.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}  {Manifest.Stage}  {Manifest.Version ?? "Unknown"}  ({Manifest.Files.Count}개)";
    }
}
