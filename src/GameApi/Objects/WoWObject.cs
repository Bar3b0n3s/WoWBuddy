using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.GameApi.Objects;

/// <summary>
/// Base of the typed game object model.
/// </summary>
/// <remarks>
/// <para>
/// A thin, allocation-light wrapper over a raw <see cref="GameObjectRef"/>. Properties read
/// through to the client on every access rather than caching, because the world changes
/// between one property read and the next and a consistent-looking stale snapshot is more
/// dangerous than an obviously changing live one.
/// </para>
/// <para>
/// Instances are valid only for the tick that produced them. Anything the bot needs to
/// remember across ticks is stored as a GUID and re-resolved.
/// </para>
/// </remarks>
public class WoWObject
{
    internal WoWObject(GameObjectRef reference, Offsets335a.PositionLayout positionLayout)
    {
        Reference = reference;
        PositionLayout = positionLayout;
    }

    /// <summary>The underlying raw handle.</summary>
    public GameObjectRef Reference { get; }

    /// <summary>The unit position layout resolved for the attached client.</summary>
    protected Offsets335a.PositionLayout PositionLayout { get; }

    /// <summary>Memory reader for the attached client.</summary>
    protected IMemoryReader Memory => Reference.Reader;

    /// <summary>The object's descriptor array.</summary>
    protected DescriptorTable Descriptors => Reference.Descriptors;

    /// <summary>The object's GUID.</summary>
    public WoWGuid Guid => Reference.Guid;

    /// <summary>The object's type.</summary>
    public WoWObjectType Type => Reference.Type;

    /// <summary>Address of the object in the client.</summary>
    public nint Address => Reference.Address;

    /// <summary>True when the handle still refers to something readable.</summary>
    public bool IsValid => Reference.IsValid;

    /// <summary>The creature or game object template id, where the GUID carries one.</summary>
    public uint Entry => Guid.Entry != 0
        ? Guid.Entry
        : Descriptors.ReadUInt32(UpdateFields335a.Object.Entry);

    /// <summary>Model scale. Normally 1.0.</summary>
    public float Scale => Descriptors.ReadSingle(UpdateFields335a.Object.ScaleX);

    /// <summary>
    /// The object's world position, or <see cref="Vector3.Zero"/> when it has none.
    /// </summary>
    /// <remarks>
    /// Only units and players are supported. Game objects store their position elsewhere and
    /// that location is not yet verified; see <see cref="WoWGameObject.Position"/>.
    /// </remarks>
    public virtual Vector3 Position => Vector3.Zero;

    public override string ToString() => $"{Type} {Guid}";
}
