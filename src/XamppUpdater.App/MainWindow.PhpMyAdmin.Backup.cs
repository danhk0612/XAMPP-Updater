using System.Windows;
using System.Windows.Controls;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly PhpMyAdminBackupService _phpMyAdminBackupService = new();
    private Button? _phpMyAdminBackupButton;

    internal void InitializePhpMyAdminBackupUi()
    {
        if (_phpMyAdminBackupButton is not null || _phpMyAdminPanel is null || _phpMyAdminUpdateButton is null) return;

        _phpMyAdminBackupButton = new Button
        {
            Margin = new Thickness(0, 8, 0, 0),
            Height = 36,
            Content = LocalizationService.Translate("phpMyAdmin 백업 생성")
        };
        _phpMyAdminBackupButton.Click += PhpMyAdminBackupButton_Click;

        var index = _phpMyAdminPanel.Children.IndexOf(_phpMyAdminUpdateButton);
        _phpMyAdminPanel.Children.Insert(index + 1, _phpMyAdminBackupButton);
        LocalizationService.ApplyToElement(_phpMyAdminBackupButton);
    }

    private async void PhpMyAdminBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_phpMyAdminUpdateRunning) return;
        var root = InstallPathComboBox.Text;
        if (string.IsNullOrWhiteSpace(root)) return;

        if (!AdministratorPrivilege.IsElevated)
        {
            MessageBox.Show(this,
                "phpMyAdmin 백업을 생성하려면 관리자 권한으로 XAMPP Updater를 실행하세요.",
                "phpMyAdmin 백업 생성",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _phpMyAdminBackupButton!.IsEnabled = false;
        SetBusy(true, LocalizationService.Translate("phpMyAdmin 전체 폴더 백업을 생성하는 중..."));
        try
        {
            var result = await Task.Run(() => _phpMyAdminBackupService.Create(root));
            StatusText.Text = LocalizationService.Translate($"phpMyAdmin 백업 완료: {result.Files:N0}개 / {FormatBytes(result.Bytes)}");
            AppendPhpMyAdminLog($"[{DateTime.Now:HH:mm:ss}] ✓ manual backup: {result.BackupPath}");
            MessageBox.Show(this,
                LocalizationService.Translate($"phpMyAdmin 전체 백업을 생성했습니다.\n\n파일: {result.Files:N0}개\n용량: {FormatBytes(result.Bytes)}\n위치: {result.BackupPath}"),
                LocalizationService.Translate("phpMyAdmin 백업 생성"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Translate("phpMyAdmin 백업 생성"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            if (_phpMyAdminBackupButton is not null) _phpMyAdminBackupButton.IsEnabled = true;
        }
    }
}
