using System.Collections.ObjectModel;
using System.Windows.Input;
using WinGnome.Core.ControlCenter;
using WinGnome.Core.Tweaks;
using WinGnome.Features.Settings.Tweaks;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.Printers;

/// <summary>One row of the Printers list.</summary>
/// <param name="Name">The printer's name, which is also what the actions pass on.</param>
/// <param name="Subtitle">"Default" and/or its status ("Ready", "Offline", "2 jobs").</param>
/// <param name="IsDefault">True for the default printer, which has nothing to set.</param>
internal sealed record PrinterItem(string Name, string Subtitle, bool IsDefault)
{
    /// <summary>True when "Set as default" applies (it isn't the default already).</summary>
    public bool CanSetDefault => !IsDefault;
}

/// <summary>
/// Printers: every local and connected printer with its status, set as default, queue, properties and preferences, and
/// whether Windows manages the default printer. The list is read when the panel opens, after each change and from
/// Refresh (no polling). Changes are verified-set: written, then the list is read again and shown as Windows holds it.
/// </summary>
internal sealed class PrintersPanelViewModel : SystemPanelViewModel
{
    private const string PrintersLink = "ms-settings:printers";

    private readonly SystemSettingWriter _writer;
    private bool _windowsManagesDefault = true;
    private bool _isBusy;
    private bool _listFailed;

    public PrintersPanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.Printers)
    {
        _writer = context.CreateWriter();
        RefreshCommand = new RelayCommand(() =>
        {
            Problem = null;
            Reload();
        });
        AddPrinterCommand = new RelayCommand(() => context.OpenLink(PrintersLink));
        SetDefaultCommand = new RelayCommand(parameter => SetDefault(parameter as PrinterItem), _ => CanChange);
        QueueCommand = new RelayCommand(parameter => Launch(PrintUiAction.Queue, parameter as PrinterItem));
        PropertiesCommand = new RelayCommand(parameter => Launch(PrintUiAction.Properties, parameter as PrinterItem));
        PreferencesCommand = new RelayCommand(parameter => Launch(PrintUiAction.Preferences, parameter as PrinterItem));
    }

    public ObservableCollection<PrinterItem> Printers { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand AddPrinterCommand { get; }

    /// <summary>Parameter: the <see cref="PrinterItem"/>.</summary>
    public ICommand SetDefaultCommand { get; }

    public ICommand QueueCommand { get; }

    public ICommand PropertiesCommand { get; }

    public ICommand PreferencesCommand { get; }

    /// <summary>True while a read or a change is in flight; the controls that change something wait.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanChange));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>True when a printer setting can be changed now (not in safe mode, nothing in flight).</summary>
    public bool CanChange => CanEdit && !IsBusy;

    /// <summary>True when the list is empty because there are no printers (not because it could not be read).</summary>
    public bool HasPrinters => Printers.Count > 0;

    public bool ShowsEmptyNote => Printers.Count == 0 && !IsBusy && !_listFailed;

    /// <summary>
    /// "Let Windows manage my default printer". Setting it writes the value and then shows what Windows holds after
    /// reading it back; the field is never changed here, so a refused write snaps back.
    /// </summary>
    public bool WindowsManagesDefault
    {
        get => _windowsManagesDefault;
        set
        {
            if (value == _windowsManagesDefault || !CanChange)
            {
                // Tell the toggle what it really holds if the change was refused.
                OnPropertyChanged();
                return;
            }

            Change($"turn {(value ? "on" : "off")} Windows managing the default printer",
                () => PrinterService.WriteWindowsManagesDefault(new RegistryStore(), value),
                "the default printer setting");
        }
    }

    protected override void Open()
    {
        Printers.Clear();
        _listFailed = false;
        OnPropertyChanged(nameof(HasPrinters));
        OnPropertyChanged(nameof(ShowsEmptyNote));
        Reload();
    }

    protected override void Close() => Printers.Clear();

    private void Reload()
    {
        IsBusy = true;
        // longRunning: a print server that doesn't answer blocks EnumPrinters for a long time.
        LoadAsync(PrinterService.Read, Show, longRunning: true);
    }

    private void Show(PrinterSnapshot snapshot)
    {
        _windowsManagesDefault = snapshot.WindowsManagesDefault;
        _listFailed = snapshot.ListFailed;
        Printers.Clear();
        foreach (var printer in snapshot.Printers.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var isDefault = string.Equals(printer.Name, snapshot.DefaultName, StringComparison.OrdinalIgnoreCase);
            var status = PrinterStatusText.Describe(printer.Status, printer.Attributes, printer.Jobs);
            Printers.Add(new PrinterItem(printer.Name, isDefault ? $"Default · {status}" : status, isDefault));
        }

        if (snapshot.ListFailed)
        {
            Problem = "Couldn't read the printer list from Windows. The print spooler may be stopped. You can see your printers in Windows Settings instead.";
        }

        OnPropertyChanged(nameof(WindowsManagesDefault));
        OnPropertyChanged(nameof(HasPrinters));
        IsBusy = false;
        OnPropertyChanged(nameof(ShowsEmptyNote));
    }

    private void SetDefault(PrinterItem? printer)
    {
        if (printer is null || printer.IsDefault || !CanChange)
        {
            return;
        }

        Change($"make \"{printer.Name}\" the default printer", () => PrinterService.SetDefault(printer.Name), "the default printer");
    }

    /// <summary>Runs a change on the writer thread, then reads everything again so the panel shows what Windows holds.</summary>
    private void Change(string what, Func<bool> write, string failureLabel)
    {
        Problem = null;
        IsBusy = true;
        _writer.Run(what, () =>
        {
            try
            {
                return write();
            }
            finally
            {
                // Success, refusal or exception: the panel shows the real state either way.
                Context.Dispatcher.BeginInvoke(Reload);
            }
        }, () => ReportWriteFailure(failureLabel));
    }

    private void Launch(PrintUiAction action, PrinterItem? printer)
    {
        var arguments = PrintUiCommand.Build(action, printer?.Name);
        if (arguments is null)
        {
            Problem = "Windows can't open this printer's window from here. Open it from Windows Settings instead.";
            return;
        }

        ShellLaunch.SystemTool("rundll32.exe", arguments);
    }
}
