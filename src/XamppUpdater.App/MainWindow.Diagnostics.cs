using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly DiagnosticExportService _diagnosticExportService = new();
    private bool _diagnosticsUiInitialized;

    private void InitializeDiagnosticsUi()
    {
        if (_diagnosticsUiInitialized) return;
        _diagnosticsUiInitialized = true;
        if (ApacheNavButton.Parent is not StackPanel panel) return;

        var appUpdateButton = panel.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "AppUpdate", StringComparison.Ordinal));
        if (appUpdateButton is null) return;

        var button = new Button
        {
            Content = LocalizationCatalog.Text("진단 정보 내보내기", "Export diagnostics"),
            Tag = "Diagnostics",
            Margin = new Thickness(0, 0, 0, 5),
            Padding = new Thickness(8, 6, 8, 6)
        };
        button.Click += ExportDiagnostics_Click;
        panel.Children.Insert(panel.Children.IndexOf(appUpdateButton) + 1, button);
    }

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "XAMPP Updater 진단 정보 저장",
            Filter = "ZIP 파일 (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = $"XAMPP-Updater-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
        };
        if (dialog.ShowDialog(this) != true) return;

        var diagnosticsText = BuildDiagnosticsText();
        var activityLogs = _activityLogs.ToDictionary(
            pair => pair.Key.ToString(),
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);

        SetBusy(true, "진단 정보와 작업 로그를 내보내는 중...");
        try
        {
            var result = await Task.Run(() => _diagnosticExportService.Export(dialog.FileName, diagnosticsText, activityLogs));
            StatusText.Text = $"진단 정보 내보내기 완료: {result.IncludedFiles:N0}개 파일";
            MessageBox.Show(
                this,
                $"진단 정보 내보내기를 완료했습니다.\n\n{result.FilePath}\n\n설정 파일 원문, 데이터베이스, 롤백 백업은 포함하지 않았습니다.",
                "진단 정보 내보내기",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"진단 정보 내보내기 실패: {ex.Message}";
            MessageBox.Show(this, "진단 정보 내보내기에 실패했습니다.\n\n" + ex.Message, "진단 정보 내보내기", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private string BuildDiagnosticsText()
    {
        var builder = new StringBuilder();
        var version = Assembly.GetEntryAssembly()?.GetName().Version;

        builder.AppendLine("XAMPP Updater Diagnostics");
        builder.AppendLine($"ExportedAt: {DateTimeOffset.Now:O}");
        builder.AppendLine($"AppVersion: {(version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}")}");
        builder.AppendLine($"Executable: {Environment.ProcessPath ?? "unknown"}");
        builder.AppendLine($"Privilege: {(AdministratorPrivilege.IsElevated ? "Administrator" : "Standard")}");
        builder.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"ProcessArchitecture: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"OSArchitecture: {RuntimeInformation.OSArchitecture}");
        builder.AppendLine();

        if (_lastInstallation is null)
        {
            builder.AppendLine("XAMPP: not detected");
        }
        else
        {
            builder.AppendLine($"XamppRoot: {_lastInstallation.RootPath}");
            builder.AppendLine($"DiscoverySource: {_lastInstallation.DiscoverySource}");
            foreach (var component in _lastInstallation.Components)
            {
                builder.AppendLine();
                builder.AppendLine($"[{component.Type}]");
                builder.AppendLine($"Installed: {component.IsInstalled}");
                builder.AppendLine($"Version: {component.Version ?? "unknown"}");
                builder.AppendLine($"ExecutablePath: {component.ExecutablePath}");
                builder.AppendLine($"ServiceName: {component.ServiceName ?? "none"}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Included: diagnostics summary, current in-memory activity logs, persistent execution logs, self-update log when present.");
        builder.AppendLine("Excluded: configuration file contents, database contents, rollback backups, downloaded component packages, credentials.");
        return builder.ToString();
    }
}
