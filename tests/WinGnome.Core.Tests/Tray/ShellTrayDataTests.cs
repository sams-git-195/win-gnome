using System.Buffers.Binary;
using System.Text;
using WinGnome.Core.Geometry;
using WinGnome.Core.Tray;

namespace WinGnome.Core.Tests.Tray;

public class ShellTrayDataTests
{
    // sizeof(SHELLTRAYDATA): two DWORDs plus the 956-byte 32-bit NOTIFYICONDATAW.
    private const int FullSize = 8 + 956;

    internal static byte[] Block(
        NotifyIconMessage message, uint owner, uint id, NotifyIconFields flags,
        uint callback = 0, uint icon = 0, string tip = "", uint state = 0, uint stateMask = 0, uint version = 0,
        Guid guid = default, int size = FullSize)
    {
        var data = new byte[FullSize];
        BinaryPrimitives.WriteUInt32LittleEndian(data, ShellTrayData.Signature);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)message);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 956);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), owner);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), id);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), (uint)flags);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), callback);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), icon);
        Encoding.Unicode.GetBytes(tip).CopyTo(data, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(288), state);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(292), stateMask);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(808), version);
        guid.TryWriteBytes(data.AsSpan(944));
        return data[..size];
    }

    [Fact]
    public void ParsesEveryField()
    {
        var guid = Guid.NewGuid();
        var data = Block(NotifyIconMessage.Modify, 0x1234, 7, NotifyIconFields.Message | NotifyIconFields.Tip | NotifyIconFields.ItemGuid,
            callback: 0x8001, icon: 0x5678, tip: "Steam", state: 1, stateMask: 1, version: 4, guid: guid);

        var command = ShellTrayData.ParseNotifyIcon(data);

        Assert.NotNull(command);
        Assert.Equal(NotifyIconMessage.Modify, command.Message);
        Assert.Equal((nint)0x1234, command.Owner);
        Assert.Equal(7u, command.Id);
        Assert.Equal(0x8001u, command.CallbackMessage);
        Assert.Equal((nint)0x5678, command.Icon);
        Assert.Equal("Steam", command.Tip);
        Assert.Equal(NotifyIconStates.Hidden, command.State);
        Assert.Equal(NotifyIconStates.Hidden, command.StateMask);
        Assert.Equal(4u, command.Version);
        Assert.Equal(guid, command.ItemGuid);
        Assert.Equal(guid, command.Key.ItemGuid);
    }

    [Fact]
    public void HandlesAreSignExtended()
    {
        var command = ShellTrayData.ParseNotifyIcon(Block(NotifyIconMessage.Add, 0xFFFF_FFF0, 1, NotifyIconFields.Icon, icon: 0x8000_0001));

        Assert.Equal((nint)(-16), command!.Owner);
        Assert.Equal((nint)unchecked((int)0x8000_0001), command.Icon);
    }

    [Fact]
    public void TipUsesAllOf128Characters()
    {
        var tip = new string('x', 128);

        var command = ShellTrayData.ParseNotifyIcon(Block(NotifyIconMessage.Add, 1, 1, NotifyIconFields.Tip, tip: tip));

        Assert.Equal(tip, command!.Tip);
    }

    [Fact]
    public void GuidIgnoredWithoutFlag()
    {
        var command = ShellTrayData.ParseNotifyIcon(Block(NotifyIconMessage.Add, 1, 1, NotifyIconFields.Icon, guid: Guid.NewGuid()));

        Assert.Equal(Guid.Empty, command!.Key.ItemGuid);
    }

    [Fact]
    public void ShortBlockZeroFillsMissingFields()
    {
        // Only up to the end of szTip, like a NOTIFYICONDATA_V2-era block truncated before the state fields.
        var command = ShellTrayData.ParseNotifyIcon(Block(NotifyIconMessage.Add, 1, 2, NotifyIconFields.Tip, tip: "Hi", state: 1, size: 288));

        Assert.Equal("Hi", command!.Tip);
        Assert.Equal(NotifyIconStates.None, command.State);
        Assert.Equal(Guid.Empty, command.ItemGuid);
    }

    [Fact]
    public void RejectsWrongSignatureTruncatedOrUnknownMessage()
    {
        var wrongSignature = Block(NotifyIconMessage.Add, 1, 1, NotifyIconFields.Icon);
        wrongSignature[0] ^= 0xFF;

        Assert.Null(ShellTrayData.ParseNotifyIcon(wrongSignature));
        Assert.Null(ShellTrayData.ParseNotifyIcon(Block(NotifyIconMessage.Add, 1, 1, NotifyIconFields.Icon, size: 20)));
        Assert.Null(ShellTrayData.ParseNotifyIcon(Block((NotifyIconMessage)9, 1, 1, NotifyIconFields.Icon)));
        Assert.Null(ShellTrayData.ParseNotifyIcon([]));
    }

    [Theory]
    [InlineData(1u, false)]
    [InlineData(2u, true)]
    public void ParsesIconRectQuery(uint part, bool size)
    {
        var guid = Guid.NewGuid();
        var data = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), part);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 0x42);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 9);
        guid.TryWriteBytes(data.AsSpan(24));

        var query = ShellTrayData.ParseIconRectQuery(data);

        Assert.Equal(new TrayIconRectQuery(new TrayIconId(0x42, 9, guid), size), query);
    }

    [Fact]
    public void IconRectAnswers_AreTopLeftThenSize()
    {
        // shell32 builds the rectangle as (first, first + second): the second answer is the size, not the corner.
        var bounds = new PixelRect(2345, 4, 2381, 36);
        var icon = new TrayIconId(0x42, 9, Guid.Empty);

        Assert.Equal(TrayCallback.PackPoint(2345, 4), new TrayIconRectQuery(icon, Size: false).Answer(bounds));
        Assert.Equal(TrayCallback.PackPoint(36, 32), new TrayIconRectQuery(icon, Size: true).Answer(bounds));
    }

    [Fact]
    public void RejectsMalformedIconRectQuery()
    {
        var data = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 3);

        Assert.Null(ShellTrayData.ParseIconRectQuery(data));
        Assert.Null(ShellTrayData.ParseIconRectQuery(new byte[39]));
    }
}
