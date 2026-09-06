using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.Common.Logging;
using WoWBuddy.Common.Scheduling;

namespace WoWBuddy.Live;

/// <summary>What one tick of the bot did.</summary>
public enum TickOutcome
{
    /// <summary>The tree ran.</summary>
    Ran,

    /// <summary>Nothing to do: no character is logged in.</summary>
    NotInWorld,

    /// <summary>The session says to stop, and the bot has.</summary>
    Stopped,

    /// <summary>Something threw. The bot has stopped and the character is standing still.</summary>
    Faulted,
}

/// <summary>
/// Drives the behaviour tree against a live client, one tick at a time.
/// </summary>
/// <remarks>
/// <para>
/// The order inside a tick is the whole content of this class, and each step is where it is for
/// a reason.
/// </para>
/// <list type="number">
/// <item>Nothing at all when no character is in the world — during a loading screen the object
/// manager is empty, and every reading taken from it would be about nobody.</item>
/// <item>The session clock, so that a break or a stop takes effect before the tree gets a
/// chance to start something new.</item>
/// <item>Movement, before the tree, so the tree sees the position the character actually
/// reached rather than the one it had when the last decision was made.</item>
/// <item>The tree.</item>
/// <item>Plugins last, so they see the state after the bot has acted rather than before.</item>
/// </list>
/// <para>
/// <b>Anything that throws stops the bot and leaves the character standing.</b> Not the process,
/// and not the window — the bot. An unattended session that hits an unexpected state should stop
/// somewhere harmless, because a bot that keeps acting on a picture of the world it could not
/// finish assembling is how a character ends up dead in a place nobody expected.
/// </para>
/// </remarks>
public sealed class BotRunner
{
    private readonly LiveBotState _state;
    private readonly Node<IBotState> _tree;
    private readonly Action<IBotState>? _pulsePlugins;

    private bool _wasInWorld;

    /// <summary>Builds a runner over a live state and a behaviour tree.</summary>
    /// <param name="state">The live picture of the client.</param>
    /// <param name="tree">The root tree, already built around a bot base.</param>
    /// <param name="pulsePlugins">
    /// Gives the plugins their tick. Optional, and passed as a delegate rather than a manager so
    /// that this project does not depend on the plugin loader to run a bot.
    /// </param>
    public BotRunner(LiveBotState state, Node<IBotState> tree, Action<IBotState>? pulsePlugins = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _pulsePlugins = pulsePlugins;
    }

    /// <summary>True while the bot is playing.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>How many ticks have run.</summary>
    public long Ticks { get; private set; }

    /// <summary>What the last tick did.</summary>
    public TickOutcome LastOutcome { get; private set; } = TickOutcome.Stopped;

    /// <summary>What went wrong, when something did.</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <summary>What the tree said last time it ran.</summary>
    public RunStatus LastStatus { get; private set; } = RunStatus.Failure;

    /// <summary>Starts playing.</summary>
    public void Start()
    {
        IsRunning = true;
        LastError = string.Empty;
        LastOutcome = TickOutcome.Ran;

        Log.For<BotRunner>().Information("Bot started");
    }

    /// <summary>Stops playing and leaves the character standing still.</summary>
    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;

        // Stopping the character is the last thing done and it is done unconditionally: a bot
        // that stops while the character is still walking somewhere is not stopped.
        SafelyStopMoving();

        Log.For<BotRunner>().Information("Bot stopped after {Ticks} tick(s)", Ticks);
    }

    /// <summary>Runs one tick.</summary>
    public TickOutcome Tick(DateTimeOffset now)
    {
        if (!IsRunning)
        {
            return LastOutcome = TickOutcome.Stopped;
        }

        try
        {
            if (!_state.IsInWorld)
            {
                // A loading screen, a character select, a disconnection. Every reading taken
                // from an empty object manager would be about nobody.
                _wasInWorld = false;
                return LastOutcome = TickOutcome.NotInWorld;
            }

            if (!_wasInWorld)
            {
                // Just arrived. Everything read before this is about a different situation.
                _wasInWorld = true;
                _state.InvalidateCaches();

                Log.For<BotRunner>().Debug("Character is in the world; cached readings cleared");
            }

            if (_state.UpdateSession(now) == SessionState.Stopped)
            {
                Log.For<BotRunner>().Information("The session has ended");
                Stop();
                return LastOutcome = TickOutcome.Stopped;
            }

            _state.Advance(now);

            LastStatus = _tree.Tick(_state);

            // Plugins last, so they see the state after the bot has acted rather than before.
            _pulsePlugins?.Invoke(_state);

            Ticks++;
            return LastOutcome = TickOutcome.Ran;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;

            Log.For<BotRunner>().Error(
                exception,
                "The bot hit an unexpected error and has stopped. The character has been left "
                + "standing where it was.");

            Stop();
            return LastOutcome = TickOutcome.Faulted;
        }
    }

    private void SafelyStopMoving()
    {
        try
        {
            _state.StopMoving();
        }
        catch (Exception exception)
        {
            // Already stopping because something went wrong; there is nothing better to do
            // than say so.
            Log.For<BotRunner>().Warning(exception, "Could not stop the character cleanly");
        }
    }
}
