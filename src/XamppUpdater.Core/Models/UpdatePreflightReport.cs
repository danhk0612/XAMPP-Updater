namespace XamppUpdater.Core.Models;

public sealed record UpdatePreflightReport(
    XamppComponentType Type,
    string CurrentVersion,
    string TargetVersion,
    string ComponentRoot,
    bool ProcessRunning,
    string? ServiceName,
    string? ServiceState,
    long BackupBytes,
    int BackupFileCount,
    IReadOnlyList<PreflightConfigFile> ConfigFiles,
    IReadOnlyList<string> Warnings,
    string BackupDestination)
{
    public string BackupSizeText => FormatBytes(BackupBytes);

    private static string FormatBytes(long bytes)
    {
        var value = (double)bytes;
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes:N0} {units[unit]}" : $"{value:N2} {units[unit]}";
    }
}

public sealed record PreflightConfigFile(
    string RelativePath,
    long Size,
    string Sha256);
