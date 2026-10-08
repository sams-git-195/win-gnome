using WinGnome.Core.Settings;

namespace WinGnome.Core.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_AreAlreadyNormalised()
    {
        var settings = new AppSettings();
        var before = SettingsSerializer.Serialize(settings);

        settings.Normalize();

        Assert.Equal(before, SettingsSerializer.Serialize(settings));
        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Fact]
    public void Normalize_ReturnsSameInstance()
    {
        var settings = new AppSettings();
        Assert.Same(settings, settings.Normalize());
    }

    [Fact]
    public void Normalize_ResetsSchemaVersion()
    {
        var settings = new AppSettings { SchemaVersion = 99 };
        settings.Normalize();
        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Fact]
    public void Normalize_RepairsNullSections()
    {
        var settings = new AppSettings
        {
            General = null!,
            TopBar = null!,
            Dock = null!,
            WindowButtons = null!,
            Activities = null!,
            EnabledTweaks = null!,
        };

        settings.Normalize();

        Assert.NotNull(settings.General);
        Assert.NotNull(settings.TopBar);
        Assert.NotNull(settings.Dock);
        Assert.NotNull(settings.WindowButtons);
        Assert.NotNull(settings.Activities);
        Assert.Empty(settings.EnabledTweaks);
    }

    [Theory]
    [InlineData(0, 24)]
    [InlineData(23.9, 24)]
    [InlineData(24, 24)]
    [InlineData(40, 40)]
    [InlineData(48, 48)]
    [InlineData(500, 48)]
    [InlineData(double.NaN, 32)]
    [InlineData(double.PositiveInfinity, 32)]
    public void TopBar_Height_IsClamped(double input, double expected)
    {
        var settings = new AppSettings { TopBar = { Height = input } };
        Assert.Equal(expected, settings.Normalize().TopBar.Height);
    }

    [Theory]
    [InlineData(0, 0.3)]
    [InlineData(0.5, 0.5)]
    [InlineData(2, 1)]
    [InlineData(double.NaN, 1)]
    public void TopBar_Opacity_IsClamped(double input, double expected)
    {
        var settings = new AppSettings { TopBar = { Opacity = input } };
        Assert.Equal(expected, settings.Normalize().TopBar.Opacity);
    }

    [Theory]
    [InlineData("not a colour", "#000000")]
    [InlineData("", "#000000")]
    [InlineData(null, "#000000")]
    [InlineData("#112233", "#112233")]
    public void TopBar_BackgroundColor_FallsBackWhenInvalid(string? input, string expected)
    {
        var settings = new AppSettings { TopBar = { BackgroundColor = input! } };
        Assert.Equal(expected, settings.Normalize().TopBar.BackgroundColor);
    }

    [Theory]
    [InlineData(1, 24)]
    [InlineData(48, 48)]
    [InlineData(999, 128)]
    [InlineData(double.NaN, 48)]
    public void Dock_IconSize_IsClamped(double input, double expected)
    {
        var settings = new AppSettings { Dock = { IconSize = input } };
        Assert.Equal(expected, settings.Normalize().Dock.IconSize);
    }

    [Theory]
    [InlineData(0.2, 1)]
    [InlineData(1.5, 1.5)]
    [InlineData(5, 2)]
    [InlineData(double.NegativeInfinity, 1)]
    public void Dock_Magnification_IsClamped(double input, double expected)
    {
        var settings = new AppSettings { Dock = { Magnification = input } };
        Assert.Equal(expected, settings.Normalize().Dock.Magnification);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0.4, 0.4)]
    [InlineData(3, 1)]
    [InlineData(double.NaN, 0.75)]
    public void Dock_Opacity_IsClamped(double input, double expected)
    {
        var settings = new AppSettings { Dock = { Opacity = input } };
        Assert.Equal(expected, settings.Normalize().Dock.Opacity);
    }

    [Fact]
    public void Dock_PinnedApps_DropsBlankAndNullEntries()
    {
        var settings = new AppSettings();
        settings.Dock.PinnedApps =
        [
            new PinnedApp { Name = "A", LaunchId = "a" },
            new PinnedApp { Name = "Blank", LaunchId = "   " },
            new PinnedApp { Name = "Empty", LaunchId = "" },
            null!,
        ];

        settings.Normalize();

        var only = Assert.Single(settings.Dock.PinnedApps);
        Assert.Equal("a", only.LaunchId);
    }

    [Fact]
    public void Dock_PinnedApps_DedupesByLaunchIdIgnoringCase_KeepingFirst()
    {
        var settings = new AppSettings();
        settings.Dock.PinnedApps =
        [
            new PinnedApp { Name = "First", LaunchId = "Contoso.App" },
            new PinnedApp { Name = "Other", LaunchId = "other" },
            new PinnedApp { Name = "Second", LaunchId = "CONTOSO.APP" },
        ];

        settings.Normalize();

        Assert.Equal(["First", "Other"], settings.Dock.PinnedApps.Select(p => p.Name));
    }

    [Fact]
    public void Dock_PinnedApps_PreservesOrder()
    {
        var settings = new AppSettings();
        settings.Dock.PinnedApps =
        [
            new PinnedApp { Name = "C", LaunchId = "c" },
            new PinnedApp { Name = "A", LaunchId = "a" },
            new PinnedApp { Name = "B", LaunchId = "b" },
        ];

        settings.Normalize();

        Assert.Equal(["c", "a", "b"], settings.Dock.PinnedApps.Select(p => p.LaunchId));
    }

    [Fact]
    public void Dock_PinnedApps_BlankNameFallsBackToLaunchId_AndNamesAreTrimmed()
    {
        var settings = new AppSettings();
        settings.Dock.PinnedApps =
        [
            new PinnedApp { Name = "  ", LaunchId = "x.y" },
            new PinnedApp { Name = "  Padded  ", LaunchId = "padded" },
            new PinnedApp { Name = null!, LaunchId = "nullname" },
        ];

        settings.Normalize();

        Assert.Equal(["x.y", "Padded", "nullname"], settings.Dock.PinnedApps.Select(p => p.Name));
    }

    [Fact]
    public void Dock_PinnedApps_NullListBecomesEmpty()
    {
        var settings = new AppSettings();
        settings.Dock.PinnedApps = null!;
        Assert.Empty(settings.Normalize().Dock.PinnedApps);
    }

    [Fact]
    public void Dock_DefaultPinnedApps_SurviveNormalize()
    {
        var settings = new AppSettings();
        var expected = DockSettings.DefaultPinnedApps().Select(p => p.LaunchId).ToList();
        Assert.Equal(expected, settings.Normalize().Dock.PinnedApps.Select(p => p.LaunchId));
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(14, 14)]
    [InlineData(100, 24)]
    [InlineData(double.NaN, 14)]
    public void WindowButtons_Diameter_IsClamped(double input, double expected)
    {
        var settings = new AppSettings { WindowButtons = { Diameter = input } };
        Assert.Equal(expected, settings.Normalize().WindowButtons.Diameter);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(8, 8)]
    [InlineData(50, 20)]
    [InlineData(double.NaN, 8)]
    public void WindowButtons_Spacing_IsClamped(double input, double expected)
    {
        var settings = new AppSettings { WindowButtons = { Spacing = input } };
        Assert.Equal(expected, settings.Normalize().WindowButtons.Spacing);
    }

    [Fact]
    public void WindowButtons_Colours_AreCanonicalisedOrReset()
    {
        var settings = new AppSettings
        {
            WindowButtons =
            {
                CloseColor = "#f00",
                MinimizeColor = "garbage",
                MaximizeColor = "00ff00",
            },
        };

        settings.Normalize();

        Assert.Equal("#FF0000", settings.WindowButtons.CloseColor);
        Assert.Equal("#FEBC2E", settings.WindowButtons.MinimizeColor);
        Assert.Equal("#00FF00", settings.WindowButtons.MaximizeColor);
    }

    [Fact]
    public void WindowButtons_ExcludedProcesses_StripExeTrimDedupeAndDropBlanks()
    {
        var settings = new AppSettings();
        settings.WindowButtons.ExcludedProcesses = ["Chrome.exe", " chrome ", "CHROME.EXE", "", "  ", "Code", null!, ".exe", "notepad.EXE"];

        settings.Normalize();

        Assert.Equal(["Chrome", "Code", "notepad"], settings.WindowButtons.ExcludedProcesses);
    }

    [Fact]
    public void WindowButtons_ExcludedProcesses_NullListBecomesEmpty()
    {
        var settings = new AppSettings();
        settings.WindowButtons.ExcludedProcesses = null!;
        Assert.Empty(settings.Normalize().WindowButtons.ExcludedProcesses);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(150, 150)]
    [InlineData(99999, 2000)]
    public void Activities_HotCornerDelay_IsClamped(int input, int expected)
    {
        var settings = new AppSettings { Activities = { HotCornerDelayMs = input } };
        Assert.Equal(expected, settings.Normalize().Activities.HotCornerDelayMs);
    }

    [Theory]
    [InlineData(0, 0.2)]
    [InlineData(0.9, 0.9)]
    [InlineData(4, 1)]
    [InlineData(double.NaN, 0.85)]
    public void Activities_BackdropOpacity_IsClamped(double input, double expected)
    {
        var settings = new AppSettings { Activities = { BackdropOpacity = input } };
        Assert.Equal(expected, settings.Normalize().Activities.BackdropOpacity);
    }

    [Theory]
    [InlineData("", "Alt+F1")]
    [InlineData("   ", "Alt+F1")]
    [InlineData(null, "Alt+F1")]
    [InlineData("Ctrl+Nonsense", "Alt+F1")]
    [InlineData("A", "Alt+F1")]
    [InlineData("Ctrl+Alt+T", "Ctrl+Alt+T")]
    [InlineData("Win+Space", "Win+Space")]
    public void Activities_Hotkey_FallsBackWhenInvalid(string? input, string expected)
    {
        var settings = new AppSettings { Activities = { Hotkey = input! } };
        Assert.Equal(expected, settings.Normalize().Activities.Hotkey);
    }

    [Fact]
    public void EnabledTweaks_DedupeIgnoringCase_DropBlanks_KeepFirstSpelling()
    {
        var settings = new AppSettings { EnabledTweaks = ["dark-mode", "Dark-Mode", "", "  ", null!, "show-file-extensions"] };

        settings.Normalize();

        Assert.Equal(["dark-mode", "show-file-extensions"], settings.EnabledTweaks);
    }

    [Fact]
    public void Clone_IsDeepAndEqualInContent()
    {
        var original = new AppSettings();
        original.Dock.Position = DockPosition.Left;
        original.WindowButtons.ExcludedProcesses.Add("code");
        original.EnabledTweaks.Add("dark-mode");

        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.NotSame(original.Dock, clone.Dock);
        Assert.NotSame(original.WindowButtons.ExcludedProcesses, clone.WindowButtons.ExcludedProcesses);
        Assert.Equal(SettingsSerializer.Serialize(original), SettingsSerializer.Serialize(clone));

        clone.Dock.Position = DockPosition.Right;
        clone.EnabledTweaks.Clear();
        Assert.Equal(DockPosition.Left, original.Dock.Position);
        Assert.Single(original.EnabledTweaks);
    }
}
