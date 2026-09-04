using System.Windows;
using System.Windows.Controls;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private void ApplyNavigationLayout()
    {
        if (ApacheNavButton.Parent is not StackPanel panel) return;
        if (panel.Parent is ScrollViewer scroll)
        {
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.PanningMode = PanningMode.None;
        }

        foreach (var child in panel.Children)
        {
            switch (child)
            {
                case Button button:
                    button.Height = 32;
                    button.Margin = new Thickness(0, 0, 0, 3);
                    button.Padding = new Thickness(6, 3, 6, 3);
                    break;
                case Separator separator:
                    separator.Margin = new Thickness(0, 6, 0, 6);
                    break;
                case TextBlock text:
                    text.Margin = new Thickness(4, 0, 0, 4);
                    break;
                case ComboBox combo when string.Equals(combo.Tag?.ToString(), "LanguageSelector", StringComparison.Ordinal):
                    combo.Height = 29;
                    combo.Margin = new Thickness(0);
                    break;
            }
        }
    }
}
