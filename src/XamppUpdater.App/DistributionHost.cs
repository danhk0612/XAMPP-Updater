namespace XamppUpdater.App;

internal static class DistributionHost
{
    private const string BootstrapPathEnvironmentVariable = "XAMPP_UPDATER_BOOTSTRAP_PATH";

    public static bool IsDotnetHost
    {
        get
        {
            if (TryGetBootstrapPath() is not null)
                return false;

            var processPath = Environment.ProcessPath;
            return !string.IsNullOrWhiteSpace(processPath) &&
                   string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string GetLaunchExecutablePath()
    {
        var bootstrapPath = TryGetBootstrapPath();
        if (bootstrapPath is not null)
            return bootstrapPath;

        return Environment.ProcessPath
            ?? throw new InvalidOperationException("현재 실행 파일 경로를 확인할 수 없습니다.");
    }

    public static string GetLaunchWorkingDirectory()
    {
        var executable = GetLaunchExecutablePath();
        return Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory;
    }

    public static string? TryGetBootstrapPath()
    {
        var value = Environment.GetEnvironmentVariable(BootstrapPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(value);
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }
}
