using System.IO;
using WinGnome.Core.Tweaks;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Tweaks;

/// <summary>
/// Applies and reverts the streamline tweaks for the settings UI. It owns the <see cref="TweakEngine"/>, persists the
/// backup after every change, keeps <c>AppSettings.EnabledTweaks</c> in step with the registry, broadcasts theme
/// changes and tracks which tweaks still wait for an Explorer restart.
/// </summary>
/// <remarks>
/// In simulated mode (safe mode and self-test) the registry is an in-memory store, no backup file is read or written,
/// and settings are left alone, so nothing a developer does can leak into the real profile.
/// </remarks>
internal sealed class TweakService
{
    private readonly SettingsService _settings;
    private readonly TweakEngine _engine;
    private readonly TweakBackupFile? _file;
    private readonly HashSet<string> _pendingRestart = new(StringComparer.OrdinalIgnoreCase);
    private bool _saveFailureIsFatal = true;

    public TweakService(SettingsService settings, bool simulate)
    {
        _settings = settings;
        IsSimulated = simulate;
        if (simulate)
        {
            _engine = new TweakEngine(new InMemoryRegistryStore(), new TweakBackup());
        }
        else
        {
            _file = new TweakBackupFile(settings.Directory);
            _engine = new TweakEngine(new RegistryStore(), _file.Load());
        }

        _engine.BackupChanged += OnBackupChanged;
    }

    /// <summary>True when changes only affect an in-memory registry.</summary>
    public bool IsSimulated { get; }

    /// <summary>Raised after a tweak was applied or reverted, or after Explorer was restarted.</summary>
    public event EventHandler? StateChanged;

    /// <summary>True when the tweak's registry values are currently in place. Unreadable state counts as not applied.</summary>
    public bool IsApplied(TweakDefinition tweak)
    {
        try
        {
            return _engine.IsApplied(tweak);
        }
        catch (RegistryAccessException ex)
        {
            Log.Warn($"Could not check tweak {tweak.Id}", ex);
            return false;
        }
    }

    /// <summary>True when the tweak was changed and still needs an Explorer restart to take effect.</summary>
    public bool NeedsExplorerRestart(TweakDefinition tweak) => _pendingRestart.Contains(tweak.Id);

    /// <summary>Makes <c>EnabledTweaks</c> list exactly the tweaks that are applied in the registry.</summary>
    public void SyncEnabledSetting()
    {
        if (IsSimulated)
        {
            return;
        }

        try
        {
            var applied = _engine.AppliedTweakIds();
            var listed = _settings.Current.EnabledTweaks;
            if (!applied.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(listed))
            {
                _settings.Update(s => s.EnabledTweaks = [.. applied]);
            }
        }
        catch (RegistryAccessException ex)
        {
            Log.Warn("Could not compare enabled tweaks with the registry", ex);
        }
    }

    /// <summary>Applies or reverts one tweak. On failure the registry is rolled back as far as possible.</summary>
    public OperationResult SetApplied(TweakDefinition tweak, bool apply)
    {
        try
        {
            if (apply)
            {
                Apply(tweak);
            }
            else
            {
                Revert(tweak);
            }
        }
        catch (Exception ex) when (ex is RegistryAccessException or IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not {(apply ? "apply" : "revert")} tweak {tweak.Id}", ex);
            return OperationResult.Failure(DescribeFailure(tweak, apply, ex));
        }

        AfterChange(tweak);
        SyncEnabledSetting();
        StateChanged?.Invoke(this, EventArgs.Empty);
        return OperationResult.Success;
    }

    /// <summary>Reverts every tweak that has a backup and clears <c>EnabledTweaks</c>. Reports the first failure but tries them all.</summary>
    public OperationResult RevertAll()
    {
        OperationResult result = default;
        foreach (var id in _engine.BackedUpTweakIds)
        {
            var tweak = TweakCatalog.Find(id);
            if (tweak is null)
            {
                Log.Warn($"Backup exists for unknown tweak '{id}'; leaving it untouched");
                continue;
            }

            var single = SetApplied(tweak, apply: false);
            if (result.Succeeded && !single.Succeeded)
            {
                result = single;
            }
        }

        if (!IsSimulated)
        {
            _settings.Update(s => s.EnabledTweaks = []);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>Restarts Explorer after the user confirmed. A simulated run only logs.</summary>
    public async Task<OperationResult> RestartExplorerAsync()
    {
        OperationResult result;
        if (IsSimulated)
        {
            Log.Info("Safe mode: Explorer restart skipped");
            result = OperationResult.Success;
        }
        else
        {
            result = await ExplorerRestarter.RestartAsync();
        }

        if (result.Succeeded)
        {
            _pendingRestart.Clear();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    private void Apply(TweakDefinition tweak)
    {
        try
        {
            // The engine saves the backup (through BackupChanged) before it writes anything, so a save failure aborts here.
            _engine.Apply(tweak);
        }
        catch (Exception ex) when (ex is RegistryAccessException or IOException or UnauthorizedAccessException)
        {
            RollBack(tweak);
            throw;
        }
    }

    /// <summary>Puts the registry back after a failed apply. Best effort: the original failure is what gets reported.</summary>
    private void RollBack(TweakDefinition tweak)
    {
        try
        {
            Revert(tweak);
        }
        catch (RegistryAccessException ex)
        {
            Log.Error($"Rollback of tweak {tweak.Id} failed", ex);
        }
    }

    /// <summary>
    /// Reverts a tweak. Once the original values are back, a backup file that cannot be updated is harmless
    /// (it only lists a tweak that no longer needs restoring), so that failure is logged rather than reported.
    /// </summary>
    private void Revert(TweakDefinition tweak)
    {
        _saveFailureIsFatal = false;
        try
        {
            _engine.Revert(tweak);
        }
        finally
        {
            _saveFailureIsFatal = true;
        }
    }

    private void OnBackupChanged(object? sender, EventArgs e)
    {
        try
        {
            _file?.Save(_engine.Backup);
        }
        catch (Exception ex) when (!_saveFailureIsFatal && ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not update the tweak backup file", ex);
        }
    }

    private void AfterChange(TweakDefinition tweak)
    {
        if (TweakActivationRules.For(tweak) == TweakActivation.RestartExplorer)
        {
            _pendingRestart.Add(tweak.Id);
        }

        if (!tweak.BroadcastThemeChange)
        {
            return;
        }

        if (IsSimulated)
        {
            Log.Info("Safe mode: theme change broadcast skipped");
        }
        else
        {
            _ = SystemBroadcast.ThemeChangedAsync();
        }
    }

    private static string DescribeFailure(TweakDefinition tweak, bool apply, Exception ex) =>
        $"\"{tweak.Title}\" could not be {(apply ? "turned on" : "turned off")}. {(ex is RegistryAccessException ? ex.Message : "WinGnome could not save its backup of your original settings, so nothing was changed.")}";
}
