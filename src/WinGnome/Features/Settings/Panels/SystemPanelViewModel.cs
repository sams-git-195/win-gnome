using System.Windows.Input;
using System.Windows.Threading;
using WinGnome.Core.ControlCenter;
using WinGnome.Features.Settings.ViewModels;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels;

/// <summary>What every system panel needs from the settings window.</summary>
/// <param name="Settings">WinGnome's own settings (some panels show WinGnome options beside Windows ones).</param>
/// <param name="Dispatcher">The UI dispatcher.</param>
/// <param name="IsReadOnly">True in --safe and --selftest mode: panels show system settings but never change them.</param>
/// <param name="OpenLink">Opens an <c>ms-settings:</c> page or control panel.</param>
/// <param name="SettingsDirectory">WinGnome's profile folder (for the display revert record).</param>
internal sealed record SystemPanelContext(
    SettingsService Settings,
    Dispatcher Dispatcher,
    bool IsReadOnly,
    Action<string> OpenLink,
    string SettingsDirectory)
{
    /// <summary>Runs system writes off the UI thread, one at a time, and never in read-only mode.</summary>
    public SystemSettingWriter CreateWriter() => new(Dispatcher, IsReadOnly);
}

/// <summary>
/// Base of the panels that show Windows settings (Sound, Displays, Power, ...). The panel reads the system when it is
/// opened and releases whatever it holds (COM objects, timers, callbacks) when another panel replaces it, so a closed
/// panel costs nothing. Failures are shown with a link to the matching Windows Settings page.
/// </summary>
internal abstract class SystemPanelViewModel : SettingsPageViewModel
{
    private string? _problem;
    private bool _isOpen;

    protected SystemPanelViewModel(SystemPanelContext context, string panelId)
        : this(context, SettingsPanelCatalog.Find(panelId) ?? throw new ArgumentException($"Unknown panel \"{panelId}\".", nameof(panelId)))
    {
    }

    private SystemPanelViewModel(SystemPanelContext context, SettingsPanel panel)
        : base(context.Settings, panel.Title, panel.Icon)
    {
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
        if (_isOpen)
        {
            return;
        }

        _isOpen = true;
        Problem = null;
        Open();
    }

    public sealed override void OnDeselected()
    {
        if (!_isOpen)
        {
            return;
        }

        _isOpen = false;
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

    /// <summary>Reports a failed change; the panel shows it with the Windows Settings link.</summary>
    protected void ReportWriteFailure(string what) =>
        Problem = $"Windows didn't accept the change to {what}. It may be managed by your organisation; you can try in Windows Settings instead.";
}
