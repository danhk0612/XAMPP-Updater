using System.Windows.Controls;

namespace XamppUpdater.App;

internal static class PopupLocalization
{
    public static void Apply(ContextMenu menu)
    {
        if (!LocalizationService.IsEnglish) return;
        ApplyItems(menu.Items);
    }

    private static void ApplyItems(ItemCollection items)
    {
        foreach (var item in items)
        {
            if (item is not MenuItem menuItem) continue;
            if (menuItem.Header is string header)
                menuItem.Header = ExtendedLocalization.TranslateText(header);
            if (menuItem.ToolTip is string toolTip)
                menuItem.ToolTip = ExtendedLocalization.TranslateText(toolTip);
            ApplyItems(menuItem.Items);
        }
    }
}
