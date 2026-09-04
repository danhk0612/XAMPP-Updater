using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XamppUpdater.App;

// Compatibility wrapper: existing unqualified MessageBox.Show calls in this namespace
// automatically receive localized user-facing text without touching update logic.
// In English mode a small WPF dialog is used so button captions do not follow the
// Windows display language (for example, Korean Windows showing 확인/예/아니요).
internal static class MessageBox
{
    private static string T(string text) => ExtendedLocalization.TranslateText(text);

    public static MessageBoxResult Show(string messageBoxText) =>
        ShowCore(null, messageBoxText, string.Empty, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(string messageBoxText, string caption) =>
        ShowCore(null, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button) =>
        ShowCore(null, messageBoxText, caption, button, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
        ShowCore(null, messageBoxText, caption, button, icon, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
        ShowCore(null, messageBoxText, caption, button, icon, defaultResult, MessageBoxOptions.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText) =>
        ShowCore(owner, messageBoxText, string.Empty, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption) =>
        ShowCore(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button) =>
        ShowCore(owner, messageBoxText, caption, button, MessageBoxImage.None, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
        ShowCore(owner, messageBoxText, caption, button, icon, MessageBoxResult.None, MessageBoxOptions.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
        ShowCore(owner, messageBoxText, caption, button, icon, defaultResult, MessageBoxOptions.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options) =>
        ShowCore(owner, messageBoxText, caption, button, icon, defaultResult, options);

    private static MessageBoxResult ShowCore(
        Window? owner,
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon,
        MessageBoxResult defaultResult,
        MessageBoxOptions options)
    {
        var translatedText = T(messageBoxText);
        var translatedCaption = T(caption);

        if (!LocalizationService.IsEnglish)
        {
            return owner is null
                ? System.Windows.MessageBox.Show(translatedText, translatedCaption, button, icon, defaultResult, options)
                : System.Windows.MessageBox.Show(owner, translatedText, translatedCaption, button, icon, defaultResult, options);
        }

        var dialog = new EnglishMessageBoxWindow(
            owner,
            translatedText,
            translatedCaption,
            button,
            icon,
            defaultResult);
        dialog.ShowDialog();
        return dialog.Result;
    }

    private sealed class EnglishMessageBoxWindow : Window
    {
        private MessageBoxResult _result = MessageBoxResult.None;
        private readonly MessageBoxButton _buttons;

        public MessageBoxResult Result => _result == MessageBoxResult.None ? FallbackResult(_buttons) : _result;

        public EnglishMessageBoxWindow(
            Window? owner,
            string text,
            string caption,
            MessageBoxButton buttons,
            MessageBoxImage icon,
            MessageBoxResult defaultResult)
        {
            _buttons = buttons;
            if (owner is not null) Owner = owner;
            Title = string.IsNullOrWhiteSpace(caption) ? "XAMPP Updater" : caption;
            MinWidth = 360;
            MaxWidth = 680;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var glyph = GetGlyph(icon);
            if (!string.IsNullOrEmpty(glyph))
            {
                var iconText = new TextBlock
                {
                    Text = glyph,
                    FontSize = 30,
                    Width = 42,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                body.Children.Add(iconText);
            }

            var message = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 560,
                MinWidth = 260,
                Margin = new Thickness(string.IsNullOrEmpty(glyph) ? 0 : 12, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(message, 1);
            body.Children.Add(message);
            root.Children.Add(body);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            foreach (var (label, result) in GetButtons(buttons))
            {
                var dialogButton = new Button
                {
                    Content = label,
                    MinWidth = 88,
                    Padding = new Thickness(14, 6, 14, 6),
                    Margin = new Thickness(8, 0, 0, 0),
                    IsDefault = result == NormalizeDefault(defaultResult, buttons),
                    IsCancel = result == MessageBoxResult.Cancel
                };
                dialogButton.Click += (_, _) =>
                {
                    _result = result;
                    Close();
                };
                buttonPanel.Children.Add(dialogButton);
            }

            Grid.SetRow(buttonPanel, 1);
            root.Children.Add(buttonPanel);
            Content = root;

            Closing += (_, _) =>
            {
                if (_result == MessageBoxResult.None)
                    _result = FallbackResult(buttons);
            };
        }

        private static IEnumerable<(string Label, MessageBoxResult Result)> GetButtons(MessageBoxButton buttons) => buttons switch
        {
            MessageBoxButton.OK => new[] { ("OK", MessageBoxResult.OK) },
            MessageBoxButton.OKCancel => new[] { ("OK", MessageBoxResult.OK), ("Cancel", MessageBoxResult.Cancel) },
            MessageBoxButton.YesNo => new[] { ("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No) },
            MessageBoxButton.YesNoCancel => new[] { ("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No), ("Cancel", MessageBoxResult.Cancel) },
            _ => new[] { ("OK", MessageBoxResult.OK) }
        };

        private static MessageBoxResult NormalizeDefault(MessageBoxResult requested, MessageBoxButton buttons)
        {
            var available = GetButtons(buttons).Select(item => item.Result).ToHashSet();
            if (requested != MessageBoxResult.None && available.Contains(requested)) return requested;
            return GetButtons(buttons).First().Result;
        }

        private static MessageBoxResult FallbackResult(MessageBoxButton buttons) => buttons switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.None
        };

        private static string GetGlyph(MessageBoxImage icon) => icon switch
        {
            MessageBoxImage.Information => "ℹ",
            MessageBoxImage.Question => "?",
            MessageBoxImage.Warning => "⚠",
            MessageBoxImage.Error => "✕",
            _ => string.Empty
        };
    }
}
