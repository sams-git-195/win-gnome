using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinGnome.Core.Geometry;
using WinGnome.Core.Overview;
using WinGnome.Core.Search;
using WinGnome.Core.Settings;
using WinGnome.Core.Windows;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Overview;

/// <summary>
/// The full-screen Activities overview: window thumbnails or the app grid, plus type-to-search.
/// </summary>
/// <remarks>
/// Created once and then shown and hidden, which keeps opening well under the 150 ms budget. Unlike the
/// top bar and dock it is a normal activatable window, because it must receive the user's typing. It
/// remembers which window was focused before it opened and gives focus back when dismissed without a
/// choice.
/// <para>
/// Opening and closing are animated (spec 0008): thumbnails glide between the windows and the grid while the
/// dim layer eases in or out. The window is prepared while cloaked and revealed once WPF has drawn its first
/// frame, so the backdrop never shows without its content. The acrylic blur itself can't fade.
/// </para>
/// </remarks>
internal sealed partial class OverviewWindow : Window
{
    /// <summary>Width and height of an app tile in DIPs; matches AppTileTemplate in the XAML.</summary>
    public const double AppTileSize = 136;

    private const int MaxAppResults = 24;
    private const int MaxWindowResults = 8;
    private const int MinGridColumns = 6;
    private const int MaxGridColumns = 8;

    /// <summary>Thumbnail area margins (DIPs): below the search box, at the sides and at the bottom.</summary>
    private const double ContentTop = 112;
    private const double SideMargin = 48;
    private const double BottomMargin = 32;

    /// <summary>How much of <see cref="ActivitiesSettings.BackdropOpacity"/> turns into black over the blur.</summary>
    private const double DimStrength = 0.6;

    private readonly ShellContext _context;
    private readonly AppTileCatalog _apps;
    private readonly ThumbnailLayer _thumbnails;
    private readonly OverviewAnimator _animator;
    private readonly SolidColorBrush _dimBrush = new(Colors.Transparent);
    private readonly List<SelectableItem> _results = [];
    private OverviewRequest _request = new(OverviewMode.Windows);
    private nint _hwnd;
    private nint _previousForeground;
    private PixelRect _bounds;
    private double _scale = 1;
    private int _gridColumns = MinGridColumns;
    private int _resultIndex = -1;
    private int _gridIndex = -1;
    private bool _thumbnailsShown;
    private bool _closing;
    private double _dimLevel;
    private bool _activating;
    private bool _allowClose;
    private bool _closed;

    public OverviewWindow(ShellContext context, AppTileCatalog apps)
    {
        _context = context;
        _apps = apps;
        InitializeComponent();

        _thumbnails = new ThumbnailLayer(ThumbnailCanvas, context.Icons);
        _thumbnails.WindowActivated += (_, hwnd) => ActivateWindow(hwnd);
        _thumbnails.WindowCloseRequested += (_, hwnd) => WindowActivator.Close(hwnd);
        _animator = new OverviewAnimator(Dispatcher);
        Dimmer.Fill = _dimBrush;

        AppGrid.ItemsSource = _apps.Tiles;
        _apps.Changed += OnAppsChanged;

        // Create the native window now (hidden) so the first open is as fast as every other one.
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        var exStyle = NativeMethods.GetExStyle(_hwnd) | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, (nint)exStyle);
        OverviewBackdrop.Apply(this);

