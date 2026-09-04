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
        LocalizationService.Initialize();

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ApplyWindowIcon));
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ApplyWindowLocalization));
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ApplyLocalization));

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
                mainWindow.InitializePhpMyAdminUi();
                mainWindow.InitializeLocalizationUi();
                mainWindow.InitializeConfigHistoryUi();

                // StartupUri로 만들어진 정적 XAML과 위에서 동적으로 추가한 UI를
                // 창 전체 기준으로 다시 적용해 Loaded 이벤트 순서와 무관하게 현지화한다.
                LocalizationService.ApplyToTree(window);

                await mainWindow.InspectStartupRootAsync();
                await mainWindow.InitializeReleaseLifecycleAsync();

                // 초기화 중 동적으로 갱신된 사용자 표시 문자열도 현재 언어로 보정한다.
                LocalizationService.ApplyToTree(window);

                var resume = AdministratorPrivilege.GetStartupResumeUpdate();
                if (resume is { } phpMyAdminResume &&
                    string.Equals(phpMyAdminResume.Component, "PhpMyAdmin", StringComparison.OrdinalIgnoreCase))
                {
                    await mainWindow.ResumeStartupPhpMyAdminUpdateAsync(phpMyAdminResume.Version);
                }
                else
                {
                    await mainWindow.ResumeStartupUpdateAsync();
                }

                await mainWindow.ResumeStartupRollbackAsync();
            }
        });
    }

    private static void ApplyLocalization(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            LocalizationService.ApplyToElement(element);
        }
    }

    private static void ApplyWindowLocalization(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
        {
            LocalizationService.ApplyToTree(window);
        }
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
