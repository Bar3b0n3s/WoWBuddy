using System.Buffers.Binary;
using System.Text;

namespace WoWBuddy.WorldData.Dbc;

/// <summary>
/// A client data file, in the format 3.3.5a uses.
/// </summary>
/// <remarks>
/// <para>
/// A DBC is a header, a block of fixed-width records, and a block of strings the records point
/// into by offset. Every field is four bytes and the file does not say which are numbers and
/// which are string offsets — the reader has to know, which is why this class only reads
/// integers and leaves interpreting them to whoever asked.
/// </para>
/// <para>
/// <b>This is client data, extracted from your own game.</b> None ships here and none ever may,
/// the same rule as the navigation meshes. The extractor that produces the meshes produces
/// these too.
/// </para>
/// </remarks>
public sealed class DbcFile
{
    /// <summary>The four bytes every DBC starts with.</summary>
    public const uint Magic = 0x43424457;

    /// <summary>Bytes of header before the records begin.</summary>
    public const int HeaderSize = 20;

    private readonly byte[] _records;
    private readonly byte[] _strings;

    private DbcFile(int recordCount, int fieldCount, int recordSize, byte[] records, byte[] strings)
    {
        RecordCount = recordCount;
        FieldCount = fieldCount;
        RecordSize = recordSize;
        _records = records;
        _strings = strings;
    }

    /// <summary>How many rows the file holds.</summary>
    public int RecordCount { get; }

    /// <summary>How many four-byte fields each row has.</summary>
    public int FieldCount { get; }

    /// <summary>Bytes per row.</summary>
    public int RecordSize { get; }

    /// <summary>Reads a DBC from bytes.</summary>
    /// <param name="bytes">The whole file.</param>
    /// <param name="file">The parsed file.</param>
    /// <param name="error">Why it could not be read.</param>
    public static bool TryRead(ReadOnlySpan<byte> bytes, out DbcFile? file, out string error)
    {
        file = null;
        error = string.Empty;

        if (bytes.Length < HeaderSize)
        {
            error = "The file is too short to be a DBC.";
            return false;
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic)
        {
            error = "The file does not start with 'WDBC', so it is not a 3.3.5 DBC.";
            return false;
        }

        int recordCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        int fieldCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        int recordSize = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        int stringSize = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);

        if (recordCount < 0 || fieldCount <= 0 || recordSize <= 0 || stringSize < 0)
        {
            error = "The DBC header holds impossible sizes.";
            return false;
        }

        // Every field is four bytes, so the record size and the field count have to agree.
        // They disagreeing means the file is for a different build, and reading it anyway
        // would produce numbers that look plausible and are not.
        if (recordSize != fieldCount * 4)
        {
            error = $"The DBC says {fieldCount} fields but {recordSize} bytes per record. "
                + "It is probably from a different game version.";
            return false;
        }

        long needed = (long)HeaderSize + ((long)recordCount * recordSize) + stringSize;

        if (bytes.Length < needed)
        {
            error = $"The DBC says it holds {recordCount} records but the file is too short.";
            return false;
        }

        byte[] records = bytes.Slice(HeaderSize, recordCount * recordSize).ToArray();
        byte[] strings = bytes.Slice(HeaderSize + (recordCount * recordSize), stringSize).ToArray();

        file = new DbcFile(recordCount, fieldCount, recordSize, records, strings);
        return true;
    }

    /// <summary>Reads a DBC from a file on disk.</summary>
    public static bool TryReadFile(string path, out DbcFile? file, out string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        file = null;

        if (!File.Exists(path))
        {
            error = $"{path} is not there.";
            return false;
        }

        try
        {
            return TryRead(File.ReadAllBytes(path), out file, out error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"{path} could not be read: {exception.Message}";
            return false;
        }
    }

    /// <summary>One field of one row, as a number.</summary>
    public int GetInt(int record, int field)
    {
        if (record < 0 || record >= RecordCount || field < 0 || field >= FieldCount)
        {
            return 0;
        }

        return BinaryPrimitives.ReadInt32LittleEndian(
            _records.AsSpan((record * RecordSize) + (field * 4)));
    }

    /// <summary>
    /// One field of one row, treated as an offset into the string block.
    /// </summary>
    /// <remarks>
    /// Strings are null-terminated and UTF-8. An offset outside the block returns nothing
    /// rather than throwing: a field this project guessed was a string and is not would
    /// otherwise take the whole read down.
    /// </remarks>
    public string GetString(int record, int field)
    {
        int offset = GetInt(record, field);

        if (offset <= 0 || offset >= _strings.Length)
        {
            return string.Empty;
        }

        int end = Array.IndexOf(_strings, (byte)0, offset);

        if (end < 0)
        {
            end = _strings.Length;
        }

        return Encoding.UTF8.GetString(_strings, offset, end - offset);
    }
}
