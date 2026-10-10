using WinGnome.Core.ControlCenter;

namespace WinGnome.Features.Settings.Panels.About;

/// <summary>About: device name, hardware and Windows version, read off the UI thread each time the panel opens.</summary>
internal sealed class AboutPanelViewModel(SystemPanelContext context) : SystemPanelViewModel(context, PanelIds.About)
{
    private SystemInfo? _info;

    /// <summary>Null while reading.</summary>
    public SystemInfo? Info
    {
        get => _info;
        private set => SetProperty(ref _info, value);
    }

    protected override void Open() => LoadAsync(SystemInfoReader.Read, info => Info = info);
}
