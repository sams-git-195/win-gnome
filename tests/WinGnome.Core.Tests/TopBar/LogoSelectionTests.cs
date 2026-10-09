using WinGnome.Core.Settings;
using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class LogoSelectionTests
{
    private static readonly Func<string, bool> Exists = _ => true;
    private static readonly Func<string, bool> Missing = _ => false;

    [Fact]
    public void Resolve_Windows_IsTheWindowsMark()
    {
        var result = LogoSelection.Resolve(TopBarLogo.Windows, "", Exists);

        Assert.Equal(new LogoTarget(LogoKind.WindowsMark, null, null), result);
    }

    [Fact]
    public void Resolve_Foot_UsesTheLogoFootKey()
    {
        var result = LogoSelection.Resolve(TopBarLogo.Foot, "", Exists);

        Assert.Equal(new LogoTarget(LogoKind.Geometry, "LogoFoot", null), result);
    }

    [Fact]
    public void Resolve_Star_UsesTheLogoStarKey()
    {
        var result = LogoSelection.Resolve(TopBarLogo.Star, "", Exists);

        Assert.Equal(new LogoTarget(LogoKind.Geometry, "LogoStar", null), result);
    }

    [Fact]
    public void Resolve_Terminal_UsesTheLogoTerminalKey()
    {
        var result = LogoSelection.Resolve(TopBarLogo.Terminal, "", Exists);

        Assert.Equal(new LogoTarget(LogoKind.Geometry, "LogoTerminal", null), result);
    }

    [Fact]
    public void Resolve_CustomWithExistingFile_IsAnImage()
    {
        var result = LogoSelection.Resolve(TopBarLogo.Custom, @"C:\Pictures\logo.png", Exists);

        Assert.Equal(new LogoTarget(LogoKind.Image, null, @"C:\Pictures\logo.png"), result);
    }

    [Fact]
    public void Resolve_CustomWithMissingFile_FallsBackToTheWindowsMark()
    {
        var result = LogoSelection.Resolve(TopBarLogo.Custom, @"C:\Pictures\gone.png", Missing);

        Assert.Equal(new LogoTarget(LogoKind.WindowsMark, null, null), result);
    }

    [Fact]
    public void Resolve_CustomWithEmptyPath_FallsBackToTheWindowsMark()
    {
        // Even a file-existence check that would accept it: an empty path is never an image.
        var result = LogoSelection.Resolve(TopBarLogo.Custom, "", Exists);

        Assert.Equal(new LogoTarget(LogoKind.WindowsMark, null, null), result);
    }

    [Fact]
    public void Resolve_CustomWithWhitespacePath_FallsBackToTheWindowsMark()
    {
        var result = LogoSelection.Resolve(TopBarLogo.Custom, "   ", Exists);

        Assert.Equal(new LogoTarget(LogoKind.WindowsMark, null, null), result);
    }

    [Fact]
    public void Resolve_BuiltInMarkIgnoresThePathAndExistence()
    {
        // A leftover LogoImagePath from a previous Custom choice must not turn a built-in mark into an image.
        var result = LogoSelection.Resolve(TopBarLogo.Foot, @"C:\Pictures\logo.png", Exists);

        Assert.Equal(new LogoTarget(LogoKind.Geometry, "LogoFoot", null), result);
    }

    [Fact]
    public void Resolve_UndefinedLogo_FallsBackToTheWindowsMark()
    {
        // A hand-edited settings file can carry an undefined enum value; EnumSetting.Normalize resets it before this
        // runs, but Resolve is also safe on its own.
        var result = LogoSelection.Resolve((TopBarLogo)42, @"C:\Pictures\logo.png", Exists);

        Assert.Equal(new LogoTarget(LogoKind.WindowsMark, null, null), result);
    }
}
