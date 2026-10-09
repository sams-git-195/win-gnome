using System.Globalization;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Management.Deployment;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Panels.Apps;

/// <summary>What a packaged row shows once expanded.</summary>
internal sealed record PackageDetails(string? Publisher, string? Version, DateTimeOffset? InstalledOn, bool CanRemove);

/// <summary>How a packaged-app removal ended.</summary>
internal enum PackageRemoval
{
    Removed,

    /// <summary>A system or framework package: Windows Settings decides whether it can go.</summary>
    NotRemovable,

    /// <summary>No package of that family is installed for this user any more.</summary>
    NotFound,
    Failed,
}

/// <summary>
/// The package API, for the current user only (no administrator rights). It is the only class that touches
/// Windows.Management.Deployment, and is called only when a packaged row is expanded or uninstalled, so opening the
/// Apps panel loads no WinRT (the projection then stays loaded for the session). Calls block: run them on a worker.
/// </summary>
internal static class PackagedAppsService
{
    /// <summary>Publisher, version and install date of the family's main package, or null when none is installed for this user.</summary>
    public static PackageDetails? Details(string familyName)
    {
        var package = MainPackage(new PackageManager(), familyName);
        if (package is null)
        {
            return null;
        }

        var version = package.Id.Version;
        return new PackageDetails(
            Text(() => package.PublisherDisplayName),
            string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"),
            Date(() => package.InstalledDate),
            IsRemovable(package));
    }

    /// <summary>Removes the family's app package for the current user. Blocks until Windows has finished.</summary>
    public static PackageRemoval Remove(string familyName)
    {
        var manager = new PackageManager();
        var package = MainPackage(manager, familyName);
        if (package is null)
        {
            return PackageRemoval.NotFound;
        }

        if (!IsRemovable(package))
        {
            Log.Info($"Apps: {familyName} is a system or framework package; not removed");
            return PackageRemoval.NotRemovable;
        }

        var fullName = package.Id.FullName;
        Log.Info($"Apps: removing {fullName} for the current user");
        var result = manager.RemovePackageAsync(fullName).AsTask().GetAwaiter().GetResult();
        if (result.ExtendedErrorCode is { } error)
        {
            Log.Warn($"Apps: removing {fullName} failed: {result.ErrorText} (0x{error.HResult:X8})");
            return PackageRemoval.Failed;
        }

        Log.Info($"Apps: removed {fullName}");
        return PackageRemoval.Removed;
    }

    /// <summary>The family's app package (not a framework, resource or optional package) for the current user.</summary>
    private static Package? MainPackage(PackageManager manager, string familyName) =>
        manager.FindPackagesForUser(string.Empty, familyName).FirstOrDefault(p => !p.IsFramework && !p.IsResourcePackage && !p.IsOptional);

    /// <summary>Windows Settings offers no uninstall for system-signed, framework or non-removable apps; neither does WinGnome.</summary>
    private static bool IsRemovable(Package package) =>
        !package.IsFramework && package.SignatureKind != PackageSignatureKind.System;

    // Some packages (staged, or with a broken manifest) throw from these getters; the row then just leaves the value out.
    private static string? Text(Func<string> read)
    {
        try
        {
            var text = read();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return null;
        }
    }

    private static DateTimeOffset? Date(Func<DateTimeOffset> read)
    {
        try
        {
            var date = read();
            return date.Year > 1601 ? date : null;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return null;
        }
    }
}
