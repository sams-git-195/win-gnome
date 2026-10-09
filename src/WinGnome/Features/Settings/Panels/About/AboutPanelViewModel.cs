using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.About;

/// <summary>About: device name, hardware and Windows version, read off the UI thread each time the panel opens.</summary>
internal sealed class AboutPanelViewModel(SystemPanelContext context) : SystemPanelViewModel(context, PanelIds.About)
{
    private SystemInfo? _info;
    private int _generation;

    /// <summary>Null while reading.</summary>
    public SystemInfo? Info
    {
        get => _info;
        private set => SetProperty(ref _info, value);
    }

    protected override void Open()
    {
        var generation = ++_generation;
        Task.Run(SystemInfoReader.Read).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Log.Warn("Could not read the system information", task.Exception);
                return;
            }

            // Ignore a slow read that finished after the panel was closed and reopened.
            Context.Dispatcher.BeginInvoke(() =>
            {
                if (generation == _generation)
                {
                    Info = task.Result;
                }
            });
        }, TaskScheduler.Default);
    }

    protected override void Close() => _generation++;
}
