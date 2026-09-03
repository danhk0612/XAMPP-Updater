using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XamppUpdater.Core.Models;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private XamppComponentType _activeComponent = XamppComponentType.Apache;
    private readonly Dictionary<XamppComponentType, List<string>> _activityLogs = new()
    {
        [XamppComponentType.Apache] = new(),
        [XamppComponentType.Php] = new(),
        [XamppComponentType.MariaDb] = new()
    };
    private bool _primaryWorkflowRunning;

    private void InitializeSimplifiedUi()
    {
        PrivilegeText.Text = AdministratorPrivilege.IsElevated ? "관리자 권한" : "일반 권한";
        ShowComponent(XamppComponentType.Apache);
        RefreshPrimaryUpdateButtons();
    }

    private void ComponentNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !Enum.TryParse(button.Tag?.ToString(), true, out XamppComponentType type)) return;
        ShowComponent(type);
    }

    private void ShowComponent(XamppComponentType type)
    {
        _activeComponent = type;
        ApachePanel.Visibility = type == XamppComponentType.Apache ? Visibility.Visible : Visibility.Collapsed;
        PhpPanel.Visibility = type == XamppComponentType.Php ? Visibility.Visible : Visibility.Collapsed;
        MariaDbPanel.Visibility = type == XamppComponentType.MariaDb ? Visibility.Visible : Visibility.Collapsed;
        RefreshActivityLog();
    }

    private void InstallPathComboBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _ = InspectPathAndOnlineAsync(InstallPathComboBox.Text, "Manual");
    }

    private void InstallPathComboBox_DropDownClosed(object? sender, EventArgs e)
    {
        if (InstallPathComboBox.SelectedItem is string path && !string.IsNullOrWhiteSpace(path))
            _ = InspectPathAndOnlineAsync(path, "Selected");
    }

    private async Task InspectPathAndOnlineAsync(string? path, string source)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        await InspectAsync(path, source);
        if (_lastInstallation is not null) await CheckOnlineVersionsAsync();
        RefreshPrimaryUpdateButtons();
    }

    private void RefreshPrimaryUpdateButtons()
    {
        ApachePrimaryUpdateButton.IsEnabled = CanStartPrimaryUpdate(XamppComponentType.Apache);
        PhpPrimaryUpdateButton.IsEnabled = CanStartPrimaryUpdate(XamppComponentType.Php);
        MariaDbPrimaryUpdateButton.IsEnabled = CanStartPrimaryUpdate(XamppComponentType.MariaDb);
    }

    private bool CanStartPrimaryUpdate(XamppComponentType type)
    {
        if (_primaryWorkflowRunning || _lastInstallation is null) return false;
        return GetTargetComboBox(type).SelectedItem is UpdateTargetOption;
    }

    private async void PrimaryUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_primaryWorkflowRunning || sender is not Button button ||
            !Enum.TryParse(button.Tag?.ToString(), true, out XamppComponentType type) ||
            _lastInstallation is null || GetTargetComboBox(type).SelectedItem is not UpdateTargetOption target)
            return;

        _primaryWorkflowRunning = true;
        RefreshPrimaryUpdateButtons();
        ShowComponent(type);
        UpdateProgressBar.Value = 0;
        ProgressPercentText.Text = "0%";

        try
        {
            var installation = _lastInstallation;
            StatusText.Text = $"{type} {target.Version} 업데이트 준비를 시작합니다.";
            AppendDetail(type, $"업데이트 준비 시작: {target.Version}");

            UpdateUiProgress(5, "현재 설치 및 서비스 상태 점검 중...");
            var preflight = await Task.Run(() => _preflightService.Inspect(installation, type, target.Version));
            _preflightReports[type] = preflight;
            if (type == XamppComponentType.MariaDb && !CanRunMariaDbSafeBackup(preflight))
                throw new InvalidOperationException("실행 중인 MariaDB를 안전하게 중지할 Windows 서비스를 찾지 못했습니다.");

            UpdateUiProgress(15, "업데이트 패키지 다운로드 및 검증 중...");
            var package = await _packagePreparationService.PrepareAsync(target, _lastProfile!);
            _packageResults[type] = package;
            AppendDetail(type, $"패키지 검증 완료: {package.FileName} / SHA256 {package.Sha256}");

            UpdateUiProgress(35, "설정 및 호환성 사전 검사 중...");
            var diff = await Task.Run(() => _configDiffService.Compare(preflight, package));
            AppendDetail(type, $"설정 비교: 변경 {diff.Changed:N0} / 기존만 {diff.CurrentOnly:N0} / 신규만 {diff.TargetOnly:N0}");

            if (!await RunAutomaticReviewAsync(type, installation, target, package)) return;

            UpdateUiProgress(55, "롤백 백업 생성 중...");
            if (type == XamppComponentType.MariaDb)
            {
                var backup = _backupLocator.FindLatest(installation.RootPath, type, preflight.CurrentVersion, target.Version);
                if (backup?.Manifest.LogicalBackup is null)
                {
                    await CreateMariaDbRollbackBackupForPipelineAsync(preflight, target);
                }
            }
            else
            {
                var existing = _backupLocator.FindLatest(installation.RootPath, type, preflight.CurrentVersion, target.Version);
                if (existing is null)
                {
                    var backup = await Task.Run(() => _backupService.CreateBackup(preflight));
                    AppendDetail(type, $"롤백 백업 완료: {backup.CopiedFiles:N0}개 / {FormatBytes(backup.CopiedBytes)}");
                }
                else
                {
                    AppendDetail(type, "기존 일치 롤백 백업 재사용");
                }
            }

            UpdateUiProgress(70, "업데이트 준비 완료. 실제 업데이트 단계로 이동합니다.");
            switch (type)
            {
                case XamppComponentType.Apache:
                    ApacheExecuteButton_Click(this, new RoutedEventArgs());
                    break;
                case XamppComponentType.Php:
                    PhpExecuteButton_Click(this, new RoutedEventArgs());
                    break;
                case XamppComponentType.MariaDb:
                    MariaDbExecuteButton_Click(this, new RoutedEventArgs());
                    break;
            }
        }
        catch (Exception ex)
        {
            UpdateUiProgress(0, "업데이트 준비 실패: " + ex.Message);
            AppendDetail(type, "준비 실패: " + ex.Message);
            MessageBox.Show(this, ex.Message, $"{type} 업데이트", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _primaryWorkflowRunning = false;
            RefreshPrimaryUpdateButtons();
        }
    }

    private async Task<bool> RunAutomaticReviewAsync(
        XamppComponentType type,
        XamppInstallation installation,
        UpdateTargetOption target,
        PackagePreparationResult package)
    {
        if (type == XamppComponentType.Apache)
        {
            await EnsureApacheRuntimeAsync(package);
            var review = await _apacheMigrationReviewService.BuildAsync(installation, target, package);
            if (!review.SyntaxValid || review.Items.Any(item => item.Kind == ApacheMigrationReviewKind.NeedsReview))
            {
                var confRoot = Path.Combine(installation.RootPath, "apache", "conf");
                var dialog = new ApacheMigrationReviewWindow(review, confRoot) { Owner = this };
                if (dialog.ShowDialog() != true || dialog.FinalFiles is null) return false;
                _apacheMigrationOverrideStore.Save(installation.RootPath, target.Version, confRoot, dialog.FinalFiles);
                var verified = await _apacheMigrationReviewService.BuildAsync(installation, target, package);
                if (!verified.SyntaxValid) throw new InvalidOperationException("확정한 Apache 설정이 대상 버전 검증을 통과하지 못했습니다.");
            }
            AppendDetail(type, "Apache 마이그레이션 사전 검증 통과");
            return true;
        }

        if (type == XamppComponentType.Php)
        {
            var review = await _phpMigrationReviewService.BuildAsync(installation, target, package);
            var currentIni = Path.Combine(installation.RootPath, "php", "php.ini");
            if (review.NeedsReviewCount > 0)
            {
                var dialog = new PhpMigrationReviewWindow(review) { Owner = this };
                if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FinalIniText)) return false;
                _phpMigrationOverrideStore.Save(installation.RootPath, target.Version, currentIni, dialog.FinalIniText);
            }
            else
            {
                _phpMigrationOverrideStore.Save(installation.RootPath, target.Version, currentIni, review.ProposedIni);
            }
            AppendDetail(type, $"PHP 마이그레이션 사전 검증 통과 / 사용자 확인 {review.NeedsReviewCount}");
            return true;
        }

        return true;
    }

    private async Task CreateMariaDbRollbackBackupForPipelineAsync(UpdatePreflightReport report, UpdateTargetOption target)
    {
        if (_lastInstallation is null) throw new InvalidOperationException("XAMPP 설치 정보가 없습니다.");
        var type = XamppComponentType.MariaDb;
        var isRunning = report.ProcessRunning || report.ServiceState?.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) == true;
        var serviceStopped = false;
        LogicalBackupManifest? logicalManifest = null;

        if (isRunning && !AdministratorPrivilege.IsElevated)
            throw new InvalidOperationException("MariaDB 안전 백업에는 관리자 권한이 필요합니다. 업데이트를 다시 실행하면 관리자 권한을 요청합니다.");

        try
        {
            if (isRunning)
            {
                var logical = await _mariaDbLogicalBackupService.CreateAsync(report);
                if (!logical.Success && logical.AuthenticationRequired)
                {
                    var credentials = await MariaDbCredentialsDialog.RequestAsync(this);
                    if (credentials is null) throw new OperationCanceledException("MariaDB 백업 인증정보 입력이 취소되었습니다.");
                    logical = await _mariaDbLogicalBackupService.CreateAsync(report, credentials);
                }
                if (!logical.Success || logical.FilePath is null || logical.Sha256 is null)
                    throw new InvalidOperationException("MariaDB 논리 백업 실패: " + logical.ErrorText);
                logicalManifest = new LogicalBackupManifest(Path.GetRelativePath(report.BackupDestination, logical.FilePath), logical.Size, logical.Sha256);
                AppendDetail(type, $"논리 백업 완료: {FormatBytes(logical.Size)}");
            }

            if (report.ServiceName is not null && report.ServiceState?.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) == true)
            {
                await Task.Run(() => _windowsServiceController.Stop(report.ServiceName, TimeSpan.FromSeconds(30)));
                serviceStopped = true;
            }

            var stopped = await Task.Run(() => _preflightService.Inspect(_lastInstallation, type, target.Version));
            stopped = stopped with { BackupDestination = report.BackupDestination };
            var physical = await Task.Run(() => _backupService.CreateBackup(stopped, logicalManifest));
            AppendDetail(type, $"MariaDB 롤백 백업 완료: {physical.CopiedFiles:N0}개 / {FormatBytes(physical.CopiedBytes)}");
        }
        finally
        {
            if (serviceStopped && report.ServiceName is not null)
                await Task.Run(() => _windowsServiceController.Start(report.ServiceName, TimeSpan.FromSeconds(30)));
        }

        var backup = _backupLocator.FindLatest(_lastInstallation.RootPath, type, report.CurrentVersion, target.Version);
        if (backup?.Manifest.LogicalBackup is null)
            throw new InvalidOperationException("MariaDB 논리/물리 롤백 백업을 확인하지 못했습니다.");

        var review = _mariaDbMigrationReviewService.Build(_lastInstallation, target, _packageResults[type], backup);
        if (!review.CanExecute)
        {
            new MariaDbMigrationReviewWindow(review) { Owner = this }.ShowDialog();
            throw new InvalidOperationException("MariaDB 마이그레이션 검토에서 해결이 필요한 항목이 있습니다.");
        }
        AppendDetail(type, "MariaDB 마이그레이션 사전 검토 통과");
    }

    private void UpdateUiProgress(int percent, string message)
    {
        UpdateProgressBar.Value = Math.Clamp(percent, 0, 100);
        ProgressPercentText.Text = $"{Math.Clamp(percent, 0, 100)}%";
        StatusText.Text = message;
    }

    private void AppendVisibleLog(XamppComponentType type, string text)
    {
        if (!_activityLogs.TryGetValue(type, out var log)) return;
        log.Add(text);
        if (type == _activeComponent) RefreshActivityLog();
    }

    private void RefreshActivityLog()
    {
        ActivityLogTextBox.Text = string.Join(Environment.NewLine, _activityLogs[_activeComponent]);
        ActivityLogTextBox.ScrollToEnd();
    }

    private void OpenSelectedRecentLog_Click(object sender, RoutedEventArgs e)
    {
        var component = _activeComponent == XamppComponentType.MariaDb ? "MariaDB" : _activeComponent.ToString();
        var path = _executionLogService.FindLatest(component);
        if (path is null)
        {
            MessageBox.Show(this, "저장된 업데이트 로그가 없습니다.", "업데이트 로그", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void OpenSelectedConfigHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_lastInstallation is null)
        {
            MessageBox.Show(this, "먼저 XAMPP 설치를 확인하세요.", "설정 이력", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new ConfigHistoryWindow(_lastInstallation, _activeComponent) { Owner = this }.ShowDialog();
    }

    private void OpenStorageFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XamppUpdater");
        var path = button.Tag?.ToString() switch
        {
            "Backups" => Path.Combine(root, "Backups"),
            "ConfigHistory" => Path.Combine(root, "ConfigHistory"),
            _ => root
        };
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
}
