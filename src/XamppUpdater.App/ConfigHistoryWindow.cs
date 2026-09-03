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
    private readonly XamppComponentType _type;
    private readonly IConfigSnapshotService _snapshots;
    private readonly IConfigSnapshotCompareService _compare = new ConfigSnapshotCompareService();
    private readonly IConfigSnapshotRestoreService _restore = new ConfigSnapshotRestoreService();
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Extended, MinWidth = 410 };
    private readonly TextBox _details = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new System.Windows.Media.FontFamily("Consolas")
    };

    public ConfigHistoryWindow(XamppInstallation installation, XamppComponentType type, IConfigSnapshotService? service = null)
    {
        _installation = installation;
        _type = type;
        _snapshots = service ?? new ConfigSnapshotService();

        Title = $"{type} 설정 이력";
        Width = 1160;
        Height = 680;
        MinWidth = 900;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(410) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _list.SelectionChanged += (_, _) => ShowSelection();
        Grid.SetColumn(_list, 0);
        root.Children.Add(_list);

        Grid.SetColumn(_details, 2);
        root.Children.Add(_details);

        var buttons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        AddButton(buttons, "현재 설정 snapshot 저장", ManualSnapshot_Click);
        AddButton(buttons, "현재 설정과 비교", (_, _) => CompareWithCurrent());
        AddButton(buttons, "선택한 2개 내용 비교", (_, _) => CompareSelected());
        AddButton(buttons, "무결성 검사", (_, _) => VerifySelected());
        AddButton(buttons, "메모 수정", (_, _) => EditSelectedNote());
        AddButton(buttons, "snapshot 폴더 열기", (_, _) => OpenSelectedFolder());
        AddButton(buttons, "snapshot 삭제", (_, _) => DeleteSelected());
        AddButton(buttons, "파일 선택 복원", SelectiveRestore_Click);
        AddButton(buttons, "전체 snapshot 복원", RestoreSelected_Click);
        var close = new Button { Content = "닫기", Padding = new Thickness(18, 5, 18, 5), Margin = new Thickness(0, 0, 0, 6), IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
        ReloadSnapshots();
    }

    private static void AddButton(Panel panel, string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 6) };
        button.Click += handler;
        panel.Children.Add(button);
    }

    private void ManualSnapshot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ManualSnapshotDialog(_type.ToString()) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var snapshot = _snapshots.Capture(_installation.RootPath, _type, CurrentVersion(), "Manual", dialog.Note);
            ReloadSnapshots();
            SelectSnapshot(snapshot.ManifestPath);
            MessageBox.Show(this, $"현재 {_type} 설정 snapshot을 저장했습니다.\n\n{snapshot.ManifestPath}", "설정 snapshot", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "설정 snapshot", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ReloadSnapshots()
    {
        _list.Items.Clear();
        foreach (var snapshot in _snapshots.List(_installation.RootPath, _type)) _list.Items.Add(new SnapshotItem(snapshot));
        if (_list.Items.Count == 0)
            _details.Text = "저장된 설정 snapshot이 없습니다.\r\n\r\n'현재 설정 snapshot 저장'으로 기준점을 만들거나, 업데이트 실행 시 자동으로 전/후 snapshot이 저장됩니다.";
        else _list.SelectedIndex = 0;
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
            _details.Text = $"snapshot {_list.SelectedItems.Count:N0}개 선택됨\r\n\r\n무결성 검사와 snapshot 삭제는 여러 snapshot을 한 번에 처리할 수 있습니다.";
            return;
        }
        if (_list.SelectedItems.Count != 1 || _list.SelectedItem is not SnapshotItem selected) return;
        var snapshot = selected.Manifest;
        _details.Text =
            $"캡처: {snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff}\r\n" +
            $"단계: {snapshot.Stage}\r\n버전: {snapshot.Version ?? "Unknown"}\r\n메모: {snapshot.Note ?? "(없음)"}\r\n" +
            $"XAMPP: {snapshot.XamppRoot}\r\nmanifest: {snapshot.ManifestPath}\r\n설정 파일: {snapshot.Files.Count}개\r\n\r\n" +
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
            current = _snapshots.CaptureTemporary(_installation.RootPath, _type, CurrentVersion());
            new ConfigSnapshotContentDiffWindow(_compare.Compare(selected, current)) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "현재 설정과 비교", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally
        {
            if (current is not null)
            {
                try { _snapshots.Delete(current); } catch { }
            }
        }
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
            var name = $"{snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} / {snapshot.Stage}";
            failed.Add(name + Environment.NewLine + string.Join(Environment.NewLine, result.Errors.Take(4).Select(error => "  - " + error)));
        }

        var summary = $"선택 snapshot: {selected.Length:N0}개\n정상: {validCount:N0}개\n문제: {selected.Length - validCount:N0}개\n검증 성공 파일: {totalFiles:N0}개";
        if (failed.Count > 0) summary += "\n\n[문제 snapshot]\n" + string.Join("\n\n", failed.Take(8));
        _details.Text = summary.Replace("\n", "\r\n");
        MessageBox.Show(this, summary, "snapshot 일괄 무결성 검사", MessageBoxButton.OK, failed.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void EditSelectedNote()
    {
        var selected = SingleSelected("메모 수정");
        if (selected is null) return;
        var dialog = new ManualSnapshotDialog(_type.ToString(), selected.Note, editMode: true) { Owner = this };
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
            .Select(item => $"- {item.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} / {item.Stage} / {item.Version ?? "Unknown"}"));
        if (selected.Length > 10) preview += $"\n- 외 {selected.Length - 10:N0}개";

        var answer = MessageBox.Show(this,
            $"선택한 snapshot {selected.Length:N0}개를 삭제합니다.\n실제 {_type} 설정에는 영향을 주지 않지만 삭제한 이력은 복구할 수 없습니다.\n\n{preview}\n\n모두 삭제하시겠습니까?",
            "snapshot 일괄 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        var deleted = 0;
        var failures = new List<string>();
        foreach (var snapshot in selected)
        {
            try { _snapshots.Delete(snapshot); deleted++; }
            catch (Exception ex) { failures.Add($"{snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}: {ex.Message}"); }
        }
        ReloadSnapshots();
        MessageBox.Show(this,
            failures.Count == 0 ? $"snapshot {deleted:N0}개를 삭제했습니다." : $"삭제 완료: {deleted:N0}개\n삭제 실패: {failures.Count:N0}개\n\n{string.Join("\n", failures.Take(8))}",
            "snapshot 삭제", MessageBoxButton.OK, failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void SelectiveRestore_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = SingleSelected("파일 선택 복원");
        if (snapshot is null) return;
        if (!AdministratorPrivilege.EnsureElevated(this, _installation.RootPath, $"{_type} 파일 선택 복원")) return;

        ConfigSnapshotManifest? current = null;
        try
        {
            current = _snapshots.CaptureTemporary(_installation.RootPath, _type, CurrentVersion());
            var diff = _compare.Compare(snapshot, current);
            if (diff.Items.All(item => item.Kind == ConfigSnapshotDiffKind.Same))
            {
                MessageBox.Show(this, "선택한 snapshot과 현재 설정이 동일합니다.", "파일 선택 복원", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SelectiveRestoreDialog(diff) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var paths = dialog.SelectedPaths;
            var answer = MessageBox.Show(this,
                $"선택한 설정 파일 {paths.Count:N0}개를 snapshot 상태로 복원합니다.\n\n" +
                "현재 전체 설정은 적용 직전에 안전 snapshot으로 저장되며, 검증 실패 시 자동 원복합니다. 계속하시겠습니까?",
                "파일 선택 복원", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            IsEnabled = false;
            var result = await _restore.RestoreSelectedAsync(_installation, snapshot, paths);
            _details.Text = string.Join("\r\n", result.Steps) + (string.IsNullOrWhiteSpace(result.Error) ? string.Empty : "\r\n\r\n오류: " + result.Error);
            ReloadSnapshots();
            MessageBox.Show(this,
                result.Success
                    ? $"선택한 설정 파일 {paths.Count:N0}개의 복원이 완료되었습니다.\n\n복원 직전 안전 snapshot:\n{result.SafetySnapshotPath}\n\n복원 후 snapshot:\n{result.AfterRestoreSnapshotPath}"
                    : $"파일 선택 복원에 실패했습니다.\n\n{result.Error}\n\n" + (result.RolledBack ? "복원 직전 전체 설정으로 자동 원복했습니다." : "자동 원복도 완료되지 않았습니다."),
                "파일 선택 복원", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "파일 선택 복원", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
            if (current is not null)
            {
                try { _snapshots.Delete(current); } catch { }
            }
        }
    }

    private async void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = SingleSelected("복원");
        if (snapshot is null) return;
        if (!AdministratorPrivilege.EnsureElevated(this, _installation.RootPath, $"{_type} 설정 복원")) return;

        var answer = MessageBox.Show(this,
            $"{_type} 설정을 선택한 snapshot 상태로 전체 복원합니다.\n\nsnapshot: {snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}\n" +
            $"당시 버전: {snapshot.Version ?? "Unknown"}\n단계: {snapshot.Stage}\n메모: {snapshot.Note ?? "(없음)"}\n\n" +
            "현재 설정은 복원 직전에 별도 안전 snapshot으로 자동 저장합니다. 적용 후 검증하고 실패하면 직전 설정으로 자동 원복합니다. 계속하시겠습니까?",
            "전체 설정 snapshot 복원", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        IsEnabled = false;
        try
        {
            var result = await _restore.RestoreAsync(_installation, snapshot);
            _details.Text = string.Join("\r\n", result.Steps) + (string.IsNullOrWhiteSpace(result.Error) ? string.Empty : "\r\n\r\n오류: " + result.Error);
            ReloadSnapshots();
            MessageBox.Show(this,
                result.Success
                    ? $"{_type} 전체 설정 복원이 완료되었습니다.\n\n복원 직전 안전 snapshot:\n{result.SafetySnapshotPath}\n\n복원 후 snapshot:\n{result.AfterRestoreSnapshotPath}"
                    : $"설정 복원에 실패했습니다.\n\n{result.Error}\n\n" + (result.RolledBack ? "복원 직전 설정으로 자동 원복했습니다." : "자동 원복도 완료되지 않았습니다. 로그와 현재 설정을 확인하세요."),
                "설정 복원", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
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

    private string? CurrentVersion() => _installation.Components.FirstOrDefault(item => item.Type == _type)?.Version;

    private sealed class SnapshotItem
    {
        public SnapshotItem(ConfigSnapshotManifest manifest) => Manifest = manifest;
        public ConfigSnapshotManifest Manifest { get; }
        public override string ToString()
        {
            var note = string.IsNullOrWhiteSpace(Manifest.Note) ? string.Empty : $"  [{Manifest.Note.Replace("\r", " ").Replace("\n", " ")}]";
            return $"{Manifest.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}  {Manifest.Stage}  {Manifest.Version ?? "Unknown"}  ({Manifest.Files.Count}개){note}";
        }
    }
}
