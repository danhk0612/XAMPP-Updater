using System.Windows;
using System.Windows.Controls;
using XamppUpdater.Core.Services;

namespace XamppUpdater.App;

public sealed class MariaDbCredentialsDialog : Window
{
    private readonly TextBox _userName = new() { Text = "root", MinWidth = 260 };
    private readonly PasswordBox _password = new() { MinWidth = 260 };
    private readonly TaskCompletionSource<MariaDbCredentials?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MariaDbCredentials? _acceptedCredentials;

    private MariaDbCredentialsDialog(Window owner)
    {
        Owner = owner;
        Title = "MariaDB 인증";
        Width = 420;
        MinHeight = 240;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "자동 접속에 실패했습니다. 논리 백업에 사용할 MariaDB 계정을 입력하세요.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });
        panel.Children.Add(new TextBlock { Text = "사용자", Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(_userName);
        panel.Children.Add(new TextBlock { Text = "암호", Margin = new Thickness(0, 10, 0, 4) });
        panel.Children.Add(_password);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var cancel = new Button
        {
            Content = "취소",
            Width = 80,
            Height = 30
        };
        var ok = new Button
        {
            Content = "확인",
            Width = 80,
            Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true
        };

        cancel.Click += (_, _) => Close();
        ok.Click += (_, _) =>
        {
            _acceptedCredentials = new MariaDbCredentials(_userName.Text.Trim(), _password.Password);
            Close();
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        Content = panel;

        Closed += (_, _) => _completion.TrySetResult(_acceptedCredentials);
    }

    public static Task<MariaDbCredentials?> RequestAsync(Window owner)
    {
        var dialog = new MariaDbCredentialsDialog(owner);
        dialog.Show();
        dialog.Activate();
        _ = dialog._password.Focus();
        return dialog._completion.Task;
    }
}
