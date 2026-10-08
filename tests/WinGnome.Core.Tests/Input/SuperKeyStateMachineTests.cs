using WinGnome.Core.Input;

namespace WinGnome.Core.Tests.Input;

public class SuperKeyStateMachineTests
{
    private const int LWin = 0x5B;
    private const int RWin = 0x5C;
    private const int KeyE = 0x45;
    private const int Ctrl = 0xA2;

    [Fact]
    public void WinPressedAndReleasedAlone_OpensOverview()
    {
        var m = new SuperKeyStateMachine();

        Assert.Equal(SuperKeyAction.PassThrough, m.OnKeyDown(LWin));
        Assert.Equal(SuperKeyAction.SuppressAndOpenOverview, m.OnKeyUp(LWin));
    }

    [Fact]
    public void RightWin_WorksToo()
    {
        var m = new SuperKeyStateMachine();

        m.OnKeyDown(RWin);

        Assert.Equal(SuperKeyAction.SuppressAndOpenOverview, m.OnKeyUp(RWin));
    }

    [Fact]
    public void Constants_MatchWin32()
    {
        Assert.Equal(0x5B, SuperKeyStateMachine.VkLeftWin);
        Assert.Equal(0x5C, SuperKeyStateMachine.VkRightWin);
    }

    [Fact]
    public void OtherKeyWhileWinHeld_MakesItACombo()
    {
        var m = new SuperKeyStateMachine();

        m.OnKeyDown(LWin);
        Assert.Equal(SuperKeyAction.PassThrough, m.OnKeyDown(KeyE));
        Assert.Equal(SuperKeyAction.PassThrough, m.OnKeyUp(KeyE));

        Assert.Equal(SuperKeyAction.PassThrough, m.OnKeyUp(LWin));
    }

    [Fact]
    public void Autorepeat_KeepsItArmed()
    {
        var m = new SuperKeyStateMachine();

        m.OnKeyDown(LWin);
        m.OnKeyDown(LWin);
        m.OnKeyDown(LWin);

        Assert.Equal(SuperKeyAction.SuppressAndOpenOverview, m.OnKeyUp(LWin));
    }

    [Fact]
    public void Autorepeat_AfterACombo_DoesNotRearm()
    {
        var m = new SuperKeyStateMachine();

        m.OnKeyDown(LWin);
        m.OnKeyDown(KeyE);
        m.OnKeyDown(LWin);

        Assert.Equal(SuperKeyAction.PassThrough, m.OnKeyUp(LWin));
    }

    [Fact]
    public void MouseClickWhileWinHeld_MakesItACombo()
    {
        var m = new SuperKeyStateMachine();

        m.OnKeyDown(LWin);
        m.OnMouseButton();

        Assert.Equal(SuperKeyAction.PassThrough, m.OnKeyUp(LWin));
    }

    [Fact]
    public void MouseClickWithoutWin_HasNoEffectOnALaterTap()
    {
        var m = new SuperKeyStateMachine();

        m.OnMouseButton();
        m.OnKeyDown(LWin);

        Assert.Equal(SuperKeyAction.SuppressAndOpenOverview, m.OnKeyUp(LWin));
    }

    [Fact]
    public void KeyPressedBeforeWin_DoesNotDisarm()
    {
        var m = new SuperKeyStateMachine();

        m.OnKeyDown(Ctrl);
        m.OnKeyUp(Ctrl);
        m.OnKeyDown(LWin);

        Assert.Equal(SuperKeyAction.SuppressAndOpenOverview, m.OnKeyUp(LWin));
    }

    [Fact]
    public void AfterACombo_TheNextCleanTapWorksAgain()
    {
        var m = new SuperKeyStateMachine();
        m.OnKeyDown(LWin);
        m.OnKeyDown(KeyE);
        m.OnKeyUp(KeyE);
        m.OnKeyUp(LWin);

        m.OnKeyDown(LWin);

        Assert.Equal(SuperKeyAction.SuppressAndOpenOverview, m.OnKeyUp(LWin));
    }

    [Fact]
    public void TwoTapsInARow_EachOpenTheOverview()
    {
        var m = new SuperKeyStateMachine();

        for (var i = 0; i < 3; i++)
        {
            m.OnKeyDown(LWin);
            Assert.Equal(SuperKeyAction.SuppressAndOpenOverview, m.OnKeyUp(LWin));
        }
    }

    [Fact]
    public void ReleaseWithoutPress_PassesThrough()
    {
        var m = new SuperKeyStateMachine();
        Assert.Equal(SuperKeyAction.PassThrough, m.OnKeyUp(LWin));
    }

    [Fact]
    public void ReleaseTwice_OnlyTheFirstCounts()
    {
        var m = new SuperKeyStateMachine();
        m.OnKeyDown(LWin);

        Assert.Equal(SuperKeyAction.SuppressAndOpenOverview, m.OnKeyUp(LWin));
        Assert.Equal(SuperKeyAction.PassThrough, m.OnKeyUp(LWin));
    }

    [Fact]
    public void BothWinKeys_OnlyTheFinalReleaseCounts()
    {
        var m = new SuperKeyStateMachine();

        m.OnKeyDown(LWin);
        m.OnKeyDown(RWin);

        Assert.Equal(SuperKeyAction.PassThrough, m.OnKeyUp(RWin));
        Assert.Equal(SuperKeyAction.SuppressAndOpenOverview, m.OnKeyUp(LWin));
    }

    [Fact]
    public void SecondWinKeyDoesNotRearmAfterACombo()
    {
        var m = new SuperKeyStateMachine();

        m.OnKeyDown(LWin);
        m.OnKeyDown(KeyE);
        m.OnKeyDown(RWin);
        m.OnKeyUp(RWin);

        Assert.Equal(SuperKeyAction.PassThrough, m.OnKeyUp(LWin));
    }

    [Fact]
    public void Reset_ForgetsAHeldKey()
    {
        var m = new SuperKeyStateMachine();
        m.OnKeyDown(LWin);

        m.Reset();

        Assert.Equal(SuperKeyAction.PassThrough, m.OnKeyUp(LWin));
    }

    [Fact]
    public void Reset_AllowsAFreshTap()
    {
        var m = new SuperKeyStateMachine();
        m.OnKeyDown(LWin);
        m.OnKeyDown(KeyE);
        m.Reset();

        m.OnKeyDown(LWin);

        Assert.Equal(SuperKeyAction.SuppressAndOpenOverview, m.OnKeyUp(LWin));
    }

    [Fact]
    public void NonWinKeys_AlwaysPassThrough()
    {
        var m = new SuperKeyStateMachine();

        Assert.Equal(SuperKeyAction.PassThrough, m.OnKeyDown(KeyE));
        Assert.Equal(SuperKeyAction.PassThrough, m.OnKeyUp(KeyE));
    }
}
