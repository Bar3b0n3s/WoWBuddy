using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.GameApi.Objects;

/// <summary>
/// The character the bot is playing.
/// </summary>
/// <remarks>
/// Distinguished from other players because a handful of things are only knowable about the
/// local character, and because the rest of the bot needs one obvious "me" to reason from.
/// </remarks>
public sealed class WoWLocalPlayer : WoWPlayer
{
    internal WoWLocalPlayer(
        GameObjectRef reference,
        Offsets335a.PositionLayout positionLayout,
        PlayerNameCache? names)
        : base(reference, positionLayout, names)
    {
    }

    /// <summary>
    /// Current map (continent) id: 0 Eastern Kingdoms, 1 Kalimdor, 530 Outland, 571 Northrend.
    /// </summary>
    /// <remarks>
    /// Read from a static address with only a single published source, so treat a surprising
    /// value as a bad read rather than as the character having teleported.
    /// </remarks>
    public int MapId => Memory.ReadOrDefault<int>(
        Offsets335a.Rebase(Offsets335a.ClientState.MapId, Memory.ModuleBase), -1);

    /// <summary>Current zone id. Single-source, like <see cref="MapId"/>.</summary>
    public int ZoneId => Memory.ReadOrDefault<int>(
        Offsets335a.Rebase(Offsets335a.ClientState.ZoneId, Memory.ModuleBase), -1);

    /// <summary>True when the character is at full health and has no power to restore.</summary>
    public bool IsRested => HealthPercent >= 99d && PowerPercent >= 99d;
}
