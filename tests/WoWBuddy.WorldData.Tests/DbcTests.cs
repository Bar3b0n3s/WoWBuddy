using System.Buffers.Binary;
using System.Text;
using WoWBuddy.WorldData.Dbc;
using Xunit;

namespace WoWBuddy.WorldData.Tests;

/// <summary>
/// Builds a DBC byte for byte, the way the client writes one.
/// </summary>
/// <remarks>
/// The same approach the navigation mesh tests take: a reader is only worth trusting if
/// something built the file to the format's own rules rather than to the reader's assumptions.
/// </remarks>
internal sealed class DbcBuilder(int fieldCount)
{
    private readonly List<int[]> _records = [];
    private readonly List<byte> _strings = [0];

    public DbcBuilder WithRecord(params int[] fields)
    {
        Assert.Equal(fieldCount, fields.Length);
        _records.Add(fields);
        return this;
    }

    /// <summary>Adds a string and returns the offset to point a field at.</summary>
    public int AddString(string text)
    {
        int offset = _strings.Count;
        _strings.AddRange(Encoding.UTF8.GetBytes(text));
        _strings.Add(0);
        return offset;
    }

    public byte[] Build(uint magic = DbcFile.Magic, int? recordSizeOverride = null)
    {
        int recordSize = recordSizeOverride ?? (fieldCount * 4);

        byte[] bytes = new byte[
            DbcFile.HeaderSize + (_records.Count * (fieldCount * 4)) + _strings.Count];

        BinaryPrimitives.WriteUInt32LittleEndian(bytes, magic);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), _records.Count);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), fieldCount);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), recordSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), _strings.Count);

        int at = DbcFile.HeaderSize;

        foreach (int[] record in _records)
        {
            foreach (int field in record)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), field);
                at += 4;
            }
        }

        _strings.CopyTo(bytes, at);
        return bytes;
    }
}

public sealed class DbcTests
{
    [Fact]
    public void ReadsRecordsAndFields()
    {
        byte[] bytes = new DbcBuilder(3)
            .WithRecord(1, 20, 300)
            .WithRecord(2, 40, 600)
            .Build();

        Assert.True(DbcFile.TryRead(bytes, out DbcFile? dbc, out string error), error);

        Assert.Equal(2, dbc!.RecordCount);
        Assert.Equal(3, dbc.FieldCount);
        Assert.Equal(600, dbc.GetInt(1, 2));
    }

    [Fact]
    public void ReadsStringsThroughTheirOffsets()
    {
        DbcBuilder builder = new(2);
        int offset = builder.AddString("Stormwind");
        builder.WithRecord(1, offset);

        Assert.True(DbcFile.TryRead(builder.Build(), out DbcFile? dbc, out _));
        Assert.Equal("Stormwind", dbc!.GetString(0, 1));
    }

    [Fact]
    public void AFileThatIsNotADbcIsRefused()
    {
        byte[] bytes = new DbcBuilder(2).WithRecord(1, 2).Build(magic: 0x12345678);

        Assert.False(DbcFile.TryRead(bytes, out _, out string error));
        Assert.Contains("WDBC", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AHeaderWhoseSizesDisagreeIsRefused()
    {
        // Every field is four bytes. The two disagreeing means the file is for a different
        // build, and reading it anyway produces numbers that look plausible and are not.
        byte[] bytes = new DbcBuilder(3).WithRecord(1, 2, 3).Build(recordSizeOverride: 16);

        Assert.False(DbcFile.TryRead(bytes, out _, out string error));
        Assert.Contains("different game version", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ATruncatedFileIsRefusedRatherThanReadPartly()
    {
        byte[] bytes = new DbcBuilder(2).WithRecord(1, 2).WithRecord(3, 4).Build();

        Assert.False(DbcFile.TryRead(bytes.AsSpan(0, bytes.Length - 6), out _, out string error));
        Assert.Contains("too short", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOffsetOutsideTheStringBlockReadsAsNothing()
    {
        // A field this project guessed was a string and is not would otherwise take the whole
        // read down.
        byte[] bytes = new DbcBuilder(2).WithRecord(1, 99999).Build();

        Assert.True(DbcFile.TryRead(bytes, out DbcFile? dbc, out _));
        Assert.Empty(dbc!.GetString(0, 1));
    }

    [Fact]
    public void ReadingOutsideTheTableIsZeroRatherThanAThrow()
    {
        byte[] bytes = new DbcBuilder(2).WithRecord(1, 2).Build();

        Assert.True(DbcFile.TryRead(bytes, out DbcFile? dbc, out _));

        Assert.Equal(0, dbc!.GetInt(5, 0));
        Assert.Equal(0, dbc.GetInt(0, 9));
        Assert.Equal(0, dbc.GetInt(-1, 0));
    }

    [Fact]
    public void AnEmptyFileIsNotADbc()
    {
        Assert.False(DbcFile.TryRead([], out _, out _));
    }
}
