using System.IO;
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
                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                var buildTime = File.GetLastWriteTime(assemblyPath);
                var privilege = AdministratorPrivilege.IsElevated ? "Admin" : "Standard";
                window.Title = $"XAMPP Updater - Build {buildTime:yyyy-MM-dd HH:mm:ss} [{privilege}]";
            }
            catch
            {
                window.Title = "XAMPP Updater - Build unknown";
            }

            if (window is MainWindow mainWindow)
                await mainWindow.InspectStartupRootAsync();
        });
    }
}
