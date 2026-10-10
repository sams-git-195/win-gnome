namespace WinGnome.Core.ControlCenter;

/// <summary>Splits Windows Update Agent results into the recommended updates Windows Settings shows as waiting and the optional ones it tucks under Advanced options.</summary>
public static class UpdateClassification
{
    /// <summary>The <c>IUpdate.Type</c> of a driver update (<c>utDriver</c>); software is 1.</summary>
    public const int DriverType = 2;

    /// <summary>
    /// True for an update Windows Settings lists under Optional updates rather than as waiting: anything the agent marks
    /// browse-only, and any driver that isn't auto-selected or mandatory (drivers Windows picks itself still count as waiting).
    /// </summary>
    public static bool IsOptional(int type, bool browseOnly, bool autoSelectOnWebSites, bool isMandatory) =>
        browseOnly || (type == DriverType && !autoSelectOnWebSites && !isMandatory);
}
