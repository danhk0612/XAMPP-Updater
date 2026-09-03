using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using XamppUpdater.Core.Models;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private bool _releaseLifecycleInitialized;
    private DispatcherTimer? _pathChangeTimer;
    private string? _lastAutoInspectedPath;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_releaseLifecycleInitialized) return;
        _releaseLifecycleInitialized = true;

        InitializeSimplifiedUi();
        ApacheTargetComboBox.SelectionChanged += (_, _) => RefreshPrimaryUpdateButtons();
        PhpTargetComboBox.SelectionChanged += (_, _) => RefreshPrimaryUpdateButtons();
        MariaDbTargetComboBox.SelectionChanged += (_, _) => RefreshPrimaryUpdateButtons();

        var descriptor = DependencyPropertyDescriptor.FromProperty(ComboBox.TextProperty, typeof(ComboBox));
        descriptor?.AddValueChanged(InstallPathComboBox, (_, _) => SchedulePathInspection());

        _ = CompleteInitialDetectionAsync();
    }

    private void SchedulePathInspection()
    {
        if (_pathChangeTimer is null)
        {
            _pathChangeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            _pathChangeTimer.Tick += async (_, _) =>
            {
                _pathChangeTimer.Stop();
                var path = InstallPathComboBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(path) ||
                    string.Equals(path, _lastAutoInspectedPath, StringComparison.OrdinalIgnoreCase) ||
                    !Directory.Exists(path)) return;

                _lastAutoInspectedPath = path;
                await InspectPathAndOnlineAsync(path, "PathChanged");
                UpdateCurrentVersionLabels();
            };
        }

        _pathChangeTimer.Stop();
        _pathChangeTimer.Start();
    }

    private async Task CompleteInitialDetectionAsync()
    {
        for (var attempt = 0; attempt < 50 && _lastInstallation is null; attempt++)
            await Task.Delay(100);

        if (_lastInstallation is null) return;
        _lastAutoInspectedPath = _lastInstallation.RootPath;
        await CheckOnlineVersionsAsync();
        UpdateCurrentVersionLabels();
        RefreshPrimaryUpdateButtons();
    }

    private void UpdateCurrentVersionLabels()
    {
        if (_lastInstallation is null) return;
        ApacheVersionText.Text = GetCurrentVersionText(XamppComponentType.Apache);
        PhpVersionText.Text = GetCurrentVersionText(XamppComponentType.Php);
        MariaDbVersionText.Text = GetCurrentVersionText(XamppComponentType.MariaDb);
    }

    private string GetCurrentVersionText(XamppComponentType type)
    {
        var component = _lastInstallation!.Components.FirstOrDefault(item => item.Type == type);
        if (component is null || !component.IsInstalled) return "감지되지 않음";
        return component.Version ?? "버전 확인 실패";
    }
}
