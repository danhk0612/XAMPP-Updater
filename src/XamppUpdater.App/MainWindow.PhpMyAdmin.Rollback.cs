using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly PhpMyAdminRollbackService _phpMyAdminRollbackService = new();
    private Button? _phpMyAdminRollbackButton;
    private PhpMyAdminRollbackCandidate? _phpMyAdminRollbackCandidate;
    private DispatcherTimer? _phpMyAdminRollbackTimer;
    private bool _phpMyAdminRollbackRunning;

    internal void InitializePhpMyAdminRollbackUi()
    {
        if (_phpMyAdminRollbackButton is not null || _phpMyAdminPanel is null || _phpMyAdminUpdateButton is null) return;

        _phpMyAdminRollbackButton = new Button
        {
            Height = 36,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
            IsEnabled = false,
            ToolTip = LocalizationCatalog.Text(
                "phpMyAdmin 업데이트 과정에서 생성된 전체 롤백 백업으로 이전 버전을 복원합니다.",
                "Restore the previous version from a complete rollback backup created during a phpMyAdmin update.")
        };
        _phpMyAdminRollbackButton.Click += PhpMyAdminRollbackButton_Click;

        var index = _phpMyAdminPanel.Children.IndexOf(_phpMyAdminUpdateButton);
        _phpMyAdminPanel.Children.Insert(Math.Min(index + 1, _phpMyAdminPanel.Children.Count), _phpMyAdminRollbackButton);

        _phpMyAdminRollbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _phpMyAdminRollbackTimer.Tick += (_, _) => RefreshPhpMyAdminRollbackUi();
        _phpMyAdminRollbackTimer.Start();
        RefreshPhpMyAdminRollbackUi();
    }

    private void RefreshPhpMyAdminRollbackUi()
    {
        if (_phpMyAdminRollbackButton is null) return;
        if (_phpMyAdminUpdateRunning || _phpMyAdminRollbackRunning || _primaryWorkflowRunning)
        {
            _phpMyAdminRollbackButton.IsEnabled = false;
            return;
        }

        var root = InstallPathComboBox.Text;
        if (string.IsNullOrWhiteSpace(root) || _phpMyAdminState?.IsInstalled != true || string.IsNullOrWhiteSpace(_phpMyAdminState.Version))
        {
            _phpMyAdminRollbackCandidate = null;
            _phpMyAdminRollbackButton.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            _phpMyAdminRollbackCandidate = _phpMyAdminRollbackService.FindLatestCandidate(root);
        }
        catch
        {
            _phpMyAdminRollbackCandidate = null;
        }

        var candidate = _phpMyAdminRollbackCandidate;
        _phpMyAdminRollbackButton.Visibility = candidate is null ? Visibility.Collapsed : Visibility.Visible;
        _phpMyAdminRollbackButton.IsEnabled = candidate is not null;
        if (candidate is null) return;

        _phpMyAdminRollbackButton.Content = LocalizationCatalog.Text(
            $"{candidate.BackupVersion}로 롤백",
            $"Rollback to {candidate.BackupVersion}");
        _phpMyAdminRollbackButton.ToolTip = LocalizationCatalog.Text(
            $"{candidate.CreatedAt:yyyy-MM-dd HH:mm:ss}에 업데이트 과정에서 생성된 백업으로 {candidate.CurrentVersion} → {candidate.BackupVersion} 롤백",
            $"Rollback {candidate.CurrentVersion} → {candidate.BackupVersion} using the update backup created at {candidate.CreatedAt:yyyy-MM-dd HH:mm:ss}.");
    }

    private async void PhpMyAdminRollbackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_phpMyAdminRollbackRunning || _phpMyAdminUpdateRunning) return;
        RefreshPhpMyAdminRollbackUi();
        var candidate = _phpMyAdminRollbackCandidate;
        var root = InstallPathComboBox.Text;
        if (candidate is null || string.IsNullOrWhiteSpace(root)) return;

        if (!AdministratorPrivilege.IsElevated)
        {
            AdministratorPrivilege.EnsureElevatedForRollback(
                this,
                root,
                LocalizationCatalog.Text("phpMyAdmin 프로그램 롤백", "phpMyAdmin rollback"),
                "PhpMyAdmin");
            return;
        }

        var confirm = MessageBox.Show(
            this,
            LocalizationCatalog.Text(
                $"phpMyAdmin 전체를 업데이트 전 백업 상태로 롤백합니다.\n\n현재: {candidate.CurrentVersion}\n복원: {candidate.BackupVersion}\n백업: {candidate.CreatedAt:yyyy-MM-dd HH:mm:ss}\n\n설정 파일만 복원하는 것이 아니라 phpMyAdmin 폴더 전체를 복원합니다. 롤백 직전 현재 상태도 별도 안전 백업합니다. 계속하시겠습니까?",
                $"Restore the complete phpMyAdmin folder to its pre-update backup.\n\nCurrent: {candidate.CurrentVersion}\nRestore: {candidate.BackupVersion}\nBackup: {candidate.CreatedAt:yyyy-MM-dd HH:mm:ss}\n\nThis restores the entire phpMyAdmin folder, not only configuration files. The current state is also saved separately immediately before rollback. Do you want to continue?"),
            LocalizationCatalog.Text("phpMyAdmin 프로그램 롤백", "phpMyAdmin rollback"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        await StartPhpMyAdminRollbackAsync(candidate, showCompletionDialog: true);
    }

    private async Task StartPhpMyAdminRollbackAsync(PhpMyAdminRollbackCandidate candidate, bool showCompletionDialog)
    {
        if (_phpMyAdminRollbackRunning || _phpMyAdminUpdateRunning) return;
        var root = InstallPathComboBox.Text;
        if (string.IsNullOrWhiteSpace(root)) return;

        _phpMyAdminRollbackRunning = true;
        if (_phpMyAdminRollbackButton is not null) _phpMyAdminRollbackButton.IsEnabled = false;
        if (_phpMyAdminUpdateButton is not null) _phpMyAdminUpdateButton.IsEnabled = false;
        ShowPhpMyAdminPanel();
        UpdateProgressBar.Value = 10;
        ProgressPercentText.Text = "10%";
        StatusText.Text = LocalizationCatalog.Text(
            "phpMyAdmin 롤백 백업을 검증하고 현재 상태 안전 백업을 준비하는 중...",
            "Validating the phpMyAdmin rollback backup and preparing a safety backup of the current state...");
        AppendPhpMyAdminLog($"[{DateTime.Now:HH:mm:ss}] • rollback start: {candidate.CurrentVersion} -> {candidate.BackupVersion}");
        AppendPhpMyAdminLog($"[{DateTime.Now:HH:mm:ss}] • rollback backup: {candidate.BackupPath}");

        try
        {
            var result = await Task.Run(() => _phpMyAdminRollbackService.Rollback(root, candidate));
            UpdateProgressBar.Value = 90;
            ProgressPercentText.Text = "90%";

            _phpMyAdminObservedPath = null;
            ObservePhpMyAdminPath(root);
            await RefreshPhpMyAdminReleaseAsync();
            RefreshPhpMyAdminRollbackUi();

            UpdateProgressBar.Value = 100;
            ProgressPercentText.Text = "100%";
            StatusText.Text = LocalizationCatalog.Text(
                $"phpMyAdmin {result.PreviousVersion} → {result.RestoredVersion} 롤백 완료",
                $"phpMyAdmin rollback completed: {result.PreviousVersion} → {result.RestoredVersion}");
            AppendPhpMyAdminLog($"[{DateTime.Now:HH:mm:ss}] ✓ rollback completed: {result.PreviousVersion} -> {result.RestoredVersion}");
            AppendPhpMyAdminLog($"[{DateTime.Now:HH:mm:ss}] ✓ pre-rollback safety backup: {result.SafetyBackupPath}");

            if (showCompletionDialog)
            {
                MessageBox.Show(
                    this,
                    LocalizationCatalog.Text(
                        $"phpMyAdmin 롤백이 완료되었습니다.\n\n{result.PreviousVersion} → {result.RestoredVersion}\n\n롤백 직전 안전 백업:\n{result.SafetyBackupPath}",
                        $"phpMyAdmin rollback completed.\n\n{result.PreviousVersion} → {result.RestoredVersion}\n\nPre-rollback safety backup:\n{result.SafetyBackupPath}"),
                    LocalizationCatalog.Text("phpMyAdmin 롤백 완료", "phpMyAdmin rollback completed"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            UpdateProgressBar.Value = 0;
            ProgressPercentText.Text = "0%";
            StatusText.Text = LocalizationCatalog.Text("phpMyAdmin 롤백 실패: ", "phpMyAdmin rollback failed: ") + ex.Message;
            AppendPhpMyAdminLog($"[{DateTime.Now:HH:mm:ss}] ✗ rollback failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, LocalizationCatalog.Text("phpMyAdmin 롤백 실패", "phpMyAdmin rollback failed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _phpMyAdminRollbackRunning = false;
            await RefreshPhpMyAdminReleaseAsync();
            RefreshPhpMyAdminRollbackUi();
        }
    }

    internal async Task ResumeStartupPhpMyAdminRollbackAsync()
    {
        var component = AdministratorPrivilege.GetStartupResumeRollback();
        if (!string.Equals(component, "PhpMyAdmin", StringComparison.OrdinalIgnoreCase) || !AdministratorPrivilege.IsElevated) return;

        InitializePhpMyAdminUi();
        InitializePhpMyAdminRollbackUi();
        ObservePhpMyAdminPath(InstallPathComboBox.Text);
        RefreshPhpMyAdminRollbackUi();
        if (_phpMyAdminRollbackCandidate is null || _phpMyAdminRollbackButton is null)
        {
            StatusText.Text = LocalizationCatalog.Text(
                "관리자 권한 재실행 후 phpMyAdmin 롤백 가능한 업데이트 백업을 찾지 못했습니다.",
                "No eligible phpMyAdmin update-created rollback backup was found after restarting with administrator privileges.");
            return;
        }

        ShowPhpMyAdminPanel();
        await Dispatcher.InvokeAsync(() => PhpMyAdminRollbackButton_Click(_phpMyAdminRollbackButton, new RoutedEventArgs()));
    }
}
