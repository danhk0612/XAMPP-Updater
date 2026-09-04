using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XamppUpdater.Core.Models;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly PhpMyAdminUpdateService _phpMyAdminUpdateService = new();
    private readonly List<string> _phpMyAdminActivityLog = new();
    private Button? _phpMyAdminNavButton;
    private StackPanel? _phpMyAdminPanel;
    private TextBlock? _phpMyAdminVersionText;
    private TextBlock? _phpMyAdminTargetText;
    private TextBlock? _phpMyAdminPlanText;
    private TextBlock? _phpMyAdminPathText;
    private TextBlock? _phpMyAdminCompatibilityText;
    private Button? _phpMyAdminUpdateButton;
    private PhpMyAdminInstallationState? _phpMyAdminState;
    private PhpMyAdminReleaseInfo? _phpMyAdminRelease;
    private PhpMyAdminCompatibility? _phpMyAdminCompatibility;
    private bool _phpMyAdminActive;
    private bool _phpMyAdminUpdateRunning;
    private string? _phpMyAdminObservedPath;

    internal void InitializePhpMyAdminUi()
    {
        if (_phpMyAdminNavButton is not null) return;

        MariaDbNavButton.Margin = new Thickness(0, 0, 0, 5);
        _phpMyAdminNavButton = new Button
        {
            Content = "phpMyAdmin",
            Height = 38,
            Visibility = Visibility.Collapsed
        };
        _phpMyAdminNavButton.Click += PhpMyAdminNavButton_Click;

        if (MariaDbNavButton.Parent is StackPanel navigation)
        {
            var index = navigation.Children.IndexOf(MariaDbNavButton);
            navigation.Children.Insert(index + 1, _phpMyAdminNavButton);
        }

        _phpMyAdminPanel = BuildPhpMyAdminPanel();
        if (ApachePanel.Parent is Grid componentGrid)
        {
            componentGrid.Children.Add(_phpMyAdminPanel);
        }

        ApacheNavButton.Click += ExistingComponentNav_Click;
        PhpNavButton.Click += ExistingComponentNav_Click;
        MariaDbNavButton.Click += ExistingComponentNav_Click;
        InstallPathComboBox.SelectionChanged += (_, _) => ObservePhpMyAdminPath(InstallPathComboBox.SelectedItem as string ?? InstallPathComboBox.Text);
        InstallPathComboBox.LostKeyboardFocus += (_, _) => ObservePhpMyAdminPath(InstallPathComboBox.Text);
        InstallPathComboBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => ObservePhpMyAdminPath(InstallPathComboBox.Text)));

        ObservePhpMyAdminPath(InstallPathComboBox.Text);
    }

    private StackPanel BuildPhpMyAdminPanel()
    {
        _phpMyAdminVersionText = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Text = "-" };
        _phpMyAdminTargetText = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Text = "온라인 확인 필요" };
        _phpMyAdminPlanText = new TextBlock
        {
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = Brushes.DarkSlateGray,
            TextWrapping = TextWrapping.Wrap,
            Text = "XAMPP에 포함된 phpMyAdmin을 확인하는 중입니다."
        };
        _phpMyAdminPathText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray };
        _phpMyAdminCompatibilityText = new TextBlock
        {
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DarkGoldenrod
        };
        _phpMyAdminUpdateButton = new Button
        {
            Margin = new Thickness(0, 20, 0, 0),
            Height = 42,
            Content = "phpMyAdmin 업데이트",
            IsEnabled = false
        };
        _phpMyAdminUpdateButton.Click += PhpMyAdminUpdateButton_Click;

        var advancedContent = new StackPanel { Margin = new Thickness(8) };
        advancedContent.Children.Add(_phpMyAdminPathText);
        advancedContent.Children.Add(_phpMyAdminCompatibilityText);

        var panel = new StackPanel { Visibility = Visibility.Collapsed };
        panel.Children.Add(new TextBlock { Text = "phpMyAdmin", FontSize = 21, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Margin = new Thickness(0, 18, 0, 3), Foreground = Brushes.DimGray, Text = "현재 버전" });
        panel.Children.Add(_phpMyAdminVersionText);
        panel.Children.Add(new TextBlock { Margin = new Thickness(0, 18, 0, 5), Foreground = Brushes.DimGray, Text = "업데이트 버전" });
        panel.Children.Add(_phpMyAdminTargetText);
        panel.Children.Add(_phpMyAdminPlanText);
        panel.Children.Add(_phpMyAdminUpdateButton);
        panel.Children.Add(new Expander
        {
            Header = "고급 정보",
            Margin = new Thickness(0, 14, 0, 0),
            Content = advancedContent
        });
        return panel;
    }

    private void ExistingComponentNav_Click(object sender, RoutedEventArgs e)
    {
        _phpMyAdminActive = false;
        if (_phpMyAdminPanel is not null) _phpMyAdminPanel.Visibility = Visibility.Collapsed;
    }

    private async void PhpMyAdminNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (_phpMyAdminPanel is null) return;
        ObservePhpMyAdminPath(InstallPathComboBox.Text);
        if (_phpMyAdminState?.IsInstalled != true) return;

        _phpMyAdminActive = true;
        ApachePanel.Visibility = Visibility.Collapsed;
        PhpPanel.Visibility = Visibility.Collapsed;
        MariaDbPanel.Visibility = Visibility.Collapsed;
        _phpMyAdminPanel.Visibility = Visibility.Visible;
        RefreshPhpMyAdminActivityLog();
        await RefreshPhpMyAdminReleaseAsync();
    }

    private void ObservePhpMyAdminPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            SetPhpMyAdminUnavailable();
            return;
        }

        string normalized;
        try { normalized = Path.GetFullPath(path.Trim().Trim('"')); }
        catch { return; }

        if (string.Equals(_phpMyAdminObservedPath, normalized, StringComparison.OrdinalIgnoreCase) && _phpMyAdminState is not null)
        {
            return;
        }

        _phpMyAdminObservedPath = normalized;
        try
        {
            _phpMyAdminState = _phpMyAdminUpdateService.Inspect(normalized);
        }
        catch
        {
            SetPhpMyAdminUnavailable();
            return;
        }

        if (_phpMyAdminState.IsInstalled)
        {
            if (_phpMyAdminNavButton is not null) _phpMyAdminNavButton.Visibility = Visibility.Visible;
            if (_phpMyAdminVersionText is not null)
                _phpMyAdminVersionText.Text = _phpMyAdminState.Version is null ? "설치됨 / 버전 확인 실패" : $"설치 버전 {_phpMyAdminState.Version}";
            if (_phpMyAdminPathText is not null) _phpMyAdminPathText.Text = $"경로: {_phpMyAdminState.DirectoryPath}";
            if (_phpMyAdminTargetText is not null) _phpMyAdminTargetText.Text = "온라인 확인 중...";
            if (_phpMyAdminPlanText is not null) _phpMyAdminPlanText.Text = "최신 phpMyAdmin 안정판을 확인하는 중입니다.";
            if (_phpMyAdminUpdateButton is not null) _phpMyAdminUpdateButton.IsEnabled = false;
            _phpMyAdminRelease = null;
            _phpMyAdminCompatibility = null;
            _ = RefreshPhpMyAdminReleaseAsync();
        }
        else
        {
            SetPhpMyAdminUnavailable();
        }
    }

    private void SetPhpMyAdminUnavailable()
    {
        _phpMyAdminState = null;
        _phpMyAdminRelease = null;
        _phpMyAdminCompatibility = null;
        _phpMyAdminObservedPath = null;
        if (_phpMyAdminNavButton is not null) _phpMyAdminNavButton.Visibility = Visibility.Collapsed;
        if (_phpMyAdminPanel is not null) _phpMyAdminPanel.Visibility = Visibility.Collapsed;
        _phpMyAdminActive = false;
    }

    private async Task RefreshPhpMyAdminReleaseAsync()
    {
        if (_phpMyAdminState?.IsInstalled != true || _phpMyAdminUpdateRunning) return;

        try
        {
            var release = await _phpMyAdminUpdateService.GetLatestAsync();
            if (_phpMyAdminState?.IsInstalled != true) return;
            _phpMyAdminRelease = release;

            var phpVersion = GetInstalledVersion(XamppComponentType.Php);
            var databaseVersion = GetInstalledVersion(XamppComponentType.MariaDb);
            _phpMyAdminCompatibility = _phpMyAdminUpdateService.EvaluateCompatibility(release, phpVersion, databaseVersion);

            if (_phpMyAdminTargetText is not null)
            {
                _phpMyAdminTargetText.Text = release.ReleaseDate is null
                    ? release.Version
                    : $"{release.Version}  ({release.ReleaseDate:yyyy-MM-dd})";
            }

            var current = _phpMyAdminState.Version;
            var upToDate = !string.IsNullOrWhiteSpace(current) && PhpMyAdminUpdateService.CompareVersions(current, release.Version) >= 0;
            var warningText = _phpMyAdminCompatibility.Warnings.Count == 0
                ? string.Empty
                : Environment.NewLine + string.Join(Environment.NewLine, _phpMyAdminCompatibility.Warnings.Select(value => "• " + value));

            if (_phpMyAdminPlanText is not null)
            {
                _phpMyAdminPlanText.Text = upToDate
                    ? $"현재 phpMyAdmin {current}은 최신 안정판입니다."
                    : $"phpMyAdmin {current ?? "버전 미상"} → {release.Version}. 기존 config.inc.php와 .htaccess, upload/save 폴더를 보존하고 전체 롤백 백업 후 폴더를 교체합니다.";
            }
            if (_phpMyAdminCompatibilityText is not null)
            {
                _phpMyAdminCompatibilityText.Text =
                    $"PHP: {phpVersion ?? "미상"} / MariaDB: {databaseVersion ?? "미상"}\n" +
                    $"공식 메타데이터: PHP {release.PhpVersionRange ?? "미상"}, DB {release.DatabaseVersionRange ?? "미상"}\n" +
                    _phpMyAdminCompatibility.Summary + warningText;
            }
            if (_phpMyAdminUpdateButton is not null)
            {
                _phpMyAdminUpdateButton.IsEnabled = !_phpMyAdminUpdateRunning && !upToDate &&
                    !string.IsNullOrWhiteSpace(current) && _phpMyAdminCompatibility.CanUpdate;
            }
        }
        catch (Exception ex)
        {
            if (_phpMyAdminTargetText is not null) _phpMyAdminTargetText.Text = "온라인 확인 실패";
            if (_phpMyAdminPlanText is not null) _phpMyAdminPlanText.Text = "phpMyAdmin 최신 버전 확인 실패: " + ex.Message;
            if (_phpMyAdminUpdateButton is not null) _phpMyAdminUpdateButton.IsEnabled = false;
            AppendPhpMyAdminLog($"[{DateTime.Now:HH:mm:ss}] ! 최신 버전 확인 실패: {ex.Message}");
        }
    }

    private async void PhpMyAdminUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_phpMyAdminRelease is null) await RefreshPhpMyAdminReleaseAsync();
        if (_phpMyAdminRelease is null || _phpMyAdminState?.IsInstalled != true) return;

        if (!AdministratorPrivilege.IsElevated)
        {
            AdministratorPrivilege.EnsureElevated(
                this,
                InstallPathComboBox.Text,
                "phpMyAdmin 업데이트",
                "PhpMyAdmin",
                _phpMyAdminRelease.Version);
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"phpMyAdmin {_phpMyAdminState.Version} → {_phpMyAdminRelease.Version} 업데이트를 진행합니다.\n\n" +
            "기존 phpMyAdmin 전체 폴더를 먼저 별도 백업하고 config.inc.php/.htaccess 및 upload/save 폴더를 새 설치에 이관합니다.\n" +
            "업데이트 중 phpMyAdmin 페이지 요청은 잠시 실패할 수 있습니다.\n\n계속하시겠습니까?",
            "phpMyAdmin 업데이트",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes) return;

        await StartPhpMyAdminUpdateAsync(_phpMyAdminRelease, showCompletionDialog: true);
    }

    internal async Task ResumeStartupPhpMyAdminUpdateAsync(string version)
    {
        InitializePhpMyAdminUi();
        ObservePhpMyAdminPath(InstallPathComboBox.Text);
        if (_phpMyAdminState?.IsInstalled != true) return;

        var release = await _phpMyAdminUpdateService.GetLatestAsync();
        if (!string.Equals(release.Version, version, StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = $"관리자 권한 재실행 후 phpMyAdmin {version} 대상 버전이 더 이상 최신 안정판이 아닙니다. 다시 업데이트를 실행하세요.";
            return;
        }

        _phpMyAdminRelease = release;
        _phpMyAdminCompatibility = _phpMyAdminUpdateService.EvaluateCompatibility(
            release,
            GetInstalledVersion(XamppComponentType.Php),
            GetInstalledVersion(XamppComponentType.MariaDb));
        ShowPhpMyAdminPanel();
        AppendPhpMyAdminLog($"[{DateTime.Now:HH:mm:ss}] • 관리자 권한으로 phpMyAdmin 업데이트 자동 재개: {version}");
        await StartPhpMyAdminUpdateAsync(release, showCompletionDialog: true);
    }

    private async Task StartPhpMyAdminUpdateAsync(PhpMyAdminReleaseInfo release, bool showCompletionDialog)
    {
        if (_phpMyAdminUpdateRunning || _phpMyAdminState?.IsInstalled != true) return;
        var xamppRoot = InstallPathComboBox.Text;
        if (string.IsNullOrWhiteSpace(xamppRoot)) return;

        _phpMyAdminUpdateRunning = true;
        if (_phpMyAdminUpdateButton is not null) _phpMyAdminUpdateButton.IsEnabled = false;
        ShowPhpMyAdminPanel();
        UpdateProgressBar.Value = 0;
        ProgressPercentText.Text = "0%";
        AppendPhpMyAdminLog($"[{DateTime.Now:HH:mm:ss}] • 업데이트 시작: {_phpMyAdminState.Version} → {release.Version}");

        try
        {
            var phpVersion = GetInstalledVersion(XamppComponentType.Php);
            var databaseVersion = GetInstalledVersion(XamppComponentType.MariaDb);
            var progress = new Progress<PhpMyAdminUpdateProgress>(value =>
            {
                UpdateProgressBar.Value = Math.Clamp(value.Percent, 0, 100);
                ProgressPercentText.Text = $"{Math.Clamp(value.Percent, 0, 100)}%";
                StatusText.Text = value.Message;
                var bytes = value.BytesReceived is null
                    ? string.Empty
                    : value.TotalBytes is > 0
                        ? $" ({FormatByteCount(value.BytesReceived.Value)} / {FormatByteCount(value.TotalBytes.Value)})"
                        : $" ({FormatByteCount(value.BytesReceived.Value)})";
                AppendPhpMyAdminLog($"[{DateTime.Now:HH:mm:ss}] [{value.Stage}] {value.Message}{bytes}");
            });

            var result = await _phpMyAdminUpdateService.UpdateAsync(
                xamppRoot,
                release,
                phpVersion,
                databaseVersion,
                progress);

            _phpMyAdminObservedPath = null;
            ObservePhpMyAdminPath(xamppRoot);
            StatusText.Text = $"phpMyAdmin {result.PreviousVersion} → {result.NewVersion} 업데이트 완료";
            AppendPhpMyAdminLog($"[{DateTime.Now:HH:mm:ss}] ✓ 롤백 백업: {result.BackupPath}");
            AppendPhpMyAdminLog($"[{DateTime.Now:HH:mm:ss}] ✓ SHA256: {result.Sha256}");
            if (showCompletionDialog)
            {
                MessageBox.Show(
                    this,
                    $"phpMyAdmin 업데이트가 완료되었습니다.\n\n{result.PreviousVersion} → {result.NewVersion}\n\n롤백 백업:\n{result.BackupPath}",
                    "phpMyAdmin 업데이트 완료",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "phpMyAdmin 업데이트 실패: " + ex.Message;
            AppendPhpMyAdminLog($"[{DateTime.Now:HH:mm:ss}] ✗ 업데이트 실패: {ex.Message}");
            MessageBox.Show(this, ex.Message, "phpMyAdmin 업데이트 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _phpMyAdminUpdateRunning = false;
            await RefreshPhpMyAdminReleaseAsync();
        }
    }

    private void ShowPhpMyAdminPanel()
    {
        if (_phpMyAdminPanel is null) return;
        _phpMyAdminActive = true;
        ApachePanel.Visibility = Visibility.Collapsed;
        PhpPanel.Visibility = Visibility.Collapsed;
        MariaDbPanel.Visibility = Visibility.Collapsed;
        _phpMyAdminPanel.Visibility = Visibility.Visible;
        RefreshPhpMyAdminActivityLog();
    }

    private string? GetInstalledVersion(XamppComponentType type) =>
        _lastInstallation?.Components.FirstOrDefault(component => component.Type == type)?.Version;

    private void AppendPhpMyAdminLog(string line)
    {
        _phpMyAdminActivityLog.Add(line);
        if (_phpMyAdminActivityLog.Count > 500) _phpMyAdminActivityLog.RemoveRange(0, _phpMyAdminActivityLog.Count - 500);
        if (_phpMyAdminActive) RefreshPhpMyAdminActivityLog();

        try
        {
            var logRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XAMPP-Updater",
                "Logs");
            Directory.CreateDirectory(logRoot);
            File.AppendAllText(Path.Combine(logRoot, "phpmyadmin-update.log"), line + Environment.NewLine);
        }
        catch
        {
            // 진단 로그 저장 실패가 업데이트를 중단하면 안 된다.
        }
    }

    private void RefreshPhpMyAdminActivityLog()
    {
        ActivityLogTextBox.Text = string.Join(Environment.NewLine, _phpMyAdminActivityLog);
        ActivityLogTextBox.ScrollToEnd();
    }

    private static string FormatByteCount(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024d * 1024d * 1024d):0.00} GB";
        if (bytes >= 1024L * 1024L) return $"{bytes / (1024d * 1024d):0.00} MB";
        if (bytes >= 1024L) return $"{bytes / 1024d:0.00} KB";
        return $"{bytes:N0} B";
    }
}
