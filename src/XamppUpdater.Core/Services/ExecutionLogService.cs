namespace XamppUpdater.Core.Services;

public interface IExecutionLogService
{
    string Save(string component, IEnumerable<string> lines);
    string? FindLatest(string component);
}

public sealed class ExecutionLogService : IExecutionLogService
{
    private static readonly string RootPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XamppUpdater",
        "Logs");

    public string Save(string component, IEnumerable<string> lines)
    {
        Directory.CreateDirectory(RootPath);
        var safeComponent = string.Concat(component.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var path = Path.Combine(RootPath, $"{DateTime.Now:yyyyMMdd-HHmmss}-{safeComponent}.log");
        File.WriteAllLines(path, lines);
        return path;
    }

    public string? FindLatest(string component)
    {
        if (!Directory.Exists(RootPath)) return null;
        return Directory.EnumerateFiles(RootPath, $"*-{component}.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
