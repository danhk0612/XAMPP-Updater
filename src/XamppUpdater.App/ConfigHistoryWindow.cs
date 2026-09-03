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

    public ConfigHistoryWindow(XamppInstallation installation, XamppComponentType type, IConfigSnapshotService? service = null)
    {
        _installation = installation;
        _type = type;
        _snapshots = service ?? new ConfigSnapshotService();

        Title = $"{type} 설정 이력";
        Width = 1020;
        Height = 640;
        MinWidth = 800;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
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
        var open = new Button { Content = "선택 snapshot 폴더 열기", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 6) };
        open.Click += (_, _) => OpenSelectedFolder();
        var compare = new Button { Content = "선택한 2개 내용 비교", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 6) };
        compare.Click += (_, _) => CompareSelected();
        var restore = new Button { Content = "선택 snapshot 복원", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 6) };
        restore.Click += RestoreSelected_Click;
        var close = new Button { Content = "닫기", Padding = new Thickness(18, 5, 18, 5), Margin = new Thickness(0, 0, 0, 6), IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(open);
        buttons.Children.Add(compare);
        buttons.Children.Add(restore);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
        ReloadSnapshots();
    }

    private void ReloadSnapshots()
    {
        _list.Items.Clear();
        foreach (var snapshot in _snapshots.List(_installation.RootPath, _type))
            _list.Items.Add(new SnapshotItem(snapshot));

        if (_list.Items.Count == 0)
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
            new ConfigSnapshotContentDiffWindow(diff) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "설정 이력 비교", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_list.SelectedItems.Count != 1 || _list.SelectedItem is not SnapshotItem selected)
        {
            MessageBox.Show(this, "복원할 snapshot을 정확히 1개 선택하세요.", "설정 복원", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!AdministratorPrivilege.EnsureElevated(this, _installation.RootPath, $"{_type} 설정 복원")) return;

        var snapshot = selected.Manifest;
        var answer = MessageBox.Show(
            this,
            $"{_type} 설정을 선택한 snapshot 상태로 복원합니다.\n\n" +
            $"snapshot: {snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}\n" +
            $"당시 버전: {snapshot.Version ?? "Unknown"}\n" +
            $"단계: {snapshot.Stage}\n\n" +
            "현재 설정은 복원 직전에 별도 안전 snapshot으로 자동 저장합니다. " +
            "적용 후 구성요소별 검증을 수행하고 실패하면 직전 설정으로 자동 원복합니다. 계속하시겠습니까?",
            "설정 snapshot 복원",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        IsEnabled = false;
        try
        {
            var result = await _restore.RestoreAsync(_installation, snapshot);
            _details.Text = string.Join("\r\n", result.Steps) +
                            (string.IsNullOrWhiteSpace(result.Error) ? string.Empty : "\r\n\r\n오류: " + result.Error);
            ReloadSnapshots();

            if (result.Success)
            {
                MessageBox.Show(this,
                    $"{_type} 설정 복원이 완료되었습니다.\n\n복원 직전 안전 snapshot:\n{result.SafetySnapshotPath}",
                    "설정 복원",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this,
                    $"설정 복원에 실패했습니다.\n\n{result.Error}\n\n" +
                    (result.RolledBack ? "복원 직전 설정으로 자동 원복했습니다." : "자동 원복도 완료되지 않았습니다. 로그와 현재 설정을 확인하세요."),
                    "설정 복원",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "설정 복원", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
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
