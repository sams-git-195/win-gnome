using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WinGnome.Features.Settings.Tweaks;
using WinGnome.Features.Settings.ViewModels;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services.Apps;

namespace WinGnome.Features.Settings.Views;

/// <summary>The settings window: a sidebar of pages and the selected page. Every change applies live.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The view model is disposed in OnClosed, which is the end of a window's life.")]
internal sealed partial class SettingsWindow : Window, IDialogService
{
    private readonly ShellContext _context;
    private readonly SettingsWindowViewModel _viewModel;

    public SettingsWindow(ShellContext context, TweakService tweaks)
    {
        _context = context;
        InitializeComponent();
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
        foreach (var page in _viewModel.Pages)
        {
            _viewModel.SelectedPage = page;
            UpdateLayout();
            if (!PageViewIsShowing())
            {
                throw new InvalidOperationException($"The \"{page.Title}\" settings page did not create its view.");
            }
        }

        var picker = new AppPickerViewModel(_context.Apps, _context.Icons, []);
        try
        {
            var dialog = new AppPickerWindow(picker, _context.Theme.IsDark) { Owner = this };
            dialog.Show();
            dialog.UpdateLayout();
            dialog.Close();
        }
        finally
        {
            picker.Dispose();
        }
    }

    bool IDialogService.Confirm(string title, string message, string confirmLabel, bool isDestructive)
    {
        var dialog = new ConfirmDialog(title, message, confirmLabel, isDestructive, _context.Theme.IsDark) { Owner = this };
        return dialog.ShowDialog() == true;
    }

    AppEntry? IDialogService.PickApp(IReadOnlyCollection<string> hiddenLaunchIds)
    {
        using var picker = new AppPickerViewModel(_context.Apps, _context.Icons, hiddenLaunchIds);
        var dialog = new AppPickerWindow(picker, _context.Theme.IsDark) { Owner = this };
        return dialog.ShowDialog() == true ? picker.Chosen : null;
    }

    private bool PageViewIsShowing() =>
        VisualTreeHelper.GetChildrenCount(PageHost) > 0
        && VisualTreeHelper.GetChild(PageHost, 0) is ContentPresenter presenter
        && VisualTreeHelper.GetChildrenCount(presenter) > 0
        && VisualTreeHelper.GetChild(presenter, 0) is UserControl;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsWindowViewModel.SelectedPage))
        {
            PageScroller.ScrollToTop();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => TitleBarTheme.Apply(this, _context.Theme.IsDark);

    private void OnClosed(object? sender, EventArgs e)
    {
        _context.Theme.ThemeChanged -= OnThemeChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
    }
}
