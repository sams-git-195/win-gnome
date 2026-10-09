using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinGnome.Controls.TrafficLights;
using WinGnome.Features.Settings.Tweaks;
using WinGnome.Features.Settings.ViewModels;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services.Apps;

namespace WinGnome.Features.Settings.Views;

/// <summary>The settings window: a sidebar of pages and the selected page. Every change applies live.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The view model is disposed in OnClosed, which is the end of a window's life; the header bar disposes itself when the window closes.")]
internal sealed partial class SettingsWindow : Window, IDialogService
{
    private readonly ShellContext _context;
    private readonly SettingsWindowViewModel _viewModel;
    private readonly HeaderBarWindow _headerBar;

    public SettingsWindow(ShellContext context, TweakService tweaks)
    {
        _context = context;
        InitializeComponent();
        _headerBar = HeaderBarWindow.Attach(this, Root, context.Settings, closeOnly: false);
        _headerBar.ButtonsArranged += OnHeaderButtonsArranged;
        _viewModel = new SettingsWindowViewModel(context, tweaks, this);
        DataContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        context.Theme.ThemeChanged += OnThemeChanged;
        SourceInitialized += (_, _) => TitleBarTheme.Apply(this, context.Theme.IsDark);
        Closing += (_, _) => _viewModel.Flush();
        Closed += OnClosed;
    }

    /// <summary>Brings the window to the front, restoring it if it was minimised.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        WindowActivator.Activate(new WindowInteropHelper(this).Handle);
    }

    /// <summary>
    /// Self-test: shows every page once and checks that its view was created. Throws when a page fails to build,
    /// which the application counts as a self-test failure.
    /// </summary>
    public void VisitAllPages()
    {
        foreach (var entry in _viewModel.Entries.Where(e => e.Page is not null))
        {
            _viewModel.SelectedEntry = entry;
            UpdateLayout();
            if (!PageViewIsShowing())
            {
                throw new InvalidOperationException($"The \"{entry.Title}\" settings page did not create its view.");
            }
        }

        if (_headerBar.ProbeSnapLayoutsHitTest() == false)
        {
            throw new InvalidOperationException("The settings header bar does not answer HTMAXBUTTON over its maximise circle (no Snap Layouts).");
        }

        var picker = new AppPickerViewModel(_context.Apps, _context.Icons, []);
        try
        {
            var dialog = new AppPickerWindow(picker, _context.Settings, _context.Theme.IsDark) { Owner = this };
            dialog.Show();
            dialog.UpdateLayout();
            dialog.Close();
        }
        finally
        {
            picker.Dispose();
        }
    }

    /// <summary>Shows a panel by id (see <c>PanelIds</c>).</summary>
    public void ShowPanel(string panelId) => _viewModel.ShowPanel(panelId);

    bool IDialogService.Confirm(string title, string message, string confirmLabel, bool isDestructive)
    {
        var dialog = new ConfirmDialog(title, message, confirmLabel, isDestructive, _context.Theme.IsDark) { Owner = this };
        return dialog.ShowDialog() == true;
    }

    AppEntry? IDialogService.PickApp(IReadOnlyCollection<string> hiddenLaunchIds)
    {
        using var picker = new AppPickerViewModel(_context.Apps, _context.Icons, hiddenLaunchIds);
        var dialog = new AppPickerWindow(picker, _context.Settings, _context.Theme.IsDark) { Owner = this };
        return dialog.ShowDialog() == true ? picker.Chosen : null;
    }

    private bool PageViewIsShowing() =>
        VisualTreeHelper.GetChildrenCount(PageHost) > 0
        && VisualTreeHelper.GetChild(PageHost, 0) is ContentPresenter presenter
        && VisualTreeHelper.GetChildrenCount(presenter) > 0
        && VisualTreeHelper.GetChild(presenter, 0) is UserControl;

    /// <summary>
    /// The list's selection drives the view model by hand (its binding is one-way): a two-way binding re-reads the
    /// view model while the list is still selecting, and when a link is refused that leaves the link's row looking
    /// selected. A link opens Windows Settings and the highlight then returns to the page that is showing.
    /// </summary>
    private void OnSidebarSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems is not [SidebarEntry entry])
        {
            return;
        }

        _viewModel.SelectedEntry = entry;
        if (entry.IsLink)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input,
                () => SidebarList.SetCurrentValue(Selector.SelectedItemProperty, _viewModel.SelectedEntry));
        }
    }
    /// <summary>Escape clears the search; Enter opens the best match; Down moves into the results.</summary>
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SidebarList.Items.Count > 0 && SidebarList.Items[0] is SidebarEntry best)
        {
            _viewModel.SelectedEntry = best;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.Text.Length > 0)
        {
            SearchBox.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && SidebarList.Items.Count > 0)
        {
            var target = SidebarList.SelectedIndex >= 0 ? SidebarList.SelectedIndex : 0;
            (SidebarList.ItemContainerGenerator.ContainerFromIndex(target) as UIElement)?.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Ctrl+F jumps to the search entry, as in GNOME Settings.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsWindowViewModel.SelectedPage))
        {
            PageScroller.ScrollToTop();
        }
    }

    /// <summary>With the buttons on the left, centre the sidebar title in the space to their right.</summary>
    private void OnHeaderButtonsArranged(object? sender, EventArgs e)
    {
        var buttons = _headerBar.ButtonsBounds;
        var onSidebar = !buttons.IsEmpty && buttons.Left < Root.ColumnDefinitions[0].ActualWidth;
        SidebarTitle.Margin = onSidebar ? new Thickness(buttons.Right, 0, 0, 0) : default;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => TitleBarTheme.Apply(this, _context.Theme.IsDark);

    private void OnClosed(object? sender, EventArgs e)
    {
        _headerBar.ButtonsArranged -= OnHeaderButtonsArranged;
        _context.Theme.ThemeChanged -= OnThemeChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
    }
}
