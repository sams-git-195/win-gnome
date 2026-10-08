using System.Buffers.Binary;
using WinGnome.Core.Geometry;

namespace WinGnome.Core.Tray;

/// <summary>Kinds of WM_COPYDATA request a "Shell_TrayWnd" window receives (COPYDATASTRUCT.dwData).</summary>
public enum TrayCopyDataKind
{
    /// <summary>SHAppBarMessage: an APPBARMSGDATA block. Only Explorer may answer these.</summary>
    AppBar = 0,
    /// <summary>Shell_NotifyIcon: a SHELLTRAYDATA block.</summary>
    NotifyIcon = 1,
    /// <summary>Shell_NotifyIconGetRect: a WINNOTIFYICONIDENTIFIER block.</summary>
    IconRect = 3,
}

/// <summary>A Shell_NotifyIconGetRect query: which icon, and which half of its rectangle is wanted.</summary>
/// <param name="Icon">The icon asked about.</param>
/// <param name="Size">
/// False for the top-left corner (first call), true for the width and height (second call): shell32 adds the second
/// answer to the first to build the rectangle's bottom-right corner.
/// </param>
public readonly record struct TrayIconRectQuery(TrayIconId Icon, bool Size)
{
    /// <summary>The LRESULT answering this query for an icon at <paramref name="bounds"/> (physical screen pixels).</summary>
    public nint Answer(PixelRect bounds) =>
        Size ? TrayCallback.PackPoint(bounds.Width, bounds.Height) : TrayCallback.PackPoint(bounds.Left, bounds.Top);
}

/// <summary>
/// Decodes the blocks shell32 sends to the tray window. They use a fixed, bitness-independent layout: handles are
/// 32-bit values (a 64-bit process's handles are truncated and must be sign-extended back), strings are UTF-16
/// arrays of fixed length, and shell32 zero-fills fields the caller's (possibly older, shorter) NOTIFYICONDATA lacked.
/// </summary>
public static class ShellTrayData
{
    /// <summary>dwSignature of SHELLTRAYDATA (shell32's NI_NOTIFY_SIG).</summary>
    public const uint Signature = 0x34753423;

    // SHELLTRAYDATA: DWORD dwSignature, DWORD dwMessage, then NOTIFYICONDATAW in its 32-bit layout.
    private const int MessageOffset = 4;
    private const int Nid = 8;
    private const int OwnerOffset = Nid + 4;
    private const int IdOffset = Nid + 8;
    private const int FlagsOffset = Nid + 12;
    private const int CallbackOffset = Nid + 16;
    private const int IconOffset = Nid + 20;
    private const int TipOffset = Nid + 24;
    private const int TipChars = 128;
    private const int StateOffset = TipOffset + (TipChars * 2);
    private const int StateMaskOffset = StateOffset + 4;
    private const int InfoChars = 256;
    private const int VersionOffset = StateMaskOffset + 4 + (InfoChars * 2);
    private const int InfoTitleChars = 64;
    private const int GuidOffset = VersionOffset + 4 + (InfoTitleChars * 2) + 4;

    /// <summary>Smallest block that still carries every field up to and including hIcon.</summary>
    private const int MinimumNotifyIconSize = TipOffset;

    // WINNOTIFYICONIDENTIFIER: DWORD dwMagic, DWORD dwMessage, DWORD cbSize, DWORD padding, DWORD hWnd, UINT uID, GUID.
    private const int RectMessageOffset = 4;
    private const int RectOwnerOffset = 16;
    private const int RectIdOffset = 20;
    private const int RectGuidOffset = 24;
    private const int RectQuerySize = RectGuidOffset + 16;

    // APPBARMSGDATA: the caller's APPBARDATA, whose first DWORD (cbSize) is its own size, then DWORD dwMessage, the
    // shared-memory handle and the caller's process ID. Only the two layouts shell32 has used are accepted.
    private const uint AppBarData32Size = 36;
    private const uint AppBarData3264Size = 40;
    private const uint AppBarLastMessage = 0x0C;

    /// <summary>
    /// The ABM_* message of an SHAppBarMessage block, or null when the block does not have one of the known shapes
    /// (callers then treat it as unknown, which is the safe reading).
    /// </summary>
    public static uint? ParseAppBarMessage(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return null;
        }

        var dataSize = ReadUInt32(data, 0);
        if (dataSize is not (AppBarData32Size or AppBarData3264Size) || data.Length < dataSize + 4)
        {
            return null;
        }

        var message = ReadUInt32(data, (int)dataSize);
        return message <= AppBarLastMessage ? message : null;
    }

    /// <summary>Parses a Shell_NotifyIcon request, or returns null when the block is malformed.</summary>
    public static NotifyIconCommand? ParseNotifyIcon(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinimumNotifyIconSize || ReadUInt32(data, 0) != Signature)
        {
            return null;
        }

        var message = ReadUInt32(data, MessageOffset);
        if (message > (uint)NotifyIconMessage.SetVersion)
        {
            return null;
        }

        return new NotifyIconCommand(
            (NotifyIconMessage)message,
            ReadHandle(data, OwnerOffset),
            ReadUInt32(data, IdOffset),
            (NotifyIconFields)ReadUInt32(data, FlagsOffset),
            ReadUInt32(data, CallbackOffset),
            ReadHandle(data, IconOffset),
            ReadString(data, TipOffset, TipChars),
            (NotifyIconStates)ReadUInt32(data, StateOffset),
            (NotifyIconStates)ReadUInt32(data, StateMaskOffset),
            ReadUInt32(data, VersionOffset),
            ReadGuid(data, GuidOffset));
    }

    /// <summary>Parses a Shell_NotifyIconGetRect request, or returns null when the block is malformed.</summary>
    public static TrayIconRectQuery? ParseIconRectQuery(ReadOnlySpan<byte> data)
    {
        if (data.Length < RectQuerySize)
        {
            return null;
        }

        var part = ReadUInt32(data, RectMessageOffset);
        if (part is not (1 or 2))
        {
            return null;
        }

        var icon = new TrayIconId(ReadHandle(data, RectOwnerOffset), ReadUInt32(data, RectIdOffset), ReadGuid(data, RectGuidOffset));
        return new TrayIconRectQuery(icon, part == 2);
    }

    /// <summary>Reads a DWORD, or 0 when the (shorter, older-format) block does not contain it.</summary>
    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        offset + 4 <= data.Length ? BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]) : 0;

    /// <summary>
    /// 32-bit handle on the wire. Window and icon handles only use their low 32 bits and are sign-extended to 64 bits
    /// (the documented rule for sharing handles between 32- and 64-bit processes).
    /// </summary>
    private static nint ReadHandle(ReadOnlySpan<byte> data, int offset) => (int)ReadUInt32(data, offset);

    private static string ReadString(ReadOnlySpan<byte> data, int offset, int maxChars)
    {
        if (offset >= data.Length)
        {
            return string.Empty;
        }

        var available = Math.Min(maxChars, (data.Length - offset) / 2);
        var chars = new char[available];
        var length = 0;
        while (length < available)
        {
            var c = (char)BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + (length * 2))..]);
            if (c == '\0')
            {
                break;
            }

            chars[length++] = c;
        }

        return new string(chars, 0, length);
    }

    private static Guid ReadGuid(ReadOnlySpan<byte> data, int offset) =>
        offset + 16 <= data.Length ? new Guid(data.Slice(offset, 16)) : Guid.Empty;
}
