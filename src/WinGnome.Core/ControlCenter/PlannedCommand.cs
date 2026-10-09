namespace WinGnome.Core.ControlCenter;

/// <summary>
/// A command a settings panel has decided to run, such as an app's uninstaller. The planner guarantees
/// <paramref name="Executable"/> is a fully qualified path (never resolved through the search path).
/// </summary>
/// <param name="Executable">Fully qualified path of the program.</param>
/// <param name="Arguments">Its command-line arguments, already quoted; empty for none.</param>
public sealed record PlannedCommand(string Executable, string Arguments);
