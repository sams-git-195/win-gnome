using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinGnome.Controls.TrafficLights;
using WinGnome.Features.Settings.ViewModels;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Views;

/// <summary>Modal list of installed apps with a search box; the chosen app is read from the view model.</summary>
internal sealed partial class AppPickerWindow : Window
{
    private readonly AppPickerViewModel _viewModel;

    public AppPickerWindow(AppPickerViewModel viewModel, SettingsService settings, bool isDark)
    {
        InitializeComponent();
        HeaderBarWindow.Attach(this, Root, settings, closeOnly: true);
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) => TitleBarTheme.Apply(this, isDark);
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.HasSelection && e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(AppList, source) is ListBoxItem)
        {
            DialogResult = true;
        }
    }

    /// <summary>Down arrow in the search box moves into the list, so the keyboard alone can pick an app.</summary>
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && AppList.Items.Count > 0)
        {
            AppList.Focus();
            if (AppList.ItemContainerGenerator.ContainerFromIndex(AppList.SelectedIndex < 0 ? 0 : AppList.SelectedIndex) is ListBoxItem item)
            {
                item.Focus();
            }

            e.Handled = true;
        }
    }
}
