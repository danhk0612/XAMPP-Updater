using System.Windows;
using System.Windows.Controls;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly IBackupLocatorService _backupLocator = new BackupLocatorService();
    private readonly IPhpUpdateExecutor _phpUpdateExecutor = new PhpUpdateExecutor();
    private Button? _phpExecuteButton;
    private bool _phpUpdateRunning;

    private void InitializePhase4Ui()
    {
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

        PhpDiffButton.IsEnabledChanged += (_, _) => RefreshPhpExecuteEnabled();
        PhpBackupButton.IsEnabledChanged += (_, _) => RefreshPhpExecuteEnabled();
        PhpTargetComboBox.SelectionChanged += (_, _) => RefreshPhpExecuteEnabled();
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

        var current = _lastInstallation.Components.FirstOrDefault(item => item.Type == XamppComponentType.Php)?.Version;
        if (current is null)
        {
            return;
        }

        var backup = _backupLocator.FindLatest(
            _lastInstallation.RootPath,
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
            "검증 실패 시 자동 롤백합니다. 계속하시겠습니까?",
            "PHP 업데이트 실행",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _phpUpdateRunning = true;
        RefreshPhpExecuteEnabled();
        SetBusy(true, $"PHP {current} → {target.Version} 업데이트 실행 중...");

        try
        {
            var result = await _phpUpdateExecutor.ExecuteAsync(_lastInstallation, target, package, backup);
            foreach (var step in result.Steps)
            {
                AppendDetail(XamppComponentType.Php, "실행: " + step);
            }
            foreach (var warning in result.Warnings)
            {
                AppendDetail(XamppComponentType.Php, "주의: " + warning);
            }

            if (!result.Success)
            {
                AppendDetail(XamppComponentType.Php,
                    result.RolledBack ? "업데이트 실패 후 자동 롤백됨" : "업데이트 실행 전/초기 단계에서 실패함");
                StatusText.Text = "PHP 업데이트 실패: " + result.Error;
                MessageBox.Show(this,
                    $"PHP 업데이트에 실패했습니다.\n\n{result.Error}\n\n" +
                    (result.RolledBack ? "기존 PHP로 자동 롤백했습니다." : "PHP 디렉터리 교체 전 또는 초기 단계에서 중단되었습니다."),
                    "PHP 업데이트",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            StatusText.Text = $"PHP 업데이트 완료: {current} → {target.Version}";
            MessageBox.Show(this,
                $"PHP 업데이트가 완료되었습니다.\n\n{current} → {target.Version}",
                "PHP 업데이트",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await InspectAsync(_lastInstallation.RootPath, "PostUpdate");
        }
        catch (Exception ex)
        {
            StatusText.Text = "PHP 업데이트 실행 실패: " + ex.Message;
            MessageBox.Show(this, ex.Message, "PHP 업데이트", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _phpUpdateRunning = false;
            SetBusy(false);
            RefreshPhpExecuteEnabled();
        }
    }
}
