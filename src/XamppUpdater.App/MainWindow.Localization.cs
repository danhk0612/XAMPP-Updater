using System.Diagnostics;
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

        var componentAnchor = _phpMyAdminNavButton ?? MariaDbNavButton;
        var insertIndex = navigation.Children.IndexOf(componentAnchor) + 1;
        navigation.Children.Insert(insertIndex++, separator);
        navigation.Children.Insert(insertIndex++, label);
        navigation.Children.Insert(insertIndex, _languageComboBox);
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_languageComboBox?.SelectedItem is not LanguageOption option || option.Mode == LocalizationService.Mode) return;

        try
        {
            LocalizationService.SaveMode(option.Mode);
            RestartApplication();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Get("Language_ChangeTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void RestartApplication()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            throw new InvalidOperationException("현재 실행 파일 경로를 확인할 수 없어 자동 재시작할 수 없습니다.");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true
        });

        Application.Current.Shutdown();
    }

    private sealed record LanguageOption(AppLanguageMode Mode, string DisplayName);
}
