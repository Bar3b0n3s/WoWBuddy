using WoWBuddy.Common.Logging;

namespace WoWBuddy.Common.Scheduling;

/// <summary>Getting a character that has fallen out of the world back into it.</summary>
/// <remarks>
/// <para>
/// Deliberately not a login. This project stores no credentials and never will, so it cannot
/// answer a login screen — but it does not have to for the common case: a dropped connection
/// puts the client at character select with the last character still chosen, and getting back
/// from there needs nothing secret at all.
/// </para>
/// <para>
/// A client sitting at the login screen instead needs a person, and the bot says so rather than
/// poking at it.
/// </para>
/// </remarks>
public interface IWorldEntry
{
    /// <summary>True when the client is somewhere the bot can enter the world from.</summary>
    /// <remarks>
    /// Asked at the moment it is needed rather than probed at attach. The calls this needs only
    /// exist while the character-select screen is loaded, so asking about them from inside the
    /// world would report them missing every time and switch the feature off for good.
    /// </remarks>
    bool CanEnterWorld { get; }

    /// <summary>Enters the world. False when the client would not.</summary>
    /// <param name="slot">Which character, or 0 for whichever the client already has chosen.</param>
    bool EnterWorld(int slot);
}

/// <summary>When and how hard to try getting back in.</summary>
public sealed record ReconnectSettings
{
    /// <summary>Whether to try at all.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// How long to be out of the world before treating it as a disconnection.
    /// </summary>
    /// <remarks>
    /// A loading screen is also "not in the world", and so is zoning, and so is the moment
    /// after a portal. Trying to enter the world during one of those is at best wasted and at
    /// worst confusing to watch, so the bot waits long enough to tell them apart.
    /// </remarks>
    public TimeSpan WaitBefore { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long to leave between attempts.</summary>
    public TimeSpan BetweenAttempts { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many times to try before giving up on the session.
    /// </summary>
    /// <remarks>
    /// Small. A character that has not come back after a few tries is not coming back for a
    /// reason the bot can do anything about — the realm is down, the account is suspended,
    /// there is a queue — and something clicking at a screen for six hours is worse than
    /// something that stopped and said why.
    /// </remarks>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Which character to enter as, or 0 for whichever the client already has chosen.
    /// </summary>
    /// <remarks>
    /// Zero by default, and that is the better answer: character select opens on the character
    /// that was last played, which is the one that just fell out of the world.
    /// </remarks>
    public int CharacterSlot { get; init; }
}

/// <summary>
/// Noticing a dropped connection and getting back in.
/// </summary>
/// <remarks>
/// <para>
/// The difference between a session that ends at two in the morning because the realm blinked,
/// and one that carries on. It is the smallest possible version of that: no credentials, no
/// login screen, no retry loop that outlives its usefulness.
/// </para>
/// <para>
/// Everything about it is a decision over times and counts, which is why it lives here and not
/// beside the client: the interesting part is when to give up, and that is testable without a
/// game.
/// </para>
/// </remarks>
public sealed class ReconnectWatch
{
    private readonly ReconnectSettings _settings;

    private DateTimeOffset _goneSince = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private bool _away;

    public ReconnectWatch(ReconnectSettings? settings = null)
    {
        _settings = settings ?? new ReconnectSettings();
    }

    /// <summary>What it is set to do.</summary>
    public ReconnectSettings Settings => _settings;

    /// <summary>How many times it has asked the client to enter the world.</summary>
    public int Attempts { get; private set; }

    /// <summary>True while the character is out of the world and being waited on.</summary>
    public bool IsWaiting => _away;

    /// <summary>
    /// Called on a tick where the character is not in the world.
    /// </summary>
    /// <returns>True when it asked the client to enter the world.</returns>
    public bool WhileAway(IWorldEntry entry, SessionScheduler session, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(session);

        if (!_away)
        {
            _away = true;
            _goneSince = now;
        }

        if (!_settings.Enabled || session.State == SessionState.Stopped)
        {
            return false;
        }

        // Not yet a disconnection: a loading screen looks exactly like one for the first few
        // seconds, and every zone change would otherwise be treated as a crash.
        if (now - _goneSince < _settings.WaitBefore)
        {
            return false;
        }

        if (Attempts >= _settings.MaxAttempts)
        {
            return false;
        }

        if (Attempts > 0 && now - _lastAttempt < _settings.BetweenAttempts)
        {
            return false;
        }

        if (!entry.CanEnterWorld)
        {
            // At the login screen, or a client whose scripting the bot cannot reach from here.
            // Either way a person is needed, and saying so once is better than trying forever.
            Give(session, "the client is not at character select, so a person has to log it in");
            return false;
        }

        _lastAttempt = now;
        Attempts++;

        Log.For<ReconnectWatch>().Information(
            "Character is not in the world; entering it (attempt {Attempt} of {Max})",
            Attempts, _settings.MaxAttempts);

        if (entry.EnterWorld(_settings.CharacterSlot))
        {
            return true;
        }

        if (Attempts >= _settings.MaxAttempts)
        {
            Give(session, "the client would not enter the world");
        }

        return false;
    }

    /// <summary>Called on a tick where the character is in the world.</summary>
    public void Back()
    {
        if (!_away)
        {
            return;
        }

        if (Attempts > 0)
        {
            Log.For<ReconnectWatch>().Information("Character is back in the world");
        }

        _away = false;
        Attempts = 0;
    }

    private void Give(SessionScheduler session, string why)
    {
        Log.For<ReconnectWatch>().Warning(
            "Giving up on getting back into the world: {Reason}", why);

        // Stopped rather than left trying. A bot clicking at a screen for six hours has stopped
        // being useful and started being conspicuous.
        session.Stop(StopReason.Disconnected);

        // Counted as spent so a stopped session does not log the same line every tick.
        Attempts = _settings.MaxAttempts;
    }
}
