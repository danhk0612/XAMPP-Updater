using System.Windows;
using System.Windows.Controls;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public sealed class ConfigEntryMergeWindow : Window
{
    private readonly List<EntryRow> _rows = new();
    private readonly StackPanel _items = new();
    private readonly TextBlock _summary = new() { Margin = new Thickness(0, 0, 0, 10) };

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Selections => _rows
        .Where(x => x.CheckBox.IsChecked == true && x.Item.CanApply)
        .GroupBy(x => x.Item.RelativePath, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => (IReadOnlyCollection<string>)g.Select(x => x.Item.Identity).ToArray(), StringComparer.OrdinalIgnoreCase);

    public ConfigEntryMergeWindow(IReadOnlyList<ConfigEntryMergeItem> items)
    {
        Title = "설정 항목 선택 병합";
        Width = 1050;
        Height = 720;
        MinWidth = 760;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(_summary, Dock.Top);
        root.Children.Add(_summary);

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var all = new Button { Content = "적용 가능 전체 선택", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        all.Click += (_, _) => { foreach (var row in _rows.Where(x => x.Item.CanApply)) row.CheckBox.IsChecked = true; UpdateSummary(); };
        var none = new Button { Content = "전체 해제", Padding = new Thickness(10, 5, 10, 5) };
        none.Click += (_, _) => { foreach (var row in _rows) row.CheckBox.IsChecked = false; UpdateSummary(); };
        buttons.Children.Add(all);
        buttons.Children.Add(none);
        DockPanel.SetDock(buttons, Dock.Top);
        root.Children.Add(buttons);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.Content = _items;
        root.Children.Add(scroll);

        string? lastFile = null;
        foreach (var item in items.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.Equals(lastFile, item.RelativePath, StringComparison.OrdinalIgnoreCase))
            {
                _items.Children.Add(new TextBlock { Text = item.RelativePath, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 5) });
                lastFile = item.RelativePath;
            }
            var check = new CheckBox
            {
                IsChecked = item.CanApply,
                IsEnabled = item.CanApply,
                Margin = new Thickness(8, 3, 0, 3),
                Content = item.CanApply
                    ? $"{item.DisplayName}    현재: {Short(item.CurrentValue)}    ← snapshot: {Short(item.SnapshotValue)}"
                    : $"[수동 확인] {item.DisplayName}    {item.Reason}"
            };
            check.Checked += (_, _) => UpdateSummary();
            check.Unchecked += (_, _) => UpdateSummary();
            _rows.Add(new EntryRow(item, check));
            _items.Children.Add(check);
        }

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var apply = new Button { Content = "선택 항목 적용", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        apply.Click += (_, _) =>
        {
            if (Selections.Count == 0)
            {
                MessageBox.Show(this, "적용할 설정 항목을 1개 이상 선택하세요.", "항목 병합", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
            Close();
        };
        var cancel = new Button { Content = "취소", Padding = new Thickness(16, 6, 16, 6), IsCancel = true };
        bottom.Children.Add(apply);
        bottom.Children.Add(cancel);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        Content = root;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var auto = _rows.Count(x => x.Item.CanApply);
        var conflicts = _rows.Count(x => !x.Item.CanApply);
        var selected = _rows.Count(x => x.Item.CanApply && x.CheckBox.IsChecked == true);
        _summary.Text = $"변경 항목 { _rows.Count:N0}개 / 자동 적용 가능 {auto:N0}개 / 수동 확인 {conflicts:N0}개 / 선택 {selected:N0}개\n" +
                        "자동 적용은 기존 줄의 값만 snapshot 값으로 교체하며 주석과 주변 구조는 유지합니다.";
    }

    private static string Short(string? value)
    {
        if (value is null) return "(없음)";
        var one = value.Replace("\r", " ").Replace("\n", " ");
        return one.Length <= 100 ? one : one[..100] + "…";
    }

    private sealed record EntryRow(ConfigEntryMergeItem Item, CheckBox CheckBox);
}
