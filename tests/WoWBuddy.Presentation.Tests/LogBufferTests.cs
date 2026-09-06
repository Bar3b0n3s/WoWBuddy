using WoWBuddy.Presentation;
using Xunit;

namespace WoWBuddy.Presentation.Tests;

public sealed class LogBufferTests
{
    [Fact]
    public void KeepsTheNewestLinesAndDropsTheOldest()
    {
        // A bot left running overnight writes a great many lines, and a window holding every
        // one of them ends the night using more memory than the bot does.
        LogBuffer buffer = new(capacity: 3);

        for (int index = 0; index < 5; index++)
        {
            buffer.Add(new LogLine(DateTimeOffset.UnixEpoch, "Information", $"line {index}"));
        }

        Assert.Equal(3, buffer.Lines.Count);
        Assert.Equal("line 2", buffer.Lines[0].Message);
        Assert.Equal("line 4", buffer.Lines[2].Message);
    }

    [Fact]
    public void ABufferHoldingNothingIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogBuffer(0));
    }

    [Fact]
    public void LinesReadAsAClockThenALevelThenTheMessage()
    {
        LogLine line = new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), "Warning", "something");

        Assert.StartsWith("03:04:05", line.ToString(), StringComparison.Ordinal);
        Assert.EndsWith("something", line.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheWholeBufferCanBeCopiedOut()
    {
        LogBuffer buffer = new();
        buffer.Add(new LogLine(DateTimeOffset.UnixEpoch, "Information", "first"));
        buffer.Add(new LogLine(DateTimeOffset.UnixEpoch, "Error", "second"));

        string text = buffer.AsText();

        Assert.Contains("first", text, StringComparison.Ordinal);
        Assert.Contains("second", text, StringComparison.Ordinal);
        Assert.Equal(2, text.Split(Environment.NewLine).Length);
    }

    [Fact]
    public void ClearingEmptiesIt()
    {
        LogBuffer buffer = new();
        buffer.Add(new LogLine(DateTimeOffset.UnixEpoch, "Information", "something"));

        buffer.Clear();

        Assert.Empty(buffer.Lines);
    }
}
