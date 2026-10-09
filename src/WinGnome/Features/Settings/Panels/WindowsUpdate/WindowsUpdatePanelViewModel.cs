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
        LoadAsync(() => UpdateStatusService.Read(SearchTimeout), status =>
        {
            _lines = UpdateStatusText.Build(status, DateTime.Now, CultureInfo.CurrentCulture);
            Problem = _lines.Error;
            IsLoading = false;
            OnPropertyChanged(string.Empty);
        }, longRunning: true);
    }
}
