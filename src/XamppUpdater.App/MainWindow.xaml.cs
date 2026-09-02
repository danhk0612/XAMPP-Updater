using System.Windows;
using Microsoft.Win32;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public partial class MainWindow : Window
{
    private readonly IXamppInstallationDetector _detector = new XamppInstallationDetector();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await AutoDetectAsync();
    }

    private async void AutoDetectButton_Click(object sender, RoutedEventArgs e)
    {
        await AutoDetectAsync();
    }

    private async void InspectButton_Click(object sender, RoutedEventArgs e)
    {
        var path = InstallPathComboBox.Text;
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText.Text = "XAMPP 설치 경로를 입력하거나 선택하세요.";
            return;
        }

        await InspectAsync(path, "Manual");
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "XAMPP 설치 폴더 선택",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        InstallPathComboBox.Text = dialog.FolderName;
        await InspectAsync(dialog.FolderName, "Manual");
    }

    private async Task AutoDetectAsync()
    {
        SetBusy(true, "XAMPP 설치 경로 자동 감지 중...");

        try
        {
            var candidates = await Task.Run(_detector.FindCandidates);
            InstallPathComboBox.ItemsSource = candidates;

            if (candidates.Count == 0)
            {
                StatusText.Text = "자동 감지된 XAMPP 설치가 없습니다. 설치 폴더를 직접 선택하세요.";
                return;
            }

            InstallPathComboBox.SelectedIndex = 0;
            await InspectAsync(candidates[0], "Auto");

            if (candidates.Count > 1)
            {
                StatusText.Text += $"  감지된 설치: {candidates.Count}개";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"자동 감지 실패: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task InspectAsync(string path, string source)
    {
        SetBusy(true, "구성요소 버전 확인 중...");

        try
        {
            var installation = await Task.Run(() => _detector.Inspect(path, source));
            InstallPathComboBox.Text = installation.RootPath;
            Render(installation);
            StatusText.Text = $"확인 완료: {installation.RootPath} ({installation.DiscoverySource})";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"확인 실패: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Render(XamppInstallation installation)
    {
        foreach (var component in installation.Components)
        {
            var versionText = component.IsInstalled
                ? component.Version is null ? "설치됨 / 버전 확인 실패" : $"버전 {component.Version}"
                : "감지되지 않음";

            switch (component.Type)
            {
                case XamppComponentType.Apache:
                    ApacheVersionText.Text = versionText;
                    ApacheServiceText.Text = $"서비스: {component.ServiceName ?? "미등록"}";
                    ApachePathText.Text = $"경로: {component.ExecutablePath}";
                    break;
                case XamppComponentType.Php:
                    PhpVersionText.Text = versionText;
                    PhpPathText.Text = $"경로: {component.ExecutablePath}";
                    break;
                case XamppComponentType.MariaDb:
                    MariaDbVersionText.Text = versionText;
                    MariaDbServiceText.Text = $"서비스: {component.ServiceName ?? "미등록"}";
                    MariaDbPathText.Text = $"경로: {component.ExecutablePath}";
                    MariaDbDetailText.Text = component.Detail ?? string.Empty;
                    break;
            }
        }
    }

    private void SetBusy(bool isBusy, string? message = null)
    {
        InstallPathComboBox.IsEnabled = !isBusy;
        if (message is not null)
        {
            StatusText.Text = message;
        }
    }
}
