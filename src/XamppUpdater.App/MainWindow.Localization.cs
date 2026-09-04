using System.Windows;
using System.Windows.Controls;

namespace XamppUpdater.App;

public partial class MainWindow
{
    private ComboBox? _languageComboBox;
    private bool _languageUiInitialized;

    internal void InitializeLocalizationUi()
    {
        if (_languageUiInitialized) return;
        _languageUiInitialized = true;

        if (ApacheNavButton.Parent is not StackPanel navigation) return;

        var separator = new Separator { Margin = new Thickness(0, 12, 0, 10) };
        var label = new TextBlock
        {
            Text = LocalizationService.Get("Language_Label"),
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(4, 0, 0, 6)
        };
        _languageComboBox = new ComboBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = "LanguageSelector"
        };

        var options = new[]
        {
            new LanguageOption(AppLanguageMode.System, LocalizationService.GetLanguageDisplayName(AppLanguageMode.System)),
            new LanguageOption(AppLanguageMode.Korean, LocalizationService.GetLanguageDisplayName(AppLanguageMode.Korean)),
            new LanguageOption(AppLanguageMode.English, LocalizationService.GetLanguageDisplayName(AppLanguageMode.English))
        };
        _languageComboBox.ItemsSource = options;
        _languageComboBox.DisplayMemberPath = nameof(LanguageOption.DisplayName);
        _languageComboBox.SelectedItem = options.First(item => item.Mode == LocalizationService.Mode);
        _languageComboBox.SelectionChanged += LanguageComboBox_SelectionChanged;

        navigation.Children.Add(separator);
        navigation.Children.Add(label);
        navigation.Children.Add(_languageComboBox);
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_languageComboBox?.SelectedItem is not LanguageOption option || option.Mode == LocalizationService.Mode) return;

        try
        {
            LocalizationService.SaveMode(option.Mode);
            MessageBox.Show(
                this,
                LocalizationService.Get("Language_RestartNotice"),
                LocalizationService.Get("Language_ChangeTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Get("Language_ChangeTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed record LanguageOption(AppLanguageMode Mode, string DisplayName);
}