        // The overview animates itself; Windows' own show/hide fade would run on top of it.
        var disabled = 1;
        var hr = NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, ref disabled, sizeof(int));
        if (hr < 0)
        {
            Log.Warn($"Overview: DWMWA_TRANSITIONS_FORCEDISABLED failed (hr=0x{hr:X8})");
        }
    }

    /// <summary>True while the overview is on screen.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>True when <paramref name="request"/> asks for exactly what is already shown (so it should toggle closed).</summary>
    public bool IsShowing(OverviewRequest request) =>
        IsOpen
        && request.Mode == _request.Mode
        && (request.OnlyWindows ?? []).SequenceEqual(_request.OnlyWindows ?? []);

    /// <summary>Opens the overview, or switches an open overview to another mode.</summary>
    /// <remarks>While the overview is closing, a request for the same windows view turns the close around.</remarks>
    public void Open(OverviewRequest request, ActivitiesSettings settings)
    {
        var previous = _request;
        _request = request;
        _dimLevel = settings.BackdropOpacity * DimStrength;
        if (_closing)
        {
            if (request.Mode == OverviewMode.Windows && SameWindows(previous, request))
            {
                Reopen();
                return;
            }

            FinishClosing();
        }

        if (IsOpen)
        {
            // Switching modes: no transition; a running one carries on, using the new dim level.
            ClearQuery();
            ShowModeContent(fromWindows: false);
            if (!_animator.IsRunning)
            {
                SetDim(_dimLevel);
            }

            return;
        }

        var requestedAt = Stopwatch.GetTimestamp();
        var animate = NativeMethods.AreClientAreaAnimationsEnabled();
        SetCloaked(true);

        // The first visible frame is the one WPF drew while cloaked: the start of the glide, or the end if instant.
        SetDim(animate ? 0 : _dimLevel);
        ShowOnPrimaryMonitor();
        ClearQuery();
        ShowModeContent(fromWindows: animate);

        // Last, so the reveal timeout doesn't count the preparation above; no frame renders before Open returns.
        _animator.Run(opening: true, animate, OnTransitionFrame, OnOpened, reveal: () => SetCloaked(false), requestedAt);
    }

    /// <summary>
    /// Hides the overview and releases every thumbnail. When <paramref name="restoreFocus"/> is set, focus
    /// goes back to the window that had it before the overview opened.
    /// </summary>
    public void Dismiss(bool restoreFocus)
    {
        if (!IsOpen)
        {
            return;
        }

        var previous = _previousForeground;
        DismissAndFocus(restoreFocus && previous != 0 && NativeMethods.IsWindow(previous) ? previous : 0);
    }

    /// <summary>
    /// Hides the overview and gives the foreground to <paramref name="focusTarget"/> (0: leave it to Windows).
    /// </summary>
    /// <remarks>
    /// The target is activated before the overview hides: while the overview is still the foreground window,
    /// Windows lets us hand the foreground on. Hiding first would let Windows activate whatever window is next
    /// in z-order, briefly flashing it to the front and leaving us to fight the foreground lock.
    /// <see cref="IsOpen"/> is cleared first, so the deactivation this causes is not treated as a dismissal.
    /// </remarks>
    private void DismissAndFocus(nint focusTarget)
    {
        if (!IsOpen)
        {
            if (focusTarget != 0)
            {
                WindowActivator.Activate(focusTarget);
            }

            return;
        }

        IsOpen = false;
        _previousForeground = 0;
        _context.Windows.WindowsChanged -= OnWindowsChanged;

        if (focusTarget != 0)
        {
            WindowActivator.Activate(focusTarget);
        }

        if (!CanAnimateClose())
        {
            FinishClosing();
            return;
        }

        // Input is ignored until hidden (see OnPreviewKeyDown); only Super or the hot corner turn it around (Open).
        // Interrupting the opening glide only travels back the distance already covered (OverviewTransitionState).
        _closing = true;
        Root.IsHitTestVisible = false;
        _thumbnails.BeginClosing(focusTarget);
        _animator.Run(opening: false, animationsEnabled: true, OnTransitionFrame, FinishClosing);
    }

    /// <summary>
    /// Glide back only from the window grid (not from search results or the app grid), once the overview has been
    /// revealed, and not while shutting down.
    /// </summary>
    private bool CanAnimateClose() =>
        !_closed
        && !_allowClose
        && !_animator.IsWaitingToReveal
        && _thumbnailsShown
        && _thumbnails.IsVisible
        && NativeMethods.AreClientAreaAnimationsEnabled();

    /// <summary>Hides the overview straight away and releases every thumbnail.</summary>
    private void FinishClosing()
    {
        _closing = false;
        Root.IsHitTestVisible = true;
        if (!_closed)
        {
            Hide();

            // A hidden WPF window keeps its full-screen back buffer (about 15 MB at 2560x1440, plus scratch layers).
            // Shrinking it while hidden releases that; ShowOnPrimaryMonitor sizes it again before the next show.
            NativeMethods.SetWindowPos(_hwnd, 0, _bounds.Left, _bounds.Top, 1, 1,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }

        // After hiding: stopping an opening that is still waiting uncloaks the window, which must not flash.
        _animator.Reset();
        _thumbnails.Clear();
        _thumbnailsShown = false;
        ClearQuery();
        SetResults([], []);
        SelectGridItem(-1);
    }

    /// <summary>Turns a closing overview around: it takes focus again and the thumbnails head back to the grid.</summary>
    private void Reopen()
    {
        _closing = false;
        Root.IsHitTestVisible = true;
        RememberForeground();
        _activating = true;
        try
        {
            IsOpen = true;

            // The picked window was brought to the top when the close began; topmost again, as on every open.
            PlaceOn(_bounds);
            TakeFocus();
        }
        finally
        {
            _activating = false;
        }

        // Window changes during the close weren't watched; the state applies them once the grid settles (OnOpened).
        _context.Windows.WindowsChanged += OnWindowsChanged;
        _thumbnails.BeginReopening();
        _animator.Run(opening: true, animationsEnabled: true, OnTransitionFrame, OnOpened);
    }

    private void OnTransitionFrame(TransitionFrame frame)
    {
        // The dim follows the position, so it stays continuous through reversals and picks up a new level at once.
        SetDim(_dimLevel * frame.Position);
        _thumbnails.SetProgress(frame.SegmentEased);
    }

    private void OnOpened()
    {
        var windowsChanged = _animator.State.TakeWindowChanges();
        if (!_thumbnailsShown)
        {
            return;
        }

        _thumbnails.CompleteOpening();
        if (windowsChanged)
        {
            _thumbnails.Update(GetWindows());
        }
    }

    private void SetDim(double opacity)
    {
        var alpha = OverviewTransition.DimAlpha(opacity, opacity, 1);
        _dimBrush.Color = Color.FromArgb(alpha, 0, 0, 0);
    }

    /// <summary>
    /// Cloaking hides the shown window from the screen while WPF draws its first frame. If cloaking fails the
    /// overview just appears uncloaked, as before.
    /// </summary>
    private void SetCloaked(bool cloaked)
    {
        var value = cloaked ? 1 : 0;
        var hr = NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_CLOAK, ref value, sizeof(int));
        if (hr < 0)
        {
            Log.Warn($"Overview: DWMWA_CLOAK={value} failed (hr=0x{hr:X8})");
        }
    }

    private static bool SameWindows(OverviewRequest a, OverviewRequest b) =>
        (a.OnlyWindows ?? []).SequenceEqual(b.OnlyWindows ?? []);

    /// <summary>Closes the window for good (shutdown).</summary>
    public void Destroy()
    {
        _allowClose = true;
        Dismiss(restoreFocus: false);
        if (_closing)
        {
            FinishClosing();
        }

        _apps.Changed -= OnAppsChanged;
        if (!_closed)
        {
            Close();
        }
    }

    /// <summary>Remembers who had focus, unless it is one of WinGnome's own windows (they manage themselves).</summary>
    private void RememberForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        _previousForeground = NativeMethods.GetProcessId(foreground) == NativeMethods.GetCurrentProcessId() ? 0 : foreground;
    }

    private void TakeFocus()
    {
        // We usually own the foreground right now (hotkey, click on the dock or top bar), but not after a
        // hot-corner dwell; WindowActivator works around the foreground lock in that case.
        if (NativeMethods.GetForegroundWindow() != _hwnd)
        {
            WindowActivator.Activate(_hwnd);
        }

        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void ShowOnPrimaryMonitor()
    {
        RememberForeground();
        var (monitor, _) = NativeMethods.GetPrimaryMonitorRects();
        _bounds = monitor;

        _activating = true;
        try
        {
            // Topmost again on every open: the top bar and dock are topmost too, and the last one wins.
            PlaceOn(monitor);
            if (NativeMethods.GetWindowBounds(_hwnd) != monitor)
            {
                // Moving onto a monitor with another DPI makes WPF resize the window to keep its DIP size
                // (WM_DPICHANGED); now that the DPI matches, the second move sticks.
                PlaceOn(monitor);
            }

            _scale = NativeMethods.GetWindowScale(_hwnd);
            UpdateGridColumns();

            IsOpen = true;
            Show();
            TakeFocus();
        }
        finally
        {
            _activating = false;
        }

        _context.Windows.WindowsChanged += OnWindowsChanged;
    }

    private void PlaceOn(PixelRect monitor) =>
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, monitor.Left, monitor.Top, monitor.Width, monitor.Height,
            NativeMethods.SWP_NOACTIVATE);

    private void UpdateGridColumns()
    {
        var widthDip = _bounds.Width / _scale;
        _gridColumns = Math.Clamp((int)((widthDip - (2 * SideMargin)) / AppTileSize), MinGridColumns, MaxGridColumns);
        AppGrid.Width = _gridColumns * AppTileSize;
        ResultsPanel.Width = _gridColumns * AppTileSize;
        _apps.IconSizePx = (int)Math.Round(64 * _scale);
    }

    /// <summary>Shows thumbnails or the app grid for the current mode (search results take over while typing).</summary>
    private void ShowModeContent(bool fromWindows)
    {
        ResultsScroller.Visibility = Visibility.Collapsed;
        if (_request.Mode == OverviewMode.Applications)
        {
            _thumbnails.Clear();
            _thumbnailsShown = false;
            AppGridScroller.Visibility = Visibility.Visible;
            AppGridScroller.ScrollToTop();
            _apps.LoadAllIcons();
            return;
        }

        AppGridScroller.Visibility = Visibility.Collapsed;
        SelectGridItem(-1);
        if (_thumbnailsShown)
        {
            _thumbnails.SetVisible(true);
            return;
        }

        var size = new LayoutSize(_bounds.Width / _scale, _bounds.Height / _scale);
        var area = new LayoutRect(SideMargin, ContentTop, size.Width - (2 * SideMargin), size.Height - ContentTop - BottomMargin);
        _thumbnails.Show(_hwnd, _bounds, _scale, area, GetWindows(), fromWindows);
        _thumbnailsShown = true;
    }

    /// <summary>Task windows to show: all of them, or just the ones the request named (dock previews).</summary>
    private List<WindowInfo> GetWindows()
    {
        var windows = _context.Windows.Windows;
        if (_request.OnlyWindows is not { } only)
        {
            return [.. windows];
        }

        var wanted = only.ToHashSet();
        return windows.Where(w => wanted.Contains(w.Handle)).ToList();
    }

    private void OnWindowsChanged(object? sender, EventArgs e)
    {
        if (!IsOpen)
        {
            return;
        }

        if (_thumbnailsShown)
        {
            // Rearranging mid-glide would make thumbnails jump; the grid catches up once it has settled (OnOpened).
            if (_animator.State.NoteWindowsChanged())
            {
                _thumbnails.Update(GetWindows());
            }
        }

        if (ResultsScroller.Visibility == Visibility.Visible)
        {
            RunSearch(SearchBox.Text.Trim());
        }
    }

    private void OnAppsChanged(object? sender, EventArgs e)
    {
        // Tiles survive a catalogue refresh, so the old highlight must be cleared by hand: _gridIndex now
        // points into the new list.
        foreach (var tile in _apps.Tiles)
        {
            tile.IsSelected = false;
        }

        AppGrid.ItemsSource = _apps.Tiles;
        _gridIndex = -1;
        if (IsOpen && ResultsScroller.Visibility == Visibility.Visible)
        {
            RunSearch(SearchBox.Text.Trim());
        }
    }

    // ---- Search ---------------------------------------------------------------------------------

    private void ClearQuery()
    {
        SearchBox.Clear();
        Placeholder.Visibility = Visibility.Visible;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        Placeholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!IsOpen)
        {
            return;
        }

        if (query.Length == 0)
        {
            SetResults([], []);
            ShowModeContent(fromWindows: false);
            return;
        }

        _thumbnails.SetVisible(false);
        AppGridScroller.Visibility = Visibility.Collapsed;
        ResultsScroller.Visibility = Visibility.Visible;
        ResultsScroller.ScrollToTop();
        RunSearch(query);
    }

    private void RunSearch(string query)
    {
        var apps = FuzzyMatcher.Rank(_apps.Tiles, t => t.Name, query, MaxAppResults);
        _apps.LoadIconsNow(apps);

        var iconPx = (int)Math.Round(24 * _scale);
        var windows = FuzzyMatcher.Rank(GetWindows(), w => w.Title + " " + _context.Windows.GetAppName(w), query, MaxWindowResults)
            .Select(w => new WindowResult(w.Handle, w.Title, _context.Windows.GetAppName(w), _context.Icons.GetWindowIcon(w.Handle, w.ProcessPath, iconPx)))
            .ToList();

        SetResults(apps, windows);
    }

    private void SetResults(IReadOnlyList<AppTile> apps, IReadOnlyList<WindowResult> windows)
    {
        SelectResult(-1);
        _results.Clear();
        _results.AddRange(apps);
        _results.AddRange(windows);

        AppResults.ItemsSource = apps;
        WindowResults.ItemsSource = windows;
        AppsHeader.Visibility = apps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WindowsHeader.Visibility = windows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoResults.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // GNOME highlights the top hit so Enter opens it straight away.
        SelectResult(_results.Count > 0 ? 0 : -1);
    }

    private void SelectResult(int index)
    {
        if (_resultIndex >= 0 && _resultIndex < _results.Count)
        {
            _results[_resultIndex].IsSelected = false;
        }

        _resultIndex = index;
        if (index >= 0 && index < _results.Count)
        {
            _results[index].IsSelected = true;
            BringItemIntoView(_results[index]);
        }
    }

    private void SelectGridItem(int index)
    {
        var tiles = _apps.Tiles;
        if (_gridIndex >= 0 && _gridIndex < tiles.Count)
        {
            tiles[_gridIndex].IsSelected = false;
        }

        _gridIndex = index;
        if (index >= 0 && index < tiles.Count)
        {
            tiles[index].IsSelected = true;
            (AppGrid.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement)?.BringIntoView();
        }
    }

    private void BringItemIntoView(SelectableItem item)
    {
        var container = item is AppTile
            ? AppResults.ItemContainerGenerator.ContainerFromItem(item)
            : WindowResults.ItemContainerGenerator.ContainerFromItem(item);
        (container as FrameworkElement)?.BringIntoView();
    }

    private void OpenItem(SelectableItem item)
    {
        switch (item)
        {
            case AppTile tile:
                Launch(tile);
                break;
            case WindowResult window:
                ActivateWindow(window.Handle);
                break;
        }
    }

    // ---- Actions --------------------------------------------------------------------------------

    private void ActivateWindow(nint hwnd) => DismissAndFocus(hwnd);

    private void Launch(AppTile tile)
    {
        // Launch while the overview still owns the foreground, so the new app is allowed to take it
        // (AllowSetForegroundWindow only works for the foreground process).
        _context.Launcher.Launch(tile.LaunchId);
        Dismiss(restoreFocus: false);
    }

    private bool IsPinned(AppTile tile) =>
        _context.Settings.Current.Dock.PinnedApps.Any(p => string.Equals(p.LaunchId, tile.LaunchId, StringComparison.OrdinalIgnoreCase));

    private void SetPinned(AppTile tile, bool pinned)
    {
        _context.Settings.Update(settings =>
        {
            var pins = settings.Dock.PinnedApps;
            pins.RemoveAll(p => string.Equals(p.LaunchId, tile.LaunchId, StringComparison.OrdinalIgnoreCase));
            if (pinned)
            {
                pins.Add(new PinnedApp { Name = tile.Name, LaunchId = tile.LaunchId });
            }
        });
    }

    private ContextMenu BuildAppMenu(AppTile tile)
    {
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => Launch(tile);
        menu.Items.Add(open);
        menu.Items.Add(new Separator());

        var pinned = IsPinned(tile);
        var pin = new MenuItem { Header = pinned ? "Unpin from dock" : "Pin to dock" };
        pin.Click += (_, _) => SetPinned(tile, !pinned);
        menu.Items.Add(pin);
        return menu;
    }

    // ---- Input ----------------------------------------------------------------------------------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_closing)
        {
            // Closing ignores the keyboard (handled key-downs produce no text input either).
            e.Handled = true;
            return;
        }

        var resultsShown = ResultsScroller.Visibility == Visibility.Visible;
        switch (e.Key)
        {
            case Key.Escape:
                if (SearchBox.Text.Length > 0)
                {
                    ClearQuery();
                }
                else
                {
                    Dismiss(restoreFocus: true);
                }

                break;

            case Key.Enter:
                OpenSelection(resultsShown);
                break;

            case Key.Tab:
                if (resultsShown && _results.Count > 0)
                {
                    var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? _results.Count - 1 : 1;
                    SelectResult((Math.Max(_resultIndex, 0) + step) % _results.Count);
                }

                break;

            case Key.Left or Key.Right or Key.Up or Key.Down:
                MoveSelection(ToDirection(e.Key), resultsShown);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private void OpenSelection(bool resultsShown)
    {
        if (resultsShown)
        {
            if (_resultIndex >= 0 && _resultIndex < _results.Count)
            {
                OpenItem(_results[_resultIndex]);
            }
        }
        else if (_request.Mode == OverviewMode.Applications)
        {
            if (_gridIndex >= 0 && _gridIndex < _apps.Tiles.Count)
            {
                Launch(_apps.Tiles[_gridIndex]);
            }
        }
        else if (_thumbnails.SelectedWindow is var hwnd and not 0)
        {
            ActivateWindow(hwnd);
        }
    }

    private void MoveSelection(NavigationDirection direction, bool resultsShown)
    {
        if (resultsShown)
        {
            // Results are one list (apps, then windows): left/up go back, right/down go forward.
            if (_results.Count > 0)
            {
                var delta = direction is NavigationDirection.Left or NavigationDirection.Up ? -1 : 1;
                SelectResult(Math.Clamp(_resultIndex + delta, 0, _results.Count - 1));
            }
        }
        else if (_request.Mode == OverviewMode.Applications)
        {
            SelectGridItem(SelectionNavigator.MoveInGrid(_gridIndex, _apps.Tiles.Count, _gridColumns, direction));
        }
        else
        {
            _thumbnails.MoveSelection(direction);
        }
    }

    private static NavigationDirection ToDirection(Key key) => key switch
    {
        Key.Left => NavigationDirection.Left,
        Key.Right => NavigationDirection.Right,
        Key.Up => NavigationDirection.Up,
        _ => NavigationDirection.Down,
    };

    private void OnAppTileClick(object sender, RoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is AppTile tile)
        {
            e.Handled = true;
            Launch(tile);
        }
    }

    private void OnWindowResultClick(object sender, RoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is WindowResult window)
        {
            e.Handled = true;
            ActivateWindow(window.Handle);
        }
    }

    private void OnAppTileRightClick(object sender, MouseButtonEventArgs e)
    {
        var dataContext = e.OriginalSource switch
        {
            FrameworkElement element => element.DataContext,
            FrameworkContentElement element => element.DataContext,
            _ => null,
        };
        if (dataContext is not AppTile tile)
        {
            return;
        }

        e.Handled = true;
        var menu = BuildAppMenu(tile);
        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void OnSearchPillMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SearchBox.Focus();
    }

    /// <summary>A click that no thumbnail, tile or button claimed landed on the empty backdrop.</summary>
    private void OnBackgroundMouseDown(object sender, MouseButtonEventArgs e) => Dismiss(restoreFocus: true);

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Another window took focus (Start, a notification, a click on another monitor): get out of the way.
        if (IsOpen && !_activating)
        {
            Dismiss(restoreFocus: false);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Application shutdown closes every window, possibly before the feature is disposed.
        _closed = true;
        Dismiss(restoreFocus: false);
        if (_closing)
        {
            FinishClosing();
        }

        _animator.Stop();
        base.OnClosed(e);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Alt+F4 must only dismiss: the window is reused for the lifetime of the app.
        if (!_allowClose)
        {
            e.Cancel = true;
            Dismiss(restoreFocus: true);
        }
    }
}
