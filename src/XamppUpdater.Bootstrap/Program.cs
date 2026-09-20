using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

internal static class Program
{
    private const string PayloadResourceName = "XamppUpdater.Payload.exe";
    private const string BootstrapPathEnvironmentVariable = "XAMPP_UPDATER_BOOTSTRAP_PATH";
    private const string RuntimeDownloadUrl = "https://dotnet.microsoft.com/en-us/download/dotnet/10.0";
    private static readonly Stopwatch Lifetime = Stopwatch.StartNew();

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && string.Equals(args[0], "--bootstrap-self-test", StringComparison.Ordinal))
            {
                using var payload = OpenPayload();
                return payload.Length > 0 ? 0 : 1;
            }

            if (!IsDesktopRuntime10Installed())
            {
                ShowMissingRuntimeDialog();
                KeepAliveThroughLegacyUpdaterProbe();
                return 10;
            }

            var bootstrapPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Bootstrap executable path is unavailable.");
            var payloadPath = EnsurePayloadExtracted();

            var startInfo = new ProcessStartInfo
            {
                FileName = payloadPath,
                WorkingDirectory = Path.GetDirectoryName(bootstrapPath) ?? AppContext.BaseDirectory,
                UseShellExecute = false
            };
            foreach (var arg in args)
                startInfo.ArgumentList.Add(arg);
            startInfo.Environment[BootstrapPathEnvironmentVariable] = bootstrapPath;

            using var appProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("XAMPP Updater application process could not be started.");

            var probeRemaining = TimeSpan.FromMilliseconds(2100) - Lifetime.Elapsed;
            if (probeRemaining > TimeSpan.Zero && appProcess.WaitForExit((int)Math.Ceiling(probeRemaining.TotalMilliseconds)))
                return appProcess.ExitCode;

            KeepAliveThroughLegacyUpdaterProbe();
            return 0;
        }
        catch (Exception ex)
        {
            WriteBootstrapError(ex);
            return 1;
        }
    }

    private static Stream OpenPayload() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
        ?? throw new InvalidOperationException("Embedded application payload is missing.");

    private static string EnsurePayloadExtracted()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionName = version is null
            ? "unknown"
            : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XAMPP-Updater",
            "App",
            versionName);
        Directory.CreateDirectory(appDirectory);

        var targetPath = Path.Combine(appDirectory, "XAMPP-Updater.App.exe");
        using var payload = OpenPayload();
        var embeddedHash = SHA256.HashData(payload);

        if (File.Exists(targetPath))
        {
            using var current = File.OpenRead(targetPath);
            var currentHash = SHA256.HashData(current);
            if (CryptographicOperations.FixedTimeEquals(embeddedHash, currentHash))
                return targetPath;
        }

        payload.Position = 0;
        var tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                payload.CopyTo(output);
            File.Move(tempPath, targetPath, true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }
        }

        return targetPath;
    }

    private static bool IsDesktopRuntime10Installed()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRoot(roots, Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"));
        AddRoot(roots, Environment.GetEnvironmentVariable("DOTNET_ROOT"));

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            AddRoot(roots, Path.Combine(programFiles, "dotnet"));

        foreach (var root in roots)
        {
            var sharedFx = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(sharedFx))
                continue;

            foreach (var directory in Directory.EnumerateDirectories(sharedFx))
            {
                var name = Path.GetFileName(directory);
                if (Version.TryParse(name, out var version) && version.Major == 10)
                    return true;
            }
        }

        return false;
    }

    private static void AddRoot(HashSet<string> roots, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            roots.Add(Path.GetFullPath(path));
    }

    private static void ShowMissingRuntimeDialog()
    {
        var message =
            "XAMPP Updater requires Microsoft .NET 10 Desktop Runtime (x64).\n\n" +
            "XAMPP Updater를 실행하려면 Microsoft .NET 10 Desktop Runtime (x64)이 필요합니다.\n\n" +
            "Yes / 예: Microsoft 공식 다운로드 페이지 열기\n" +
            "No / 아니요: 종료";

        var result = MessageBoxW(
            IntPtr.Zero,
            message,
            "XAMPP Updater - .NET 10 Desktop Runtime",
            0x00000004u | 0x00000040u | 0x00040000u);

        if (result == 6)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = RuntimeDownloadUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }
    }

    private static void KeepAliveThroughLegacyUpdaterProbe()
    {
        var remaining = TimeSpan.FromMilliseconds(2600) - Lifetime.Elapsed;
        if (remaining > TimeSpan.Zero)
            Thread.Sleep(remaining);
    }

    private static void WriteBootstrapError(Exception ex)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XAMPP-Updater");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "bootstrap.log"),
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {ex}\r\n");
        }
        catch
        {
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);
}
