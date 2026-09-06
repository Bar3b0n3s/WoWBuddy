using WoWBuddy.Common.Logging;

namespace WoWBuddy.Common.Scheduling;

/// <summary>Something a player said to this character privately.</summary>
/// <param name="From">Who sent it.</param>
/// <param name="Text">What they said.</param>
/// <param name="At">When the bot noticed it.</param>
public readonly record struct Whisper(string From, string Text, DateTimeOffset At);

/// <summary>What to do when someone whispers.</summary>
public enum WhisperResponse
{
    /// <summary>Carry on. The whisper is still recorded and shown in the window.</summary>
    Ignore,

    /// <summary>Stand still for a while, then carry on by itself.</summary>
    Pause,

    /// <summary>Stop for good and leave the character standing.</summary>
    Stop,
}

/// <summary>How the bot reacts to being spoken to.</summary>
public sealed record WhisperSettings
{
    /// <summary>What to do about a whisper.</summary>
    /// <remarks>
    /// Pausing by default rather than stopping. A stop ends the session over one "hi", and a
    /// character standing still for ten minutes looks far more like a person answering a
    /// whisper than one that carries on grinding mid-conversation.
    /// </remarks>
    public WhisperResponse Response { get; init; } = WhisperResponse.Pause;

    /// <summary>How long to stand still for.</summary>
    public TimeSpan PauseFor { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Names whose whispers are ignored.
    /// </summary>
    /// <remarks>
    /// Your own alt, a friend who knows, the guild's usual chatter. Without this the feature is
    /// unusable in a guild: one "afk?" and the session is over.
    /// </remarks>
    public IReadOnlySet<string> Ignore { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many whispers to keep for the window to show.</summary>
    public int Remember { get; init; } = 20;

    /// <summary>True when the bot should do anything at all about a whisper.</summary>
    public bool Reacts => Response != WhisperResponse.Ignore;
}

/// <summary>
/// Where whispers come from.
/// </summary>
/// <remarks>
/// Draining rather than reading: each whisper must be acted on exactly once, and a source that
/// simply reported "the last thing said" would either miss messages that arrived between ticks
/// or act on the same one repeatedly.
/// </remarks>
public interface IWhisperSource
{
    /// <summary>Takes everything that has arrived since the last call.</summary>
    IReadOnlyList<Whisper> Drain();
}

/// <summary>
/// Stopping when a person starts talking to the character.
/// </summary>
/// <remarks>
/// <para>
/// The one thing an unattended bot most needs to notice. A character that keeps killing boars
/// while a game master asks it a question is the clearest possible answer to the question being
/// asked, and it is the failure mode that ends accounts rather than sessions.
/// </para>
/// <para>
/// <b>It does not reply.</b> Answering automatically would be worse than silence: a bot that
/// says "hi" to a game master has still demonstrated exactly what it is, and this project will
/// not write something whose purpose is to be mistaken for a person in conversation. What it
/// does is stop playing, which is what a person who has stepped away would look like anyway.
/// </para>
/// </remarks>
public sealed class WhisperWatch
{
    private readonly WhisperSettings _settings;
    private readonly List<Whisper> _seen = [];

    public WhisperWatch(WhisperSettings? settings = null)
    {
        _settings = settings ?? new WhisperSettings();
    }

    /// <summary>What it is set to do.</summary>
    public WhisperSettings Settings => _settings;

    /// <summary>The most recent whispers, newest last, for the window to show.</summary>
    public IReadOnlyList<Whisper> Seen => _seen;

    /// <summary>The last whisper that made the bot do something, if any.</summary>
    public Whisper? Acted { get; private set; }

    /// <summary>How many whispers have arrived, ignored ones included.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Looks at whatever has arrived and acts on the session.
    /// </summary>
    /// <returns>True when the bot changed what it was doing because of one.</returns>
    public bool Consider(IWhisperSource source, SessionScheduler session, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(session);

        bool acted = false;

        foreach (Whisper whisper in source.Drain())
        {
            Count++;
            Record(whisper);

            if (_settings.Ignore.Contains(whisper.From))
            {
                Log.For<WhisperWatch>().Debug(
                    "{From} whispered, and is on the ignore list", whisper.From);
                continue;
            }

            // The sender at information level and the message only at debug: knowing the bot
            // stopped because someone spoke to it belongs in the log, and another player's
            // words are not the bot's to write down where they will be read later.
            Log.For<WhisperWatch>().Information("{From} whispered", whisper.From);
            Log.For<WhisperWatch>().Debug("{From} said: {Text}", whisper.From, whisper.Text);

            if (!_settings.Reacts)
            {
                continue;
            }

            Acted = whisper;
            acted = true;

            if (_settings.Response == WhisperResponse.Stop)
            {
                session.Stop(StopReason.Whispered);
                return true;
            }

            // Extended rather than started afresh, so that a conversation keeps the character
            // still for as long as it lasts rather than for ten minutes from the first line.
            session.Pause(now + _settings.PauseFor, StopReason.Whispered);
        }

        return acted;
    }

    /// <summary>Forgets what it has seen, for a fresh session.</summary>
    public void Reset()
    {
        _seen.Clear();
        Acted = null;
        Count = 0;
    }

    private void Record(Whisper whisper)
    {
        _seen.Add(whisper);

        if (_seen.Count > Math.Max(1, _settings.Remember))
        {
            _seen.RemoveAt(0);
        }
    }
}
