using System.IO;
using System.Windows;
using XamppUpdater.Core.Models;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private readonly IMariaDbLogicalBackupService _mariaDbLogicalBackupService = new MariaDbLogicalBackupService();
    private readonly IWindowsServiceController _windowsServiceController = new WindowsServiceController();

    private void InitializeMariaDbSafeBackupUi()
    {
        MariaDbBackupButton.Click -= BackupButton_Click;
        MariaDbBackupButton.Click += MariaDbSafeBackupButton_Click;
    }

    private async void MariaDbSafeBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActionTarget(sender, out var type, out var target) ||
            type != XamppComponentType.MariaDb ||
            _lastInstallation is null)
        {
            return;
        }

        SetBusy(true, "MariaDB 논리/물리 롤백 백업을 생성하는 중...");
        var serviceWasRunning = false;
        var serviceStoppedByUpdater = false;

        try
        {
            var report = await Task.Run(() => _preflightService.Inspect(_lastInstallation, type, target.Version));
            _preflightReports[type] = report;

            if (!CanRunMariaDbSafeBackup(report))
            {
                throw new InvalidOperationException(
                    "MariaDB 프로세스가 실행 중이지만 관리 가능한 Windows 서비스를 찾지 못했습니다. 안전한 물리 백업을 위해 먼저 MariaDB를 중지해야 합니다.");
            }

            LogicalBackupManifest? logicalManifest = null;
            var isRunning = report.ProcessRunning ||
                            report.ServiceState?.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) == true;

            if (isRunning)
            {
                var logical = await _mariaDbLogicalBackupService.CreateAsync(report);
                if (!logical.Success && logical.AuthenticationRequired)
                {
                    var credentials = await MariaDbCredentialsDialog.RequestAsync(this);
                    if (credentials is null)
                    {
                        StatusText.Text = "MariaDB 백업 취소: 인증정보 입력이 취소되었습니다.";
                        return;
                    }

                    logical = await _mariaDbLogicalBackupService.CreateAsync(report, credentials);
                }

                if (!logical.Success || logical.FilePath is null || logical.Sha256 is null)
                {
                    throw new InvalidOperationException(
                        "MariaDB 논리 백업 실패: " +
                        (string.IsNullOrWhiteSpace(logical.ErrorText) ? "dump 명령이 실패했습니다." : logical.ErrorText));
                }

                logicalManifest = new LogicalBackupManifest(
                    Path.GetRelativePath(report.BackupDestination, logical.FilePath),
                    logical.Size,
                    logical.Sha256);
                AppendDetail(type, $"논리 백업 완료: {FormatBytes(logical.Size)} / SHA256 {logical.Sha256}");
            }

            serviceWasRunning = report.ServiceName is not null &&
                                report.ServiceState?.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) == true;
            if (serviceWasRunning)
            {
                StatusText.Text = $"MariaDB 논리 백업 완료. 서비스 {report.ServiceName} 중지 중...";
                await Task.Run(() => _windowsServiceController.Stop(report.ServiceName!, TimeSpan.FromSeconds(30)));
                serviceStoppedByUpdater = true;
            }

            var stoppedReport = await Task.Run(() => _preflightService.Inspect(_lastInstallation, type, target.Version));
            stoppedReport = stoppedReport with { BackupDestination = report.BackupDestination };
            _preflightReports[type] = stoppedReport;

            var physical = await Task.Run(() => _backupService.CreateBackup(stoppedReport, logicalManifest));
            AppendDetail(type, $"물리 백업 완료: {physical.CopiedFiles:N0}개 / {FormatBytes(physical.CopiedBytes)}");
            AppendDetail(type, $"manifest: {physical.ManifestPath}");
            StatusText.Text = "MariaDB 논리/물리 롤백 백업 생성 완료";
        }
        catch (Exception ex)
        {
            AppendDetail(type, $"MariaDB 안전 백업 실패: {ex.Message}");
            StatusText.Text = $"MariaDB 백업 실패: {ex.Message}";
        }
        finally
        {
            if (serviceStoppedByUpdater && serviceWasRunning &&
                _preflightReports.TryGetValue(type, out var report) &&
                report.ServiceName is not null)
            {
                try
                {
                    await Task.Run(() => _windowsServiceController.Start(report.ServiceName, TimeSpan.FromSeconds(30)));
                    AppendDetail(type, $"서비스 원상복구: {report.ServiceName} RUNNING");
                    if (_lastInstallation is not null)
                    {
                        var refreshed = await Task.Run(() => _preflightService.Inspect(_lastInstallation, type, target.Version));
                        _preflightReports[type] = refreshed;
                    }
                }
                catch (Exception restartException)
                {
                    AppendDetail(type, $"서비스 재시작 실패: {restartException.Message}");
                    StatusText.Text = $"MariaDB 서비스 재시작 실패: {restartException.Message}";
                }
            }

            SetBusy(false);
            if (_preflightReports.TryGetValue(type, out var latestReport))
            {
                SetBackupEnabled(type, CanRunMariaDbSafeBackup(latestReport));
            }
        }
    }

    private static bool CanRunMariaDbSafeBackup(UpdatePreflightReport report)
    {
        var running = report.ProcessRunning ||
                      report.ServiceState?.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) == true;
        return !running || report.ServiceName is not null;
    }
}
