using System.Windows;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private bool _updateProgressSubscribed;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        InitializeUpdateProgressUi();
    }

    private void InitializeUpdateProgressUi()
    {
        if (_updateProgressSubscribed) return;
        _updateProgressSubscribed = true;
        UpdateProgressReporter.ProgressReported += OnUpdateProgressReported;
        Closed += (_, _) => UpdateProgressReporter.ProgressReported -= OnUpdateProgressReported;
    }

    private void OnUpdateProgressReported(UpdateProgress progress)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var percentText = progress.Percent is null ? string.Empty : $" ({progress.Percent}%)";
            var rollback = progress.IsRollback ? " [롤백]" : string.Empty;
            StatusText.Text = $"{progress.Type}{rollback} - {progress.Message}{percentText}";
            if (progress.Percent is int percent)
            {
                UpdateProgressBar.Value = Math.Clamp(percent, 0, 100);
                ProgressPercentText.Text = $"{Math.Clamp(percent, 0, 100)}%";
            }

            var line = $"[{DateTime.Now:HH:mm:ss}] {(progress.IsRollback ? "↶" : "•")} {progress.Message}{percentText}";
            AppendVisibleLog(progress.Type, line);
            AppendDetail(progress.Type, $"진행 [{progress.Stage}]{percentText}: {progress.Message}");
        });
    }
}
