using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace XamppUpdater.App;

public partial class App : Application
{
    private static readonly Uri WindowIconUri =
        new("pack://application:,,,/Assets/XamppUpdater.ico", UriKind.Absolute);

    protected override void OnStartup(StartupEventArgs e)
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ApplyWindowIcon));

        base.OnStartup(e);

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            if (MainWindow is not Window window) return;

            try
            {
                var version = Assembly.GetEntryAssembly()?.GetName().Version;
                var displayVersion = version is null
                    ? "unknown"
                    : $"{version.Major}.{version.Minor}.{version.Build}";
                var privilege = AdministratorPrivilege.IsElevated ? "Admin" : "Standard";
                window.Title = $"XAMPP Updater {displayVersion} [{privilege}]";
            }
            catch
            {
                window.Title = "XAMPP Updater [version unknown]";
            }

            if (window is MainWindow mainWindow)
            {
                mainWindow.InitializeConfigHistoryUi();
                await mainWindow.InspectStartupRootAsync();
                await mainWindow.InitializeReleaseLifecycleAsync();
                await mainWindow.ResumeStartupUpdateAsync();
                await mainWindow.ResumeStartupRollbackAsync();
            }
        });
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
}
