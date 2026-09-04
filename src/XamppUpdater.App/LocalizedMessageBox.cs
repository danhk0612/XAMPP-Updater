using System.Windows;

namespace XamppUpdater.App;

// Compatibility wrapper: existing unqualified MessageBox.Show calls in this namespace
// automatically receive localized user-facing text without touching update logic.
internal static class MessageBox
{
    public static MessageBoxResult Show(string messageBoxText) =>
        System.Windows.MessageBox.Show(LocalizationService.Translate(messageBoxText));

    public static MessageBoxResult Show(string messageBoxText, string caption) =>
        System.Windows.MessageBox.Show(LocalizationService.Translate(messageBoxText), LocalizationService.Translate(caption));

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button) =>
        System.Windows.MessageBox.Show(LocalizationService.Translate(messageBoxText), LocalizationService.Translate(caption), button);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
        System.Windows.MessageBox.Show(LocalizationService.Translate(messageBoxText), LocalizationService.Translate(caption), button, icon);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
        System.Windows.MessageBox.Show(LocalizationService.Translate(messageBoxText), LocalizationService.Translate(caption), button, icon, defaultResult);

    public static MessageBoxResult Show(Window owner, string messageBoxText) =>
        System.Windows.MessageBox.Show(owner, LocalizationService.Translate(messageBoxText));

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption) =>
        System.Windows.MessageBox.Show(owner, LocalizationService.Translate(messageBoxText), LocalizationService.Translate(caption));

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button) =>
        System.Windows.MessageBox.Show(owner, LocalizationService.Translate(messageBoxText), LocalizationService.Translate(caption), button);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
        System.Windows.MessageBox.Show(owner, LocalizationService.Translate(messageBoxText), LocalizationService.Translate(caption), button, icon);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
        System.Windows.MessageBox.Show(owner, LocalizationService.Translate(messageBoxText), LocalizationService.Translate(caption), button, icon, defaultResult);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options) =>
        System.Windows.MessageBox.Show(owner, LocalizationService.Translate(messageBoxText), LocalizationService.Translate(caption), button, icon, defaultResult, options);
}
