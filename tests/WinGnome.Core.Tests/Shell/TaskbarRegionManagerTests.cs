using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class TaskbarRegionManagerTests
{
    private const int Primary = 100;
    private const int Secondary = 200;
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeHost : ITaskbarRegionHost
    {
        /// <summary>Live Explorer taskbar windows and their region types.</summary>
        public Dictionary<long, int> Explorer { get; } = new() { [Primary] = TaskbarRegionPolicy.ErrorRegion, [Secondary] = TaskbarRegionPolicy.ErrorRegion };

        /// <summary>Live windows of other processes (a reused handle) and their region types.</summary>
        public Dictionary<long, int> Foreign { get; } = [];

        public HashSet<long> Hung { get; } = [];
        public HashSet<long> RemoveFails { get; } = [];
        public List<long> Records { get; } = [];
        public bool RecordFails { get; set; }
        public int RecordCalls { get; private set; }
        public List<long> EmptyCalls { get; } = [];
        public List<long> RemoveCalls { get; } = [];
        public List<string> Infos { get; } = [];
        public List<string> Warns { get; } = [];

        public bool IsExplorerTaskbarWindow(long hwnd) => Explorer.ContainsKey(hwnd);

        public IReadOnlyList<long> FindExplorerTaskbarWindows() => [.. Explorer.Keys];

        public bool IsHung(long hwnd) => Hung.Contains(hwnd);

        public int GetRegionType(long hwnd) => Explorer.TryGetValue(hwnd, out var type) ? type : Foreign.GetValueOrDefault(hwnd);

        public bool TryEmpty(long hwnd)
        {
            EmptyCalls.Add(hwnd);
            if (Explorer.ContainsKey(hwnd))
            {
                Explorer[hwnd] = TaskbarRegionPolicy.NullRegion;
            }
            else
            {
                Foreign[hwnd] = TaskbarRegionPolicy.NullRegion;
            }

            return true;
        }

        public bool RemoveIfEmpty(long hwnd)
        {
            RemoveCalls.Add(hwnd);
            if (GetRegionType(hwnd) != TaskbarRegionPolicy.NullRegion || RemoveFails.Contains(hwnd))
            {
                return false;
            }

            if (Explorer.ContainsKey(hwnd))
            {
                Explorer[hwnd] = TaskbarRegionPolicy.ErrorRegion;
            }
            else
            {
                Foreign[hwnd] = TaskbarRegionPolicy.ErrorRegion;
            }

            return true;
        }

        public IReadOnlyList<long> ReadRecords() => [.. Records];

        public bool Record(long hwnd)
        {
            RecordCalls++;
            if (RecordFails)
            {
                return false;
            }

            if (!Records.Contains(hwnd))
            {
                Records.Add(hwnd);
            }

            return true;
        }

        public void ReplaceRecords(IReadOnlyList<long> hwnds)
        {
            Records.Clear();
            Records.AddRange(hwnds);
        }

        public void Info(string message) => Infos.Add(message);

        public void Warn(string message) => Warns.Add(message);

        public void ThrottledInfo(string key, string message) => Infos.Add(message);

        public void ThrottledWarn(string key, string message) => Warns.Add(message);
    }

    private static (TaskbarRegionManager Manager, FakeHost Host) Started()
    {
        var host = new FakeHost();
        var manager = new TaskbarRegionManager(host, () => T0);
        manager.Start();
        return (manager, host);
    }

    [Fact]
    public void Start_EmptiesEveryExplorerTaskbarWindowAndRecordsThemFirst()
    {
        var (manager, host) = Started();

        Assert.True(manager.Active);
        Assert.Equal([Primary, Secondary], host.EmptyCalls.Select(h => (int)h));
        Assert.Equal([100L, 200L], host.Records);
        Assert.Equal(["Taskbar regions emptied on 2 window(s)"], host.Infos);
    }

    [Fact]
    public void Start_MarkerCannotRecord_EmptiesNothing()
    {
        var host = new FakeHost { RecordFails = true };
        var manager = new TaskbarRegionManager(host, () => T0);

        manager.Start();

        Assert.Empty(host.EmptyCalls);
        Assert.Equal(["Taskbar regions emptied on 0 window(s)"], host.Infos);
    }

    [Fact]
    public void LocationChange_KnownWindowRegionChangedByExplorer_IsEmptiedAgainWithoutAnotherMarkerWrite()
    {
        var (manager, host) = Started();
        host.Explorer[Secondary] = TaskbarRegionPolicy.SimpleRegion;
        var recordCalls = host.RecordCalls;

        manager.OnWindowEvent(TaskbarRegionEvent.LocationChange, Secondary);

        Assert.Equal([100L, 200L, 200L], host.EmptyCalls);
        Assert.Equal(recordCalls, host.RecordCalls);
    }

    [Fact]
    public void LocationChange_RegionStillEmpty_DoesNothing()
    {
        var (manager, host) = Started();
        host.EmptyCalls.Clear();

        manager.OnWindowEvent(TaskbarRegionEvent.LocationChange, Primary);

        Assert.Empty(host.EmptyCalls);
    }

    [Fact]
    public void LocationChange_UnknownWindow_IsIgnored()
    {
        var (manager, host) = Started();
        host.EmptyCalls.Clear();
        host.Explorer[300] = TaskbarRegionPolicy.ErrorRegion;

        manager.OnWindowEvent(TaskbarRegionEvent.LocationChange, 300);

        Assert.Empty(host.EmptyCalls);
    }

    [Fact]
    public void Show_NewSecondaryTaskbar_IsEmptiedAndRecorded()
    {
        var (manager, host) = Started();
        host.Explorer[300] = TaskbarRegionPolicy.ErrorRegion;

        manager.OnWindowEvent(TaskbarRegionEvent.ShowOrCreate, 300);

        Assert.Contains(300L, host.Records);
        Assert.Equal(TaskbarRegionPolicy.NullRegion, host.Explorer[300]);
    }

    [Fact]
    public void Show_WindowOfAnotherProcess_IsNeverTouched()
    {
        var (manager, host) = Started();
        host.Foreign[500] = TaskbarRegionPolicy.ErrorRegion;

        manager.OnWindowEvent(TaskbarRegionEvent.ShowOrCreate, 500);

        Assert.DoesNotContain(500L, host.EmptyCalls);
        Assert.DoesNotContain(500L, host.Records);
    }

    [Fact]
    public void LocationChange_KnownHandleNowAnotherProcessWindow_IsNotTouched()
    {
        var (manager, host) = Started();
        host.Explorer.Remove(Secondary);
        host.Foreign[Secondary] = TaskbarRegionPolicy.ErrorRegion;

        manager.OnWindowEvent(TaskbarRegionEvent.LocationChange, Secondary);

        Assert.Equal([100L, 200L], host.EmptyCalls);
        Assert.Equal(TaskbarRegionPolicy.ErrorRegion, host.Foreign[Secondary]);
    }

    [Fact]
    public void Suspend_RemovesRegionsOfKnownExplorerWindowsOnly()
    {
        var (manager, host) = Started();
        host.Explorer.Remove(Secondary);
        host.Foreign[Secondary] = TaskbarRegionPolicy.NullRegion;

        manager.Suspend();

        Assert.Equal([100L], host.RemoveCalls);
        Assert.Equal(TaskbarRegionPolicy.NullRegion, host.Foreign[Secondary]);
    }

    [Fact]
    public void Suspend_IgnoresWindowEventsUntilResumed()
    {
        var (manager, host) = Started();
        manager.Suspend();
        host.EmptyCalls.Clear();
        host.Explorer[Primary] = TaskbarRegionPolicy.SimpleRegion;

        manager.OnWindowEvent(TaskbarRegionEvent.LocationChange, Primary);

        Assert.Empty(host.EmptyCalls);
    }

    [Fact]
    public void Resume_WithReapply_EmptiesTheWindowsAgain()
    {
        var (manager, host) = Started();
        manager.Suspend();
        host.EmptyCalls.Clear();

        manager.Resume(reapply: true);

        Assert.Equal([100L, 200L], host.EmptyCalls);
    }

    [Fact]
    public void DestroyWhileSuspended_HandleReusedByAnotherProcess_IsNotEmptiedOnResume()
    {
        var (manager, host) = Started();
        manager.Suspend();
        manager.OnWindowEvent(TaskbarRegionEvent.Destroy, Secondary);
        host.Explorer.Remove(Secondary);
        host.Foreign[Secondary] = TaskbarRegionPolicy.ErrorRegion;
        host.EmptyCalls.Clear();

        manager.Resume(reapply: true);
        manager.OnWindowEvent(TaskbarRegionEvent.LocationChange, Secondary);

        Assert.DoesNotContain(Secondary, host.EmptyCalls);
        Assert.Equal(TaskbarRegionPolicy.ErrorRegion, host.Foreign[Secondary]);
    }

    [Fact]
    public void Resume_WithoutReapply_ForgetsKnownWindows()
    {
        var (manager, host) = Started();
        manager.Suspend();
        manager.Resume(reapply: false);
        host.EmptyCalls.Clear();
        host.Explorer[Primary] = TaskbarRegionPolicy.SimpleRegion;

        manager.OnWindowEvent(TaskbarRegionEvent.LocationChange, Primary);

        Assert.Empty(host.EmptyCalls);
    }

    [Fact]
    public void Stop_RemovesEveryRegionAndClearsTheRecord()
    {
        var (manager, host) = Started();

        manager.Stop();

        Assert.False(manager.Active);
        Assert.All(host.Explorer.Values, type => Assert.Equal(TaskbarRegionPolicy.ErrorRegion, type));
        Assert.Empty(host.Records);
        Assert.Equal("Taskbar regions removed from 2 window(s); re-applied 0 time(s) while active", host.Infos[^1]);
    }

    [Fact]
    public void Stop_RemovalFailsOnALiveWindow_KeepsItRecorded()
    {
        var (manager, host) = Started();
        host.RemoveFails.Add(Secondary);

        manager.Stop();

        Assert.Equal([200L], host.Records);
    }

    [Fact]
    public void Stop_RecordedWindowGone_DropsTheRecord()
    {
        var (manager, host) = Started();
        host.Explorer.Remove(Secondary);

        manager.Stop();

        Assert.Empty(host.Records);
    }

    [Fact]
    public void Breaker_ExplorerKeepsRewriting_StopsManagingForTheSession()
    {
        var (manager, host) = Started();
        for (var i = 0; i < 25 && manager.Active; i++)
        {
            host.Explorer[Secondary] = TaskbarRegionPolicy.SimpleRegion;
            manager.OnWindowEvent(TaskbarRegionEvent.LocationChange, Secondary);
        }

        Assert.True(manager.Tripped);
        Assert.False(manager.Active);
        Assert.Equal(TaskbarRegionPolicy.ErrorRegion, host.Explorer[Primary]);
        Assert.Single(host.Warns);

        manager.Start();
        Assert.False(manager.Active);
    }

    [Fact]
    public void Apply_HungWindow_IsSkippedAndLoggedOnce()
    {
        var host = new FakeHost();
        host.Hung.Add(Secondary);
        var manager = new TaskbarRegionManager(host, () => T0);
        manager.Start();
        manager.Apply();
        manager.Apply();

        Assert.DoesNotContain(Secondary, host.EmptyCalls);
        Assert.Single(host.Warns);
    }

    [Fact]
    public void Sweep_RemovesAnEmptyRegionNobodyRecorded()
    {
        var host = new FakeHost();
        host.Explorer[Primary] = TaskbarRegionPolicy.NullRegion;

        var result = TaskbarRegionManager.Sweep(host, []);

        Assert.Equal(1, result.Removed);
        Assert.Equal(TaskbarRegionPolicy.ErrorRegion, host.Explorer[Primary]);
    }

    [Fact]
    public void Sweep_LeavesExplorersOwnRegionsAndOtherProcessesAlone()
    {
        var host = new FakeHost();
        host.Explorer[Primary] = TaskbarRegionPolicy.SimpleRegion;
        host.Foreign[500] = TaskbarRegionPolicy.NullRegion;

        var result = TaskbarRegionManager.Sweep(host, [Primary, 500]);

        Assert.Equal(0, result.Removed);
        Assert.Empty(host.RemoveCalls);
    }

    [Fact]
    public void Sweep_RemovalFails_ReportsTheWindowAsRemaining()
    {
        var host = new FakeHost();
        host.Explorer[Primary] = TaskbarRegionPolicy.NullRegion;
        host.RemoveFails.Add(Primary);

        var result = TaskbarRegionManager.Sweep(host, [Primary]);

        Assert.Equal([100L], result.Remaining);
    }

    [Fact]
    public void Sweep_HungWindow_IsNotTouchedButStaysRemaining()
    {
        var host = new FakeHost();
        host.Explorer[Primary] = TaskbarRegionPolicy.NullRegion;
        host.Hung.Add(Primary);

        var result = TaskbarRegionManager.Sweep(host, [Primary]);

        Assert.Empty(host.RemoveCalls);
        Assert.Equal([100L], result.Remaining);
    }
}
