namespace XamppUpdater.Core.Services;

public interface ILocalStorageCleanupService
{
    LocalStorageUsage GetUsage();
    LocalStorageCleanupResult Clear();
}

public sealed class LocalStorageCleanupService : ILocalStorageCleanupService
{
    private static readonly string RootPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XamppUpdater");

    private static readonly string[] ManagedDirectories = { "Backups", "Packages", "ExternalExtensions" };

    public LocalStorageUsage GetUsage()
    {
        long bytes = 0;
        var files = 0;

        foreach (var name in ManagedDirectories)
        {
            var directory = Path.Combine(RootPath, name);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    bytes += new FileInfo(file).Length;
                    files++;
                }
                catch
                {
                    // 정리 실행에서 다시 시도한다.
                }
            }
        }

        return new LocalStorageUsage(RootPath, files, bytes);
    }

    public LocalStorageCleanupResult Clear()
    {
        var before = GetUsage();
        var errors = new List<string>();

        foreach (var name in ManagedDirectories)
        {
            var directory = Path.Combine(RootPath, name);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex)
            {
                errors.Add($"{name}: {ex.Message}");
            }
        }

        var after = GetUsage();
        return new LocalStorageCleanupResult(
            before.Files - after.Files,
            Math.Max(0, before.Bytes - after.Bytes),
            errors);
    }
}

public sealed record LocalStorageUsage(string RootPath, int Files, long Bytes);

public sealed record LocalStorageCleanupResult(
    int DeletedFiles,
    long ReclaimedBytes,
    IReadOnlyList<string> Errors);
