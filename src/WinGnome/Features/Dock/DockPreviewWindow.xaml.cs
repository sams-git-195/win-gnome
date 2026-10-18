using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinGnome.Core.Dock;
using WinGnome.Core.Geometry;
using WinGnome.Interop;

namespace WinGnome.Features.Dock;

/// <summary>
/// The window preview popover beside the dock (spec 0024): a non-activating, topmost, non-layered window that draws
/// the items <see cref="DockPreviewLayout"/> placed and owns the live DWM thumbnails composited into it. It decides
/// nothing; <see cref="DockPreviewPresenter"/> tells it what to show and handles its events.
/// </summary>
internal sealed partial class DockPreviewWindow : Window
{
    private const double TitleFontSize = 12;
    private const double StatusFontSize = 11;
    private const double CloseFontSize = 10;

    /// <summary>Space between an ellipsised title and the ×.</summary>
    private const double TitleCloseGap = 4;

    /// <summary>Largest app icon shown in a thumbnail box without a picture.</summary>
    private const double PlaceholderIconDip = 48;

    private readonly List<DwmThumbnail?> _thumbnails = [];
    private readonly List<ItemParts> _parts = [];
    private HwndSource? _source;
    private PixelRect _bounds;

    public DockPreviewWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Handle = new WindowInteropHelper(this).EnsureHandle();
    }

    /// <summary>A window's item was clicked (left button): focus it.</summary>
    public event EventHandler<nint>? WindowChosen;

    /// <summary>A window's × or a middle click on its item: close it. Never raised for elevated windows.</summary>
    public event EventHandler<DockPreviewItem>? CloseRequested;

    /// <summary>The "+N more" item was clicked.</summary>
    public event EventHandler? OverflowChosen;

    /// <summary>The popover's HWND, which the thumbnails are drawn into.</summary>
    public nint Handle { get; }

    /// <summary>
    /// Registers one thumbnail per item (releasing the previous ones) and returns their source sizes, null where DWM
    /// has no picture or registration failed. They stay invisible until <see cref="Render"/> places them.
    /// </summary>
    public IReadOnlyList<DockPreviewSourceSize?> RegisterThumbnails(IReadOnlyList<DockPreviewItem> items)
    {
        ReleaseThumbnails();
        var sizes = new List<DockPreviewSourceSize?>(items.Count);
        foreach (var item in items)
        {
            var thumbnail = DwmThumbnail.TryRegister(Handle, item.Handle);
            _thumbnails.Add(thumbnail);
            sizes.Add(thumbnail?.QuerySourceSize() is { } size ? new DockPreviewSourceSize(size.Width, size.Height) : null);
        }

        return sizes;
    }

    /// <summary>
    /// Builds the items for <paramref name="plan"/> (one per entry of <c>plan.Items</c>, then "+N more") and shows the
    /// thumbnails at their rectangles. Thumbnails of placeholders and of windows past the overflow are released.
    /// </summary>
    /// <param name="appName">The app's name, for the window's accessible name.</param>
    /// <param name="icon">The app's icon, shown in boxes without a picture.</param>
    /// <param name="items">What <see cref="RegisterThumbnails"/> was given.</param>
    /// <param name="plan">The layout, in physical pixels relative to the popover.</param>
    /// <param name="scale">The DPI scale of the popover's monitor.</param>
    public void Render(string appName, ImageSource? icon, IReadOnlyList<DockPreviewItem> items, DockPreviewPlan plan, double scale)
    {
        Title = appName + " windows";
        ItemsCanvas.Children.Clear();
        _parts.Clear();
        ItemsCanvas.Width = plan.Popover.Width / scale;
        ItemsCanvas.Height = plan.Popover.Height / scale;

        for (var i = 0; i < _thumbnails.Count; i++)
        {
            var layout = i < plan.Items.Count ? plan.Items[i] : null;
            if (layout is null || layout.IsPlaceholder)
            {
                _thumbnails[i]?.Dispose();
                _thumbnails[i] = null;
            }

            if (layout is not null)
            {
                AddItem(items[i], layout, icon, scale);
                _thumbnails[i]?.Show(layout.Thumbnail);
            }
        }

        if (plan.OverflowCount > 0)
        {
            AddOverflow(plan.OverflowCount, plan.OverflowItem, scale);
        }
    }

    /// <summary>Refreshes titles in place when only they changed (<see cref="DockPreviewContent.NeedsRebuild"/>).</summary>
    public void UpdateTitles(IReadOnlyList<DockPreviewItem> items)
    {
        for (var i = 0; i < _parts.Count && i < items.Count; i++)
        {
            _parts[i].Apply(items[i]);
        }
    }

    /// <summary>Moves the popover (physical pixels) to the top of the topmost band, without activating it.</summary>
    public void Place(PixelRect bounds)
    {
        _bounds = bounds;
        ShellSurface.SetBounds(this, bounds, topmost: true);
    }

    /// <summary>Hides the popover, releases its thumbnails and shrinks it to 1 × 1 px until it is next shown.</summary>
    public void Conceal()
    {
        ReleaseThumbnails();
        Hide();
        ItemsCanvas.Children.Clear();
        _parts.Clear();
        Place(PixelRect.FromSize(_bounds.Left, _bounds.Top, 1, 1));
    }

    protected override void OnClosed(EventArgs e)
    {
        ReleaseThumbnails();
        _source?.RemoveHook(WndProc);
        _source = null;
        base.OnClosed(e);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ShellSurface.MakeNonActivating(this, topmost: true);
        ShellSurface.SetCornerPreference(this, rounded: true);
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _source?.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_DPICHANGED && lParam != 0 && !_bounds.IsEmpty)
        {
            // Placed on a monitor with another DPI, WPF would resize the window to the rectangle suggested in lParam.
            // Our bounds are exact physical pixels for that monitor, so substitute them before WPF reads it (this hook
            // runs before WPF's own handler); the presenter lays the content out again on DpiChanged.
            Marshal.StructureToPtr(RECT.From(_bounds), lParam, fDeleteOld: false);
        }

        return 0;
    }

    private void ReleaseThumbnails()
    {
        // Reverse order of registration.
        for (var i = _thumbnails.Count - 1; i >= 0; i--)
        {
            _thumbnails[i]?.Dispose();
        }

        _thumbnails.Clear();
    }

    private void AddItem(DockPreviewItem item, DockPreviewItemLayout layout, ImageSource? icon, double scale)
    {
        var origin = layout.Item;
        var content = new Canvas { Width = layout.Item.Width / scale, Height = layout.Item.Height / scale };

        var caption = layout.Caption.Offset(-origin.Left, -origin.Top);
        var titleWidth = item.CanClose ? ((layout.Close.Left - layout.Caption.Left) / scale) - TitleCloseGap : layout.Caption.Width / scale;
        var title = new TextBlock
        {
            FontSize = TitleFontSize,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var titleHost = new Grid { Width = Math.Max(0, titleWidth), Height = caption.Height / scale, IsHitTestVisible = false };
        titleHost.Children.Add(title);
        Put(content, titleHost, caption, scale);

        Button? close = null;
        if (item.CanClose)
        {
            close = CloseButton(layout.Close.Width / scale);
            close.Click += (_, e) =>
            {
                // Handled, so the click does not also reach the item and focus the window being closed.
                e.Handled = true;
                CloseRequested?.Invoke(this, item);
            };
            Put(content, close, layout.Close.Offset(-origin.Left, -origin.Top), scale);
        }

        if (layout.IsPlaceholder)
        {
            Put(content, Placeholder(icon, item.IsMinimized, layout.Box, scale), layout.Box.Offset(-origin.Left, -origin.Top), scale);
        }

        var button = ItemButton(content);
        button.Click += (_, _) => WindowChosen?.Invoke(this, item.Handle);
        button.MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle && item.CanClose)
            {
                e.Handled = true;
                CloseRequested?.Invoke(this, item);
            }
        };
        if (close is not null)
        {
            // Opacity, not visibility: the × stays in the UI Automation tree for screen readers.
            button.MouseEnter += (_, _) => close.Opacity = 1;
            button.MouseLeave += (_, _) => close.Opacity = 0;
        }

        Put(ItemsCanvas, button, origin, scale);
        var parts = new ItemParts(button, title, close);
        parts.Apply(item);
        _parts.Add(parts);
    }

    private void AddOverflow(int count, PixelRect rect, double scale)
    {
        var text = string.Format(CultureInfo.CurrentCulture, "+{0} more", count);
        var label = new TextBlock { Text = text, FontSize = TitleFontSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid { Width = rect.Width / scale, Height = rect.Height / scale };
        content.Children.Add(label);

        var button = ItemButton(content);
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => OverflowChosen?.Invoke(this, EventArgs.Empty);
        Put(ItemsCanvas, button, rect, scale);
    }

    private static Button ItemButton(FrameworkElement content)
    {
        var button = new Button { Content = content, Focusable = false, Padding = new Thickness(0) };
        button.SetResourceReference(StyleProperty, "FlatButton");
        return button;
    }

    private static Button CloseButton(double size)
    {
        var close = new Button
        {
            Content = DockMenuBuilder.CloseGlyph,
            Focusable = false,
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            FontSize = CloseFontSize,
            Opacity = 0,
        };
        close.SetResourceReference(StyleProperty, "FlatButton");
        close.SetResourceReference(FontFamilyProperty, "IconFont");
        return close;
    }

    /// <summary>The app icon centred in the box, with "Minimised" under it for minimised windows.</summary>
    private static Grid Placeholder(ImageSource? icon, bool minimized, PixelRect box, double scale)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null)
        {
            var side = Math.Min(PlaceholderIconDip, box.Height / scale / 2);
            var image = new Image { Source = icon, Width = side, Height = side };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            stack.Children.Add(image);
        }

        if (minimized)
        {
            var status = new TextBlock { Text = "Minimised", FontSize = StatusFontSize, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            status.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
            stack.Children.Add(status);
        }

        var host = new Grid { Width = box.Width / scale, Height = box.Height / scale, IsHitTestVisible = false };
        host.Children.Add(stack);
        return host;
    }

    private static void Put(Canvas canvas, FrameworkElement element, PixelRect rect, double scale)
    {
        Canvas.SetLeft(element, rect.Left / scale);
        Canvas.SetTop(element, rect.Top / scale);
        element.Width = double.IsNaN(element.Width) ? rect.Width / scale : element.Width;
        element.Height = double.IsNaN(element.Height) ? rect.Height / scale : element.Height;
    }

    /// <summary>The parts of an item that change with its window's title.</summary>
    private sealed record ItemParts(Button Item, TextBlock Title, Button? Close)
    {
        public void Apply(DockPreviewItem item)
        {
            Title.Text = item.Title;
            AutomationProperties.SetName(Item, item.Title);
            AutomationProperties.SetItemStatus(Item, item.IsMinimized ? "Minimised" : "");
            if (Close is not null)
            {
                AutomationProperties.SetName(Close, "Close " + item.Title);
            }
        }
    }
}
