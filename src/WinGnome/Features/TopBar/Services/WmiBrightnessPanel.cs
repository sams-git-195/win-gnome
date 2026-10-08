using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using WinGnome.Core.TopBar;

namespace WinGnome.Features.TopBar.Services;

/// <summary>
/// The internal laptop panel as WMI exposes it: <c>WmiMonitorBrightness</c> in <c>root\wmi</c> for the supported
/// levels and the current value, <c>WmiMonitorBrightnessMethods.WmiSetBrightness</c> to change it.
/// </summary>
/// <remarks>
/// Reached through late-bound COM (<c>WbemScripting.SWbemLocator</c>) so no package is needed. Every member does
/// blocking WMI work and must run off the UI thread. Calls are serialised, and <see cref="Dispose"/> waits for
/// one in flight. Writes go through <c>SWbemServices.ExecMethod</c> rather than calling
/// <c>WmiSetBrightness</c> on an instance object: on the supported hardware only the first instance object
/// fetched in a process can call the method, and every later one fails with E_FAIL ("Unspecified error").
/// </remarks>
internal sealed class WmiBrightnessPanel : IDisposable
{
    private const string Namespace = @"root\wmi";
    private const string MethodsClass = "WmiMonitorBrightnessMethods";
    private const string SetMethod = "WmiSetBrightness";

    private readonly object _lock = new();
    private readonly object _services;
    private readonly object _inParameters;
    private readonly string _instancePath;
    private bool _disposed;

    private WmiBrightnessPanel(object services, object inParameters, string instancePath, IReadOnlyList<int> levels, int level)
    {
        _services = services;
        _inParameters = inParameters;
        _instancePath = instancePath;
        Levels = levels;
        Level = level;
    }

    /// <summary>Brightness levels (percent) the panel accepts, ascending and never empty.</summary>
    public IReadOnlyList<int> Levels { get; }

    /// <summary>Brightness (percent) when the panel was opened, snapped to a supported level.</summary>
    public int Level { get; }

    /// <summary>Opens the first active panel, or returns null when no display supports brightness control.</summary>
    public static WmiBrightnessPanel? Open()
    {
        object? locator = null;
        object? services = null;
        object? inParameters = null;
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator", throwOnError: true)!);
            services = ((dynamic)locator!).ConnectServer(".", Namespace);

            string? instanceName = null;
            int[]? levels = null;
            var current = 0;
            foreach (var instance in Instances(services, "SELECT * FROM WmiMonitorBrightness WHERE Active = TRUE"))
            {
                try
                {
                    instanceName = PropertyValue(instance, "InstanceName") as string;
                    levels = BrightnessScale.NormalizeLevels(((IEnumerable)PropertyValue(instance, "Level")!).Cast<object>().Select(level => Convert.ToInt32(level, CultureInfo.InvariantCulture))).ToArray();
                    current = Convert.ToInt32(PropertyValue(instance, "CurrentBrightness"), CultureInfo.InvariantCulture);
                }
                finally
                {
                    Release(instance);
                }

                break;
            }

            if (instanceName is null || levels is not { Length: > 0 })
            {
                return null;
            }

            inParameters = SpawnSetBrightnessInParameters(services);
            var panel = new WmiBrightnessPanel(services, inParameters, InstancePath(instanceName), levels, BrightnessScale.Snap(current, levels));
            services = null;
            inParameters = null;
            return panel;
        }
        finally
        {
            Release(inParameters);
            Release(services);
            Release(locator);
        }
    }

    /// <summary>Sets the brightness to <paramref name="percent"/>, which must be one of <see cref="Levels"/>.</summary>
    public void SetBrightness(int percent)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // The in-parameters object is built once in Open and reused; only the level changes per write.
            SetProperty(_inParameters, "Brightness", (byte)percent);
            Release(((dynamic)_services).ExecMethod(_instancePath, SetMethod, _inParameters));
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Release(_inParameters);
            Release(_services);
        }
    }

    /// <summary>The in-parameters object for <c>WmiSetBrightness</c>, with its timeout already set.</summary>
    private static object SpawnSetBrightnessInParameters(object services)
    {
        object? methodsClass = null;
        object? methods = null;
        object? method = null;
        object? template = null;
        object? inParameters = null;
        try
        {
            methodsClass = ((dynamic)services).Get(MethodsClass);
            methods = ((dynamic)methodsClass).Methods_;
            method = ((dynamic)methods!).Item(SetMethod);
            template = ((dynamic)method!).InParameters;
            inParameters = ((dynamic)template!).SpawnInstance_();
            SetProperty(inParameters!, "Timeout", 1u);
            var result = inParameters!;
            inParameters = null;
            return result;
        }
        finally
        {
            Release(inParameters);
            Release(template);
            Release(method);
            Release(methods);
            Release(methodsClass);
        }
    }

    /// <summary>The object path of the methods instance that belongs to a brightness instance (same <c>InstanceName</c>).</summary>
    private static string InstancePath(string instanceName) =>
        $"{MethodsClass}.InstanceName=\"{instanceName.Replace(@"\", @"\\").Replace("\"", "\\\"")}\"";

    /// <summary>Enumerates a query's results; the caller releases each one it keeps or finishes with.</summary>
    private static IEnumerable<object> Instances(object services, string query)
    {
        object results = ((dynamic)services).ExecQuery(query);
        try
        {
            foreach (var instance in (IEnumerable)results)
            {
                yield return instance;
            }
        }
        finally
        {
            Release(results);
        }
    }

    // Properties go through Properties_.Item: direct late-bound property access on an SWbemObject depends on
    // dynamic name resolution and was seen returning nothing.
    private static object? PropertyValue(object instance, string name)
    {
        object? properties = null;
        object? property = null;
        try
        {
            properties = ((dynamic)instance).Properties_;
            property = ((dynamic)properties!).Item(name);
            return ((dynamic)property!).Value;
        }
        finally
        {
            Release(property);
            Release(properties);
        }
    }

    private static void SetProperty(object instance, string name, object value)
    {
        object? properties = null;
        object? property = null;
        try
        {
            properties = ((dynamic)instance).Properties_;
            property = ((dynamic)properties!).Item(name);
            ((dynamic)property!).Value = value;
        }
        finally
        {
            Release(property);
            Release(properties);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}
