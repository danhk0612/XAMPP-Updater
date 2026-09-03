using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public sealed class ConfigHistoryWindow : Window
{
    private readonly XamppInstallation _installation;
    private readonly IConfigSnapshotService _snapshots;
    private readonly IConfigSnapshotCompareService _compare = new ConfigSnapshotCompareService();
    private readonly ConfigSnapshotRestoreService _restore = new();
    private readonly ComboBox _filter = new() { Width = 150, Margin = new Thickness(8, 0, 0, 0) };
    private readonly DataGrid _list = new()
    {
        SelectionMode = DataGridSelectionMode.Extended,
        SelectionUnit = DataGridSelectionUnit.FullRow,
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        MinWidth = 610
    };
    private readonly TextBox _details = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new System.Windows.Media.FontFamily("Consolas")
    };

    public ConfigHistoryWindow(XamppInstallation installation, IConfigSnapshotService? service = null)
        : this(installation, null, service)
    {
    }

    public ConfigHistoryWindow(XamppInstallation installation, XamppComponentType type, IConfigSnapshotService? service = null)
        : this(installation, (XamppComponentType?)type, service)
    {
    }

    private ConfigHistoryWindow(XamppInstallation installation, XamppComponentType? initialFilter, IConfigSnapshotService? service)
    {
        _installation = installation;
        _snapshots = service ?? new ConfigSnapshotService();

        Title = "설정 이력 및 복원";
        Width = 1260;
        Height = 720;
        MinWidth = 980;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        BuildColumns();
        _list.SelectionChanged += (_, _) => ShowSelection();

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(new TextBlock
        {
            Text = "설정 이력",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(_filter, 1);
        header.Children.Add(_filter);
        var hint = new TextBlock
        {
            Text = "업데이트 전·후 설정을 비교하고 문제가 있을 때 안전하게 복원합니다.",
            Foreground = System.Windows.Media.Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(hint, 2);
        header.Children.Add(hint);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetColumn(_list, 0);
        body.Children.Add(_list);
        Grid.SetColumn(_details, 2);
        body.Children.Add(_details);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var primary = new WrapPanel { Orientation = Orientation.Horizontal };
        AddButton(primary, "현재와 비교", (_, _) => CompareWithCurrent());
        AddButton(primary, "복원", RestoreSelected_Click);
        AddButton(primary, "삭제", (_, _) => DeleteSelected());
        footer.Children.Add(primary);

        var secondary = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var moreButton = new Button
        {
            Content = "더보기 ▼",
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "고급 복원, 비교, 무결성 검사 등 자주 사용하지 않는 작업을 엽니다."
        };
        var moreMenu = BuildMoreMenu();
        moreButton.ContextMenu = moreMenu;
        moreButton.Click += (_, _) =>
        {
            moreMenu.PlacementTarget = moreButton;
            moreMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            moreMenu.IsOpen = true;
        };
        secondary.Children.Add(moreButton);

        var close = new Button { Content = "닫기", Padding = new Thickness(18, 5, 18, 5), IsCancel = true };
        close.Click += (_, _) => Close();
        secondary.Children.Add(close);
        Grid.SetColumn(secondary, 1);
        footer.Children.Add(secondary);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;

        _filter.ItemsSource = new object[] { "전체", XamppComponentType.Apache, XamppComponentType.Php, XamppComponentType.MariaDb };
        _filter.SelectedItem = initialFilter is null ? "전체" : initialFilter.Value;
        _filter.SelectionChanged += (_, _) => ReloadSnapshots();
        ReloadSnapshots();
    }

    private ContextMenu BuildMoreMenu()
    {
        var menu = new ContextMenu();
        var selectiveRestore = AddMenuItem(menu, "파일 선택 복원", SelectiveRestore_Click);
        var entryMerge = AddMenuItem(menu, "설정 항목 병합", EntryMerge_Click);
        menu.Items.Add(new Separator());
        var compareTwo = AddMenuItem(menu, "선택한 2개 snapshot 비교", (_, _) => CompareSelected());
        var verify = AddMenuItem(menu, "무결성 검사", (_, _) => VerifySelected());
        var editNote = AddMenuItem(menu, "메모 수정", (_, _) => EditSelectedNote());
        var openFolder = AddMenuItem(menu, "snapshot 폴더 열기", (_, _) => OpenSelectedFolder());
        menu.Items.Add(new Separator());
        var manual = AddMenuItem(menu, "현재 설정 snapshot 저장", ManualSnapshot_Click);

        menu.Opened += (_, _) =>
        {
            var count = _list.SelectedItems.Count;
            var selected = _list.SelectedItems.Cast<SnapshotItem>().Select(item => item.Manifest).ToArray();
            var one = count == 1;
            var sameTypeTwo = count == 2 && selected.Select(item => item.Type).Distinct().Count() == 1;

            selectiveRestore.IsEnabled = one;
            entryMerge.IsEnabled = one;
            compareTwo.IsEnabled = sameTypeTwo;
            verify.IsEnabled = count >= 1;
            editNote.IsEnabled = one;
            openFolder.IsEnabled = one;
            manual.IsEnabled = SelectedFilterType() is not null;
        };
        return menu;
    }

    private static MenuItem AddMenuItem(ItemsControl menu, string text, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = text };
        item.Click += handler;
        menu.Items.Add(item);
        return item;
    }

    private void BuildColumns()
    {
        _list.Columns.Add(new DataGridTextColumn { Header = "구성요소", Binding = new System.Windows.Data.Binding(nameof(SnapshotItem.Component)), Width = 90 });
        _list.Columns.Add(new DataGridTextColumn { Header = "시각", Binding = new System.Windows.Data.Binding(nameof(SnapshotItem.Captured)), Width = 155 });
        _list.Columns.Add(new DataGridTextColumn { Header = "단계", Binding = new System.Windows.Data.Binding(nameof(SnapshotItem.Stage)), Width = 150 });
        _list.Columns.Add(new DataGridTextColumn { Header = "버전", Binding = new System.Windows.Data.Binding(nameof(SnapshotItem.Version)), Width = 95 });
        _list.Columns.Add(new DataGridTextColumn { Header = "파일", Binding = new System.Windows.Data.Binding(nameof(SnapshotItem.FileCount)), Width = 55 });
        _list.Columns.Add(new DataGridTextColumn { Header = "메모", Binding = new System.Windows.Data.Binding(nameof(SnapshotItem.Note)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
    }

    private static void AddButton(Panel panel, string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 6) };
        button.Click += handler;
        panel.Children.Add(button);
    }

    private XamppComponentType? SelectedFilterType() => _filter.SelectedItem is XamppComponentType type ? type : null;

    private void ReloadSnapshots()
    {
        var selectedPaths = _list.SelectedItems.Cast<SnapshotItem>().Select(item => item.Manifest.ManifestPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var types = SelectedFilterType() is { } type
            ? new[] { type }
            : new[] { XamppComponentType.Apache, XamppComponentType.Php, XamppComponentType.MariaDb };
        var items = types
            .SelectMany(item => _snapshots.List(_installation.RootPath, item))
            .OrderByDescending(item => item.CapturedAt)
            .Select(item => new SnapshotItem(item))
            .ToArray();
        _list.ItemsSource = items;

        foreach (var item in items.Where(item => selectedPaths.Contains(item.Manifest.ManifestPath)))
            _list.SelectedItems.Add(item);

        if (items.Length == 0)
        {
            _details.Text = "저장된 설정 snapshot이 없습니다.\r\n\r\n업데이트 실행 시 전·후 snapshot이 자동으로 저장됩니다.";
        }
        else if (_list.SelectedItems.Count == 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    private void SelectSnapshot(string manifestPath)
    {
        foreach (var item in _list.Items.OfType<SnapshotItem>())
        {
            if (!string.Equals(item.Manifest.ManifestPath, manifestPath, StringComparison.OrdinalIgnoreCase)) continue;
            _list.SelectedItem = item;
            _list.ScrollIntoView(item);
            return;
        }
    }

    private ConfigSnapshotManifest? SingleSelected(string action)
    {
        if (_list.SelectedItems.Count == 1 && _list.SelectedItem is SnapshotItem selected) return selected.Manifest;
        MessageBox.Show(this, $"{action}할 snapshot을 정확히 1개 선택하세요.", "설정 이력", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private ConfigSnapshotManifest[] MultiSelected(string action)
    {
        var selected = _list.SelectedItems.Cast<SnapshotItem>().Select(item => item.Manifest).ToArray();
        if (selected.Length > 0) return selected;
        MessageBox.Show(this, $"{action}할 snapshot을 1개 이상 선택하세요.", "설정 이력", MessageBoxButton.OK, MessageBoxImage.Information);
        return Array.Empty<ConfigSnapshotManifest>();
    }

    private void ShowSelection()
    {
        if (_list.SelectedItems.Count > 1)
        {
            var groups = _list.SelectedItems.Cast<SnapshotItem>().GroupBy(item => item.Manifest.Type)
                .Select(group => $"{group.Key} {group.Count():N0}개");
            _details.Text = $"snapshot {_list.SelectedItems.Count:N0}개 선택됨\r\n{string.Join(" / ", groups)}\r\n\r\n다중 선택은 삭제와 무결성 검사에 사용할 수 있습니다. snapshot 비교는 같은 구성요소 2개를 선택해야 합니다.";
            return;
        }
        if (_list.SelectedItems.Count != 1 || _list.SelectedItem is not SnapshotItem selected) return;
        var snapshot = selected.Manifest;
        _details.Text =
            $"구성요소: {snapshot.Type}\r\n캡처: {snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff}\r\n" +
            $"단계: {snapshot.Stage}\r\n버전: {snapshot.Version ?? "Unknown"}\r\n메모: {snapshot.Note ?? "(없음)"}\r\n" +
            $"XAMPP: {snapshot.XamppRoot}\r\nmanifest: {snapshot.ManifestPath}\r\n설정 파일: {snapshot.Files.Count}개\r\n\r\n" +
            string.Join("\r\n", snapshot.Files.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"{item.RelativePath}  {item.Size:N0} bytes  {item.Sha256}"));
    }

    private void ManualSnapshot_Click(object sender, RoutedEventArgs e)
    {
        var type = SelectedFilterType();
        if (type is null)
        {
            MessageBox.Show(this, "현재 설정 snapshot을 저장하려면 상단 필터에서 Apache, PHP 또는 MariaDB 중 하나를 선택하세요.", "설정 snapshot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new ManualSnapshotDialog(type.Value.ToString()) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var snapshot = _snapshots.Capture(_installation.RootPath, type.Value, CurrentVersion(type.Value), "Manual", dialog.Note);
            ReloadSnapshots();
            SelectSnapshot(snapshot.ManifestPath);
            MessageBox.Show(this, $"현재 {type.Value} 설정 snapshot을 저장했습니다.", "설정 snapshot", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "설정 snapshot", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void CompareSelected()
    {
        var selected = _list.SelectedItems.Cast<SnapshotItem>().Select(item => item.Manifest).OrderBy(item => item.CapturedAt).ToArray();
        if (selected.Length != 2)
        {
            MessageBox.Show(this, "비교할 snapshot을 정확히 2개 선택하세요.", "설정 이력 비교", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (selected[0].Type != selected[1].Type)
        {
            MessageBox.Show(this, "서로 다른 구성요소의 snapshot은 비교할 수 없습니다.", "설정 이력 비교", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { new ConfigSnapshotContentDiffWindow(_compare.Compare(selected[0], selected[1])) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "설정 이력 비교", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void CompareWithCurrent()
    {
        var selected = SingleSelected("현재 설정과 비교");
        if (selected is null) return;
        ConfigSnapshotManifest? current = null;
        try
        {
            current = _snapshots.CaptureTemporary(_installation.RootPath, selected.Type, CurrentVersion(selected.Type));
            new ConfigSnapshotContentDiffWindow(_compare.Compare(selected, current)) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "현재 설정과 비교", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { if (current is not null) { try { _snapshots.Delete(current); } catch { } } }
    }

    private void VerifySelected()
    {
        var selected = MultiSelected("검사");
        if (selected.Length == 0) return;
        var validCount = 0;
        var totalFiles = 0;
        var failed = new List<string>();
        foreach (var snapshot in selected)
        {
            var result = _snapshots.Verify(snapshot);
            totalFiles += result.VerifiedFiles;
            if (result.Valid) { validCount++; continue; }
            var name = $"{snapshot.Type} / {snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} / {snapshot.Stage}";
            failed.Add(name + Environment.NewLine + string.Join(Environment.NewLine, result.Errors.Take(4).Select(error => "  - " + error)));
        }
        var summary = $"선택 snapshot: {selected.Length:N0}개\n정상: {validCount:N0}개\n문제: {selected.Length - validCount:N0}개\n검증 성공 파일: {totalFiles:N0}개";
        if (failed.Count > 0) summary += "\n\n[문제 snapshot]\n" + string.Join("\n\n", failed.Take(8));
        _details.Text = summary.Replace("\n", "\r\n");
        MessageBox.Show(this, summary, "snapshot 무결성 검사", MessageBoxButton.OK, failed.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void EditSelectedNote()
    {
        var selected = SingleSelected("메모 수정");
        if (selected is null) return;
        var dialog = new ManualSnapshotDialog(selected.Type.ToString(), selected.Note, editMode: true) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var updated = _snapshots.UpdateNote(selected, dialog.Note);
            ReloadSnapshots();
            SelectSnapshot(updated.ManifestPath);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "메모 수정", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void DeleteSelected()
    {
        var selected = MultiSelected("삭제");
        if (selected.Length == 0) return;
        var preview = string.Join("\n", selected.OrderByDescending(item => item.CapturedAt).Take(10)
            .Select(item => $"- {item.Type} / {item.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} / {item.Stage} / {item.Version ?? "Unknown"}"));
        if (selected.Length > 10) preview += $"\n- 외 {selected.Length - 10:N0}개";
        var answer = MessageBox.Show(this,
            $"선택한 snapshot {selected.Length:N0}개를 삭제합니다.\n실제 설정에는 영향을 주지 않지만 삭제한 이력은 복구할 수 없습니다.\n\n{preview}\n\n삭제하시겠습니까?",
            "snapshot 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        var deleted = 0;
        var failures = new List<string>();
        foreach (var snapshot in selected)
        {
            try { _snapshots.Delete(snapshot); deleted++; }
            catch (Exception ex) { failures.Add($"{snapshot.Type} {snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}: {ex.Message}"); }
        }
        ReloadSnapshots();
        MessageBox.Show(this,
            failures.Count == 0 ? $"snapshot {deleted:N0}개를 삭제했습니다." : $"삭제 완료: {deleted:N0}개\n삭제 실패: {failures.Count:N0}개\n\n{string.Join("\n", failures.Take(8))}",
            "snapshot 삭제", MessageBoxButton.OK, failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void EntryMerge_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = SingleSelected("설정 항목 병합");
        if (snapshot is null) return;
        if (!AdministratorPrivilege.EnsureElevated(this, _installation.RootPath, $"{snapshot.Type} 설정 항목 병합")) return;
        ConfigSnapshotManifest? current = null;
        try
        {
            current = _snapshots.CaptureTemporary(_installation.RootPath, snapshot.Type, CurrentVersion(snapshot.Type));
            var items = new ConfigEntryMergeService().Compare(snapshot, current);
            if (items.Count == 0)
            {
                MessageBox.Show(this, "자동 비교 가능한 설정 항목에서 차이를 찾지 못했습니다.", "설정 항목 병합", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dialog = new ConfigEntryMergeWindow(items) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var selections = dialog.Selections;
            var selectedCount = selections.Sum(x => x.Value.Count);
            if (MessageBox.Show(this, $"선택한 설정 항목 {selectedCount:N0}개를 snapshot 값으로 병합합니다.\n검증 실패 시 직전 설정으로 자동 원복합니다. 계속하시겠습니까?", "설정 항목 병합", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            IsEnabled = false;
            var result = await _restore.MergeEntriesAsync(_installation, snapshot, selections);
            ReloadSnapshots();
            MessageBox.Show(this, result.Success ? $"설정 항목 {selectedCount:N0}개 병합을 완료했습니다." : $"설정 항목 병합에 실패했습니다.\n\n{result.Error}\n\n" + (result.RolledBack ? "직전 설정으로 자동 원복했습니다." : "자동 원복도 완료되지 않았습니다."), "설정 항목 병합", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "설정 항목 병합", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally
        {
            IsEnabled = true;
            if (current is not null) { try { _snapshots.Delete(current); } catch { } }
        }
    }

    private async void SelectiveRestore_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = SingleSelected("파일 선택 복원");
        if (snapshot is null) return;
        if (!AdministratorPrivilege.EnsureElevated(this, _installation.RootPath, $"{snapshot.Type} 파일 선택 복원")) return;
        ConfigSnapshotManifest? current = null;
        try
        {
            current = _snapshots.CaptureTemporary(_installation.RootPath, snapshot.Type, CurrentVersion(snapshot.Type));
            var diff = _compare.Compare(snapshot, current);
            if (diff.Items.All(item => item.Kind == ConfigSnapshotDiffKind.Same))
            {
                MessageBox.Show(this, "선택한 snapshot과 현재 설정이 동일합니다.", "파일 선택 복원", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dialog = new SelectiveRestoreDialog(diff) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var paths = dialog.SelectedPaths;
            if (MessageBox.Show(this, $"선택한 설정 파일 {paths.Count:N0}개를 snapshot 상태로 복원합니다.\n검증 실패 시 직전 설정으로 자동 원복합니다. 계속하시겠습니까?", "파일 선택 복원", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            IsEnabled = false;
            var result = await _restore.RestoreSelectedAsync(_installation, snapshot, paths);
            ReloadSnapshots();
            MessageBox.Show(this, result.Success ? $"선택한 설정 파일 {paths.Count:N0}개의 복원이 완료되었습니다." : $"파일 선택 복원에 실패했습니다.\n\n{result.Error}\n\n" + (result.RolledBack ? "직전 설정으로 자동 원복했습니다." : "자동 원복도 완료되지 않았습니다."), "파일 선택 복원", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "파일 선택 복원", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally
        {
            IsEnabled = true;
            if (current is not null) { try { _snapshots.Delete(current); } catch { } }
        }
    }

    private async void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = SingleSelected("복원");
        if (snapshot is null) return;
        if (!AdministratorPrivilege.EnsureElevated(this, _installation.RootPath, $"{snapshot.Type} 설정 복원")) return;
        var answer = MessageBox.Show(this,
            $"{snapshot.Type} 설정을 선택한 snapshot 상태로 전체 복원합니다.\n\nsnapshot: {snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}\n당시 버전: {snapshot.Version ?? "Unknown"}\n단계: {snapshot.Stage}\n메모: {snapshot.Note ?? "(없음)"}\n\n현재 설정은 복원 직전에 안전 snapshot으로 자동 저장하고 검증 실패 시 자동 원복합니다. 계속하시겠습니까?",
            "설정 복원", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        IsEnabled = false;
        try
        {
            var result = await _restore.RestoreAsync(_installation, snapshot);
            ReloadSnapshots();
            MessageBox.Show(this, result.Success ? $"{snapshot.Type} 설정 복원이 완료되었습니다." : $"설정 복원에 실패했습니다.\n\n{result.Error}\n\n" + (result.RolledBack ? "복원 직전 설정으로 자동 원복했습니다." : "자동 원복도 완료되지 않았습니다."), "설정 복원", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "설정 복원", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsEnabled = true; }
    }

    private void OpenSelectedFolder()
    {
        var selected = SingleSelected("폴더 열기");
        if (selected is null) return;
        var folder = Path.GetDirectoryName(selected.ManifestPath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    private string? CurrentVersion(XamppComponentType type) => _installation.Components.FirstOrDefault(item => item.Type == type)?.Version;

    private sealed class SnapshotItem
    {
        public SnapshotItem(ConfigSnapshotManifest manifest) => Manifest = manifest;
        public ConfigSnapshotManifest Manifest { get; }
        public string Component => Manifest.Type.ToString();
        public string Captured => Manifest.CapturedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        public string Stage => Manifest.Stage;
        public string Version => Manifest.Version ?? "Unknown";
        public int FileCount => Manifest.Files.Count;
        public string Note => Manifest.Note ?? string.Empty;
    }
}
