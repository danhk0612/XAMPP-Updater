using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace XamppUpdater.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
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
}
