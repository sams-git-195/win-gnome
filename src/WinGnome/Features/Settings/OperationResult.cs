namespace WinGnome.Features.Settings;

/// <summary>Outcome of an action the user triggered in settings: success, or a message that is safe to show them.</summary>
internal readonly record struct OperationResult(string? Error)
{
    public static OperationResult Success => default;

    public bool Succeeded => Error is null;

    public static OperationResult Failure(string message) => new(message);
}
