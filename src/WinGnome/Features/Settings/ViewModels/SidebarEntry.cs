using WinGnome.Core.ControlCenter;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>One row of the settings sidebar: a native page, or a link that opens Windows Settings.</summary>
internal sealed class SidebarEntry(SettingsPanel panel, SettingsPageViewModel? page)
{
    public SettingsPanel Panel { get; } = panel;

    /// <summary>The page shown for a native panel; null for links.</summary>
    public SettingsPageViewModel? Page { get; } = page;

    public string Id => Panel.Id;

    public string Title => Panel.Title;

    public string Glyph => Panel.Icon;

    /// <summary>True for panels that open in Windows Settings (shown with an arrow).</summary>
    public bool IsLink => Panel.Kind == PanelKind.Link;

    /// <summary>Sidebar group heading; the list groups by it.</summary>
    public string GroupTitle => SettingsPanelCatalog.GroupTitle(Panel.Group);

    /// <summary>Spoken by screen readers (the list item's automation name).</summary>
    public string AccessibleName => IsLink ? Title + ", opens in Windows Settings" : Title;

    public override string ToString() => AccessibleName;
}
