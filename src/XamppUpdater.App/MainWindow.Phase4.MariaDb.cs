using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly IMariaDbUpdateExecutor _mariaDbUpdateExecutor = new MariaDbUpdateExecutor();
    private readonly IMariaDbMigrationReviewService _mariaDbMigrationReviewService = new MariaDbMigrationReviewService();
    private Button? _mariaDbReviewButton;
    private Button? _mariaDbExecuteButton;
    private Button? _mariaDbLogButton;
    private bool _mariaDbUpdateRunning;

    private void InitializeMariaDbPhase4Ui()
    {
        if (_mariaDbExecuteButton is not null || MariaDbDiffButton.Parent is not Panel actionPanel) return;

        _mariaDbReviewButton = new Button
        {
            Content = "마이그레이션 검토",
            Margin = new Thickness(0, 0, 8, 6),
            Padding = new Thickness(12, 5, 12, 5),
            IsEnabled = false,
            ToolTip = "대상 패키지, data/설정 보존, 논리·물리 백업, 업그레이드 도구와 서비스 조건을 실제 교체 전에 검토합니다."
        };
        _mariaDbReviewButton.Click += MariaDbReviewButton_Click;
        actionPanel.Children.Add(_mariaDbReviewButton);

        _mariaDbExecuteButton = new Button
        {
            Content = "MariaDB 업데이트 실행",
            Margin = new Thickness(0, 0, 8, 6),
            Padding = new Thickness(12, 5, 12, 5),
            IsEnabled = false,
            ToolTip = "검토를 통과한 논리/물리 백업과 패키지로 XAMPP 내부 MariaDB를 업데이트합니다. major 업그레이드도 data 사본에서 검증 후 확정합니다."
        };
        _mariaDbExecuteButton.Click += MariaDbExecuteButton_Click;
        actionPanel.Children.Add(_mariaDbExecuteButton);

        _mariaDbLogButton = new Button
        {
            Content = "최근 로그 열기",
            Margin = new Thickness(0, 0, 8, 6),
            Padding = new Thickness(12, 5, 12, 5),
            IsEnabled = _executionLogService.FindLatest("MariaDB") is not null,
            ToolTip = "%LOCALAPPDATA%\\XamppUpdater\\Logs에 저장된 최근 MariaDB 업데이트 로그를 엽니다."
        };
        _mariaDbLogButton.Click += MariaDbLogButton_Click;
        actionPanel.Children.Add(_mariaDbLogButton);

        MariaDbDiffButton.IsEnabledChanged += (_, _) => RefreshMariaDbExecuteEnabled();
        MariaDbBackupButton.IsEnabledChanged += (_, _) => RefreshMariaDbExecuteEnabled();
        MariaDbTargetComboBox.SelectionChanged += (_, _) => RefreshMariaDbExecuteEnabled();
    }

    private void MariaDbLogButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _executionLogService.FindLatest("MariaDB");
        if (path is null)
        {
            MessageBox.Show(this, "저장된 MariaDB 업데이트 로그가 없습니다.", "MariaDB 업데이트 로그", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void RefreshMariaDbExecuteEnabled()
    {
        if (_mariaDbExecuteButton is null || _mariaDbUpdateRunning || _mariaDbBackupRunning || _lastInstallation is null)
        {
            if (_mariaDbExecuteButton is not null) _mariaDbExecuteButton.IsEnabled = false;
            if (_mariaDbReviewButton is not null) _mariaDbReviewButton.IsEnabled = false;
            return;
        }

        if (MariaDbTargetComboBox.SelectedItem is not UpdateTargetOption target ||
            !_packageResults.TryGetValue(XamppComponentType.MariaDb, out var package) ||
            !string.Equals(package.Version, target.Version, StringComparison.OrdinalIgnoreCase))
        {
            _mariaDbExecuteButton.IsEnabled = false;
            if (_mariaDbReviewButton is not null) _mariaDbReviewButton.IsEnabled = false;
            return;
        }

        var current = _lastInstallation.Components.FirstOrDefault(item => item.Type == XamppComponentType.MariaDb)?.Version;
        if (string.IsNullOrWhiteSpace(current))
        {
            _mariaDbExecuteButton.IsEnabled = false;
            _mariaDbExecuteButton.ToolTip = "현재 MariaDB 버전을 확인할 수 없습니다.";
            if (_mariaDbReviewButton is not null) _mariaDbReviewButton.IsEnabled = false;
            return;
        }

        var backup = _backupLocator.FindLatest(_lastInstallation.RootPath, XamppComponentType.MariaDb, current, target.Version);
        var ready = backup?.Manifest.LogicalBackup is not null;
        if (_mariaDbReviewButton is not null) _mariaDbReviewButton.IsEnabled = ready;
        _mariaDbExecuteButton.IsEnabled = ready;
        _mariaDbExecuteButton.ToolTip = backup is null
            ? "현재/대상 버전에 일치하는 MariaDB 안전 백업을 먼저 생성하세요."
            : backup.Manifest.LogicalBackup is null
                ? "전체 논리 백업 SQL이 포함된 MariaDB 안전 백업을 다시 생성하세요."
                : IsSameMariaDbSeries(current, target.Version)
                    ? "동일 계열 패치 업데이트를 실행합니다."
                    : $"MariaDB {current} → {target.Version} 직접 major 업그레이드입니다. 검토 후 data 사본에서 새 서버 기동/upgrade를 검증하고 실패 시 원본으로 롤백합니다.";
    }

    private void MariaDbReviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastInstallation is null || MariaDbTargetComboBox.SelectedItem is not UpdateTargetOption target ||
            !_packageResults.TryGetValue(XamppComponentType.MariaDb, out var package)) return;

        var current = _lastInstallation.Components.FirstOrDefault(item => item.Type == XamppComponentType.MariaDb)?.Version;
        if (string.IsNullOrWhiteSpace(current)) return;
        var backup = _backupLocator.FindLatest(_lastInstallation.RootPath, XamppComponentType.MariaDb, current, target.Version);
        if (backup?.Manifest.LogicalBackup is null)
        {
            MessageBox.Show(this, "MariaDB 논리/물리 안전 백업을 먼저 생성하세요.", "MariaDB 마이그레이션 검토", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var review = _mariaDbMigrationReviewService.Build(_lastInstallation, target, package, backup);
            new MariaDbMigrationReviewWindow(review) { Owner = this }.ShowDialog();
            AppendDetail(XamppComponentType.MariaDb,
                $"MariaDB 마이그레이션 검토: 사용자 확인 {review.ReviewItems.Count} / 자동 처리 {review.AutomaticItems.Count} / 실행 가능={review.CanExecute}");
            StatusText.Text = review.CanExecute
                ? $"MariaDB {current} → {target.Version} 마이그레이션 검토 통과"
                : "MariaDB 마이그레이션 검토에서 해결이 필요한 항목이 있습니다.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "MariaDB 마이그레이션 검토", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void MariaDbExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mariaDbUpdateRunning || _mariaDbBackupRunning || _lastInstallation is null ||
            MariaDbTargetComboBox.SelectedItem is not UpdateTargetOption target ||
            !_packageResults.TryGetValue(XamppComponentType.MariaDb, out var package)) return;

        var installation = _lastInstallation;
        var current = installation.Components.FirstOrDefault(item => item.Type == XamppComponentType.MariaDb)?.Version;
        if (string.IsNullOrWhiteSpace(current)) return;

        var backup = _backupLocator.FindLatest(installation.RootPath, XamppComponentType.MariaDb, current, target.Version);
        if (backup?.Manifest.LogicalBackup is null)
        {
            MessageBox.Show(this,
                "현재/대상 버전에 일치하고 전체 논리 백업 SQL이 포함된 MariaDB 안전 백업을 찾지 못했습니다. 준비 점검 후 백업 생성을 다시 실행하세요.",
                "MariaDB 업데이트", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshMariaDbExecuteEnabled();
            return;
        }

        var review = _mariaDbMigrationReviewService.Build(installation, target, package, backup);
        if (!review.CanExecute)
        {
            new MariaDbMigrationReviewWindow(review) { Owner = this }.ShowDialog();
            MessageBox.Show(this, "마이그레이션 검토에서 해결이 필요한 항목이 있어 실제 업데이트를 실행하지 않습니다.",
                "MariaDB 업데이트", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var credentials = await MariaDbCredentialsDialog.RequestAsync(this);
        if (credentials is null)
        {
            StatusText.Text = "MariaDB 업데이트 취소: 업그레이드 인증정보 입력이 취소되었습니다.";
            return;
        }

        var directMajor = !IsSameMariaDbSeries(current, target.Version);
        var confirmation = MessageBox.Show(this,
            $"XAMPP 내부 MariaDB를 실제로 업데이트합니다.\n\n현재: {current}\n대상: {target.Version}\n방식: {(directMajor ? "직접 major 업그레이드" : "동일 계열 패치 업데이트")}\n\n" +
            "실행 전에 논리/물리 백업의 크기와 SHA256을 다시 검증합니다. 기존 mysql 디렉터리는 롤백 원본으로 그대로 유지하고, " +
            "새 패키지에는 data를 복사한 뒤 mariadb-upgrade/mysql_upgrade와 서비스 기동/버전 검증을 수행합니다. " +
            "입력한 DB 인증정보는 업그레이드 도구용 임시 option 파일에만 사용하고 즉시 삭제합니다. 실패하면 기존 mysql 디렉터리로 자동 롤백합니다. 계속하시겠습니까?",
            "MariaDB 업데이트 실행", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;

        var runLog = new List<string>();
        void Log(string text)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
            runLog.Add(line);
            AppendDetail(XamppComponentType.MariaDb, line);
        }

        _mariaDbUpdateRunning = true;
        RefreshMariaDbExecuteEnabled();
        SetBusy(true, $"MariaDB {current} → {target.Version} 업데이트 준비 중...");
        Log($"실행 시작: MariaDB {current} → {target.Version}");
        Log($"업데이트 방식: {(directMajor ? "직접 major 업그레이드" : "동일 계열 패치 업데이트")}");
        Log($"XAMPP 경로: {installation.RootPath}");
        Log($"패키지: {package.PackagePath}");
        Log($"패키지 SHA256: {package.Sha256}");
        Log($"롤백 manifest: {backup.ManifestPath}");
        Log($"논리 백업: {backup.Manifest.LogicalBackup.RelativePath} / SHA256 {backup.Manifest.LogicalBackup.Sha256}");
        Log($"업그레이드 DB 사용자: {credentials.UserName} / 암호는 저장하지 않음");

        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => StatusText.Text = $"MariaDB {current} → {target.Version} 업데이트 작업 중... {stopwatch.Elapsed:mm\\:ss}";
        timer.Start();
        var success = false;

        try
        {
            var result = await Task.Run(async () =>
                await _mariaDbUpdateExecutor.ExecuteAsync(installation, target, package, backup, credentials));
            timer.Stop();
            foreach (var step in result.Steps) Log("실행: " + step);
            foreach (var warning in result.Warnings) Log("주의: " + warning);

            if (!result.Success)
            {
                Log(result.RolledBack ? "업데이트 실패 후 자동 롤백됨" : "업데이트 실행 전/초기 단계에서 실패함");
                Log("오류: " + result.Error);
                StatusText.Text = "MariaDB 업데이트 실패: " + result.Error;
                MessageBox.Show(this,
                    $"MariaDB 업데이트에 실패했습니다.\n\n{result.Error}\n\n" +
                    (result.RolledBack ? "기존 MariaDB로 자동 롤백했습니다." : "MariaDB 디렉터리 교체 전 또는 초기 단계에서 중단되었습니다."),
                    "MariaDB 업데이트", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            success = true;
            Log($"업데이트 완료: MariaDB {current} → {target.Version}");
            StatusText.Text = $"MariaDB 업데이트 완료: {current} → {target.Version}";
            MessageBox.Show(this, $"MariaDB 업데이트가 완료되었습니다.\n\n{current} → {target.Version}",
                "MariaDB 업데이트", MessageBoxButton.OK, MessageBoxImage.Information);

            await InspectAsync(installation.RootPath, "PostUpdate");
            AppendDetail(XamppComponentType.MariaDb, "--- 최근 MariaDB 업데이트 실행 로그 ---");
            foreach (var line in runLog) AppendDetail(XamppComponentType.MariaDb, line);
        }
        catch (Exception ex)
        {
            timer.Stop();
            Log("실행 예외: " + ex.Message);
            StatusText.Text = "MariaDB 업데이트 실행 실패: " + ex.Message;
            MessageBox.Show(this, ex.Message, "MariaDB 업데이트", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            timer.Stop();
            stopwatch.Stop();
            Log($"실행 종료: {(success ? "성공" : "실패/중단")} / 경과 {stopwatch.Elapsed:mm\\:ss}");
            try
            {
                var logPath = _executionLogService.Save("MariaDB", runLog);
                AppendDetail(XamppComponentType.MariaDb, "로그 파일: " + logPath);
                if (_mariaDbLogButton is not null) _mariaDbLogButton.IsEnabled = true;
            }
            catch (Exception logEx) { AppendDetail(XamppComponentType.MariaDb, "로그 저장 실패: " + logEx.Message); }

            _mariaDbUpdateRunning = false;
            SetBusy(false);
            RefreshMariaDbExecuteEnabled();
        }
    }

    private static bool IsSameMariaDbSeries(string currentVersion, string targetVersion) =>
        Version.TryParse(currentVersion, out var current) && Version.TryParse(targetVersion, out var target) &&
        current.Major == target.Major && current.Minor == target.Minor;
}
