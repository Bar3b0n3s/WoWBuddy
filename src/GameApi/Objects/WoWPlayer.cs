using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.GameApi.Objects;

/// <summary>
/// A player character, whether the bot's own or somebody else's.
/// </summary>
public class WoWPlayer : WoWUnit
{
    private readonly PlayerNameCache? _names;

    internal WoWPlayer(
        GameObjectRef reference,
        Offsets335a.PositionLayout positionLayout,
        PlayerNameCache? names)
        : base(reference, positionLayout)
    {
        _names = names;
    }

    /// <summary>
    /// The character's name, or an empty string when it is not in the client's name cache.
    /// </summary>
    /// <remarks>
    /// Advisory. The name cache has unverified structure offsets, so this can legitimately
    /// come back empty; see <see cref="PlayerNameCache"/>. Nothing the bot decides is keyed
    /// on a name.
    /// </remarks>
    public string Name => _names?.GetName(Guid) ?? string.Empty;

    /// <summary>Money carried, in copper.</summary>
    public uint Copper => Descriptors.ReadUInt32(UpdateFields335a.Player.Coinage);

    /// <summary>Money carried, in gold.</summary>
    public double Gold => Copper / 10000d;

    /// <summary>Experience earned toward the next level.</summary>
    public uint Experience => Descriptors.ReadUInt32(UpdateFields335a.Player.Xp);

    /// <summary>Experience needed to reach the next level.</summary>
    public uint NextLevelExperience => Descriptors.ReadUInt32(UpdateFields335a.Player.NextLevelXp);

    /// <summary>Progress through the current level, 0 to 100.</summary>
    public double ExperiencePercent
    {
        get
        {
            uint needed = NextLevelExperience;
            return needed == 0 ? 0d : Experience * 100d / needed;
        }
    }

    public override string ToString()
    {
        string name = Name;
        string label = string.IsNullOrEmpty(name) ? Guid.ToString() : name;
        return $"Player {label} level {Level} {Class}";
    }
}
