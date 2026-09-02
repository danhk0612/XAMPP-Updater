using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly IBackupLocatorService _backupLocator = new BackupLocatorService();
    private readonly IPhpUpdateExecutor _phpUpdateExecutor = new PhpUpdateExecutor();
    private readonly IVisualCppRuntimeInstaller _vcRuntimeInstaller = new VisualCppRuntimeInstaller();
    private readonly IExecutionLogService _executionLogService = new ExecutionLogService();
    private Button? _phpExecuteButton;
    private Button? _phpLogButton;
    private bool _phpUpdateRunning;

    private void InitializePhase4Ui()
    {
        if (_phpExecuteButton is not null)
        {
            return;
        }

        if (PhpDiffButton.Parent is not StackPanel actionPanel)
        {
            return;
        }

        _phpExecuteButton = new Button
        {
            Content = "PHP 업데이트 실행",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 5, 12, 5),
            IsEnabled = false,
            ToolTip = "준비된 패키지와 롤백 백업을 사용해 PHP를 실제 교체합니다."
        };
        _phpExecuteButton.Click += PhpExecuteButton_Click;
        actionPanel.Children.Add(_phpExecuteButton);

        _phpLogButton = new Button
        {
            Content = "최근 로그 열기",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 5, 12, 5),
            IsEnabled = _executionLogService.FindLatest("PHP") is not null,
            ToolTip = "%LOCALAPPDATA%\\XamppUpdater\\Logs에 저장된 최근 PHP 업데이트 로그를 엽니다."
        };
        _phpLogButton.Click += PhpLogButton_Click;
        actionPanel.Children.Add(_phpLogButton);

        PhpDiffButton.IsEnabledChanged += (_, _) => RefreshPhpExecuteEnabled();
        PhpBackupButton.IsEnabledChanged += (_, _) => RefreshPhpExecuteEnabled();
        PhpTargetComboBox.SelectionChanged += (_, _) => RefreshPhpExecuteEnabled();
    }

    private void PhpLogButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _executionLogService.FindLatest("PHP");
        if (path is null)
        {
            MessageBox.Show(this, "저장된 PHP 업데이트 로그가 없습니다.", "PHP 업데이트 로그", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void RefreshPhpExecuteEnabled()
    {
        if (_phpExecuteButton is null || _phpUpdateRunning || _lastInstallation is null)
        {
            if (_phpExecuteButton is not null) _phpExecuteButton.IsEnabled = false;
            return;
        }

        if (PhpTargetComboBox.SelectedItem is not UpdateTargetOption target ||
            !_packageResults.TryGetValue(XamppComponentType.Php, out var package) ||
            !string.Equals(package.Version, target.Version, StringComparison.OrdinalIgnoreCase))
        {
            _phpExecuteButton.IsEnabled = false;
            return;
        }

        var current = _lastInstallation.Components.FirstOrDefault(item => item.Type == XamppComponentType.Php)?.Version;
        if (current is null)
        {
            _phpExecuteButton.IsEnabled = false;
            return;
        }

        _phpExecuteButton.IsEnabled = _backupLocator.FindLatest(
            _lastInstallation.RootPath,
            XamppComponentType.Php,
            current,
            target.Version) is not null;
    }

    private async void PhpExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_phpUpdateRunning || _lastInstallation is null ||
            PhpTargetComboBox.SelectedItem is not UpdateTargetOption target ||
            !_packageResults.TryGetValue(XamppComponentType.Php, out var package))
        {
            return;
        }

        var installation = _lastInstallation;
        var current = installation.Components.FirstOrDefault(item => item.Type == XamppComponentType.Php)?.Version;
        if (current is null)
        {
            return;
        }

        var backup = _backupLocator.FindLatest(
            installation.RootPath,
            XamppComponentType.Php,
            current,
            target.Version);
        if (backup is null)
        {
            MessageBox.Show(this,
                "현재 PHP 버전과 선택 버전에 일치하는 롤백 백업을 찾지 못했습니다. 준비 점검 후 백업 생성을 다시 실행하세요.",
                "PHP 업데이트",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            RefreshPhpExecuteEnabled();
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"PHP를 실제로 업데이트합니다.\n\n현재: {current}\n대상: {target.Version}\n\n" +
            "Apache가 실행 중이면 자동 중지하며, php.ini와 Apache PHP 모듈 설정을 마이그레이션합니다. " +
            "필요한 Visual C++ 런타임이 부족하면 Microsoft 공식 재배포 패키지 설치를 요청할 수 있습니다. " +
            "검증 실패 시 자동 롤백합니다. 계속하시겠습니까?",
            "PHP 업데이트 실행",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var runLog = new List<string>();
        void Log(string text)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
            runLog.Add(line);
            AppendDetail(XamppComponentType.Php, line);
        }

        _phpUpdateRunning = true;
        RefreshPhpExecuteEnabled();
        SetBusy(true, $"PHP {current} → {target.Version} 업데이트 준비 중...");
        Log($"실행 시작: PHP {current} → {target.Version}");
        Log($"XAMPP 경로: {installation.RootPath}");
        Log($"패키지: {package.PackagePath}");
        Log($"롤백 manifest: {backup.ManifestPath}");

        var stopwatch = Stopwatch.StartNew();
        var progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        progressTimer.Tick += (_, _) =>
        {
            StatusText.Text = $"PHP {current} → {target.Version} 업데이트 작업 중... {stopwatch.Elapsed:mm\\:ss}";
        };
        progressTimer.Start();

        var completedSuccessfully = false;
        try
        {
            var minimumRuntime = GetRequiredVcRuntime(target.Version);
            if (minimumRuntime is not null)
            {
                StatusText.Text = $"Visual C++ 런타임 확인 중... 최소 {minimumRuntime.Major}.{minimumRuntime.Minor}";
                Log($"Visual C++ 런타임 확인: 최소 {minimumRuntime}");
                var runtimeResult = await _vcRuntimeInstaller.EnsureMinimumAsync(package.Architecture, minimumRuntime);
                Log($"VC++ 런타임 결과: before={runtimeResult.BeforeVersion?.ToString() ?? "확인 불가"}, after={runtimeResult.AfterVersion?.ToString() ?? "확인 불가"}, installed={runtimeResult.Installed}, exit={runtimeResult.ExitCode}");
                if (!runtimeResult.Success)
                {
                    throw new InvalidOperationException(
                        $"Visual C++ Redistributable 설치/검증에 실패했습니다. 종료 코드: {runtimeResult.ExitCode}, " +
                        $"현재 런타임: {runtimeResult.AfterVersion?.ToString() ?? "확인 불가"}");
                }

                if (!string.IsNullOrWhiteSpace(runtimeResult.Sha256))
                {
                    Log($"VC++ 재배포 패키지 SHA256: {runtimeResult.Sha256}");
                }

                if (runtimeResult.RebootRequired)
                {
                    throw new InvalidOperationException(
                        "Visual C++ Redistributable 설치는 완료됐지만 Windows 재부팅이 필요합니다. 재부팅 후 PHP 업데이트를 다시 실행하세요.");
                }
            }

            var result = await Task.Run(async () =>
                await _phpUpdateExecutor.ExecuteAsync(installation, target, package, backup));

            progressTimer.Stop();
            foreach (var step in result.Steps)
            {
                Log("실행: " + step);
            }
            foreach (var warning in result.Warnings)
            {
                Log("주의: " + warning);
            }

            if (!result.Success)
            {
                Log(result.RolledBack ? "업데이트 실패 후 자동 롤백됨" : "업데이트 실행 전/초기 단계에서 실패함");
                Log("오류: " + result.Error);
                StatusText.Text = "PHP 업데이트 실패: " + result.Error;
                MessageBox.Show(this,
                    $"PHP 업데이트에 실패했습니다.\n\n{result.Error}\n\n" +
                    (result.RolledBack ? "기존 PHP로 자동 롤백했습니다." : "PHP 디렉터리 교체 전 또는 초기 단계에서 중단되었습니다."),
                    "PHP 업데이트",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            completedSuccessfully = true;
            Log($"업데이트 완료: PHP {current} → {target.Version}");
            StatusText.Text = $"PHP 업데이트 완료: {current} → {target.Version}";
            MessageBox.Show(this,
                $"PHP 업데이트가 완료되었습니다.\n\n{current} → {target.Version}",
                "PHP 업데이트",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await InspectAsync(installation.RootPath, "PostUpdate");
            AppendDetail(XamppComponentType.Php, "--- 최근 PHP 업데이트 실행 로그 ---");
            foreach (var line in runLog)
            {
                AppendDetail(XamppComponentType.Php, line);
            }
        }
        catch (Exception ex)
        {
            progressTimer.Stop();
            StatusText.Text = "PHP 업데이트 실행 실패: " + ex.Message;
            Log("실행 예외: " + ex.Message);
            MessageBox.Show(this, ex.Message, "PHP 업데이트", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            progressTimer.Stop();
            stopwatch.Stop();
            Log($"실행 종료: {(completedSuccessfully ? "성공" : "실패/중단")} / 경과 {stopwatch.Elapsed:mm\\:ss}");
            try
            {
                var logPath = _executionLogService.Save("PHP", runLog);
                AppendDetail(XamppComponentType.Php, "로그 파일: " + logPath);
                if (_phpLogButton is not null) _phpLogButton.IsEnabled = true;
            }
            catch (Exception logEx)
            {
                AppendDetail(XamppComponentType.Php, "로그 저장 실패: " + logEx.Message);
            }

            _phpUpdateRunning = false;
            SetBusy(false);
            RefreshPhpExecuteEnabled();
        }
    }

    private static Version? GetRequiredVcRuntime(string phpVersion)
    {
        var parts = phpVersion.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
        {
            return null;
        }

        if (major > 8 || major == 8 && minor >= 5) return new Version(14, 44, 0, 0);
        if (major == 8 && minor >= 4) return new Version(14, 40, 0, 0);
        if (major == 8 && minor >= 0) return new Version(14, 29, 0, 0);
        return null;
    }
}
