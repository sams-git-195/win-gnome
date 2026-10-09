using System.Runtime.InteropServices;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.WindowsUpdate;

/// <summary>
/// Reads Windows Update's state through the documented Windows Update Agent: when updates were last checked and
/// installed, whether a restart is pending, and the updates it already knows about. The search is offline
/// (<c>Online = false</c>): it reads the agent's cache, never reaches the network, never starts a scan and never
/// installs anything. Call it from one background thread: it creates and releases every COM object itself.
/// </summary>
internal static class UpdateStatusService
{
    private const string PendingCriteria = "IsInstalled=0 and IsHidden=0";
    private static readonly TimeSpan AbortGrace = TimeSpan.FromSeconds(3);

    /// <summary>Dates before this are an OLE "zero" date, which WUA returns for "never".</summary>
    private static readonly DateTime EarliestRealDate = new(1980, 1, 1);

    /// <summary>
    /// Reads the status. A search that doesn't finish within <paramref name="searchTimeout"/> is aborted and reported
    /// as <see cref="UpdateStatusText.TimedOut"/>. The first failure's HRESULT is kept; the other values are still filled in.
    /// </summary>
    public static UpdateStatus Read(TimeSpan searchTimeout)
    {
        var com = new List<object>();
        var failure = new FirstFailure();
        try
        {
            var dates = Guard("search and install times", failure, () => ReadDates(com));
            var rebootRequired = Guard("restart state", failure, () => ReadRebootRequired(com));
            var pending = Guard("pending updates", failure, () => SearchOffline(com, searchTimeout, failure));
            return new UpdateStatus(dates.LastChecked, dates.LastInstalled, pending ?? [], rebootRequired, failure.HResult);
        }
        finally
        {
            for (var i = com.Count - 1; i >= 0; i--)
            {
                Marshal.FinalReleaseComObject(com[i]);
            }
        }
    }

    private static (DateTime? LastChecked, DateTime? LastInstalled) ReadDates(List<object> com)
    {
        dynamic auto = Create(com, WindowsUpdateProgIds.AutoUpdate);
        dynamic results = Track(com, auto.Results);
        return (ToLocal(results.LastSearchSuccessDate), ToLocal(results.LastInstallationSuccessDate));
    }

    private static bool ReadRebootRequired(List<object> com)
    {
        dynamic info = Create(com, WindowsUpdateProgIds.SystemInfo);
        return (bool)info.RebootRequired;
    }

    private static List<string> SearchOffline(List<object> com, TimeSpan timeout, FirstFailure failure)
    {
        dynamic session = Create(com, WindowsUpdateProgIds.Session);
        dynamic searcher = Track(com, session.CreateUpdateSearcher());
        searcher.Online = false;

        using var finished = new ManualResetEventSlim();
        var callback = new SearchFinished(finished);
        dynamic job = Track(com, searcher.BeginSearch(PendingCriteria, callback, null));
        try
        {
            if (!finished.Wait(timeout))
            {
                Log.Warn($"Windows Update: the offline search took longer than {timeout.TotalSeconds:0} s; aborting it");
                AbortSearch(job);

                // Whether or not the abort lands in time, nothing is left blocked: the job is released with the rest.
                finished.Wait(AbortGrace);
                failure.Record(UpdateStatusText.TimedOut);
                return [];
            }

            dynamic result = Track(com, searcher.EndSearch(job));
            dynamic updates = Track(com, result.Updates);
            var titles = new List<string>();
            int count = updates.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic update = Track(com, updates.Item(i));
                titles.Add((string)update.Title);
            }

            return titles;
        }
        finally
        {
            GC.KeepAlive(callback);
        }
    }

    private static void AbortSearch(dynamic job)
    {
        try
        {
            job.RequestAbort();
        }
        catch (COMException ex)
        {
            Log.Warn($"Windows Update: could not abort the search (0x{(uint)ex.HResult:X8})", ex);
        }
    }

    /// <summary>Runs <paramref name="read"/>; a COM or binding failure is logged and its HRESULT kept in <paramref name="failure"/>.</summary>
    private static T? Guard<T>(string what, FirstFailure failure, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // Feature boundary: the other values are still shown, and the failure is logged with the code.
            Log.Warn($"Windows Update: could not read the {what}", ex);
            failure.Record(ex.HResult);
            return default;
        }
    }

    private static object Create(List<object> com, string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: true)!;
        return Track(com, Activator.CreateInstance(type)!);
    }

    private static object Track(List<object> com, object o)
    {
        if (Marshal.IsComObject(o))
        {
            com.Add(o);
        }

        return o;
    }

    /// <summary>WUA reports dates in UTC; null for a "never" (zero) date.</summary>
    private static DateTime? ToLocal(object value)
    {
        if (value is not DateTime date || date < EarliestRealDate)
        {
            return null;
        }

        return DateTime.SpecifyKind(date, DateTimeKind.Utc).ToLocalTime();
    }

    /// <summary>The HRESULT of the first thing that went wrong, kept so later successes don't hide it.</summary>
    private sealed class FirstFailure
    {
        public int? HResult { get; private set; }

        public void Record(int hresult) => HResult ??= hresult;
    }

    /// <summary>Sets an event when the asynchronous search ends, however it ends.</summary>
    [ComVisible(true)]
    private sealed class SearchFinished(ManualResetEventSlim finished) : ISearchCompletedCallback
    {
        public void Invoke(object searchJob, object callbackArgs) => finished.Set();
    }
}
