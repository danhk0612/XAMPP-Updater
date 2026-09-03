using System.Windows;
using System.Windows.Controls;
using XamppUpdater.Core.Models;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private bool _configHistoryUiInitialized;

    internal void InitializeConfigHistoryUi()
    {
        if (_configHistoryUiInitialized) return;
        _configHistoryUiInitialized = true;
        AddHistoryButton(ApacheDiffButton, XamppComponentType.Apache);
        AddHistoryButton(PhpDiffButton, XamppComponentType.Php);
        AddHistoryButton(MariaDbDiffButton, XamppComponentType.MariaDb);
    }

    private void AddHistoryButton(Button anchor, XamppComponentType type)
    {
        if (anchor.Parent is not Panel panel) return;
        var button = new Button
        {
            Content = "설정 이력",
            Margin = new Thickness(0, 0, 8, 6),
            Padding = new Thickness(12, 5, 12, 5),
            ToolTip = "업데이트 전/후에 자동 저장된 설정 snapshot을 보고 두 시점을 비교합니다.",
            Tag = type
        };
        button.Click += ConfigHistoryButton_Click;
        panel.Children.Add(button);
    }

    private void ConfigHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: XamppComponentType type }) return;
        if (_lastInstallation is null)
        {
            MessageBox.Show(this, "먼저 XAMPP 설치를 감지하거나 검사하세요.", "설정 이력", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new ConfigHistoryWindow(_lastInstallation.RootPath, type) { Owner = this }.ShowDialog();
    }
}
