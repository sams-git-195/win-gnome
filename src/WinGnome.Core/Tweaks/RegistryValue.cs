namespace WinGnome.Core.Tweaks;

/// <summary>Registry value types WinGnome cares about (a separate enum so Core never references Microsoft.Win32).</summary>
#pragma warning disable CA1720 // Member names mirror Microsoft.Win32.RegistryValueKind.
public enum RegistryValueKind { String, DWord, QWord, Binary, ExpandString }
#pragma warning restore CA1720

/// <summary>
/// A typed registry value. <see cref="Data"/> is a string (String/ExpandString), an int (DWord),
/// a long (QWord) or a byte[] (Binary). Equality compares byte arrays by content.
/// </summary>
public sealed record RegistryValue(RegistryValueKind Kind, object Data)
{
    /// <summary>Creates a REG_SZ value.</summary>
    public static RegistryValue Text(string data) => new(RegistryValueKind.String, data);

    /// <summary>Creates a REG_EXPAND_SZ value.</summary>
    public static RegistryValue ExpandText(string data) => new(RegistryValueKind.ExpandString, data);

    /// <summary>Creates a REG_DWORD value.</summary>
    public static RegistryValue DWord(int data) => new(RegistryValueKind.DWord, data);

    /// <summary>Creates a REG_QWORD value.</summary>
    public static RegistryValue QWord(long data) => new(RegistryValueKind.QWord, data);

    /// <summary>Creates a REG_BINARY value (the array is copied).</summary>
    public static RegistryValue Binary(byte[] data) => new(RegistryValueKind.Binary, (byte[])data.Clone());

    /// <inheritdoc />
    public bool Equals(RegistryValue? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Kind != other.Kind)
        {
            return false;
        }

        return Data is byte[] a && other.Data is byte[] b ? a.AsSpan().SequenceEqual(b) : Equals(Data, other.Data);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        if (Data is byte[] bytes)
        {
            hash.AddBytes(bytes);
        }
        else
        {
            hash.Add(Data);
        }

        return hash.ToHashCode();
    }
}
