namespace XamppUpdater.Core.Models;

public sealed record PackagePreparationResult(
    XamppComponentType Type,
    string Version,
    string SourceUrl,
    string DownloadUrl,
    string PackagePath,
    string FileName,
    long Size,
    string Sha256,
    BinaryArchitecture Architecture,
    string PayloadEntry,
    int ArchiveEntries,
    bool PhpApacheModulePresent,
    IReadOnlyList<string> Warnings)
{
    public string SizeText => FormatBytes(Size);

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
