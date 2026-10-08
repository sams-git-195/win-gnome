using System.Reflection;

namespace WinGnome.Infrastructure;

/// <summary>
/// Start order for an <see cref="IFeature"/> (lower starts first, disposes last). Features are discovered
/// by reflection so each one lives entirely in its own folder with no central registration list.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class FeatureOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}

internal static class FeatureDiscovery
{
    /// <summary>
    /// Finds every concrete <see cref="IFeature"/> with a public or internal constructor taking a
    /// <see cref="ShellContext"/>, ordered by <see cref="FeatureOrderAttribute"/>.
    /// </summary>
    public static IReadOnlyList<Type> FindFeatureTypes() =>
        typeof(IFeature).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IFeature).IsAssignableFrom(t))
            .Where(t => t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(ShellContext)]) is not null)
            .OrderBy(t => t.GetCustomAttribute<FeatureOrderAttribute>()?.Order ?? 100)
            .ThenBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

    public static IFeature Create(Type type, ShellContext context)
    {
        var ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(ShellContext)])
            ?? throw new InvalidOperationException($"{type.Name} has no ShellContext constructor.");
        return (IFeature)ctor.Invoke([context]);
    }
}
