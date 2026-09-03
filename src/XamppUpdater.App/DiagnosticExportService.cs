using System.IO.Compression;
using System.Text;

namespace XamppUpdater.App;

internal sealed record DiagnosticExportResult(string FilePath, int IncludedFiles);

internal sealed class DiagnosticExportService
{
    private static readonly string PersistentLogRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XamppUpdater",
        "Logs");

    private static readonly string SelfUpdateLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XAMPP-Updater",
        "SelfUpdate",
        "self-update.log");

    public DiagnosticExportResult Export(
        string destinationPath,
        string diagnosticsText,
        IReadOnlyDictionary<string, IReadOnlyList<string>> activityLogs)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);
        if (File.Exists(destinationPath)) File.Delete(destinationPath);

        var includedFiles = 0;
        using var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create);

        AddTextEntry(archive, "diagnostics.txt", diagnosticsText);
        includedFiles++;

        foreach (var (component, lines) in activityLogs)
        {
            if (lines.Count == 0) continue;
            AddTextEntry(archive, $"current-activity/{component}.log", string.Join(Environment.NewLine, lines));
            includedFiles++;
        }

        if (Directory.Exists(PersistentLogRoot))
        {
            foreach (var path in Directory.EnumerateFiles(PersistentLogRoot, "*.log", SearchOption.TopDirectoryOnly)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                archive.CreateEntryFromFile(path, $"logs/{Path.GetFileName(path)}", CompressionLevel.Optimal);
                includedFiles++;
            }
        }

        if (File.Exists(SelfUpdateLogPath))
        {
            archive.CreateEntryFromFile(SelfUpdateLogPath, "self-update/self-update.log", CompressionLevel.Optimal);
            includedFiles++;
        }

        return new DiagnosticExportResult(destinationPath, includedFiles);
    }

    private static void AddTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }
}
