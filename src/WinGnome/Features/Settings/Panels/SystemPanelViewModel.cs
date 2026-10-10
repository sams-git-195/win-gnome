using System.Windows.Input;
using System.Windows.Threading;
using WinGnome.Core.ControlCenter;
using WinGnome.Core.Settings;
using WinGnome.Features.Settings.ViewModels;
using WinGnome.Infrastructure;
using WinGnome.Services.Apps;

namespace WinGnome.Features.Settings.Panels;

/// <summary>What every system panel needs from the settings window.</summary>
/// <param name="Settings">WinGnome's own settings (some panels show WinGnome options beside Windows ones).</param>
/// <param name="Dispatcher">The UI dispatcher.</param>
/// <param name="IsReadOnly">True in --safe and --selftest mode: panels show system settings but never change them.</param>
/// <param name="OpenLink">Opens an <c>ms-settings:</c> page or control panel.</param>
/// <param name="SettingsDirectory">WinGnome's profile folder (for the display revert record).</param>
/// <param name="Services">The shell services some panels use (apps, icons, dialogs).</param>
/// <param name="Options">The command line, for panels that must not touch the network or radios in <c>--selftest</c>.</param>
internal sealed record SystemPanelContext(
    SettingsService Settings,
    Dispatcher Dispatcher,
    bool IsReadOnly,
    Action<string> OpenLink,
    string SettingsDirectory,
    SystemPanelServices Services,
    CommandLineOptions Options)
{
    /// <summary>Runs system writes off the UI thread, one at a time, and never in read-only mode.</summary>
    public SystemSettingWriter CreateWriter() => new(Dispatcher, IsReadOnly);
}

/// <summary>
/// Shell services for the system panels, in one record so adding a service doesn't change every panel's constructor.
/// Spec 0017's Do Not Disturb service joins it when that lands.
/// </summary>
/// <param name="Apps">The installed-apps catalogue the shell has already loaded.</param>
/// <param name="Icons">App icons at a given size.</param>
/// <param name="Launcher">Starts apps and opens URIs.</param>
/// <param name="Dialogs">Confirmation and app-picker prompts owned by the settings window.</param>
internal sealed record SystemPanelServices(
    IAppCatalog Apps,
    IIconProvider Icons,
    IAppLauncher Launcher,
    IDialogService Dialogs);

/// <summary>
/// Base of the panels that show Windows settings (Sound, Displays, Power, ...). The panel reads the system when it is
/// opened and releases whatever it holds (COM objects, timers, callbacks) when another panel replaces it, so a closed
/// panel costs nothing. Failures are shown with a link to the matching Windows Settings page.
/// </summary>
internal abstract class SystemPanelViewModel : SettingsPageViewModel
{
    private readonly string _panelId;
    private string? _problem;

    // Which load may show its result: the open's generation and the newest load per channel. The rule is pure
    // counting, so it lives in Core (PanelLoadGate) with the tests; the showing itself stays here on the dispatcher.
    private readonly PanelLoadGate _loads = new();

    protected SystemPanelViewModel(SystemPanelContext context, string panelId)
        : this(context, SettingsPanelCatalog.Find(panelId) ?? throw new ArgumentException($"Unknown panel \"{panelId}\".", nameof(panelId)))
    {
    }

    private SystemPanelViewModel(SystemPanelContext context, SettingsPanel panel)
        : base(context.Settings, panel.Title, panel.Icon)
    {
        _panelId = panel.Id;
        Context = context;
        var link = panel.LinkUri ?? "ms-settings:";
        OpenInWindowsCommand = new RelayCommand(() => context.OpenLink(link));
    }

    /// <summary>True when system settings can be changed (not in safe mode).</summary>
    public bool CanEdit => !Context.IsReadOnly;

    /// <summary>Shown above the panel in safe mode.</summary>
    public string? ReadOnlyNote => Context.IsReadOnly
        ? "Safe mode: these Windows settings are shown but can't be changed. Restart WinGnome without --safe to change them."
        : null;

    /// <summary>What went wrong reading or changing a setting, shown with a link to Windows Settings; null when all is well.</summary>
    public string? Problem
    {
        get => _problem;
        protected set => SetProperty(ref _problem, value);
    }

    /// <summary>Opens the matching Windows Settings page.</summary>
    public ICommand OpenInWindowsCommand { get; }

    protected SystemPanelContext Context { get; }

