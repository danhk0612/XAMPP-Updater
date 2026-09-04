using System.Windows;
using System.Windows.Controls;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly SelfUpdateService _selfUpdateService = new();
    private bool _selfUpdateUiInitialized;

    private static string AppUpdateText(string korean, string english) =>
        LocalizationCatalog.Text(korean, english);

    private void InitializeSelfUpdateUi()
    {
        if (_selfUpdateUiInitialized) return;
        _selfUpdateUiInitialized = true;
        if (ApacheNavButton.Parent is not StackPanel panel) return;

        var cleanupButton = panel.Children
            .OfType<Button>()
            .FirstOrDefault(button =>
                string.Equals(button.Content?.ToString(), "저장 데이터 정리", StringComparison.Ordinal) ||
                string.Equals(button.Content?.ToString(), LocalizationCatalog.TranslateUserText("저장 데이터 정리"), StringComparison.Ordinal));
        if (cleanupButton is null) return;

        var button = new Button
        {
            Content = AppUpdateText("앱 업데이트 확인", "Check for app updates"),
            Tag = "AppUpdate",
            Margin = new Thickness(0, 0, 0, 5),
            Padding = new Thickness(8, 6, 8, 6)
        };
        button.Click += CheckAppUpdate_Click;
        panel.Children.Insert(panel.Children.IndexOf(cleanupButton) + 1, button);
    }

    private async void CheckAppUpdate_Click(object sender, RoutedEventArgs e)
    {
        var title = AppUpdateText("앱 업데이트", "App update");

        if (!_selfUpdateService.IsPublishedExecutable)
        {
            MessageBox.Show(
                this,
                AppUpdateText(
                    "dotnet run 개발 실행 상태에서는 앱 자체 업데이트를 적용할 수 없습니다.\n\n배포된 XAMPP-Updater.exe에서 다시 확인하세요.",
                    "Self-update is not available when running with dotnet run.\n\nRun the published XAMPP-Updater.exe and check again."),
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SetBusy(true, AppUpdateText(
            "XAMPP Updater 최신 릴리스를 확인하는 중...",
            "Checking the latest XAMPP Updater release..."));

        var shuttingDown = false;
        AppUpdateProgressWindow? progressWindow = null;
        CancellationTokenSource? downloadCancellation = null;
        try
        {
            var (update, releaseExists) = await _selfUpdateService.CheckLatestAsync();
            if (!releaseExists)
            {
                StatusText.Text = AppUpdateText(
                    "아직 게시된 XAMPP Updater 릴리스가 없습니다.",
                    "No XAMPP Updater release has been published yet.");
                MessageBox.Show(
                    this,
                    AppUpdateText(
                        "아직 GitHub에 게시된 XAMPP Updater 릴리스가 없습니다.\n\n첫 릴리스가 게시된 이후부터 앱 자체 업데이트를 사용할 수 있습니다.",
                        "No XAMPP Updater release has been published on GitHub yet.\n\nSelf-update will be available after the first release is published."),
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (update is null)
            {
                var version = _selfUpdateService.CurrentVersion.ToString(3);
                StatusText.Text = AppUpdateText(
                    $"XAMPP Updater는 최신 버전입니다. ({version})",
                    $"XAMPP Updater is up to date. ({version})");
                MessageBox.Show(
                    this,
                    AppUpdateText(
                        $"현재 버전 {version}이 최신 버전입니다.",
                        $"Current version {version} is the latest version."),
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var currentVersion = _selfUpdateService.CurrentVersion.ToString(3);
            var newVersion = update.Version.ToString(3);
            var answer = MessageBox.Show(
                this,
                AppUpdateText(
                    $"새 XAMPP Updater 버전 {newVersion}을 사용할 수 있습니다.\n\n현재 버전: {currentVersion}\n새 버전: {newVersion}\n\n다운로드 중에는 취소할 수 있습니다. SHA256 검증이 시작된 뒤에는 현재 EXE 보호를 위해 취소하지 않고 교체 단계까지 진행합니다. 계속하시겠습니까?",
                    $"XAMPP Updater {newVersion} is available.\n\nCurrent version: {currentVersion}\nNew version: {newVersion}\n\nYou can cancel while the file is downloading. Once SHA256 verification starts, cancellation is disabled to protect the current executable and the update proceeds through the replacement stage. Continue?"),
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);
            if (answer != MessageBoxResult.Yes)
            {
                StatusText.Text = AppUpdateText(
                    $"앱 업데이트를 취소했습니다. 사용 가능 버전: {newVersion}",
                    $"App update canceled. Available version: {newVersion}");
                return;
            }

            downloadCancellation = new CancellationTokenSource();
            progressWindow = new AppUpdateProgressWindow(this, _selfUpdateService.CurrentVersion, update.Version);
            progressWindow.CancelRequested += (_, _) => downloadCancellation.Cancel();
            progressWindow.Show();

            StatusText.Text = AppUpdateText(
                $"XAMPP Updater {newVersion} 다운로드 중...",
                $"Downloading XAMPP Updater {newVersion}...");
            var progress = new Progress<AppUpdateDownloadProgress>(progressWindow.ReportDownload);
            string downloadedPath;
            try
            {
                downloadedPath = await _selfUpdateService.DownloadAsync(update, progress, downloadCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                progressWindow.CloseAfterCancellation();
                progressWindow = null;
                StatusText.Text = AppUpdateText(
                    "앱 업데이트 다운로드를 취소했습니다. 현재 EXE는 변경되지 않았습니다.",
                    "App update download canceled. The current executable was not changed.");
                return;
            }

            progressWindow.BeginVerification();
            StatusText.Text = AppUpdateText(
                "앱 업데이트 다운로드 완료. SHA256 검증 중...",
                "App update download complete. Verifying SHA256...");
            var stagedPath = await _selfUpdateService.VerifyAndStageAsync(update, downloadedPath);
            StatusText.Text = AppUpdateText(
                "앱 업데이트 검증 완료. 5초 후 재시작합니다.",
                "App update verified. Restarting in 5 seconds.");

            for (var seconds = 5; seconds > 0; seconds--)
            {
                progressWindow.SetRestartCountdown(seconds);
                if (progressWindow.RestartNowRequested) break;
                await Task.Delay(1000);
            }

            _selfUpdateService.StartReplacement(stagedPath);
            progressWindow.AllowShutdown();
            shuttingDown = true;
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            if (progressWindow is not null)
            {
                progressWindow.AllowShutdown();
                progressWindow.Close();
                progressWindow = null;
            }
            StatusText.Text = AppUpdateText(
                "앱 업데이트 관리자 권한 요청이 취소되었습니다.",
                "Administrator permission request for app update was canceled.");
            MessageBox.Show(
                this,
                AppUpdateText(
                    "관리자 권한 요청이 취소되어 앱 업데이트를 적용하지 않았습니다.",
                    "Administrator permission was canceled, so the app update was not applied."),
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            if (progressWindow is not null)
            {
                progressWindow.AllowShutdown();
                progressWindow.Close();
                progressWindow = null;
            }

            var localizedError = SelfUpdateErrorLocalization.Translate(ex.Message);
            StatusText.Text = AppUpdateText(
                $"앱 업데이트 실패: {localizedError}",
                $"App update failed: {localizedError}");
            MessageBox.Show(
                this,
                AppUpdateText(
                    "앱 업데이트에 실패했습니다.\n\n",
                    "The app update failed.\n\n") + localizedError,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            downloadCancellation?.Dispose();
            if (!shuttingDown) SetBusy(false);
        }
    }
}
