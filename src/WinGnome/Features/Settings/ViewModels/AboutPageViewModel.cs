using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.ViewModels;

/// <summary>About: version, where settings and the log live, reset and quit.</summary>
internal sealed class AboutPageViewModel : SettingsPageViewModel
{
    private readonly ShellCommands _commands;
    private readonly IDialogService _dialogs;
    private string? _error;

    public AboutPageViewModel(SettingsService settings, ShellCommands commands, IDialogService dialogs)
        : base(settings, "About", "")
    {
        _commands = commands;
        _dialogs = dialogs;
        Version = typeof(AboutPageViewModel).Assembly.GetName().Version?.ToString(3) ?? "";
        OpenFolderCommand = new RelayCommand(() => Open(new ProcessStartInfo("explorer.exe", $"\"{SettingsFolder}\"")));
        OpenLogCommand = new RelayCommand(() => Open(new ProcessStartInfo(LogFile) { UseShellExecute = true }), () => Log.FilePath is not null);
        ResetCommand = new RelayCommand(Reset);
        QuitCommand = new RelayCommand(_commands.Quit);
    }

    public string Version { get; }

    public string SettingsFolder => Settings.Directory;

    public string LogFile { get; } = Log.FilePath ?? "Logging is unavailable.";

    public ICommand OpenFolderCommand { get; }

    public ICommand OpenLogCommand { get; }

    public ICommand ResetCommand { get; }

    public ICommand QuitCommand { get; }

    /// <summary>Why a folder or the log could not be opened, or null.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    private void Open(ProcessStartInfo start)
    {
        try
        {
            Error = null;
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Log.Warn("Could not open " + start.FileName, ex);
            Error = "Windows could not open that location.";
        }
    }

    private void Reset()
    {
        if (_dialogs.Confirm("Reset all settings?", "Every WinGnome setting returns to its default. Registry tweaks stay as they are; use Streamline to revert them.", "Reset", isDestructive: true))
        {
            // EnabledTweaks mirrors the registry, which a reset leaves alone, so it is carried over.
            Settings.Replace(new AppSettings { EnabledTweaks = [.. Settings.Current.EnabledTweaks] });
        }
    }
}
