using WinGnome.Core.Dock;

namespace WinGnome.Core.Tests.Dock;

public class DockRunningOrderTests
{
    /// <summary>An unpinned running app named after the exe, windows in the order given (z-order, topmost first).</summary>
    private static DockApp Run(string name, params nint[] windows) =>
        new($@"path:c:\{name}.exe", name, null, $@"C:\{name}.exe", null, IsPinned: false, windows, IsFocused: false,
            MostRecentWindow: windows.Length > 0 ? windows[0] : 0);

    private static DockApp Pinned(string launchId, params nint[] windows) =>
        new($@"path:c:\{launchId}.exe", launchId, launchId, null, null, IsPinned: true, windows, IsFocused: false,
            MostRecentWindow: windows.Length > 0 ? windows[0] : 0);

    private static string[] Names(IReadOnlyList<DockApp> apps) => apps.Select(a => a.Name).ToArray();

    private static nint[] WindowsOf(IReadOnlyList<DockApp> apps, string name) =>
        apps.Single(a => a.Name == name).Windows.ToArray();

    [Fact]
    public void Apply_ZOrderFlips_KeepsFirstAppearanceOrder()
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 1), Run("B", 2)], 0);

        var result = order.Apply([Run("B", 2), Run("A", 1)], 100);

        Assert.Equal(["A", "B"], Names(result));
    }

    [Fact]
    public void Apply_NewApp_IsAppendedEvenWhenTopmost()
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 1), Run("B", 2)], 0);

        var result = order.Apply([Run("C", 3), Run("A", 1), Run("B", 2)], 100);

        Assert.Equal(["A", "B", "C"], Names(result));
    }

    [Fact]
    public void Apply_TwoNewAppsInOneCall_AreNumberedInInputOrder()
    {
        var order = new DockRunningOrder();
        order.Apply([], 0);

        var result = order.Apply([Run("C", 3), Run("A", 1)], 100);

        Assert.Equal(["C", "A"], Names(result));
    }

    [Theory]
    [InlineData(2100, "ABC")]
    [InlineData(2101, "BCA")]
    public void Apply_AppBackAfterAbsence_KeepsItsSlotUpToTheGraceBoundary(long backAtMs, string expected)
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 1), Run("B", 2), Run("C", 3)], 0);
        order.Apply([Run("B", 2), Run("C", 3)], 100);

        var result = order.Apply([Run("A", 1), Run("B", 2), Run("C", 3)], backAtMs);

        Assert.Equal(expected, string.Concat(Names(result)));
    }

    [Fact]
    public void Apply_AbsenceIsMeasuredFromTheFirstMiss_NotFromTheLastSighting()
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 1), Run("B", 2)], 0);
        order.Apply([Run("B", 2)], 1900);

        var result = order.Apply([Run("A", 1), Run("B", 2)], 2500);

        Assert.Equal(["A", "B"], Names(result));
    }

    [Fact]
    public void Apply_RepeatedMisses_DoNotRestartTheGrace()
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 1), Run("B", 2)], 0);
        order.Apply([Run("B", 2)], 100);
        order.Apply([Run("B", 2)], 2000);

        var result = order.Apply([Run("A", 1), Run("B", 2)], 2200);

        Assert.Equal(["B", "A"], Names(result));
    }

    [Fact]
    public void Apply_LongGapWhilePresent_DoesNotReorder()
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 1), Run("B", 2)], 0);

        var result = order.Apply([Run("B", 2), Run("A", 1)], 600_000);

        Assert.Equal(["A", "B"], Names(result));
    }

    [Fact]
    public void Apply_PinnedApps_KeepInputOrder()
    {
        var order = new DockRunningOrder();
        Assert.Equal(["P2", "P1", "A"], Names(order.Apply([Pinned("P2"), Pinned("P1"), Run("A", 1)], 0)));

        var result = order.Apply([Pinned("P1"), Pinned("P2"), Run("A", 1)], 100);

        Assert.Equal(["P1", "P2", "A"], Names(result));
    }

    [Fact]
    public void Apply_PinnedAppsStayAheadOfUnpinned_WhateverTheirSequence()
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 1)], 0);

        var result = order.Apply([Run("A", 1), Pinned("P1", 5)], 100);

        Assert.Equal(["P1", "A"], Names(result));
    }

    [Fact]
    public void Apply_WindowsInsideAnApp_KeepFirstAppearanceOrder()
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 1, 2)], 0);

        var result = order.Apply([Run("A", 2, 1)], 100);

        Assert.Equal(new nint[] { 1, 2 }, WindowsOf(result, "A"));
        Assert.Equal(2, result[0].MostRecentWindow);
    }

    [Fact]
    public void Apply_WindowsInsideAPinnedApp_KeepFirstAppearanceOrder()
    {
        var order = new DockRunningOrder();
        order.Apply([Pinned("P", 10, 20, 30)], 0);

        var result = order.Apply([Pinned("P", 30, 10, 20)], 100);

        Assert.Equal(new nint[] { 10, 20, 30 }, WindowsOf(result, "P"));
    }

    [Fact]
    public void Apply_NewWindow_IsAppendedInsideItsApp()
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 1, 2)], 0);

        var result = order.Apply([Run("A", 3, 1, 2)], 100);

        Assert.Equal(new nint[] { 1, 2, 3 }, WindowsOf(result, "A"));
    }

    [Theory]
    [InlineData(2100, "123")]
    [InlineData(2101, "231")]
    public void Apply_WindowBackAfterAbsence_KeepsItsSlotUpToTheGraceBoundary(long backAtMs, string expected)
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 1, 2, 3)], 0);
        order.Apply([Run("A", 2, 3)], 100);

        var result = order.Apply([Run("A", 1, 2, 3)], backAtMs);

        Assert.Equal(expected, string.Concat(WindowsOf(result, "A")));
    }

    [Fact]
    public void Apply_HandleReusedByAnotherApp_IsNewInThatApp()
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 5)], 0);

        var result = order.Apply([Run("A", 6), Run("B", 7, 5)], 100);

        Assert.Equal(new nint[] { 7, 5 }, WindowsOf(result, "B"));
    }

    [Fact]
    public void Apply_IdentityCaseDiffers_KeepsTheSlot()
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 1), Run("B", 2)], 0);
        var shouting = Run("A", 1) with { Identity = @"PATH:C:\A.EXE" };

        var result = order.Apply([Run("B", 2), shouting], 100);

        Assert.Equal(["A", "B"], Names(result));
    }

    [Fact]
    public void Apply_UnpinnedThenPinned_LeavesOtherSlotsInOrder()
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 1), Run("B", 2), Run("C", 3)], 0);

        var result = order.Apply([Pinned("A", 1), Run("C", 3), Run("B", 2)], 100);

        Assert.Equal(["A", "B", "C"], Names(result));
        Assert.True(result[0].IsPinned);
    }

    [Theory]
    [InlineData(2500, "ABC")]
    [InlineData(2501, "BCA")]
    public void Apply_PinnedThenUnpinnedAgain_KeepsTheOldSlotWithinGrace(long unpinnedAtMs, string expected)
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 1), Run("B", 2), Run("C", 3)], 0);
        order.Apply([Pinned("A", 1), Run("B", 2), Run("C", 3)], 500);

        var result = order.Apply([Run("C", 3), Run("A", 1), Run("B", 2)], unpinnedAtMs);

        Assert.Equal(expected, string.Concat(Names(result)));
    }

    [Fact]
    public void Apply_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(new DockRunningOrder().Apply([], 0));
    }

    [Fact]
    public void Apply_AllGoneThenBackAfterGrace_StartsFresh()
    {
        var order = new DockRunningOrder();
        order.Apply([Run("A", 1), Run("B", 2)], 0);
        order.Apply([], 100);

        var result = order.Apply([Run("B", 2), Run("A", 1)], 3000);

        Assert.Equal(["B", "A"], Names(result));
    }

    [Fact]
    public void Apply_KeepsEveryOtherField_AndPinnedAppWithoutWindows()
    {
        var order = new DockRunningOrder();
        var focused = Run("A", 4, 5) with { IsFocused = true };

        var result = order.Apply([focused, Pinned("P")], 0);

        Assert.Equal(["P", "A"], Names(result));
        Assert.Empty(result[0].Windows);
        Assert.True(result[1].IsFocused);
        Assert.Equal(4, result[1].MostRecentWindow);
        Assert.Equal(@"C:\A.exe", result[1].ProcessPath);
    }
}
