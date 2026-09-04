using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private bool _runtimeLocalizationInitialized;

    internal void InitializeRuntimeLocalizationUi()
    {
        if (_runtimeLocalizationInitialized || !LocalizationService.IsEnglish) return;
        _runtimeLocalizationInitialized = true;

        foreach (var combo in new[] { ApacheTargetComboBox, PhpTargetComboBox, MariaDbTargetComboBox })
            ApplyLocalizedTargetTemplate(combo);

        foreach (var block in new TextBlock?[]
                 {
                     StatusText,
                     PrivilegeText,
                     ApacheVersionText, ApachePlanText, ApacheEnvironmentText, ApacheServiceText, ApachePathText, ApacheCompatibilityText,
                     PhpVersionText, PhpPlanText, PhpEnvironmentText, PhpPathText, PhpCompatibilityText,
                     MariaDbVersionText, MariaDbPlanText, MariaDbEnvironmentText, MariaDbServiceText, MariaDbPathText, MariaDbDetailText, MariaDbCompatibilityText,
                     _phpMyAdminVersionText, _phpMyAdminTargetText, _phpMyAdminPlanText, _phpMyAdminPathText, _phpMyAdminCompatibilityText
                 })
        {
            if (block is not null) WatchRuntimeText(block);
        }
    }

    private static void ApplyLocalizedTargetTemplate(ComboBox combo)
    {
        combo.DisplayMemberPath = string.Empty;
        var template = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(".")
        {
            Converter = LocalizedDisplayValueConverter.Instance
        });
        template.VisualTree = text;
        combo.ItemTemplate = template;
    }

    private static void WatchRuntimeText(TextBlock block)
    {
        void Apply()
        {
            var current = block.Text;
            var translated = ExtendedLocalization.TranslateText(current);
            if (!string.Equals(current, translated, StringComparison.Ordinal))
                block.Text = translated;
        }

        Apply();
        var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        descriptor?.AddValueChanged(block, (_, _) => Apply());
    }

    private sealed class LocalizedDisplayValueConverter : IValueConverter
    {
        public static readonly LocalizedDisplayValueConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            ExtendedLocalization.TranslateText(LocalizationCatalog.TranslateDisplayValue(value));

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
