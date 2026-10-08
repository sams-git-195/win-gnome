using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WinGnome.Features.Settings.ViewModels;

namespace WinGnome.Features.Settings.Views;

internal sealed partial class StreamlinePage : UserControl
{
    public StreamlinePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>When another page linked to a tweak group, scrolls that group into sight once the page has been laid out.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StreamlinePageViewModel { PendingGroup: { } category } page)
        {
            return;
        }

        page.PendingGroup = null;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            var group = page.Groups.FirstOrDefault(g => g.Category == category);
            if (group is not null)
            {
                (GroupsHost.ItemContainerGenerator.ContainerFromItem(group) as FrameworkElement)?.BringIntoView();
            }
        });
    }
}
