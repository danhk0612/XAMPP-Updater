using System.Windows;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly SelfUpdateService _selfUpdateService = new();

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
                $"새 XAMPP Updater 버전 {update.Version.ToString(3)}을 사용할 수 있습니다.\n\n현재 버전: {_selfUpdateService.CurrentVersion.ToString(3)}\n새 버전: {update.Version.ToString(3)}\n\nSHA256 검증 후 현재 실행 파일을 교체하고 앱을 다시 시작합니다. 계속하시겠습니까?",
                "앱 업데이트",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);
            if (answer != MessageBoxResult.Yes)
            {
                StatusText.Text = $"앱 업데이트를 취소했습니다. 사용 가능 버전: {update.Version.ToString(3)}";
                return;
            }

            StatusText.Text = $"XAMPP Updater {update.Version.ToString(3)}을 다운로드하고 검증하는 중...";
            var stagedPath = await _selfUpdateService.DownloadAndVerifyAsync(update);
            StatusText.Text = "앱 업데이트 검증 완료. 재시작하여 적용합니다.";
            _selfUpdateService.StartReplacement(stagedPath);
            shuttingDown = true;
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            StatusText.Text = "앱 업데이트 관리자 권한 요청이 취소되었습니다.";
            MessageBox.Show(this, "관리자 권한 요청이 취소되어 앱 업데이트를 적용하지 않았습니다.", "앱 업데이트", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"앱 업데이트 실패: {ex.Message}";
            MessageBox.Show(this, "앱 업데이트에 실패했습니다.\n\n" + ex.Message, "앱 업데이트", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!shuttingDown) SetBusy(false);
        }
    }
}
