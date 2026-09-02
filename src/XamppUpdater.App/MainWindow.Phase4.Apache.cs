using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly IApacheUpdateExecutor _apacheUpdateExecutor = new ApacheUpdateExecutor();
    private Button? _apacheExecuteButton;
    private Button? _apacheLogButton;
    private bool _apacheUpdateRunning;

    private void InitializeApachePhase4Ui()
    {
        if (_apacheExecuteButton is not null) return;
        if (ApacheDiffButton.Parent is not StackPanel actionPanel) return;

        _apacheExecuteButton = new Button
        {
            Content = "Apache 업데이트 실행",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 5, 12, 5),
            IsEnabled = false,
            ToolTip = "준비된 패키지와 롤백 백업으로 Apache를 실제 교체하고 실패 시 자동 롤백합니다."
        };
        _apacheExecuteButton.Click += ApacheExecuteButton_Click;
        actionPanel.Children.Add(_apacheExecuteButton);

        _apacheLogButton = new Button
        {
            Content = "최근 로그 열기",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 5, 12, 5),
            IsEnabled = _executionLogService.FindLatest("Apache") is not null,
            ToolTip = "%LOCALAPPDATA%\\XamppUpdater\\Logs에 저장된 최근 Apache 업데이트 로그를 엽니다."
        };
        _apacheLogButton.Click += ApacheLogButton_Click;
        actionPanel.Children.Add(_apacheLogButton);

        ApacheDiffButton.IsEnabledChanged += (_, _) => RefreshApacheExecuteEnabled();
        ApacheBackupButton.IsEnabledChanged += (_, _) => RefreshApacheExecuteEnabled();
        ApacheTargetComboBox.SelectionChanged += (_, _) => RefreshApacheExecuteEnabled();
    }

    private void ApacheLogButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _executionLogService.FindLatest("Apache");
        if (path is null)
        {
            MessageBox.Show(this, "저장된 Apache 업데이트 로그가 없습니다.", "Apache 업데이트 로그", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void RefreshApacheExecuteEnabled()
    {
        if (_apacheExecuteButton is null || _apacheUpdateRunning || _lastInstallation is null)
        {
            if (_apacheExecuteButton is not null) _apacheExecuteButton.IsEnabled = false;
            return;
        }

        if (ApacheTargetComboBox.SelectedItem is not UpdateTargetOption target ||
            !_packageResults.TryGetValue(XamppComponentType.Apache, out var package) ||
            !string.Equals(package.Version, target.Version, StringComparison.OrdinalIgnoreCase))
        {
            _apacheExecuteButton.IsEnabled = false;
            return;
        }

        var current = _lastInstallation.Components.FirstOrDefault(item => item.Type == XamppComponentType.Apache)?.Version;
        if (current is null)
        {
            _apacheExecuteButton.IsEnabled = false;
            return;
        }

        _apacheExecuteButton.IsEnabled = _backupLocator.FindLatest(
            _lastInstallation.RootPath,
            XamppComponentType.Apache,
            current,
            target.Version) is not null;
    }

    private async void ApacheExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apacheUpdateRunning || _lastInstallation is null ||
            ApacheTargetComboBox.SelectedItem is not UpdateTargetOption target ||
            !_packageResults.TryGetValue(XamppComponentType.Apache, out var package))
            return;

        var installation = _lastInstallation;
        var current = installation.Components.FirstOrDefault(item => item.Type == XamppComponentType.Apache)?.Version;
        if (current is null) return;

        var backup = _backupLocator.FindLatest(
            installation.RootPath,
            XamppComponentType.Apache,
            current,
            target.Version);
        if (backup is null)
        {
            MessageBox.Show(this,
                "현재 Apache 버전과 선택 버전에 일치하는 롤백 백업을 찾지 못했습니다. 준비 점검 후 백업 생성을 다시 실행하세요.",
                "Apache 업데이트",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            RefreshApacheExecuteEnabled();
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Apache를 실제로 업데이트합니다.\n\n현재: {current}\n대상: {target.Version}\n\n" +
            "현재 conf 설정을 보존하고, 새 패키지에 없는 참조 모듈은 기존 설치에서 보존한 뒤 httpd -t와 서비스 기동을 검증합니다. " +
            "검증 실패 시 기존 Apache로 자동 롤백합니다. 계속하시겠습니까?",
            "Apache 업데이트 실행",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;

        var runLog = new List<string>();
        void Log(string text)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
            runLog.Add(line);
            AppendDetail(XamppComponentType.Apache, line);
        }

        _apacheUpdateRunning = true;
        RefreshApacheExecuteEnabled();
        SetBusy(true, $"Apache {current} → {target.Version} 업데이트 준비 중...");
        Log($"실행 시작: Apache {current} → {target.Version}");
        Log($"XAMPP 경로: {installation.RootPath}");
        Log($"패키지: {package.PackagePath}");
        Log($"롤백 manifest: {backup.ManifestPath}");

        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => StatusText.Text = $"Apache {current} → {target.Version} 업데이트 작업 중... {stopwatch.Elapsed:mm\\:ss}";
        timer.Start();
        var success = false;

        try
        {
            var result = await Task.Run(async () =>
                await _apacheUpdateExecutor.ExecuteAsync(installation, target, package, backup));

            timer.Stop();
            foreach (var step in result.Steps) Log("실행: " + step);
            foreach (var warning in result.Warnings) Log("주의: " + warning);

            if (!result.Success)
            {
                Log(result.RolledBack ? "업데이트 실패 후 자동 롤백됨" : "업데이트 실행 전/초기 단계에서 실패함");
                Log("오류: " + result.Error);
                StatusText.Text = "Apache 업데이트 실패: " + result.Error;
                MessageBox.Show(this,
                    $"Apache 업데이트에 실패했습니다.\n\n{result.Error}\n\n" +
                    (result.RolledBack ? "기존 Apache로 자동 롤백했습니다." : "Apache 디렉터리 교체 전 또는 초기 단계에서 중단되었습니다."),
                    "Apache 업데이트",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            success = true;
            Log($"업데이트 완료: Apache {current} → {target.Version}");
            StatusText.Text = $"Apache 업데이트 완료: {current} → {target.Version}";
            MessageBox.Show(this,
                $"Apache 업데이트가 완료되었습니다.\n\n{current} → {target.Version}",
                "Apache 업데이트",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await InspectAsync(installation.RootPath, "PostApacheUpdate");
            AppendDetail(XamppComponentType.Apache, "--- 최근 Apache 업데이트 실행 로그 ---");
            foreach (var line in runLog) AppendDetail(XamppComponentType.Apache, line);
        }
        catch (Exception ex)
        {
            timer.Stop();
            Log("실행 예외: " + ex.Message);
            StatusText.Text = "Apache 업데이트 실행 실패: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Apache 업데이트", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            timer.Stop();
            stopwatch.Stop();
            Log($"실행 종료: {(success ? "성공" : "실패/중단")} / 경과 {stopwatch.Elapsed:mm\\:ss}");
            try
            {
                var path = _executionLogService.Save("Apache", runLog);
                AppendDetail(XamppComponentType.Apache, "로그 파일: " + path);
                if (_apacheLogButton is not null) _apacheLogButton.IsEnabled = true;
            }
            catch (Exception logEx)
            {
                AppendDetail(XamppComponentType.Apache, "로그 저장 실패: " + logEx.Message);
            }

            _apacheUpdateRunning = false;
            SetBusy(false);
            RefreshApacheExecuteEnabled();
        }
    }
}
