using System.Windows;
using System.Windows.Input;
using WinGnome.Core.Settings;
using WinGnome.Core.Tweaks;
using WinGnome.Features.Settings.Tweaks;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>One tweak row: a toggle that applies or reverts the tweak, plus its hints, errors and restart button.</summary>
internal sealed class TweakItemViewModel : ObservableObject
{
    private readonly TweakService _service;
    private readonly Action<TweakItemViewModel> _restartExplorer;
    private string? _error;

    public TweakItemViewModel(TweakService service, TweakDefinition definition, Action<TweakItemViewModel> restartExplorer)
    {
        _service = service;
        Definition = definition;
        _restartExplorer = restartExplorer;
        Activation = TweakActivationRules.For(definition);
        RestartExplorerCommand = new RelayCommand(() => _restartExplorer(this));
    }

    public TweakDefinition Definition { get; }

    public TweakActivation Activation { get; }

    public string Title => Definition.Title;

    public string Description => Definition.Description;

    /// <summary>Short badge text for tweaks that do not take effect immediately, or null.</summary>
    public string? Tag => Activation switch
    {
        TweakActivation.RestartExplorer => "Restart Explorer",
        TweakActivation.SignOut => "Sign out",
        _ => null,
    };

    /// <summary>Reflects the real registry state, not just the settings list.</summary>
    public bool IsApplied
    {
        get => _service.IsApplied(Definition);
        set
        {
            if (value == IsApplied)
            {
                return;
            }

            var result = _service.SetApplied(Definition, value);
            Error = result.Error;

            // A failed change must flip the switch back, but WPF ignores notifications raised from inside its own setter call.
            Application.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(IsApplied)));
        }
    }

    /// <summary>Why the last change failed, or null.</summary>
    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => _error is not null;

    /// <summary>True when the change only shows after Explorer restarts and that has not happened yet.</summary>
    public bool NeedsExplorerRestart => Activation == TweakActivation.RestartExplorer && _service.NeedsExplorerRestart(Definition);

    public ICommand RestartExplorerCommand { get; }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsApplied));
        OnPropertyChanged(nameof(NeedsExplorerRestart));
    }
}

/// <summary>The tweaks of one category.</summary>
internal sealed record TweakGroupViewModel(TweakCategory Category, string Title, string? Description, IReadOnlyList<TweakItemViewModel> Items);

/// <summary>Streamline: reversible registry tweaks grouped by category, with revert-all and an Explorer restart helper.</summary>
internal sealed class StreamlinePageViewModel : SettingsPageViewModel
{
    private readonly TweakService _tweaks;
    private readonly IDialogService _dialogs;
    private string? _message;
    private bool _messageIsError;

    public StreamlinePageViewModel(SettingsService settings, TweakService tweaks, IDialogService dialogs)
        : base(settings, "Streamline", "")
    {
        _tweaks = tweaks;
        _dialogs = dialogs;
        Groups = TweakCatalog.All
            .GroupBy(t => t.Category)
            .Select(g => new TweakGroupViewModel(g.Key, CategoryTitle(g.Key), CategoryDescription(g.Key),
                g.Select(t => new TweakItemViewModel(tweaks, t, RestartExplorer)).ToList()))
            .ToList();
        RevertAllCommand = new RelayCommand(RevertAll);
        tweaks.StateChanged += OnTweaksChanged;
    }

    public IReadOnlyList<TweakGroupViewModel> Groups { get; }

    /// <summary>True when changes are applied to an in-memory registry only.</summary>
    public bool IsSimulated => _tweaks.IsSimulated;

    public ICommand RevertAllCommand { get; }

    /// <summary>Outcome of the last page-level action, or null.</summary>
    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => _message is not null;

    /// <summary>True when <see cref="Message"/> reports a failure rather than a success.</summary>
    public bool MessageIsError
    {
        get => _messageIsError;
        private set => SetProperty(ref _messageIsError, value);
    }

    /// <summary>A group the view should scroll into sight the next time it is shown (set when another page links here).</summary>
    public TweakCategory? PendingGroup { get; set; }

    public override void OnSelected() => RefreshItems();

    public override void Dispose()
    {
        _tweaks.StateChanged -= OnTweaksChanged;
        base.Dispose();
    }

    protected override void OnSettingsApplied(AppSettings settings) => RefreshItems();

    private void OnTweaksChanged(object? sender, EventArgs e) => RefreshItems();

    private void RefreshItems()
    {
        foreach (var item in Groups.SelectMany(g => g.Items))
        {
            item.Refresh();
        }
    }

    private void Show(OperationResult result, string success)
    {
        MessageIsError = !result.Succeeded;
        Message = result.Error ?? success;
    }

    private void RevertAll()
    {
        if (!_dialogs.Confirm("Revert all tweaks?", "Every streamline tweak WinGnome applied goes back to the value it had before.", "Revert all", isDestructive: true))
        {
            return;
        }

        Show(_tweaks.RevertAll(), "All tweaks were reverted.");
    }

    private async void RestartExplorer(TweakItemViewModel item)
    {
        if (!_dialogs.Confirm("Restart Explorer?", $"\"{item.Title}\" takes effect once Explorer restarts. The taskbar, desktop and any open File Explorer windows close and come back in a moment.", "Restart Explorer"))
        {
            return;
        }

        Show(await _tweaks.RestartExplorerAsync(), "Explorer was restarted.");
    }

    private static string CategoryTitle(TweakCategory category) => category switch
    {
        TweakCategory.Appearance => "Appearance",
        TweakCategory.Shell => "Start and shell",
        TweakCategory.Privacy => "Privacy",
        TweakCategory.Taskbar => "Taskbar",
        _ => "Behaviour",
    };

    private static string? CategoryDescription(TweakCategory category) =>
        category == TweakCategory.Taskbar ? "For native taskbar mode — uses supported Windows settings only." : null;
}
