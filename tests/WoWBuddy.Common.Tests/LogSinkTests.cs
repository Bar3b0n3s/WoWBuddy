using Serilog.Core;
using Serilog.Events;
using WoWBuddy.Common.Logging;
using Xunit;

namespace WoWBuddy.Common.Tests;

/// <summary>Collects whatever it is given.</summary>
internal sealed class CollectingSink : ILogEventSink
{
    public List<string> Messages { get; } = [];

    public void Emit(LogEvent logEvent) => Messages.Add(logEvent.RenderMessage());
}

public sealed class LogSinkTests
{
    [Fact]
    public void AnExtraSinkSeesEverythingTheFileDoes()
    {
        // The window's log panel is a second sink rather than a second logger, so what it shows
        // and what the file records cannot drift apart.
        string directory = Path.Combine(Path.GetTempPath(), $"wowbuddy-log-{Guid.NewGuid():N}");
        CollectingSink sink = new();

        try
        {
            Log.Shutdown();
            Log.Initialise(directory, sink);

            Log.For<LogSinkTests>().Information("something happened");
            Log.Shutdown();

            Assert.Contains("something happened", sink.Messages);
        }
        finally
        {
            Log.Shutdown();

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
