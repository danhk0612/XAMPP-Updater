using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace XamppUpdater.App;

internal sealed class AppUpdateProgressWindow : Window
{
    private readonly TextBlock _messageText;
    private readonly TextBlock _detailText;
    private readonly ProgressBar _progressBar;
    private readonly Button _cancelButton;
    private readonly Button _restartNowButton;
    private bool _allowClose;
    private bool _cancelSignaled;

    public bool RestartNowRequested { get; private set; }
    public event EventHandler? CancelRequested;

    private static string L(string korean, string english) => LocalizationCatalog.Text(korean, english);

    public AppUpdateProgressWindow(Window owner, Version currentVersion, Version targetVersion)
    {
        Owner = owner;
        Title = L("XAMPP Updater 앱 업데이트", "XAMPP Updater - App update");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var current = currentVersion.ToString(3);
        var target = targetVersion.ToString(3);

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _messageText = new TextBlock
        {
            Text = L(
                $"XAMPP Updater {target} 다운로드 중",
                $"Downloading XAMPP Updater {target}"),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_messageText);

        _detailText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = System.Windows.Media.Brushes.DimGray,
            Text = L(
                $"현재 {current} → 새 버전 {target}",
                $"Current {current} → New version {target}"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(_detailText, 1);
        root.Children.Add(_detailText);

        _progressBar = new ProgressBar
        {
            Margin = new Thickness(0, 16, 0, 0),
            Height = 18,
            Minimum = 0,
            Maximum = 100,
            IsIndeterminate = true
        };
        Grid.SetRow(_progressBar, 2);
        root.Children.Add(_progressBar);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        _cancelButton = new Button
        {
            Content = L("취소", "Cancel"),
            MinWidth = 90,
            Padding = new Thickness(12, 6, 12, 6)
        };
        _cancelButton.Click += (_, _) => RequestCancel();
        buttons.Children.Add(_cancelButton);

        _restartNowButton = new Button
        {
            Content = L("지금 재시작", "Restart now"),
            MinWidth = 110,
            Padding = new Thickness(12, 6, 12, 6),
            Visibility = Visibility.Collapsed
        };
        _restartNowButton.Click += (_, _) =>
        {
            RestartNowRequested = true;
            _restartNowButton.IsEnabled = false;
            _messageText.Text = L(
                "앱을 재시작하여 업데이트를 적용합니다.",
                "Restarting the app to apply the update.");
        };
        buttons.Children.Add(_restartNowButton);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
        Closing += OnClosing;
    }

    public void ReportDownload(AppUpdateDownloadProgress progress)
    {
        if (progress.TotalBytes is > 0)
        {
            _progressBar.IsIndeterminate = false;
            _progressBar.Value = Math.Min(100, progress.BytesReceived * 100d / progress.TotalBytes.Value);
            _detailText.Text = $"{FormatBytes(progress.BytesReceived)} / {FormatBytes(progress.TotalBytes.Value)} ({_progressBar.Value:0}%)";
        }
        else
        {
            _progressBar.IsIndeterminate = true;
            var downloaded = FormatBytes(progress.BytesReceived);
            _detailText.Text = L($"{downloaded} 다운로드됨", $"{downloaded} downloaded");
        }
    }

    public void BeginVerification()
    {
        _cancelButton.IsEnabled = false;
        _messageText.Text = L(
            "다운로드 완료. SHA256을 검증하는 중입니다.",
            "Download complete. Verifying SHA256.");
        _detailText.Text = L(
            "이 단계부터는 업데이트를 취소할 수 없습니다.",
            "The update can no longer be canceled after this stage.");
        _progressBar.IsIndeterminate = true;
    }

    public void SetRestartCountdown(int seconds)
    {
        _cancelButton.Visibility = Visibility.Collapsed;
        _restartNowButton.Visibility = Visibility.Visible;
        _progressBar.IsIndeterminate = false;
        _progressBar.Value = 100;
        _messageText.Text = L(
            "SHA256 검증 완료. 업데이트를 적용할 준비가 되었습니다.",
            "SHA256 verification complete. Ready to apply the update.");
        _detailText.Text = L(
            $"{seconds}초 후 자동으로 재시작합니다.",
            seconds == 1
                ? "Automatically restarting in 1 second."
                : $"Automatically restarting in {seconds} seconds.");
    }

    public void AllowShutdown()
    {
        _allowClose = true;
    }

    public void CloseAfterCancellation()
    {
        _allowClose = true;
        Close();
    }

    private void RequestCancel()
    {
        if (_cancelSignaled || !_cancelButton.IsEnabled) return;
        _cancelSignaled = true;
        _cancelButton.IsEnabled = false;
        _messageText.Text = L(
            "다운로드를 취소하는 중입니다.",
            "Canceling the download.");
        _detailText.Text = L(
            "현재 실행 파일은 변경되지 않습니다.",
            "The current executable will not be changed.");
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        RequestCancel();
    }

    private static string FormatBytes(long bytes)
    {
        var value = (double)bytes;
        var units = new[] { "B", "KB", "MB", "GB" };
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes:N0} B" : $"{value:N1} {units[unit]}";
    }
}
