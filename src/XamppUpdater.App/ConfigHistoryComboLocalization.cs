using System.Windows;
using System.Windows.Controls;
using XamppUpdater.Core.Models;

namespace XamppUpdater.App;

internal static class ConfigHistoryComboLocalization
{
    private static readonly DependencyProperty AppliedProperty =
        DependencyProperty.RegisterAttached(
            "Applied",
            typeof(bool),
            typeof(ConfigHistoryComboLocalization),
            new PropertyMetadata(false));

    public static void Apply(FrameworkElement element)
    {
        if (!LocalizationService.IsEnglish || element is not ComboBox combo || Window.GetWindow(combo) is not ConfigHistoryWindow) return;
        if ((bool)combo.GetValue(AppliedProperty)) return;

        var items = combo.ItemsSource?.Cast<object>().ToArray();
        if (items is null || !items.Any(item => item is string text && text == "전체")) return;

        var selectedType = combo.SelectedItem as XamppComponentType?;
        var localized = items.Select(item => item is string text && text == "전체" ? (object)"All" : item).ToArray();
        combo.ItemsSource = localized;
        combo.SelectedItem = selectedType is { } type ? type : "All";
        combo.SetValue(AppliedProperty, true);
    }
}
