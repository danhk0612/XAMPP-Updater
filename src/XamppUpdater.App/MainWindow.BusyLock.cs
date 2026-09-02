using System.Windows;

namespace XamppUpdater.App;

public partial class MainWindow
{
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        OnlineCheckButton.IsEnabledChanged += OnlineCheckButton_IsEnabledChanged;
        InitializeMariaDbSafeBackupUi();
        SyncWindowInputLock();
    }

    private void OnlineCheckButton_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SyncWindowInputLock();
    }

    private void SyncWindowInputLock()
    {
        RootGrid.IsHitTestVisible = OnlineCheckButton.IsEnabled;
    }
}
