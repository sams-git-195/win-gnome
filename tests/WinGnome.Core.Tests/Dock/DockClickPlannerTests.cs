using WinGnome.Core.Dock;
using WinGnome.Core.Settings;

namespace WinGnome.Core.Tests.Dock;

public class DockClickPlannerTests
{
    private static DockApp App(bool focused, params nint[] windows) =>
        new("id", "App", "launch", null, null, IsPinned: true, windows, focused);

    private static DockClickResult Plan(DockApp app, DockClickAction action, nint foreground = 0) =>
        DockClickPlanner.Plan(app, foreground, action, lastActivated: 0);

    [Theory]
    [InlineData(DockClickAction.FocusOrMinimize)]
    [InlineData(DockClickAction.Cycle)]
    [InlineData(DockClickAction.Previews)]
    public void NotRunning_AlwaysLaunches(DockClickAction action)
    {
        var result = Plan(App(false), action);

        Assert.Equal(DockClickKind.Launch, result.Kind);
        Assert.Equal(0, result.Window);
    }

    [Fact]
    public void FocusOrMinimize_NotFocused_ActivatesMostRecentWindow()
    {
        var result = Plan(App(false, 11, 22, 33), DockClickAction.FocusOrMinimize, foreground: 99);

        Assert.Equal(new DockClickResult(DockClickKind.Activate, 11), result);
    }

    [Fact]
    public void FocusOrMinimize_Focused_MinimisesTheForegroundWindow()
    {
        var result = Plan(App(true, 11, 22, 33), DockClickAction.FocusOrMinimize, foreground: 22);

        Assert.Equal(new DockClickResult(DockClickKind.Minimize, 22), result);
    }

    [Fact]
    public void Cycle_NotFocused_ActivatesFirstWindow()
    {
        var result = Plan(App(false, 11, 22), DockClickAction.Cycle, foreground: 99);

        Assert.Equal(new DockClickResult(DockClickKind.Activate, 11), result);
    }

    [Fact]
    public void Cycle_FocusedWithSeveralWindows_ActivatesTheNextOne()
    {
        var app = App(true, 11, 22, 33);

        Assert.Equal(new DockClickResult(DockClickKind.Activate, 22), Plan(app, DockClickAction.Cycle, foreground: 11));
        Assert.Equal(new DockClickResult(DockClickKind.Activate, 33), Plan(app, DockClickAction.Cycle, foreground: 22));
    }

    [Fact]
    public void Cycle_WrapsAroundAtTheEnd()
    {
        var result = Plan(App(true, 11, 22, 33), DockClickAction.Cycle, foreground: 33);

        Assert.Equal(new DockClickResult(DockClickKind.Activate, 11), result);
    }

    [Fact]
    public void Cycle_FocusedWithSingleWindow_Minimises()
    {
        var result = Plan(App(true, 11), DockClickAction.Cycle, foreground: 11);

        Assert.Equal(new DockClickResult(DockClickKind.Minimize, 11), result);
    }

    [Fact]
    public void Previews_SeveralWindows_ShowsPreviews_EvenWhenFocused()
    {
        Assert.Equal(DockClickKind.ShowPreviews, Plan(App(false, 11, 22), DockClickAction.Previews, foreground: 99).Kind);
        Assert.Equal(DockClickKind.ShowPreviews, Plan(App(true, 11, 22), DockClickAction.Previews, foreground: 11).Kind);
    }

    [Fact]
    public void Previews_SingleWindow_BehavesLikeFocusOrMinimize()
    {
        Assert.Equal(new DockClickResult(DockClickKind.Activate, 11), Plan(App(false, 11), DockClickAction.Previews, foreground: 99));
        Assert.Equal(new DockClickResult(DockClickKind.Minimize, 11), Plan(App(true, 11), DockClickAction.Previews, foreground: 11));
    }

    [Fact]
    public void InconsistentFocusFlag_IsNotTrusted()
    {
        // IsFocused says true, but the foreground window does not belong to the app: treat as not focused.
        var result = Plan(App(true, 11, 22), DockClickAction.FocusOrMinimize, foreground: 99);

        Assert.Equal(new DockClickResult(DockClickKind.Activate, 11), result);
    }

    [Fact]
    public void LastActivated_DoesNotChangeTheOutcome()
    {
        var app = App(false, 11, 22);
        var a = DockClickPlanner.Plan(app, 99, DockClickAction.Cycle, 0);
        var b = DockClickPlanner.Plan(app, 99, DockClickAction.Cycle, 22);

        Assert.Equal(a, b);
    }

    [Fact]
    public void UnknownAction_DoesNothing()
    {
        var result = Plan(App(false, 11), (DockClickAction)99);
        Assert.Equal(DockClickKind.None, result.Kind);
    }
}
