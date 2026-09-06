using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;

namespace WoWBuddy.BotBases.Group;

/// <summary>What the bot does about keeping up with someone.</summary>
public enum FollowAction
{
    /// <summary>Close enough. Stay put.</summary>
    Stay,

    /// <summary>Too far. Move towards them.</summary>
    Close,

    /// <summary>Far enough that walking there is a bad idea; say so instead.</summary>
    GiveUp,
}

/// <summary>How closely to keep up with someone.</summary>
public sealed record FollowSettings
{
    /// <summary>Start moving once further away than this, in yards.</summary>
    public float FollowDistance { get; init; } = 12f;

    /// <summary>Stop moving once closer than this, in yards.</summary>
    /// <remarks>
    /// Must be meaningfully below <see cref="FollowDistance"/>. The gap between the two is
    /// what stops the character shuffling forward and back on the spot every time the person
    /// being followed breathes, which is both useless and one of the more obvious things a
    /// watching player would notice.
    /// </remarks>
    public float StopDistance { get; init; } = 6f;

    /// <summary>Beyond this, do not try to walk there at all.</summary>
    /// <remarks>
    /// Someone this far away has zoned, hearthed, taken a flight path or died somewhere else.
    /// Pathing across a continent to catch up is worse than stopping and saying so.
    /// </remarks>
    public float GiveUpDistance { get; init; } = 150f;

    /// <summary>True when the settings make sense.</summary>
    public bool IsUsable => StopDistance > 0f
        && FollowDistance > StopDistance
        && GiveUpDistance > FollowDistance;
}

/// <summary>
/// Keeps the character near someone else.
/// </summary>
/// <remarks>
/// <para>
/// The whole of this is a hysteresis band, and that is the point. Following on a single
/// distance produces a character that starts and stops many times a second, which wastes the
/// tick, fights the movement controller, and looks exactly like a bot. Two distances — one to
/// start at, a nearer one to stop at — produce something that moves in the way a person keeping
/// up with a friend does.
/// </para>
/// <para>
/// It remembers whether it is currently closing, which is the state the band needs and the only
/// state it keeps.
/// </para>
/// </remarks>
public sealed class FollowController(FollowSettings? settings = null)
{
    private readonly FollowSettings _settings = settings ?? new FollowSettings();
    private bool _closing;

    /// <summary>How closely it is keeping up.</summary>
    public FollowSettings Settings => _settings;

    /// <summary>True while it is currently moving to catch up.</summary>
    public bool IsClosing => _closing;

    /// <summary>Decides what to do about the distance to whoever is being followed.</summary>
    public FollowAction Decide(float distance)
    {
        if (!_settings.IsUsable)
        {
            // Refuse rather than guess: a follow distance below the stop distance would make
            // the character oscillate forever, which is worse than not following.
            Log.For<FollowController>().Error(
                "Follow distances make no sense (stop {Stop}, follow {Follow}, give up {GiveUp}); "
                + "not following", _settings.StopDistance, _settings.FollowDistance,
                _settings.GiveUpDistance);

            _closing = false;
            return FollowAction.GiveUp;
        }

        if (distance > _settings.GiveUpDistance)
        {
            _closing = false;
            return FollowAction.GiveUp;
        }

        if (_closing)
        {
            // Already moving: keep going until properly caught up, not merely until back
            // inside the follow distance.
            if (distance <= _settings.StopDistance)
            {
                _closing = false;
                return FollowAction.Stay;
            }

            return FollowAction.Close;
        }

        if (distance > _settings.FollowDistance)
        {
            _closing = true;
            return FollowAction.Close;
        }

        return FollowAction.Stay;
    }

    /// <summary>Forgets that it was closing, for when the thing being followed changes.</summary>
    public void Reset() => _closing = false;

    /// <summary>Decides what to do about a party member, treating a missing one as give up.</summary>
    public FollowAction Decide(PartyMember? member) =>
        member is { } present ? Decide(present.Distance) : FollowAction.GiveUp;

    /// <summary>Where to stand relative to someone, for roles that keep their distance.</summary>
    /// <remarks>
    /// A point <paramref name="range"/> yards from the anchor on the line back towards where
    /// the character already is. Deliberately not a fixed offset: standing at a set compass
    /// point relative to the tank puts a healer in the fire as often as out of it, and moving
    /// the shortest distance to a workable spot is both safer and less obviously mechanical.
    /// </remarks>
    public static Vector3 StandOff(Vector3 anchor, Vector3 me, float range)
    {
        float distance = anchor.Distance2D(me);

        if (distance <= 0.01f || !float.IsFinite(distance))
        {
            // Standing on top of them: no direction to back away in, so stay put and let the
            // next tick decide once something has moved.
            return me;
        }

        float scale = range / distance;

        return new Vector3(
            anchor.X + ((me.X - anchor.X) * scale),
            anchor.Y + ((me.Y - anchor.Y) * scale),
            anchor.Z + ((me.Z - anchor.Z) * scale));
    }
}
