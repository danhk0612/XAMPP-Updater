using System.Windows;
using System.Windows.Controls;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly SelfUpdateService _selfUpdateService = new();
    private bool _selfUpdateUiInitialized;

    private void InitializeSelfUpdateUi()
    {
        if (_selfUpdateUiInitialized) return;
        _selfUpdateUiInitialized = true;
        if (ApacheNavButton.Parent is not StackPanel panel) return;

        var cleanupButton = panel.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "저장 데이터 정리", StringComparison.Ordinal));
        if (cleanupButton is null) return;

        var button = new Button
        {
            Content = "앱 업데이트 확인",
            Margin = new Thickness(0, 0, 0, 5),
            Padding = new Thickness(8, 6, 8, 6)
        };
        button.Click += CheckAppUpdate_Click;
        panel.Children.Insert(panel.Children.IndexOf(cleanupButton) + 1, button);
    }

    private async void CheckAppUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!_selfUpdateService.IsPublishedExecutable)
        {
            MessageBox.Show(
                this,
                "dotnet run 개발 실행 상태에서는 앱 자체 업데이트를 적용할 수 없습니다.\n\n배포된 XAMPP-Updater.exe에서 다시 확인하세요.",
                "앱 업데이트",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SetBusy(true, "XAMPP Updater 최신 릴리스를 확인하는 중...");
        var shuttingDown = false;
        AppUpdateProgressWindow? progressWindow = null;
        CancellationTokenSource? downloadCancellation = null;
        try
        {
            var update = await _selfUpdateService.CheckLatestAsync();
            if (update is null)
            {
                StatusText.Text = $"XAMPP Updater는 최신 버전입니다. ({_selfUpdateService.CurrentVersion.ToString(3)})";
                MessageBox.Show(
                    this,
                    $"현재 버전 {_selfUpdateService.CurrentVersion.ToString(3)}이 최신 버전입니다.",
                    "앱 업데이트",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var answer = MessageBox.Show(
                this,
                $"새 XAMPP Updater 버전 {update.Version.ToString(3)}을 사용할 수 있습니다.\n\n현재 버전: {_selfUpdateService.CurrentVersion.ToString(3)}\n새 버전: {update.Version.ToString(3)}\n\n다운로드 중에는 취소할 수 있습니다. SHA256 검증이 시작된 뒤에는 현재 EXE 보호를 위해 취소하지 않고 교체 단계까지 진행합니다. 계속하시겠습니까?",
                "앱 업데이트",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);
            if (answer != MessageBoxResult.Yes)
            {
                StatusText.Text = $"앱 업데이트를 취소했습니다. 사용 가능 버전: {update.Version.ToString(3)}";
                return;
            }

            downloadCancellation = new CancellationTokenSource();
            progressWindow = new AppUpdateProgressWindow(this, _selfUpdateService.CurrentVersion, update.Version);
            progressWindow.CancelRequested += (_, _) => downloadCancellation.Cancel();
            progressWindow.Show();

            StatusText.Text = $"XAMPP Updater {update.Version.ToString(3)} 다운로드 중...";
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
                StatusText.Text = "앱 업데이트 다운로드를 취소했습니다. 현재 EXE는 변경되지 않았습니다.";
                return;
            }

            progressWindow.BeginVerification();
            StatusText.Text = "앱 업데이트 다운로드 완료. SHA256 검증 중...";
            var stagedPath = await _selfUpdateService.VerifyAndStageAsync(update, downloadedPath);
            StatusText.Text = "앱 업데이트 검증 완료. 5초 후 재시작합니다.";

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
            StatusText.Text = "앱 업데이트 관리자 권한 요청이 취소되었습니다.";
            MessageBox.Show(this, "관리자 권한 요청이 취소되어 앱 업데이트를 적용하지 않았습니다.", "앱 업데이트", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            if (progressWindow is not null)
            {
                progressWindow.AllowShutdown();
                progressWindow.Close();
                progressWindow = null;
            }
            StatusText.Text = $"앱 업데이트 실패: {ex.Message}";
            MessageBox.Show(this, "앱 업데이트에 실패했습니다.\n\n" + ex.Message, "앱 업데이트", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            downloadCancellation?.Dispose();
            if (!shuttingDown) SetBusy(false);
        }
    }
}
