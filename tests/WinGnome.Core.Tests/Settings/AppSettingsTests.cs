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
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(0.5, 0.5)]
    [InlineData(2, 1)]
    [InlineData(double.NaN, 1)]
    public void TopBar_Opacity_IsClamped(double input, double expected)
    {
        var settings = new AppSettings { TopBar = { Opacity = input } };
        Assert.Equal(expected, settings.Normalize().TopBar.Opacity);
    }

    [Fact]
    public void TopBar_AppearanceDefaults_AreGnomeLike()
    {
        var bar = new AppSettings().Normalize().TopBar;
        Assert.Equal("#000000", bar.BackgroundColor);
        Assert.Equal("#FFFFFF", bar.ForegroundColor);
        Assert.Equal(BlurEffect.None, bar.Blur);
        Assert.Equal(13.5, bar.FontSize);
        Assert.Equal(0, bar.Margin);
        Assert.Equal(0, bar.CornerRadius);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(16, 16)]
    [InlineData(99, 20)]
    [InlineData(double.NaN, 13.5)]
    public void TopBar_FontSize_IsClamped(double input, double expected)
    {
        var settings = new AppSettings { TopBar = { FontSize = input } };
        Assert.Equal(expected, settings.Normalize().TopBar.FontSize);
    }

    [Fact]
    public void TopBar_MarginAndRadius_AreClamped()
    {
        var settings = new AppSettings { TopBar = { Margin = -5, CornerRadius = 500 } }.Normalize();
        Assert.Equal(0, settings.TopBar.Margin);
        Assert.Equal(24, settings.TopBar.CornerRadius);
    }

    [Theory]
    [InlineData(-4, 0)]
    [InlineData(0, 0)]
    [InlineData(12, 12)]
    [InlineData(250, 100)]
    [InlineData(double.NaN, 0)]
    public void TopBar_ItemCornerRadius_DefaultsSquareAndIsClamped(double input, double expected)
    {
        Assert.Equal(0, new AppSettings().Normalize().TopBar.ItemCornerRadius);
        var settings = new AppSettings { TopBar = { ItemCornerRadius = input } };
        Assert.Equal(expected, settings.Normalize().TopBar.ItemCornerRadius);
    }

    [Fact]
    public void TopBar_InvalidForeground_FallsBackToWhite()
    {
        var settings = new AppSettings { TopBar = { ForegroundColor = "nope" } }.Normalize();
        Assert.Equal("#FFFFFF", settings.TopBar.ForegroundColor);
    }

    [Fact]
    public void TopBar_UndefinedBlur_FallsBackToNone()
    {
        var settings = new AppSettings { TopBar = { Blur = (BlurEffect)42 } }.Normalize();
        Assert.Equal(BlurEffect.None, settings.TopBar.Blur);
    }

    [Fact]
    public void Dock_AppearanceDefaults()
    {
        var dock = new AppSettings().Normalize().Dock;
        Assert.Equal("", dock.BackgroundColor);
        Assert.Equal("", dock.IndicatorColor);
        Assert.Equal(BlurEffect.Acrylic, dock.Blur);
        Assert.Equal(18, dock.CornerRadius);
        Assert.Equal(6, dock.IconSpacing);
        Assert.Equal(8, dock.EdgeMargin);
    }

    [Theory]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("#112233", "#112233")]
    [InlineData("garbage", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Dock_OptionalColours_NormaliseOrBecomeEmpty(string? input, string expected)
    {
        var settings = new AppSettings { Dock = { BackgroundColor = input!, IndicatorColor = input! } }.Normalize();
        Assert.Equal(expected, settings.Dock.BackgroundColor);
        Assert.Equal(expected, settings.Dock.IndicatorColor);
    }

    [Fact]
    public void Dock_GeometryValues_AreClamped()
    {
        var settings = new AppSettings
        {
            Dock = { CornerRadius = 100, IconSpacing = -3, EdgeMargin = double.PositiveInfinity, Blur = (BlurEffect)9 },
        }.Normalize();
        Assert.Equal(40, settings.Dock.CornerRadius);
        Assert.Equal(0, settings.Dock.IconSpacing);
        Assert.Equal(8, settings.Dock.EdgeMargin);
        Assert.Equal(BlurEffect.Acrylic, settings.Dock.Blur);
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
    public void Normalize_ResetsUndefinedEnumValuesToDefaults()
    {
        var settings = new AppSettings
        {
            General = { Theme = (ThemeMode)42 },
            TopBar = { ClockStyle = (ClockStyle)42 },
            Dock = { Position = (DockPosition)42, Visibility = (DockVisibility)42, ClickAction = (DockClickAction)42 },
            WindowButtons = { Preset = (TrafficLightPreset)42, Side = (ButtonSide)42, Order = (ButtonOrder)42 },
        };

        settings.Normalize();

        Assert.Equal(ThemeMode.Dark, settings.General.Theme);
        Assert.Equal(ClockStyle.TwentyFourHour, settings.TopBar.ClockStyle);
        Assert.Equal(DockPosition.Bottom, settings.Dock.Position);
        Assert.Equal(DockVisibility.Intellihide, settings.Dock.Visibility);
        Assert.Equal(DockClickAction.FocusOrMinimize, settings.Dock.ClickAction);
        Assert.Equal(TrafficLightPreset.MacOS, settings.WindowButtons.Preset);
        Assert.Equal(ButtonSide.Right, settings.WindowButtons.Side);
        Assert.Equal(ButtonOrder.MinimizeMaximizeClose, settings.WindowButtons.Order);
    }

    [Fact]
    public void Normalize_KeepsDefinedEnumValues()
    {
        var settings = new AppSettings
        {
            General = { Theme = ThemeMode.Light },
            Dock = { Position = DockPosition.Right, Visibility = DockVisibility.Autohide },
            WindowButtons = { Side = ButtonSide.Left },
        }.Normalize();

        Assert.Equal(ThemeMode.Light, settings.General.Theme);
        Assert.Equal(DockPosition.Right, settings.Dock.Position);
        Assert.Equal(DockVisibility.Autohide, settings.Dock.Visibility);
        Assert.Equal(ButtonSide.Left, settings.WindowButtons.Side);
    }

    [Fact]
    public void Dock_PinnedApps_LaunchIdsAreTrimmed_AndDedupedAfterTrimming()
    {
        var settings = new AppSettings();
        settings.Dock.PinnedApps =
        [
            new PinnedApp { Name = "A", LaunchId = "  contoso.app " },
            new PinnedApp { Name = "B", LaunchId = "Contoso.App" },
        ];

        settings.Normalize();

        var only = Assert.Single(settings.Dock.PinnedApps);
        Assert.Equal("contoso.app", only.LaunchId);
        Assert.Equal("A", only.Name);
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

    [Fact]
    public void Normalize_RepairsEveryUndefinedEnumSetting()
    {
        // The settings reader turns unknown enum names into undefined values and relies on Normalize to restore each
        // setting's default, so a new enum setting without a Normalize line would leak an undefined value.
        var settings = new AppSettings();
        var sections = typeof(AppSettings).GetProperties().Where(p => p.PropertyType.IsClass && p.PropertyType != typeof(string)
            && p.PropertyType.Namespace == typeof(AppSettings).Namespace);
        var enumSettings = sections
            .SelectMany(section => section.PropertyType.GetProperties()
                .Where(p => p.PropertyType.IsEnum && p.CanWrite)
                .Select(p => (Section: section.GetValue(settings)!, Property: p)))
            .ToList();
        foreach (var (section, property) in enumSettings)
        {
            property.SetValue(section, Enum.ToObject(property.PropertyType, int.MinValue));
        }

        settings.Normalize();

        Assert.Equal(13, enumSettings.Count);
        Assert.All(enumSettings, s => Assert.True(
            Enum.IsDefined(s.Property.PropertyType, s.Property.GetValue(s.Section)!),
            $"{s.Property.DeclaringType!.Name}.{s.Property.Name} is not normalised"));
    }
}
