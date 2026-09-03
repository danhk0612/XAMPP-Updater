namespace XamppUpdater.App;

public partial class MainWindow
{
    internal async Task InspectStartupRootAsync()
    {
        var root = AdministratorPrivilege.GetStartupXamppRoot();
        if (string.IsNullOrWhiteSpace(root)) return;

        InstallPathComboBox.Text = root;
        await InspectAsync(root, "ElevatedRelaunch");
        StatusText.Text = AdministratorPrivilege.IsElevated
            ? $"관리자 권한으로 재실행됨: {root}"
            : $"XAMPP 경로 복원됨: {root}";
    }
}
