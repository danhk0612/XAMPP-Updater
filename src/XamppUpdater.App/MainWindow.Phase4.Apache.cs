using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly IApacheUpdateExecutor _apacheUpdateExecutor = new VerifiedApacheUpdateExecutor();
    private readonly IApacheMigrationReviewService _apacheMigrationReviewService = new ApacheMigrationReviewService();
    private readonly IApacheMigrationOverrideStore _apacheMigrationOverrideStore = new ApacheMigrationOverrideStore();
    private Button? _apacheExecuteButton;
    private Button? _apacheReviewButton;
    private Button? _apacheLogButton;
    private bool _apacheUpdateRunning;
    private bool _apacheReviewRunning;

    private void InitializeApachePhase4Ui()
    {
        if (_apacheExecuteButton is not null) return;
        if (ApacheDiffButton.Parent is not Panel actionPanel) return;

        _apacheReviewButton = new Button
        {
            Content = "마이그레이션 검토",
            Margin = new Thickness(0, 0, 8, 6),
            Padding = new Thickness(12, 5, 12, 5),
            IsEnabled = false,
            ToolTip = "새 Apache 바이너리로 현재 conf와 참조 모듈을 실제 교체 전에 검증하고 설정을 직접 편집·확정합니다."
        };
        _apacheReviewButton.Click += ApacheReviewButton_Click;
        actionPanel.Children.Add(_apacheReviewButton);

        _apacheExecuteButton = new Button
        {
            Content = "Apache 업데이트 실행",
            Margin = new Thickness(0, 0, 8, 6),
            Padding = new Thickness(12, 5, 12, 5),
            IsEnabled = false,
            ToolTip = "사전 설정 검증 후 준비된 패키지와 롤백 백업으로 Apache를 실제 교체하고 실패 시 자동 롤백합니다."
        };
        _apacheExecuteButton.Click += ApacheExecuteButton_Click;
        actionPanel.Children.Add(_apacheExecuteButton);

        _apacheLogButton = new Button
        {
            Content = "최근 로그 열기",
            Margin = new Thickness(0, 0, 8, 6),
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
        if (_apacheExecuteButton is null || _lastInstallation is null)
        {
            if (_apacheExecuteButton is not null) _apacheExecuteButton.IsEnabled = false;
            if (_apacheReviewButton is not null) _apacheReviewButton.IsEnabled = false;
            return;
        }

        var target = ApacheTargetComboBox.SelectedItem as UpdateTargetOption;
        var packageReady = target is not null &&
                           _packageResults.TryGetValue(XamppComponentType.Apache, out var package) &&
                           string.Equals(package.Version, target.Version, StringComparison.OrdinalIgnoreCase);

        if (_apacheReviewButton is not null)
        {
            _apacheReviewButton.IsEnabled = packageReady && !_apacheUpdateRunning && !_apacheReviewRunning;
            if (packageReady && target is not null)
            {
                var confRoot = Path.Combine(_lastInstallation.RootPath, "apache", "conf");
                var reviewed = _apacheMigrationOverrideStore.TryLoad(_lastInstallation.RootPath, target.Version, confRoot);
                _apacheReviewButton.Content = reviewed is null ? "마이그레이션 검토" : "마이그레이션 검토 ✓";
            }
            else
            {
                _apacheReviewButton.Content = "마이그레이션 검토";
            }
        }

        if (_apacheUpdateRunning || _apacheReviewRunning || !packageReady || target is null)
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

    private async Task EnsureApacheRuntimeAsync(PackagePreparationResult package)
    {
        Version? minimum = null;
        if (package.FileName.Contains("-VS18", StringComparison.OrdinalIgnoreCase))
            minimum = new Version(14, 51, 36247, 0);

        if (minimum is null) return;

        StatusText.Text = $"Apache VC++ 런타임 확인 중... 최소 {minimum.Major}.{minimum.Minor}.{minimum.Build}";
        var result = await _vcRuntimeInstaller.EnsureMinimumAsync(package.Architecture, minimum);
        AppendDetail(
            XamppComponentType.Apache,
            $"VC++ 런타임 확인: 최소 {minimum} / before={result.BeforeVersion?.ToString() ?? "확인 불가"} / after={result.AfterVersion?.ToString() ?? "확인 불가"} / installed={result.Installed} / exit={result.ExitCode}");

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Apache {package.Version} 패키지에 필요한 Visual C++ Redistributable {minimum} 이상을 준비하지 못했습니다. " +
                $"현재 런타임: {result.AfterVersion?.ToString() ?? "확인 불가"}, 종료 코드: {result.ExitCode}");
        }

        if (result.RebootRequired)
        {
            throw new InvalidOperationException(
                "Apache용 Visual C++ Redistributable 설치는 완료됐지만 Windows 재부팅이 필요합니다. 재부팅 후 마이그레이션 검토를 다시 실행하세요.");
        }
    }

    private async void ApacheReviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apacheReviewRunning || _apacheUpdateRunning || _lastInstallation is null ||
            ApacheTargetComboBox.SelectedItem is not UpdateTargetOption target ||
            !_packageResults.TryGetValue(XamppComponentType.Apache, out var package))
            return;

        _apacheReviewRunning = true;
        RefreshApacheExecuteEnabled();
        SetBusy(true, $"Apache {target.Version} 설정 사전 검증 중...");

        try
        {
            var installation = _lastInstallation;
            await EnsureApacheRuntimeAsync(package);

            var review = await Task.Run(async () =>
                await _apacheMigrationReviewService.BuildAsync(installation, target, package));

            var confRoot = Path.Combine(installation.RootPath, "apache", "conf");
            var dialog = new ApacheMigrationReviewWindow(review, confRoot) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.FinalFiles is null)
            {
                StatusText.Text = "Apache 설정 마이그레이션 검토를 취소했습니다.";
                return;
            }

            var saved = _apacheMigrationOverrideStore.Save(
                installation.RootPath,
                target.Version,
                confRoot,
                dialog.FinalFiles);
            AppendDetail(XamppComponentType.Apache, "Apache 설정 마이그레이션 적용안 확정: " + saved);

            SetBusy(true, $"Apache {target.Version} 편집 설정 재검증 중...");
            var verified = await Task.Run(async () =>
                await _apacheMigrationReviewService.BuildAsync(installation, target, package));

            if (verified.SyntaxValid)
            {
                AppendDetail(XamppComponentType.Apache,
                    $"Apache {target.Version} 편집 설정 사전 검증 통과: conf {verified.ConfigurationFiles.Count}개");
                StatusText.Text = $"Apache {target.Version} 설정 적용안 확정 및 검증 통과";
            }
            else
            {
                AppendDetail(XamppComponentType.Apache, "Apache 편집 설정 사전 검증 실패: " + verified.ValidationOutput);
                StatusText.Text = $"Apache {target.Version} 설정 적용안은 저장됐지만 사전 검증에 실패했습니다.";
                MessageBox.Show(this,
                    "편집한 Apache 설정은 저장됐지만 대상 Apache의 httpd -t 검증에 실패했습니다.\n\n" + verified.ValidationOutput +
                    "\n\n마이그레이션 검토를 다시 열어 수정하세요. 실제 업데이트는 이 상태에서 실행되지 않습니다.",
                    "Apache 설정 마이그레이션",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Apache 설정 사전 검증 실패: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Apache 설정 마이그레이션", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _apacheReviewRunning = false;
            SetBusy(false);
            RefreshApacheExecuteEnabled();
        }
    }

    private async void ApacheExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_apacheUpdateRunning || _apacheReviewRunning || _lastInstallation is null ||
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

        SetBusy(true, $"Apache {target.Version} 설정 최종 사전 검증 중...");
        ApacheMigrationReviewResult precheck;
        try
        {
            await EnsureApacheRuntimeAsync(package);
            precheck = await Task.Run(async () =>
                await _apacheMigrationReviewService.BuildAsync(installation, target, package));
        }
        catch (Exception ex)
        {
            SetBusy(false);
            MessageBox.Show(this, "Apache 설정 사전 검증 실행에 실패했습니다.\n\n" + ex.Message,
                "Apache 업데이트", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        SetBusy(false);

        if (!precheck.SyntaxValid)
        {
            var confRoot = Path.Combine(installation.RootPath, "apache", "conf");
            new ApacheMigrationReviewWindow(precheck, confRoot) { Owner = this }.ShowDialog();
            MessageBox.Show(this,
                "현재 Apache 설정 적용안이 대상 버전의 사전 검증을 통과하지 못했습니다. 실제 업데이트는 실행하지 않습니다.",
                "Apache 업데이트",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Apache를 실제로 업데이트합니다.\n\n현재: {current}\n대상: {target.Version}\n\n" +
            "대상 Apache 바이너리로 최종 설정의 httpd -t 사전 검증을 통과했습니다. " +
            "확정한 설정이 있으면 해당 적용안을 사용하며, 새 패키지에 없는 참조 모듈은 필요한 경우 기존 설치에서 보존합니다. " +
            "실제 교체 후에도 다시 검증하며 실패 시 기존 Apache로 자동 롤백합니다. 계속하시겠습니까?",
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
        Log($"사전 검증: httpd -t 통과 / 설정 파일 {precheck.ConfigurationFiles.Count}개");
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
