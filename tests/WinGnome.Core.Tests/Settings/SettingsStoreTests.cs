using WinGnome.Core.Settings;

namespace WinGnome.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WinGnomeTests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankDirectory(string directory)
    {
        Assert.Throws<ArgumentException>(() => new SettingsStore(directory));
    }

    [Fact]
    public void Constructor_RejectsNullDirectory()
    {
        Assert.Throws<ArgumentNullException>(() => new SettingsStore(null!));
    }

    [Fact]
    public void FilePath_IsSettingsJsonInsideDirectory()
    {
        var store = new SettingsStore(_directory);
        Assert.Equal(Path.Combine(_directory, "settings.json"), store.FilePath);
        Assert.Equal(_directory, store.Directory);
    }

    [Fact]
    public void DefaultDirectory_EndsWithWinGnome()
    {
        Assert.Equal("WinGnome", Path.GetFileName(SettingsStore.DefaultDirectory));
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults_WithoutCreatingAnything()
    {
        var store = new SettingsStore(_directory);

        var settings = store.Load();

        Assert.Equal(DockPosition.Bottom, settings.Dock.Position);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void Save_CreatesDirectoryAndFile_AndLeavesNoTempFile()
    {
        var store = new SettingsStore(Path.Combine(_directory, "nested"));

        store.Save(new AppSettings());

        Assert.True(File.Exists(store.FilePath));
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new SettingsStore(_directory);
        var settings = new AppSettings();
        settings.Dock.Position = DockPosition.Left;
        settings.General.Theme = ThemeMode.Light;
        settings.EnabledTweaks.Add("dark-mode");

        store.Save(settings);
        var loaded = new SettingsStore(_directory).Load();

        Assert.Equal(DockPosition.Left, loaded.Dock.Position);
        Assert.Equal(ThemeMode.Light, loaded.General.Theme);
        Assert.Equal(["dark-mode"], loaded.EnabledTweaks);
    }

    [Fact]
    public void Save_OverwritesPreviousContents()
    {
        var store = new SettingsStore(_directory);
        var settings = new AppSettings();
        settings.Dock.Position = DockPosition.Left;
        store.Save(settings);

        settings.Dock.Position = DockPosition.Right;
        store.Save(settings);

        Assert.Equal(DockPosition.Right, store.Load().Dock.Position);
    }

    [Fact]
    public void Save_NullSettings_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SettingsStore(_directory).Save(null!));
    }

    [Fact]
    public void Load_ToleratesCommentsAndTrailingCommas()
    {
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.FilePath, "{ // hi\n \"Dock\": { \"Position\": \"Right\", }, }");

        Assert.Equal(DockPosition.Right, store.Load().Dock.Position);
        Assert.False(File.Exists(store.FilePath + ".corrupt"));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults_AndPreservesOriginalAsCorrupt()
    {
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        const string garbage = "{ this is : not valid json";
        File.WriteAllText(store.FilePath, garbage);

        var settings = store.Load();

        Assert.Equal(DockPosition.Bottom, settings.Dock.Position);
        Assert.True(File.Exists(store.FilePath + ".corrupt"));
        Assert.Equal(garbage, File.ReadAllText(store.FilePath + ".corrupt"));
    }

    [Fact]
    public void Load_UnknownEnumValue_CountsAsCorrupt()
    {
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.FilePath, """{ "Dock": { "Position": "Top" } }""");

        var settings = store.Load();

        Assert.Equal(DockPosition.Bottom, settings.Dock.Position);
        Assert.True(File.Exists(store.FilePath + ".corrupt"));
    }

    [Fact]
    public void SaveAfterCorruptLoad_ReplacesBrokenFile_AndKeepsCorruptCopy()
    {
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.FilePath, "garbage");

        store.Save(store.Load());

        Assert.Equal(DockPosition.Bottom, store.Load().Dock.Position);
        Assert.Equal("garbage", File.ReadAllText(store.FilePath + ".corrupt"));
    }
}
