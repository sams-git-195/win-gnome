using WinGnome.Core.Input;
using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class ShellHotkeyMapTests
{
    [Theory]
    [InlineData("Win+E", ShellAction.FileExplorer)]
    [InlineData("Win+R", ShellAction.Run)]
    [InlineData("Win+I", ShellAction.Settings)]
    [InlineData("Win+D", ShellAction.ShowDesktop)]
    [InlineData("Win+Shift+S", ShellAction.ScreenClip)]
    [InlineData("Win+Left", ShellAction.SnapLeft)]
    [InlineData("Win+Right", ShellAction.SnapRight)]
    [InlineData("Win+Up", ShellAction.Maximise)]
    [InlineData("Win+Down", ShellAction.Minimise)]
    public void TryGetAction_DefaultBindings(string text, ShellAction expected)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));

        Assert.True(ShellHotkeyMap.Default.TryGetAction(hotkey.Modifiers, hotkey.VirtualKey, out var action));
        Assert.Equal(expected, action);
    }

    [Theory]
    [InlineData("Win+L")]
    [InlineData("Win+S")]
    [InlineData("Ctrl+E")]
    [InlineData("Win+Ctrl+E")]
    [InlineData("Win+Shift+E")]
    [InlineData("Win+Alt+R")]
    [InlineData("Win+Space")]
    public void TryGetAction_UnboundOrDifferentModifiers_NoMatch(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));

        Assert.False(ShellHotkeyMap.Default.TryGetAction(hotkey.Modifiers, hotkey.VirtualKey, out var action));
        Assert.Equal(default, action);
    }

    [Fact]
    public void Default_HasOneBindingPerActionAndNoDuplicateKeys()
    {
        Assert.Equal(9, ShellHotkeyMap.Default.Bindings.Count);
        Assert.Equal(9, ShellHotkeyMap.Default.Bindings.Select(b => b.Hotkey).Distinct().Count());
        Assert.Equal(9, ShellHotkeyMap.Default.Bindings.Select(b => b.Action).Distinct().Count());
    }

    [Fact]
    public void Default_NeverBindsWinAloneOrWinL()
    {
        Assert.DoesNotContain(ShellHotkeyMap.Default.Bindings, b => b.Hotkey.VirtualKey == 'L');
        Assert.DoesNotContain(ShellHotkeyMap.Default.Bindings, b => b.Hotkey.Modifiers == HotkeyModifiers.None);
    }

    [Theory]
    [InlineData(ShellAction.FileExplorer, "Win+E")]
    [InlineData(ShellAction.ScreenClip, "Shift+Win+S")]
    [InlineData(ShellAction.Minimise, "Win+Down")]
    public void GetHotkey_ReturnsTheBinding(ShellAction action, string expected)
    {
        Assert.Equal(expected, ShellHotkeyMap.Default.GetHotkey(action).ToString());
    }

    [Fact]
    public void FindConflicts_UserHotkeyEqualToABinding_IsReported()
    {
        Assert.True(Hotkey.TryParse("Win+D", out var overview));
        Assert.True(Hotkey.TryParse("Ctrl+Alt+T", out var terminal));

        var conflicts = ShellHotkeyMap.Default.FindConflicts([overview, terminal]);

        Assert.Equal([new ShellHotkeyConflict(overview, ShellAction.ShowDesktop)], conflicts);
    }

    [Fact]
    public void FindConflicts_NoOverlap_ReturnsEmpty()
    {
        Assert.True(Hotkey.TryParse("Win+Tab", out var overview));

        Assert.Empty(ShellHotkeyMap.Default.FindConflicts([overview]));
    }

    [Fact]
    public void FindConflicts_NoUserHotkeys_ReturnsEmpty()
    {
        Assert.Empty(ShellHotkeyMap.Default.FindConflicts([]));
    }

    [Fact]
    public void FindConflicts_SeveralConflicts_KeepUserOrder()
    {
        Assert.True(Hotkey.TryParse("Win+R", out var a));
        Assert.True(Hotkey.TryParse("Win+E", out var b));

        var conflicts = ShellHotkeyMap.Default.FindConflicts([a, b]);

        Assert.Equal([ShellAction.Run, ShellAction.FileExplorer], conflicts.Select(c => c.Action));
    }

    [Theory]
    [InlineData("Win+L", true)]
    [InlineData("Win+Shift+L", false)]
    [InlineData("Ctrl+L", false)]
    [InlineData("Win+E", false)]
    public void IsOperatingSystemOwned_OnlyWinL(string text, bool expected)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));

        Assert.Equal(expected, ShellHotkeyMap.IsOperatingSystemOwned(hotkey));
    }
}
