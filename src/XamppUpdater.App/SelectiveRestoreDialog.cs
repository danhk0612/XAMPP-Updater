using System.Windows;
using System.Windows.Controls;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public sealed class SelectiveRestoreDialog : Window
{
    private readonly List<RestoreItem> _items = new();
    private readonly StackPanel _list = new();
    private readonly TextBlock _summary = new() { Margin = new Thickness(0, 0, 0, 8) };

    public IReadOnlyList<string> SelectedPaths => _items.Where(item => item.CheckBox.IsChecked == true).Select(item => item.Path).ToArray();

    public SelectiveRestoreDialog(ConfigSnapshotDiff diff)
    {
        Title = LocalizationCatalog.Text($"선택 설정 복원 - {diff.Older.Type}", $"Selective configuration restore - {diff.Older.Type}");
        Width = 780;
        Height = 620;
        MinWidth = 640;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new StackPanel();
        top.Children.Add(new TextBlock
        {
            Text = LocalizationCatalog.Text(
                "snapshot 상태로 되돌릴 파일만 선택하세요. 현재 설정과 동일한 파일은 목록에서 제외됩니다.",
                "Select only the files to restore to the snapshot state. Files identical to the current configuration are omitted."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });
        top.Children.Add(new TextBlock
        {
            Text = LocalizationCatalog.Text(
                "'현재에만 존재' 항목을 선택하면 해당 파일은 삭제됩니다. 적용 전에는 전체 현재 설정이 안전 snapshot으로 저장됩니다.",
                "Selecting an item that exists only in the current configuration will delete that file. The complete current configuration is saved as a safety snapshot before applying changes."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        top.Children.Add(_summary);

        var selectButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var all = new Button
        {
            Content = LocalizationCatalog.Text("전체 선택", "Select all"),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0)
        };
        all.Click += (_, _) => { foreach (var item in _items) item.CheckBox.IsChecked = true; UpdateSummary(); };
        var none = new Button
        {
            Content = LocalizationCatalog.Text("전체 해제", "Clear selection"),
            Padding = new Thickness(10, 4, 10, 4)
        };
        none.Click += (_, _) => { foreach (var item in _items) item.CheckBox.IsChecked = false; UpdateSummary(); };
        selectButtons.Children.Add(all);
        selectButtons.Children.Add(none);
        top.Children.Add(selectButtons);
        root.Children.Add(top);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 10, 0, 10),
            Content = _list
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        foreach (var item in diff.Items.Where(item => item.Kind != ConfigSnapshotDiffKind.Same))
        {
            var label = item.Kind switch
            {
                ConfigSnapshotDiffKind.Changed => LocalizationCatalog.Text("변경됨 — snapshot 파일로 덮어쓰기", "Changed — overwrite with snapshot file"),
                ConfigSnapshotDiffKind.Added => LocalizationCatalog.Text("현재에만 존재 — 선택 시 삭제", "Exists only in current configuration — delete when selected"),
                ConfigSnapshotDiffKind.Removed => LocalizationCatalog.Text("snapshot에만 존재 — 선택 시 복원", "Exists only in snapshot — restore when selected"),
                _ => item.Kind.ToString()
            };
            var check = new CheckBox
            {
                IsChecked = true,
                Content = $"{item.RelativePath}\n    {label}",
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(4),
                Tag = item.RelativePath
            };
            check.Checked += (_, _) => UpdateSummary();
            check.Unchecked += (_, _) => UpdateSummary();
            _items.Add(new RestoreItem(item.RelativePath, check));
            _list.Children.Add(check);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var apply = new Button
        {
            Content = LocalizationCatalog.Text("선택 파일 복원", "Restore selected files"),
            Padding = new Thickness(16, 5, 16, 5),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        apply.Click += (_, _) =>
        {
            if (SelectedPaths.Count == 0)
            {
                MessageBox.Show(
                    this,
                    LocalizationCatalog.Text("복원할 파일을 하나 이상 선택하세요.", "Select at least one file to restore."),
                    LocalizationCatalog.Text("선택 설정 복원", "Selective configuration restore"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
            Close();
        };
        var cancel = new Button
        {
            Content = LocalizationCatalog.Text("취소", "Cancel"),
            Padding = new Thickness(16, 5, 16, 5),
            IsCancel = true
        };
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var selected = _items.Count(item => item.CheckBox.IsChecked == true);
        _summary.Text = LocalizationCatalog.Text(
            $"변경 파일 {_items.Count:N0}개 / 복원 선택 {selected:N0}개",
            $"Changed files: {_items.Count:N0} / selected for restore: {selected:N0}");
    }

    private sealed record RestoreItem(string Path, CheckBox CheckBox);
}
