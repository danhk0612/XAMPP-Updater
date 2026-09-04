using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;

namespace XamppUpdater.App;

public partial class App : Application
{
    private bool _startupHandled;
    private static readonly Uri WindowIconUri =
        new("pack://application:,,,/Assets/XamppUpdater.ico", UriKind.Absolute);

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                WriteCrashLog("AppDomain.UnhandledException", ex);
            }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog("DispatcherUnhandledException", args.Exception);
        };

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ApplyWindowIcon));

        base.OnStartup(e);
        if (_startupHandled) return;
        _startupHandled = true;

        if (TryHandleElevatedResume(e.Args))
        {
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void ApplyWindowIcon(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window || window.Icon is not null)
        {
            return;
        }

        try
        {
            window.Icon = BitmapFrame.Create(WindowIconUri);
        }
        catch
        {
            // 아이콘 로드 실패가 업데이트 기능에 영향을 주면 안 된다.
        }
    }

    private bool TryHandleElevatedResume(IReadOnlyList<string> args)
    {
        var resumePath = TryGetArgument(args, "--resume-xampp");
        if (string.IsNullOrWhiteSpace(resumePath))
        {
            return false;
        }

        var window = new MainWindow(resumePath);
        MainWindow = window;
        window.Show();
        return true;
    }

    private static string? TryGetArgument(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static void WriteCrashLog(string source, Exception exception)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XamppUpdater",
                "Logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(path,
                $"Source: {source}{Environment.NewLine}" +
                $"Time: {DateTimeOffset.Now:O}{Environment.NewLine}" +
                $"App: {Assembly.GetExecutingAssembly().GetName().Version}{Environment.NewLine}" +
                exception);
        }
        catch
        {
        }
    }
}
