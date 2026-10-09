using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class AutoplayModelTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoHandlers = new Dictionary<string, IReadOnlyList<string>>();
    private static readonly IReadOnlyDictionary<string, string> NoNames = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, string> NothingChosen = new Dictionary<string, string>();

    private static readonly AutoplayChoice Ask = new("MSPromptEachTime", "Ask what to do");
    private static readonly AutoplayChoice Nothing = new("MSTakeNoAction", "Do nothing");
    private static readonly AutoplayChoice Folder = new("MSOpenFolder", "Open folder");

    [Theory]
    [InlineData("StorageOnArrival", "Removable drive")]
    [InlineData("ShowPicturesOnArrival", "Memory card")]
    [InlineData("PlayDVDMovieOnArrival", "DVD movie")]
    [InlineData("PlayCDAudioOnArrival", "Audio CD")]
    [InlineData("CameraMemoryOnArrival", "Camera")]
    [InlineData("storageonarrival", "Removable drive")]
    public void EventLabel_KnownEvents_AreNamedInPlainLanguage(string eventId, string expected)
    {
        Assert.Equal(expected, AutoplayModel.EventLabel(eventId));
    }

    [Theory]
    [InlineData("Rio600Arrival")]
    [InlineData("WPD")]
    [InlineData("")]
    public void EventLabel_UnknownEvents_HaveNoName(string eventId)
    {
        Assert.Null(AutoplayModel.EventLabel(eventId));
    }

    [Fact]
    public void Build_NoEvents_StillListsRemovableDriveThenMemoryCard()
    {
        var rows = AutoplayModel.Build([], NoHandlers, NoNames, NothingChosen);

        Assert.Equal(["StorageOnArrival", "ShowPicturesOnArrival"], rows.Select(r => r.EventId));
        Assert.Equal(["Removable drive", "Memory card"], rows.Select(r => r.Label));
    }

    [Fact]
    public void Build_Choices_StartWithTheThreeStandardActions()
    {
        var rows = AutoplayModel.Build(["StorageOnArrival"], NoHandlers, NoNames, NothingChosen);

        Assert.Equal([Ask, Nothing, Folder], rows[0].Choices);
    }

    [Fact]
    public void Build_InstalledHandlers_FollowTheStandardActionsSortedByName()
    {
        var handlers = new Dictionary<string, IReadOnlyList<string>>
        {
            ["StorageOnArrival"] = ["zeta", "MSStorageSense", "alpha"],
        };
        var names = new Dictionary<string, string>
        {
            ["zeta"] = "Zeta app",
            ["MSStorageSense"] = "Configure this drive for backup",
            ["alpha"] = "alpha app",
        };

        var row = AutoplayModel.Build(["StorageOnArrival"], handlers, names, NothingChosen)[0];

        Assert.Equal(
            [Ask, Nothing, Folder,
             new AutoplayChoice("alpha", "alpha app"),
             new AutoplayChoice("MSStorageSense", "Configure this drive for backup"),
             new AutoplayChoice("zeta", "Zeta app")],
            row.Choices);
    }

    [Fact]
    public void Build_HandlerWithoutAName_IsNotOffered()
    {
        var handlers = new Dictionary<string, IReadOnlyList<string>> { ["StorageOnArrival"] = ["OneDriveAutoPlay", "named"] };
        var names = new Dictionary<string, string> { ["named"] = "Named app" };

        var row = AutoplayModel.Build(["StorageOnArrival"], handlers, names, NothingChosen)[0];

        Assert.Equal([Ask, Nothing, Folder, new AutoplayChoice("named", "Named app")], row.Choices);
    }

    [Fact]
    public void Build_HandlerListedTwice_IsOfferedOnce()
    {
        var handlers = new Dictionary<string, IReadOnlyList<string>> { ["StorageOnArrival"] = ["app", "APP", "MSOpenFolder"] };
        var names = new Dictionary<string, string> { ["app"] = "App" };

        var row = AutoplayModel.Build(["StorageOnArrival"], handlers, names, NothingChosen)[0];

        Assert.Equal([Ask, Nothing, Folder, new AutoplayChoice("app", "App")], row.Choices);
    }

    [Fact]
    public void Build_UnknownEvent_IsHidden()
    {
        var handlers = new Dictionary<string, IReadOnlyList<string>> { ["Rio600Arrival"] = ["x"] };
        var names = new Dictionary<string, string> { ["x"] = "X" };

        var rows = AutoplayModel.Build(["Rio600Arrival", "WPD"], handlers, names, NothingChosen);

        Assert.DoesNotContain(rows, r => r.EventId is "Rio600Arrival" or "WPD");
    }

    [Fact]
    public void Build_KnownEventWithoutANamedHandler_IsHidden()
    {
        var handlers = new Dictionary<string, IReadOnlyList<string>> { ["PlayDVDMovieOnArrival"] = ["nameless"] };

        var rows = AutoplayModel.Build(["PlayDVDMovieOnArrival"], handlers, NoNames, NothingChosen);

        Assert.DoesNotContain(rows, r => r.EventId == "PlayDVDMovieOnArrival");
    }

    [Fact]
    public void Build_KnownEventWithANamedHandler_IsListedAfterTheTwoAlwaysShown()
    {
        var handlers = new Dictionary<string, IReadOnlyList<string>>
        {
            ["PlayDVDMovieOnArrival"] = ["dvd"],
            ["PlayCDAudioOnArrival"] = ["cd"],
        };
        var names = new Dictionary<string, string> { ["dvd"] = "Play DVD", ["cd"] = "Play CD" };

        var rows = AutoplayModel.Build(["PlayDVDMovieOnArrival", "PlayCDAudioOnArrival"], handlers, names, NothingChosen);

        Assert.Equal(["Removable drive", "Memory card", "Audio CD", "DVD movie"], rows.Select(r => r.Label));
    }

    [Fact]
    public void Build_NothingChosen_SelectsAskWhatToDo()
    {
        var row = AutoplayModel.Build([], NoHandlers, NoNames, NothingChosen)[0];

        Assert.Equal(Ask, row.Selected);
    }

    [Fact]
    public void Build_BlankChosenValue_SelectsAskWhatToDo()
    {
        var chosen = new Dictionary<string, string> { ["StorageOnArrival"] = "  " };

        var row = AutoplayModel.Build([], NoHandlers, NoNames, chosen)[0];

        Assert.Equal(Ask, row.Selected);
    }

    [Theory]
    [InlineData("MSOpenFolder", "Open folder")]
    [InlineData("msopenfolder", "Open folder")]
    [InlineData("MSTakeNoAction", "Do nothing")]
    public void Build_ChosenStandardAction_IsSelected(string chosenId, string expectedLabel)
    {
        var chosen = new Dictionary<string, string> { ["StorageOnArrival"] = chosenId };

        var row = AutoplayModel.Build([], NoHandlers, NoNames, chosen)[0];

        Assert.Equal(expectedLabel, row.Selected.Label);
        Assert.Contains(row.Selected, row.Choices);
    }

    [Fact]
    public void Build_ChosenHandlerIsOnlyInTheOtherEventsList_StaysSelectedWithItsName()
    {
        var names = new Dictionary<string, string> { ["gone"] = "Removed app" };
        var chosen = new Dictionary<string, string> { ["StorageOnArrival"] = "gone" };

        var row = AutoplayModel.Build([], NoHandlers, names, chosen)[0];

        Assert.Equal(new AutoplayChoice("gone", "Removed app"), row.Selected);
        Assert.Contains(row.Selected, row.Choices);
    }

    [Fact]
    public void Build_ChosenHandlerWithNoNameAnywhere_IsShownByItsId()
    {
        var chosen = new Dictionary<string, string> { ["StorageOnArrival"] = "mystery-handler" };

        var row = AutoplayModel.Build([], NoHandlers, NoNames, chosen)[0];

        Assert.Equal(new AutoplayChoice("mystery-handler", "mystery-handler"), row.Selected);
    }

    [Fact]
    public void MergeNames_HandlerNamedOnlyForTheUser_IsKept()
    {
        var machine = new Dictionary<string, string> { ["MSOpenFolder"] = "Open folder to view files" };
        var user = new Dictionary<string, string> { ["perUserApp"] = "Per-user app" };

        var merged = AutoplayModel.MergeNames(machine, user);

        Assert.Equal("Per-user app", merged["perUserApp"]);
        Assert.Equal("Open folder to view files", merged["MSOpenFolder"]);
    }

    [Fact]
    public void MergeNames_SameHandlerInBothHives_TheUserNameWins()
    {
        var machine = new Dictionary<string, string> { ["shared"] = "Machine name" };
        var user = new Dictionary<string, string> { ["SHARED"] = "User name" };

        var merged = AutoplayModel.MergeNames(machine, user);

        Assert.Equal("User name", merged["shared"]);
    }

    [Fact]
    public void Choice_ToString_IsTheLabelSoScreenReadersReadIt()
    {
        Assert.Equal("Open folder", Folder.ToString());
    }

    [Fact]
    public void WritesFor_RecordsTheChoiceWhereWindowsSettingsDoes()
    {
        var writes = AutoplayModel.WritesFor("StorageOnArrival", "MSTakeNoAction");

        Assert.Equal(
            [new AutoplayWrite(@"UserChosenExecuteHandlers\StorageOnArrival", "MSTakeNoAction"),
             new AutoplayWrite(@"EventHandlersDefaultSelection\StorageOnArrival", "MSTakeNoAction")],
            writes);
    }

    [Theory]
    [InlineData("", "MSOpenFolder")]
    [InlineData("StorageOnArrival", "")]
    [InlineData("  ", "MSOpenFolder")]
    [InlineData(@"Storage\OnArrival", "MSOpenFolder")]
    [InlineData("StorageOnArrival", @"..\MSOpenFolder")]
    public void WritesFor_NamesThatCannotBeKeyNames_WriteNothing(string eventId, string handlerId)
    {
        Assert.Empty(AutoplayModel.WritesFor(eventId, handlerId));
    }
}
