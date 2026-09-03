using System.Windows;
using System.Windows.Controls;

namespace XamppUpdater.App;

public sealed class ManualSnapshotDialog : Window
{
    private readonly TextBox _note = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 90,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    public string? Note => string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim();

    public ManualSnapshotDialog(string componentName, string? initialNote = null, bool editMode = false)
    {
        Title = editMode ? $"{componentName} snapshot 메모 수정" : $"{componentName} 수동 snapshot";
        Width = 470;
        Height = 260;
        MinWidth = 420;
        MinHeight = 230;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        _note.Text = initialNote ?? string.Empty;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = editMode
                ? "snapshot 메모를 수정하세요. 비우면 메모가 제거됩니다."
                : "이 snapshot을 구분할 메모를 입력하세요. 비워도 저장할 수 있습니다.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(label);

        Grid.SetRow(_note, 1);
        root.Children.Add(_note);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var save = new Button { Content = "저장", Padding = new Thickness(18, 5, 18, 5), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        save.Click += (_, _) => { DialogResult = true; Close(); };
        var cancel = new Button { Content = "취소", Padding = new Thickness(18, 5, 18, 5), IsCancel = true };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) =>
        {
            _note.Focus();
            _note.CaretIndex = _note.Text.Length;
        };
    }
}
