using System.Collections.ObjectModel;
using System.Windows.Input;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>Edits the list of programs that keep their native window buttons.</summary>
internal sealed class ExcludedAppsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private string _newName = "";
    private string? _error;

    public ExcludedAppsViewModel(SettingsService settings)
    {
        _settings = settings;
        AddCommand = new RelayCommand(Add);
        RemoveCommand = new RelayCommand(Remove);
        Rebuild(settings.Current);
        settings.Changed += OnSettingsChanged;
    }

    public ObservableCollection<string> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public ICommand AddCommand { get; }

    public ICommand RemoveCommand { get; }

    /// <summary>The program name being typed.</summary>
    public string NewName
    {
        get => _newName;
        set
        {
            if (SetProperty(ref _newName, value ?? ""))
            {
                Error = null;
            }
        }
    }

    /// <summary>Why the typed name was not added, or null.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged(object? sender, AppSettings settings) => Rebuild(settings);

    private void Rebuild(AppSettings settings)
    {
        var current = settings.WindowButtons.ExcludedProcesses;
        if (!current.SequenceEqual(Items))
        {
            Items.Clear();
            foreach (var name in current)
            {
                Items.Add(name);
            }

            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private void Add()
    {
        if (!ProcessNameParser.TryNormalize(NewName, out var name))
        {
            Error = "Enter a program name such as notepad or Notepad.exe.";
            return;
        }

        _settings.Update(s => s.WindowButtons.ExcludedProcesses.Add(name));
        NewName = "";
    }

    private void Remove(object? parameter)
    {
        if (parameter is string name)
        {
            _settings.Update(s => s.WindowButtons.ExcludedProcesses.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
