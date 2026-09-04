using System.Windows;

namespace XamppUpdater.App;

// Compatibility wrapper: existing unqualified MessageBox.Show calls in this namespace
// automatically receive localized user-facing text without touching update logic.
internal static class MessageBox
{
    private static string T(string text) => LocalizationCatalog.TranslateUserText(text);

    public static MessageBoxResult Show(string messageBoxText) =>
        System.Windows.MessageBox.Show(T(messageBoxText));

    public static MessageBoxResult Show(string messageBoxText, string caption) =>
        System.Windows.MessageBox.Show(T(messageBoxText), T(caption));

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button) =>
        System.Windows.MessageBox.Show(T(messageBoxText), T(caption), button);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
        System.Windows.MessageBox.Show(T(messageBoxText), T(caption), button, icon);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
        System.Windows.MessageBox.Show(T(messageBoxText), T(caption), button, icon, defaultResult);

    public static MessageBoxResult Show(Window owner, string messageBoxText) =>
        System.Windows.MessageBox.Show(owner, T(messageBoxText));

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption) =>
        System.Windows.MessageBox.Show(owner, T(messageBoxText), T(caption));

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button) =>
        System.Windows.MessageBox.Show(owner, T(messageBoxText), T(caption), button);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
        System.Windows.MessageBox.Show(owner, T(messageBoxText), T(caption), button, icon);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
        System.Windows.MessageBox.Show(owner, T(messageBoxText), T(caption), button, icon, defaultResult);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options) =>
        System.Windows.MessageBox.Show(owner, T(messageBoxText), T(caption), button, icon, defaultResult, options);
}
