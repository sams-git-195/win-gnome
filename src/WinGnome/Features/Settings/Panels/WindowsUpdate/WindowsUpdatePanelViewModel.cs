using System.Globalization;
using System.Windows.Input;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.WindowsUpdate;

/// <summary>
/// Windows Update: a read-only status (last checked, last installed, updates waiting, restart needed) read from the
/// Windows Update Agent's cache. Checking for updates, history and options open Windows Settings; this panel never
/// starts a scan or installs anything.
/// </summary>
internal sealed class WindowsUpdatePanelViewModel : SystemPanelViewModel
{
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(20);

    private bool _isLoading;
    private UpdateStatusLines? _lines;

    public WindowsUpdatePanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.WindowsUpdate)
    {
        CheckCommand = new RelayCommand(() => context.OpenLink("ms-settings:windowsupdate-action"));
        HistoryCommand = new RelayCommand(() => context.OpenLink("ms-settings:windowsupdate-history"));
        OptionsCommand = new RelayCommand(() => context.OpenLink("ms-settings:windowsupdate-options"));
        RefreshCommand = new RelayCommand(Load, () => !IsLoading);
    }

    /// <summary>True while the status is being read; the rows show a progress bar instead of values.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasStatus => _lines is not null;

    public string LastChecked => _lines?.LastChecked ?? "";

    public string LastInstalled => _lines?.LastInstalled ?? "";

    public string Pending => _lines?.Pending ?? "";

    public IReadOnlyList<string> PendingTitles => _lines?.PendingTitles ?? [];

    public string? Optional => _lines?.Optional;

    public bool HasOptional => _lines?.Optional is not null;

    public IReadOnlyList<string> OptionalTitles => _lines?.OptionalTitles ?? [];

    public string Restart => _lines?.Restart ?? "";

    public ICommand CheckCommand { get; }

    public ICommand HistoryCommand { get; }

    public ICommand OptionsCommand { get; }

    public ICommand RefreshCommand { get; }

    protected override void Open() => Load();

    protected override void Close() => _lines = null;

    private void Load()
    {
        IsLoading = true;
        Problem = null;

        // longRunning: the Windows Update Agent can block for as long as its service takes to answer.
        LoadAsync(ReadStatus, status =>
        {
            IsLoading = false;
            _lines = UpdateStatusText.Build(status, DateTime.Now, CultureInfo.CurrentCulture);
            Problem = _lines.Error;
            OnPropertyChanged(string.Empty);
        }, longRunning: true);
    }

    /// <summary>
    /// Never throws: any failure becomes a status carrying its HRESULT, so the rows can show Windows' own message for
    /// it instead of LoadAsync's generic failure banner (whose <c>onFailed</c> callback has no access to the exception).
    /// </summary>
    private static UpdateStatus ReadStatus()
    {
        try
        {
            var status = UpdateStatusService.Read(SearchTimeout);
            return status with { RebootRequired = PendingRestartReader.Read(status.RebootRequired).RestartNeeded };
        }
        catch (Exception ex)
        {
            Log.Warn("Windows Update: could not read the status", ex);
            return new UpdateStatus(null, null, [], [], PendingRestartReader.Read(false).RestartNeeded, ex.HResult);
        }
    }
}