    public sealed override void OnSelected()
    {
        if (_loads.IsOpen)
        {
            return;
        }

        _loads.Opened();
        Problem = null;
        Open();
    }

    public sealed override void OnDeselected()
    {
        if (!_loads.IsOpen)
        {
            return;
        }

        _loads.Closed();
        Close();
    }

    public override void Dispose()
    {
        OnDeselected();
        base.Dispose();
    }

    /// <summary>Reads the system and creates whatever the panel needs while it is visible.</summary>
    protected abstract void Open();

    /// <summary>Releases what <see cref="Open"/> created. Called once per <see cref="Open"/>.</summary>
    protected virtual void Close()
    {
    }

    /// <summary>
    /// Runs <paramref name="read"/> off the UI thread and passes its result to <paramref name="show"/> on the dispatcher,
    /// but only while the panel is still open from the same <see cref="Open"/> and no later <see cref="LoadAsync{T}"/> was started on the same <paramref name="channel"/>: a read that finishes after the panel was
    /// closed (or closed and reopened) is dropped. A failure is logged, shown as <see cref="Problem"/> and passed to
    /// <paramref name="onFailed"/>. Call it from the UI thread (usually in <see cref="Open"/>).
    /// </summary>
    /// <param name="onFailed">
    /// Runs on the dispatcher, under exactly the same conditions as <paramref name="show"/>, when
    /// <paramref name="read"/> threw: a panel that set a busy flag before loading passes this to clear the flag, so a
    /// failed read never leaves the controls disabled. A load that was superseded on its channel or outlived its open
    /// does not run it — the busy state then belongs to the newer load or to the next open.
    /// </param>
    /// <param name="longRunning">
    /// Runs the read on its own background thread (MTA, named after the panel) instead of the thread pool. Use it for
    /// reads that can block for long (an RPC to a print server, Windows Update, a walk over hundreds of registry keys),
    /// so a stuck call never starves the pool.
    /// </param>
    /// <param name="channel">
    /// Which loads replace each other: only the newest load on the same channel is shown. Existing callers use the
    /// default channel; a panel with independent lists gives each its own channel.
    /// </param>
    protected void LoadAsync<T>(Func<T> read, Action<T> show, Action? onFailed = null, bool longRunning = false, string channel = "")
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(channel);
        var generation = _loads.Generation;
        var sequence = _loads.Begin(channel);

        void Work()
        {
            T result;
            try
            {
                result = read();
            }
            catch (Exception ex)
            {
                // Thread boundary: an exception escaping a worker would end the shell process.
                Log.Warn($"Settings: could not read the \"{_panelId}\" panel's settings", ex);
                ShowIfCurrent(generation, channel, sequence, () =>
                {
                    Problem = "Couldn't read these settings from Windows. You can see them in Windows Settings instead.";
                    if (onFailed is null)
                    {
                        return;
                    }

                    try
                    {
                        onFailed();
                    }
                    catch (Exception ex)
                    {
                        // The banner above already says what failed; a broken callback must not replace it with the
                        // show-failure one (ShowIfCurrent's catch), so it is logged here and goes no further.
                        Log.Warn($"Settings: the \"{_panelId}\" panel's load-failure callback threw", ex);
                    }
                });
                return;
            }

            ShowIfCurrent(generation, channel, sequence, () => show(result));
        }

        if (longRunning)
        {
            var thread = new Thread(Work) { IsBackground = true, Name = $"WinGnome panel: {_panelId}" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }
        else
        {
            ThreadPool.QueueUserWorkItem(_ => Work());
        }
    }

    /// <summary>Runs <paramref name="action"/> on the dispatcher if the panel is still in the open state <paramref name="generation"/>.</summary>
    private void ShowIfCurrent(int generation, string channel, int sequence, Action action) =>
        Context.Dispatcher.BeginInvoke(() =>
        {
            if (!_loads.MayShow(generation, channel, sequence))
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Warn($"Settings: could not show the \"{_panelId}\" panel's settings", ex);
                Problem = "Couldn't show these settings. You can see them in Windows Settings instead.";
            }
        });

    /// <summary>Reports a failed change; the panel shows it with the Windows Settings link.</summary>
    protected void ReportWriteFailure(string what) =>
        Problem = $"Windows didn't accept the change to {what}. It may be managed by your organisation; you can try in Windows Settings instead.";
}
