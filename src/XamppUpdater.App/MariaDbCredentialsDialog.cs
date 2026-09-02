using System.Windows;
using System.Windows.Controls;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public sealed class MariaDbCredentialsDialog : Window
{
    private readonly TextBox _userName = new() { Text = "root", MinWidth = 260 };
    private readonly PasswordBox _password = new() { MinWidth = 260 };

    public MariaDbCredentialsDialog(Window owner)
    {
        Owner = owner;
        Title = "MariaDB 인증";
        Width = 380;
        Height = 210;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "자동 접속에 실패했습니다. 논리 백업에 사용할 MariaDB 계정을 입력하세요.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(new TextBlock { Text = "사용자" });
        panel.Children.Add(_userName);
        panel.Children.Add(new TextBlock { Text = "암호", Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(_password);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "취소", Width = 72, IsCancel = true };
        var ok = new Button { Content = "확인", Width = 72, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);

        Content = panel;
    }

    public MariaDbCredentials Credentials => new(_userName.Text.Trim(), _password.Password);
}
