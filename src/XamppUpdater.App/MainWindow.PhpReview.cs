using System.IO;
using System.Windows;
using System.Windows.Controls;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly IPhpMigrationReviewService _phpMigrationReviewService = new PhpMigrationReviewService();
    private readonly IPhpMigrationOverrideStore _phpMigrationOverrideStore = new PhpMigrationOverrideStore();
    private Button? _phpMigrationReviewButton;
    private bool _phpMigrationReviewRunning;

    private void InitializePhpMigrationReviewUi()
    {
        if (_phpMigrationReviewButton is not null) return;
        if (PhpDiffButton.Parent is not StackPanel actionPanel) return;

        _phpMigrationReviewButton = new Button
        {
            Content = "마이그레이션 검토",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 5, 12, 5),
            IsEnabled = false,
            ToolTip = "자동 변환/대체 결과를 확인하고 최종 php.ini를 직접 편집·확정합니다."
        };
        _phpMigrationReviewButton.Click += PhpMigrationReviewButton_Click;
        actionPanel.Children.Add(_phpMigrationReviewButton);

        PhpDiffButton.IsEnabledChanged += (_, _) => RefreshPhpMigrationReviewEnabled();
        PhpTargetComboBox.SelectionChanged += (_, _) => RefreshPhpMigrationReviewEnabled();
    }

    private void RefreshPhpMigrationReviewEnabled()
    {
        if (_phpMigrationReviewButton is null) return;
        if (_phpMigrationReviewRunning || _lastInstallation is null ||
            PhpTargetComboBox.SelectedItem is not UpdateTargetOption target ||
            !_packageResults.TryGetValue(XamppComponentType.Php, out var package) ||
            !string.Equals(package.Version, target.Version, StringComparison.OrdinalIgnoreCase))
        {
            _phpMigrationReviewButton.IsEnabled = false;
            _phpMigrationReviewButton.Content = "마이그레이션 검토";
            return;
        }

        _phpMigrationReviewButton.IsEnabled = true;
        var currentIni = Path.Combine(_lastInstallation.RootPath, "php", "php.ini");
        var reviewed = _phpMigrationOverrideStore.TryLoad(_lastInstallation.RootPath, target.Version, currentIni);
        _phpMigrationReviewButton.Content = reviewed is null ? "마이그레이션 검토" : "마이그레이션 검토 ✓";
    }

    private async void PhpMigrationReviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_phpMigrationReviewRunning || _lastInstallation is null ||
            PhpTargetComboBox.SelectedItem is not UpdateTargetOption target ||
            !_packageResults.TryGetValue(XamppComponentType.Php, out var package))
        {
            return;
        }

        _phpMigrationReviewRunning = true;
        RefreshPhpMigrationReviewEnabled();
        SetBusy(true, $"PHP {target.Version} 설정 마이그레이션안을 생성하는 중...");

        try
        {
            var installation = _lastInstallation;
            var review = await _phpMigrationReviewService.BuildAsync(installation, target, package);
            var dialog = new PhpMigrationReviewWindow(review) { Owner = this };
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FinalIniText))
            {
                StatusText.Text = "PHP 설정 마이그레이션 검토를 취소했습니다.";
                return;
            }

            var currentIni = Path.Combine(installation.RootPath, "php", "php.ini");
            var saved = _phpMigrationOverrideStore.Save(
                installation.RootPath,
                target.Version,
                currentIni,
                dialog.FinalIniText);

            AppendDetail(
                XamppComponentType.Php,
                $"마이그레이션 적용안 확정: 자동 변경 {review.AutomaticChangeCount} / 사용자 확인 {review.NeedsReviewCount}");
            AppendDetail(XamppComponentType.Php, "확정본: " + saved);
            StatusText.Text = $"PHP {target.Version} 설정 마이그레이션 적용안을 확정했습니다.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "PHP 설정 마이그레이션 검토 실패: " + ex.Message;
            MessageBox.Show(this, ex.Message, "PHP 설정 마이그레이션", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _phpMigrationReviewRunning = false;
            SetBusy(false);
            RefreshPhpMigrationReviewEnabled();
        }
    }
}
