using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly IRollbackBackupCatalogService _rollbackCatalog = new RollbackBackupCatalogService();
    private readonly IComponentRollbackService _componentRollbackService = new ComponentRollbackService();
    private readonly Dictionary<XamppComponentType, Button> _rollbackButtons = new();
    private DispatcherTimer? _catalogRefreshTimer;
    private bool _catalogRefreshRunning;
    private DateTimeOffset _nextCatalogRefreshAttempt;

    internal void InitializeRollbackUi()
    {
        if (_rollbackButtons.Count != 0) return;
        AddRollbackButton(XamppComponentType.Apache, ApachePrimaryUpdateButton);
        AddRollbackButton(XamppComponentType.Php, PhpPrimaryUpdateButton);
        AddRollbackButton(XamppComponentType.MariaDb, MariaDbPrimaryUpdateButton);

        _catalogRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _catalogRefreshTimer.Tick += async (_, _) => await CatalogRefreshTickAsync();
        _catalogRefreshTimer.Start();
        RefreshRollbackUi();
    }

    private void AddRollbackButton(XamppComponentType type, Button updateButton)
    {
        if (updateButton.Parent is not Panel panel) return;
        var button = new Button
        {
            Content = "롤백",
            Tag = type.ToString(),
            Height = 36,
            Margin = new Thickness(0, 8, 0, 0),
            IsEnabled = false,
            Visibility = Visibility.Collapsed,
            ToolTip = "XAMPP Updater가 업데이트 전에 만든 검증 가능한 전체 백업으로 이전 버전을 복원합니다."
        };
        button.Click += RollbackButton_Click;
        var index = panel.Children.IndexOf(updateButton);
        panel.Children.Insert(Math.Min(index + 1, panel.Children.Count), button);
        _rollbackButtons[type] = button;
    }

    private async Task CatalogRefreshTickAsync()
    {
        if (_lastInstallation is null || _catalogRefreshRunning) return;
        if (AnyComponentOperationRunning()) return;

        if (_targetCatalog is null && DateTimeOffset.UtcNow >= _nextCatalogRefreshAttempt)
        {
            _catalogRefreshRunning = true;
            _nextCatalogRefreshAttempt = DateTimeOffset.UtcNow.AddSeconds(30);
            try
            {
                await CheckOnlineVersionsAsync();
                UpdateCurrentVersionLabels();
                RefreshPrimaryUpdateButtons();
            }
            catch
            {
                // CheckOnlineVersionsAsync가 상태 메시지를 기록한다. 다음 주기에서 제한적으로 재시도한다.
            }
            finally
            {
                _catalogRefreshRunning = false;
            }
        }

        ApplyCurrentVersionOnlyDisplay();
        RefreshRollbackUi();
    }

    private bool AnyComponentOperationRunning() =>
        _primaryWorkflowRunning || _apacheUpdateRunning || _phpUpdateRunning || _mariaDbUpdateRunning ||
        _apacheReviewRunning || _phpMigrationReviewRunning || _mariaDbBackupRunning;

    private void ApplyCurrentVersionOnlyDisplay()
    {
        if (_lastInstallation is null || _targetCatalog is null) return;
        Apply(XamppComponentType.Apache, _targetCatalog.Apache);
        Apply(XamppComponentType.Php, _targetCatalog.Php);
        Apply(XamppComponentType.MariaDb, _targetCatalog.MariaDb);

        void Apply(XamppComponentType type, IReadOnlyList<UpdateTargetOption> targets)
        {
            var combo = GetTargetComboBox(type);
            var updateButton = GetPrimaryUpdateButton(type);
            if (targets.Count > 0)
            {
                updateButton.Visibility = Visibility.Visible;
                if (combo.ItemsSource is not IEnumerable<UpdateTargetOption>)
                {
                    combo.ItemsSource = targets;
                    combo.IsEnabled = true;
                    combo.SelectedIndex = 0;
                }
                return;
            }

            var current = _lastInstallation.Components.FirstOrDefault(item => item.Type == type)?.Version;
            updateButton.Visibility = Visibility.Collapsed;
            if (string.IsNullOrWhiteSpace(current))
            {
                combo.ItemsSource = null;
                combo.IsEnabled = false;
                return;
            }

            combo.ItemsSource = new object[] { new CurrentVersionDisplayItem($"{current} - 현재 버전 / 업데이트 없음") };
            combo.SelectedIndex = 0;
            combo.IsEnabled = false;
            SetPlanText(type, "새 업데이트가 없습니다. 고급 정보는 계속 확인할 수 있습니다.");
        }
    }

    private void RefreshRollbackUi()
    {
        if (_lastInstallation is null)
        {
            foreach (var button in _rollbackButtons.Values) button.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var type in new[] { XamppComponentType.Apache, XamppComponentType.Php, XamppComponentType.MariaDb })
        {
            if (!_rollbackButtons.TryGetValue(type, out var button)) continue;
            var current = _lastInstallation.Components.FirstOrDefault(item => item.Type == type)?.Version;
            if (string.IsNullOrWhiteSpace(current))
            {
                button.Visibility = Visibility.Collapsed;
                continue;
            }

            var candidate = _rollbackCatalog.FindLatestCandidate(_lastInstallation.RootPath, type, current);
            button.Visibility = candidate is null ? Visibility.Collapsed : Visibility.Visible;
            button.IsEnabled = candidate is not null && !AnyComponentOperationRunning();
            button.Content = candidate is null ? "롤백" : $"{candidate.Manifest.CurrentVersion}로 롤백";
            button.ToolTip = candidate is null
                ? "사용 가능한 업데이트 전 전체 백업이 없습니다."
                : $"{candidate.Manifest.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}에 생성된 전체 백업으로 {current} → {candidate.Manifest.CurrentVersion} 롤백";
        }
    }

    private async void RollbackButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !Enum.TryParse(button.Tag?.ToString(), true, out XamppComponentType type) || _lastInstallation is null)
            return;

        var installation = _lastInstallation;
        var current = installation.Components.FirstOrDefault(item => item.Type == type)?.Version;
        if (string.IsNullOrWhiteSpace(current)) return;
        var rollback = _rollbackCatalog.FindLatestCandidate(installation.RootPath, type, current);
        if (rollback is null) { RefreshRollbackUi(); return; }

        if (!AdministratorPrivilege.IsElevated)
        {
            AdministratorPrivilege.EnsureElevatedForRollback(this, installation.RootPath, $"{type} 프로그램 롤백", type.ToString());
            return;
        }

        try
        {
            BackupIntegrityVerifier.Verify(rollback, requireLogicalBackup: type == XamppComponentType.MariaDb);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "롤백 백업 무결성 검증에 실패했습니다.\n\n" + ex.Message, $"{type} 롤백", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"{type} 프로그램 전체를 업데이트 전 백업 상태로 롤백합니다.\n\n현재: {current}\n복원: {rollback.Manifest.CurrentVersion}\n백업: {rollback.Manifest.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}\n\n" +
            "설정 파일만 복원하는 기능이 아니라 프로그램 바이너리와 구성요소 폴더 전체를 복원합니다. 롤백 직전 현재 상태도 별도 안전 백업합니다. 계속하시겠습니까?",
            $"{type} 프로그램 롤백", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        var runLog = new List<string>();
        void Log(string text)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
            runLog.Add(line);
            AppendVisibleLog(type, line);
        }
        void Progress(int percent, string message)
        {
            UpdateProgressBar.Value = Math.Clamp(percent, 0, 100);
            ProgressPercentText.Text = $"{Math.Clamp(percent, 0, 100)}%";
            StatusText.Text = message;
            Log("진행: " + message + $" ({Math.Clamp(percent, 0, 100)}%)");
        }

        ShowComponent(type);
        Progress(5, $"{type} 롤백 백업 검증 완료");
        Log($"수동 롤백 시작: {type} {current} → {rollback.Manifest.CurrentVersion}");
        Log("XAMPP 경로: " + installation.RootPath);
        Log("롤백 manifest: " + rollback.ManifestPath);
        var success = false;
        try
        {
            _primaryWorkflowRunning = true;
            RefreshPrimaryUpdateButtons();
            RefreshRollbackUi();

            Progress(15, "롤백 직전 현재 상태 안전 백업 중...");
            var safety = await CreateSafetyBackupBeforeRollbackAsync(installation, type, rollback.Manifest.CurrentVersion);
            BackupIntegrityVerifier.Verify(safety, requireLogicalBackup: false);
            Log("롤백 직전 안전 백업 완료: " + safety.ManifestPath);

            Progress(35, $"{type} {current} → {rollback.Manifest.CurrentVersion} 롤백 중...");
            var result = await _componentRollbackService.RollbackAsync(installation, rollback);
            foreach (var step in result.Steps) Log("실행: " + step);
            if (!result.Success)
                throw new InvalidOperationException(result.Error ?? "프로그램 롤백에 실패했습니다.");

            Progress(90, "롤백 완료. 설치 상태와 온라인 버전을 다시 확인하는 중...");
            await InspectAsync(installation.RootPath, "PostRollback");
            _nextCatalogRefreshAttempt = DateTimeOffset.MinValue;
            await CheckOnlineVersionsAsync();
            UpdateCurrentVersionLabels();
            ApplyCurrentVersionOnlyDisplay();
            RefreshPrimaryUpdateButtons();
            RefreshRollbackUi();
            success = true;
            Progress(100, $"{type} {rollback.Manifest.CurrentVersion} 롤백 완료");
            Log($"수동 롤백 완료: {type} {current} → {rollback.Manifest.CurrentVersion}");
            MessageBox.Show(this, $"{type} 프로그램 롤백이 완료되었습니다.\n\n{current} → {rollback.Manifest.CurrentVersion}", $"{type} 롤백", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException ex)
        {
            Progress(0, "롤백 취소: " + ex.Message);
            Log("수동 롤백 취소: " + ex.Message);
        }
        catch (Exception ex)
        {
            Progress(0, "롤백 실패: " + ex.Message);
            Log("수동 롤백 실패: " + ex.Message);
            MessageBox.Show(this, ex.Message, $"{type} 롤백", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Log($"수동 롤백 실행 종료: {(success ? "성공" : "실패/중단")}");
            try
            {
                var component = type == XamppComponentType.MariaDb ? "MariaDB" : type.ToString();
                var path = _executionLogService.Save(component, runLog);
                AppendVisibleLog(type, $"[{DateTime.Now:HH:mm:ss}] ✓ 롤백 로그 파일 저장: {path}");
            }
            catch (Exception logEx)
            {
                AppendVisibleLog(type, $"[{DateTime.Now:HH:mm:ss}] ! 롤백 로그 저장 실패: {logEx.Message}");
            }

            MariaDbCredentialsDialog.ClearCachedCredentials();
            _primaryWorkflowRunning = false;
            RefreshPrimaryUpdateButtons();
            RefreshRollbackUi();
        }
    }

    private async Task<BackupResult> CreateSafetyBackupBeforeRollbackAsync(
        XamppInstallation installation,
        XamppComponentType type,
        string rollbackTargetVersion)
    {
        var report = await Task.Run(() => _preflightService.Inspect(installation, type, rollbackTargetVersion));
        if (type != XamppComponentType.MariaDb)
            return await Task.Run(() => _backupService.CreateBackup(report));

        var serviceStopped = false;
        LogicalBackupManifest? logicalManifest = null;
        try
        {
            var running = report.ProcessRunning || report.ServiceState?.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) == true;
            if (running)
            {
                var logical = await _mariaDbLogicalBackupService.CreateAsync(report);
                if (!logical.Success && logical.AuthenticationRequired)
                {
                    var credentials = await MariaDbCredentialsDialog.RequestAsync(this);
                    if (credentials is null) throw new OperationCanceledException("MariaDB 안전 백업 인증정보 입력이 취소되었습니다.");
                    logical = await _mariaDbLogicalBackupService.CreateAsync(report, credentials);
                }
                if (!logical.Success || logical.FilePath is null || logical.Sha256 is null)
                    throw new InvalidOperationException("MariaDB 롤백 직전 논리 백업 실패: " + logical.ErrorText);
                logicalManifest = new LogicalBackupManifest(
                    Path.GetRelativePath(report.BackupDestination, logical.FilePath), logical.Size, logical.Sha256);
            }

            if (report.ServiceName is not null && report.ServiceState?.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) == true)
            {
                await Task.Run(() => _windowsServiceController.Stop(report.ServiceName, TimeSpan.FromSeconds(45)));
                serviceStopped = true;
            }

            var stopped = await Task.Run(() => _preflightService.Inspect(installation, type, rollbackTargetVersion));
            stopped = stopped with { BackupDestination = report.BackupDestination };
            return await Task.Run(() => _backupService.CreateBackup(stopped, logicalManifest));
        }
        finally
        {
            if (serviceStopped && report.ServiceName is not null)
                await Task.Run(() => _windowsServiceController.Start(report.ServiceName, TimeSpan.FromSeconds(45)));
        }
    }

    internal async Task ResumeStartupRollbackAsync()
    {
        var component = AdministratorPrivilege.GetStartupResumeRollback();
        if (component is null || !AdministratorPrivilege.IsElevated || !Enum.TryParse(component, true, out XamppComponentType type)) return;
        ShowComponent(type);
        RefreshRollbackUi();
        if (!_rollbackButtons.TryGetValue(type, out var button) || button.Visibility != Visibility.Visible)
        {
            StatusText.Text = $"관리자 권한 재실행 후 {type} 롤백 가능한 백업을 찾지 못했습니다.";
            return;
        }
        await Dispatcher.InvokeAsync(() => RollbackButton_Click(button, new RoutedEventArgs()));
    }

    private sealed record CurrentVersionDisplayItem(string DisplayText);
}
