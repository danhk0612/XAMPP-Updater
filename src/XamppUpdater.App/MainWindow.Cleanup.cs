using System.Windows;
using System.Windows.Controls;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly ILocalStorageCleanupService _cleanupService = new LocalStorageCleanupService();
    private Button? _cleanupButton;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _cleanupButton ??= new Button();
    }

    private async void CleanupButton_Click(object sender, RoutedEventArgs e)
    {
        LocalStorageUsage usage;
        try
        {
            usage = await Task.Run(_cleanupService.GetUsage);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"저장 데이터 용량 확인 실패:\n{ex.Message}", "XAMPP Updater", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (usage.Files == 0)
        {
            MessageBox.Show(this, "삭제할 롤백 백업 또는 다운로드 캐시가 없습니다.", "XAMPP Updater", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"XAMPP Updater가 생성한 롤백 백업과 다운로드/확장/런타임 캐시를 삭제합니다.\n\n" +
            $"파일: {usage.Files:N0}개\n용량: {FormatBytes(usage.Bytes)}\n위치: {usage.RootPath}\n\n" +
            "설정 이력은 삭제하지 않습니다. 삭제한 롤백 백업은 복구할 수 없습니다. 계속하시겠습니까?",
            "저장 데이터 정리",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;

        SetBusy(true, "롤백 백업과 캐시를 정리하는 중...");
        try
        {
            var result = await Task.Run(_cleanupService.Clear);
            _preflightReports.Clear();
            _packageResults.Clear();
            ResetPreparedStateAfterCleanup();

            if (result.Errors.Count > 0)
            {
                StatusText.Text = $"일부 정리 완료: {result.DeletedFiles:N0}개 / {FormatBytes(result.ReclaimedBytes)}";
                MessageBox.Show(this,
                    $"일부 파일을 삭제하지 못했습니다.\n\n{string.Join(Environment.NewLine, result.Errors)}",
                    "저장 데이터 정리",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                StatusText.Text = $"정리 완료: {result.DeletedFiles:N0}개 / {FormatBytes(result.ReclaimedBytes)} 확보";
            }
        }
        finally
        {
            SetBusy(false);
            RefreshPrimaryUpdateButtons();
        }
    }

    private void ResetPreparedStateAfterCleanup()
    {
        foreach (var type in new[] { XamppComponentType.Apache, XamppComponentType.Php, XamppComponentType.MariaDb })
        {
            SetBackupEnabled(type, false);
            SetDiffEnabled(type, false);
            var combo = GetTargetComboBox(type);
            if (combo.SelectedItem is UpdateTargetOption target)
            {
                SetPreflightEnabled(type, true);
                SetPackageEnabled(type, target.PackageUrl is not null);
                SetPreflightText(type, "저장 데이터 정리 후 준비 상태가 초기화되었습니다.");
            }
        }
    }
}
