using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.GameApi.Objects;

/// <summary>
/// A player's corpse. Needed for corpse runs from phase 4.
/// </summary>
public sealed class WoWCorpse : WoWObject
{
    internal WoWCorpse(GameObjectRef reference, Offsets335a.PositionLayout positionLayout)
        : base(reference, positionLayout)
    {
    }

    /// <summary>GUID of the player this corpse belongs to.</summary>
    public WoWGuid OwnerGuid => Descriptors.ReadGuid(UpdateFields335a.Corpse.Owner);

    public override string ToString() => $"Corpse of {OwnerGuid}";
}

/// <summary>
/// A ground-targeted spell effect, such as a Blizzard or a Consecration.
/// </summary>
/// <remarks>
/// Tracked so that later phases can avoid standing in harmful ground effects.
/// </remarks>
public sealed class WoWDynamicObject : WoWObject
{
    internal WoWDynamicObject(GameObjectRef reference, Offsets335a.PositionLayout positionLayout)
        : base(reference, positionLayout)
    {
    }

    /// <summary>GUID of the unit that created the effect.</summary>
    public WoWGuid CasterGuid => Descriptors.ReadGuid(UpdateFields335a.DynamicObject.Caster);

    /// <summary>Spell that created the effect.</summary>
    public uint SpellId => Descriptors.ReadUInt32(UpdateFields335a.DynamicObject.SpellId);

    /// <summary>Effect radius in yards.</summary>
    public float Radius => Descriptors.ReadSingle(UpdateFields335a.DynamicObject.Radius);

    public override string ToString() => $"DynamicObject spell {SpellId} radius {Radius:F1}";
}
