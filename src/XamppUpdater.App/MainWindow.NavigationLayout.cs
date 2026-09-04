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
                    button.Height = 30;
                    button.Margin = new Thickness(0, 0, 0, 2);
                    button.Padding = new Thickness(6, 2, 6, 2);
                    break;
                case Separator separator:
                    separator.Margin = new Thickness(0, 4, 0, 4);
                    break;
                case TextBlock text:
                    text.Margin = new Thickness(4, 0, 0, 3);
                    break;
                case ComboBox combo when string.Equals(combo.Tag?.ToString(), "LanguageSelector", StringComparison.Ordinal):
                    combo.Height = 28;
                    combo.Margin = new Thickness(0);
                    break;
            }
        }
    }
}
