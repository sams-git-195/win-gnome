using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services;

namespace WinGnome.Features.Dock;

/// <summary>Builds the right-click menus of dock entries (flat menus: the shared MenuItem style has no submenus).</summary>
internal static class DockMenuBuilder
{
    private const int MaxTitleLength = 60;

    /// <summary>Segoe Fluent Icons "CheckMark".</summary>
    private const string CheckGlyph = "";

    /// <summary>The menu for <paramref name="entry"/>, or null when it has none (Show Applications).</summary>
    public static ContextMenu? Build(DockEntry entry, DockActions actions, WindowTracker windows) => entry switch
    {
        DockAppEntry app => BuildAppMenu(app, actions, windows),
        DockActionEntry { Kind: DockActionKind.RecycleBin } bin => Menu(Item("Open", () => actions.OpenRecycleBin(bin))),
        _ => null,
    };

    private static ContextMenu BuildAppMenu(DockAppEntry entry, DockActions actions, WindowTracker windows)
    {
        var app = entry.App;
        var menu = new ContextMenu();

        // GNOME lists the app's windows first; clicking one focuses it.
        foreach (var hwnd in app.Windows)
        {
            menu.Items.Add(Item(WindowTitle(hwnd, windows, app.Name), () => actions.ActivateWindow(hwnd)));
        }

        if (app.Windows.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var launch = Item(app.IsRunning ? "New window" : "Open", () => actions.LaunchNew(entry));
        launch.IsEnabled = actions.CanLaunch(app);
        menu.Items.Add(launch);

        // UWP apps and URIs cannot be elevated, so they get neither item (full-trust packaged apps such as Terminal do).
        if (actions.CanRunAsAdministrator(app))
        {
            menu.Items.Add(Item("Run as administrator", () => actions.RunAsAdministrator(entry)));
            if (app.IsPinned)
            {
                var always = actions.IsAlwaysRunAsAdministrator(app);
                menu.Items.Add(CheckItem("Always run as administrator", always, () => actions.SetAlwaysRunAsAdministrator(app, !always)));
            }
        }

        if (app.IsPinned)
        {
            menu.Items.Add(Item("Unpin from dock", () => actions.Unpin(app)));
        }
        else
        {
            var pin = Item("Pin to dock", () => actions.Pin(app));
            pin.IsEnabled = actions.PinFor(app) is not null;
            menu.Items.Add(pin);
        }

        if (app.IsRunning)
        {
            menu.Items.Add(new Separator());
            var quit = app.Windows.Count == 1
                ? "Quit"
                : string.Format(CultureInfo.CurrentCulture, "Close {0} windows", app.Windows.Count);
            menu.Items.Add(Item(quit, () => DockActions.Quit(app)));
        }

        return menu;
    }

    private static string WindowTitle(nint hwnd, WindowTracker windows, string fallback)
    {
        var title = windows.Windows.FirstOrDefault(w => w.Handle == hwnd)?.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = NativeMethods.GetWindowTitle(hwnd);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            title = fallback;
        }

        title = title.Trim();
        return title.Length > MaxTitleLength ? string.Concat(title.AsSpan(0, MaxTitleLength - 1), "…") : title;
    }

    private static ContextMenu Menu(params MenuItem[] items)
    {
        var menu = new ContextMenu();
        foreach (var item in items)
        {
            menu.Items.Add(item);
        }

        return menu;
    }

    private static MenuItem Item(string text, Action action)
    {
        // A TextBlock header keeps underscores in window titles literal instead of turning them into access keys.
        var item = new MenuItem { Header = new TextBlock { Text = text } };
        item.Click += (_, _) => Run(action);
        return item;
    }

    /// <summary>
    /// A toggle item. The shared MenuItem template draws no check mark, so the header carries its own glyph column;
    /// menus are rebuilt on every open, so the state never goes stale.
    /// </summary>
    private static MenuItem CheckItem(string text, bool isChecked, Action toggle)
    {
        var glyph = new TextBlock { Text = isChecked ? CheckGlyph : "", Width = 20, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(glyph);
        header.Children.Add(new TextBlock { Text = text });

        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked };
        item.Click += (_, _) => Run(toggle);
        return item;
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Warn("Dock: menu command failed", ex);
        }
    }
}
