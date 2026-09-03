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
    private Button? _mariaDbExecuteButton;
    private Button? _mariaDbLogButton;
    private bool _mariaDbUpdateRunning;

    private void InitializeMariaDbPhase4Ui()
    {
        if (_mariaDbExecuteButton is not null) return;
        if (MariaDbDiffButton.Parent is not Panel actionPanel) return;

        _mariaDbExecuteButton = new Button
        {
            Content = "MariaDB 업데이트 실행",
            Margin = new Thickness(0, 0, 8, 6),
            Padding = new Thickness(12, 5, 12, 5),
            IsEnabled = false,
            ToolTip = "논리/물리 백업과 검증된 패키지를 사용해 XAMPP 내부 MariaDB를 업데이트합니다. 현재 첫 실행 단계는 동일 major.minor 계열 패치 업데이트를 지원합니다."
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
            return;
        }

        if (MariaDbTargetComboBox.SelectedItem is not UpdateTargetOption target ||
            !_packageResults.TryGetValue(XamppComponentType.MariaDb, out var package) ||
            !string.Equals(package.Version, target.Version, StringComparison.OrdinalIgnoreCase))
        {
            _mariaDbExecuteButton.IsEnabled = false;
            return;
        }

        var current = _lastInstallation.Components.FirstOrDefault(item => item.Type == XamppComponentType.MariaDb)?.Version;
        if (current is null || !MariaDbUpdateExecutor.IsSameSeries(current, target.Version))
        {
            _mariaDbExecuteButton.IsEnabled = false;
            _mariaDbExecuteButton.ToolTip = current is null
                ? "현재 MariaDB 버전을 확인할 수 없습니다."
                : $"MariaDB {current} → {target.Version}는 중간 계열 패키지를 순차 적용하는 다음 Phase 4C 단계에서 실행합니다.";
            return;
        }

        var backup = _backupLocator.FindLatest(_lastInstallation.RootPath, XamppComponentType.MariaDb, current, target.Version);
        _mariaDbExecuteButton.IsEnabled = backup?.Manifest.LogicalBackup is not null;
        _mariaDbExecuteButton.ToolTip = backup is null
            ? "현재/대상 버전에 일치하는 MariaDB 안전 백업을 먼저 생성하세요."
            : backup.Manifest.LogicalBackup is null
                ? "전체 논리 백업 SQL이 포함된 MariaDB 안전 백업을 다시 생성하세요."
                : "논리/물리 백업 무결성을 재검증한 뒤 MariaDB 바이너리와 data 사본을 업데이트합니다.";
    }

    private async void MariaDbExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mariaDbUpdateRunning || _mariaDbBackupRunning || _lastInstallation is null ||
            MariaDbTargetComboBox.SelectedItem is not UpdateTargetOption target ||
            !_packageResults.TryGetValue(XamppComponentType.MariaDb, out var package)) return;

        var installation = _lastInstallation;
        var current = installation.Components.FirstOrDefault(item => item.Type == XamppComponentType.MariaDb)?.Version;
        if (current is null) return;

        if (!MariaDbUpdateExecutor.IsSameSeries(current, target.Version))
        {
            MessageBox.Show(this,
                $"MariaDB {current} → {target.Version}는 중간 계열 패키지를 순차 적용해야 합니다. 현재 첫 Phase 4C 실행 단계에서는 실제 파일을 변경하지 않습니다.",
                "MariaDB 업데이트", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var backup = _backupLocator.FindLatest(installation.RootPath, XamppComponentType.MariaDb, current, target.Version);
        if (backup?.Manifest.LogicalBackup is null)
        {
            MessageBox.Show(this,
                "현재/대상 버전에 일치하고 전체 논리 백업 SQL이 포함된 MariaDB 안전 백업을 찾지 못했습니다. 준비 점검 후 백업 생성을 다시 실행하세요.",
                "MariaDB 업데이트", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshMariaDbExecuteEnabled();
            return;
        }

        var confirmation = MessageBox.Show(this,
            $"XAMPP 내부 MariaDB를 실제로 업데이트합니다.\n\n현재: {current}\n대상: {target.Version}\n\n" +
            "실행 전에 논리/물리 백업의 크기와 SHA256을 다시 검증합니다. 기존 mysql 디렉터리는 롤백 원본으로 그대로 유지하고, " +
            "새 패키지에는 data를 복사한 뒤 mariadb-upgrade/mysql_upgrade와 서비스 기동/버전 검증을 수행합니다. 실패하면 기존 mysql 디렉터리로 자동 롤백합니다. 계속하시겠습니까?",
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
        Log($"XAMPP 경로: {installation.RootPath}");
        Log($"패키지: {package.PackagePath}");
        Log($"패키지 SHA256: {package.Sha256}");
        Log($"롤백 manifest: {backup.ManifestPath}");
        Log($"논리 백업: {backup.Manifest.LogicalBackup.RelativePath} / SHA256 {backup.Manifest.LogicalBackup.Sha256}");

        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => StatusText.Text = $"MariaDB {current} → {target.Version} 업데이트 작업 중... {stopwatch.Elapsed:mm\\:ss}";
        timer.Start();
        var success = false;

        try
        {
            var result = await Task.Run(async () => await _mariaDbUpdateExecutor.ExecuteAsync(installation, target, package, backup));
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
}
