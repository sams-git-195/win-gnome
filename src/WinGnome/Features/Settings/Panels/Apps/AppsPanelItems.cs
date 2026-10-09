using System.Globalization;
using System.IO;
using System.Windows.Media;
using WinGnome.Core.ControlCenter;
using WinGnome.Core.Shell;
using WinGnome.Infrastructure;
using WinGnome.Services.Apps;

namespace WinGnome.Features.Settings.Panels.Apps;

/// <summary>One installed app: a desktop app (Uninstall key) or a packaged app (one row per package family).</summary>
internal sealed class InstalledAppItem : ObservableObject
{
    private const int IconPixels = 32;

    private readonly Lazy<ImageSource?> _icon;
    private readonly Action<InstalledAppItem> _expanded;
    private readonly bool _canEdit;
    private string _subtitle;
    private bool _isExpanded;
    private bool _isBusy;
    private bool _canRemovePackage = true;

    private InstalledAppItem(string name, string subtitle, bool canEdit, Func<ImageSource?> icon, Action<InstalledAppItem> expanded)
    {
        Name = name;
        _canEdit = canEdit;
        _subtitle = subtitle;
        _icon = new Lazy<ImageSource?>(icon);
        _expanded = expanded;
    }

    public string Name { get; }

    /// <summary>Publisher, version, size and install date as far as they are known.</summary>
    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    /// <summary>Loaded when the row is first drawn (the list is virtualised).</summary>
    public ImageSource? Icon => _icon.Value;

    /// <summary>The Uninstall key of a desktop app; null for a packaged app.</summary>
    public InstalledAppRecord? Record { get; private init; }

    /// <summary>How a desktop app is uninstalled; null when WinGnome hands it over to Windows Settings.</summary>
    public PlannedCommand? Plan { get; private init; }

    /// <summary>The package family of a packaged app; null for a desktop app.</summary>
    public PackagedAppRow? Package { get; private init; }

    public bool IsPackaged => Package is not null;

    /// <summary>False for a desktop app without a safe uninstall command: its button opens Windows Settings.</summary>
    public bool HasOwnUninstall => IsPackaged ? _canRemovePackage : Plan is not null;

    public string UninstallLabel => HasOwnUninstall ? "Uninstall…" : "Uninstall in Windows Settings";

    /// <summary>True while an uninstall started here is running or a packaged row's details are loading.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanUninstall));
            }
        }
    }

    /// <summary>
    /// The row's button works: nothing is in flight for the row, and either changes are allowed (not safe mode) or the
    /// button only opens Windows Settings.
    /// </summary>
    public bool CanUninstall => !_isBusy && (_canEdit || !HasOwnUninstall);

    /// <summary>Packaged rows only: expanding loads publisher, version and install date (the first time, through the package API).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && value)
            {
                _expanded(this);
            }
        }
    }

    /// <summary>True once <see cref="ShowDetails"/> has run.</summary>
    public bool HasDetails { get; private set; }

    public static InstalledAppItem ForDesktop(InstalledAppRecord record, string systemDirectory, bool canEdit, IIconProvider icons)
    {
        var culture = CultureInfo.CurrentCulture;
        var subtitle = Join(record.Publisher, record.DisplayVersion, AppSizeText.Format(record.EstimatedSizeKb, culture),
            InstallDateText.Format(record.InstallDate, culture));
        var iconPath = record.IconPath;
        return new InstalledAppItem(record.DisplayName ?? record.KeyName, subtitle, canEdit,
            () => iconPath is not null && File.Exists(iconPath) ? icons.GetAppIcon(iconPath, IconPixels) : null, _ => { })
        {
            Record = record,
            Plan = UninstallPlan.For(record, systemDirectory),
        };
    }

    public static InstalledAppItem ForPackage(PackagedAppRow row, bool canEdit, IIconProvider icons, Action<InstalledAppItem> expanded) =>
        new(row.Name, "Microsoft Store or packaged app", canEdit, () => icons.GetAppIcon(row.AppUserModelId, IconPixels), expanded)
        {
            Package = row,
        };

    /// <summary>Shows what the package API reported for an expanded packaged row (null: not installed for this user).</summary>
    public void ShowDetails(PackageDetails? details)
    {
        HasDetails = true;
        if (details is null)
        {
            Subtitle = "Not installed for this user";
            return;
        }

        Subtitle = Join(details.Publisher, details.Version, details.InstalledOn?.LocalDateTime.ToString("d", CultureInfo.CurrentCulture));
        _canRemovePackage = details.CanRemove;
        OnPropertyChanged(nameof(HasOwnUninstall));
        OnPropertyChanged(nameof(UninstallLabel));
        OnPropertyChanged(nameof(CanUninstall));
    }

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}

/// <summary>One start-up item with its switch.</summary>
internal sealed class StartupAppItem : ObservableObject
{
    private const int IconPixels = 32;

    private readonly Lazy<ImageSource?> _icon;
    private readonly Action<StartupAppItem, bool> _setEnabled;
    private readonly bool _canEdit;
    private bool _isEnabled;
    private bool _isBusy;

    public StartupAppItem(StartupAppRow row, bool canEdit, IIconProvider icons, Action<StartupAppItem, bool> setEnabled)
    {
        Row = row;
        _canEdit = canEdit;
        _isEnabled = row.Enabled;
        _setEnabled = setEnabled;
        _icon = new Lazy<ImageSource?>(() => IconTarget(row.Entry) is { } target ? icons.GetAppIcon(target, IconPixels) : null);
    }

    public StartupAppRow Row { get; }

    public string Name => Row.DisplayName;

    public string Subtitle => Row.IsMachineWide ? "For all users · change it in Task Manager" : Row.Entry.CommandLine;

    public ImageSource? Icon => _icon.Value;

    /// <summary>The switch. Setting it asks the panel to write the change; the panel then re-reads the list.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _setEnabled(this, value);
            }
        }
    }

    public bool CanToggle => _canEdit && Row.Editable && !_isBusy;

    public bool CanRemove => _canEdit && Row.Removable && !_isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanToggle));
                OnPropertyChanged(nameof(CanRemove));
            }
        }
    }

    /// <summary>The file whose icon stands for the item: the shortcut itself, or a Run value's program when it exists.</summary>
    private static string? IconTarget(StartupEntry entry)
    {
        if (entry.Source is StartupSource.FolderUser or StartupSource.FolderCommon)
        {
            return entry.CommandLine;
        }

        if (!StartupRunner.TryParseCommand(entry.CommandLine, wholeLineIsPath: false, out var command))
        {
            return null;
        }

        var path = Environment.ExpandEnvironmentVariables(command.Executable);
        return Path.IsPathFullyQualified(path) && File.Exists(path) ? path : null;
    }
}
