using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private bool _updateProgressSubscribed;

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
            var percent = progress.Percent is null ? string.Empty : $" ({progress.Percent}%)";
            var rollback = progress.IsRollback ? " [롤백]" : string.Empty;
            StatusText.Text = $"{progress.Type}{rollback} - {progress.Message}{percent}";
            AppendDetail(progress.Type, $"진행 [{progress.Stage}]{percent}: {progress.Message}");
        });
    }
}
