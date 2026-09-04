using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private bool _phpMyAdminAvailabilityUiInitialized;

    internal void InitializePhpMyAdminAvailabilityUi()
    {
        if (_phpMyAdminAvailabilityUiInitialized || _phpMyAdminUpdateButton is null) return;
        _phpMyAdminAvailabilityUiInitialized = true;

        void Refresh()
        {
            if (_phpMyAdminUpdateButton is null) return;
            _phpMyAdminUpdateButton.Visibility = _phpMyAdminUpdateButton.IsEnabled || _phpMyAdminUpdateRunning
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        Refresh();
        var descriptor = DependencyPropertyDescriptor.FromProperty(Button.IsEnabledProperty, typeof(Button));
        descriptor?.AddValueChanged(_phpMyAdminUpdateButton, (_, _) => Refresh());
    }
}
