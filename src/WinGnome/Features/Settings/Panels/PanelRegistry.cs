using WinGnome.Core.ControlCenter;
using WinGnome.Features.Settings.Panels.About;
using WinGnome.Features.Settings.Panels.Appearance;
using WinGnome.Features.Settings.Panels.DateAndTime;
using WinGnome.Features.Settings.Panels.Displays;
using WinGnome.Features.Settings.Panels.Keyboard;
using WinGnome.Features.Settings.Panels.Mouse;
using WinGnome.Features.Settings.Panels.Multitasking;
using WinGnome.Features.Settings.Panels.Notifications;
using WinGnome.Features.Settings.Panels.Power;
using WinGnome.Features.Settings.Panels.Sound;

namespace WinGnome.Features.Settings.Panels;

/// <summary>
/// The native system panels, one line each, so adding a panel touches no shared code beyond its line here and one in
/// <c>PanelResources.xaml</c> (its view). A catalogue entry marked native without a line here stays a link to its
/// Windows Settings page (see <c>SidebarEntry.IsLink</c>), so a panel goes live by adding its line.
/// </summary>
internal static class PanelRegistry
{
    /// <summary>Creates the view model of each native system panel, by <see cref="PanelIds"/> value.</summary>
    public static IReadOnlyDictionary<string, Func<SystemPanelContext, SystemPanelViewModel>> SystemPanels { get; } =
        new Dictionary<string, Func<SystemPanelContext, SystemPanelViewModel>>(StringComparer.Ordinal)
        {
            [PanelIds.Displays] = context => new DisplaysPanelViewModel(context),
            [PanelIds.Sound] = context => new SoundPanelViewModel(context),
            [PanelIds.Power] = context => new PowerPanelViewModel(context),
            [PanelIds.Mouse] = context => new MousePanelViewModel(context),
            [PanelIds.Keyboard] = context => new KeyboardPanelViewModel(context),
            [PanelIds.Appearance] = context => new AppearancePanelViewModel(context),
            [PanelIds.Multitasking] = context => new MultitaskingPanelViewModel(context),
            [PanelIds.DateTime] = context => new DateTimePanelViewModel(context),
            [PanelIds.About] = context => new AboutPanelViewModel(context),
            [PanelIds.Notifications] = context => new NotificationsPanelViewModel(context),
        };
}
