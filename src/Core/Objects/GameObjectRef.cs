using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Objects;

/// <summary>
/// A raw handle to one object in the client's object manager.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately thin: an address, a type and a GUID, plus a way to reach the descriptor
/// array. The typed game API in <c>WoWBuddy.GameApi</c> is built on top of these; keeping
/// the raw layer separate means the part that touches unverified offsets stays small enough
/// to audit.
/// </para>
/// <para>
/// A handle is only valid for as long as the client keeps the object alive, which can be
/// less than one tick. Handles are therefore produced fresh by each enumeration and are
/// never stored across ticks; store the GUID instead.
/// </para>
/// </remarks>
public readonly struct GameObjectRef
{
    private readonly IMemoryReader _reader;

    internal GameObjectRef(IMemoryReader reader, nint address, WoWObjectType type, WoWGuid guid)
    {
        _reader = reader;
        Address = address;
        Type = type;
        Guid = guid;
    }

    /// <summary>Address of the object in the client's address space.</summary>
    public nint Address { get; }

    /// <summary>The object's type tag.</summary>
    public WoWObjectType Type { get; }

    /// <summary>The object's GUID.</summary>
    public WoWGuid Guid { get; }

    /// <summary>True when this handle refers to something.</summary>
    public bool IsValid => _reader is not null && Address > 0x1000 && !Guid.IsZero;

    /// <summary>The object's descriptor array.</summary>
    public DescriptorTable Descriptors =>
        new(_reader, _reader.ReadPointerOrZero(Address + (nint)Offsets335a.Object.Descriptors));

    /// <summary>The memory reader this handle was produced from.</summary>
    public IMemoryReader Reader => _reader;

    /// <summary>
    /// Checks the object's own GUID field against the GUID in its descriptor array.
    /// </summary>
    /// <remarks>
    /// This is the self-validating check that makes the descriptor pointer offset safe to
    /// use despite having only one published source. The two values are stored in completely
    /// different places and are written by different code paths, so if the descriptor pointer
    /// were wrong they could not agree. Used by the attach-time verifier.
    /// </remarks>
    public bool DescriptorGuidMatches()
    {
        DescriptorTable descriptors = Descriptors;
        if (!descriptors.IsValid)
        {
            return false;
        }

        return descriptors.ReadGuid(UpdateFields335a.Object.Guid) == Guid;
    }

    public override string ToString() => $"{Type} {Guid} @ 0x{Address:X8}";
}
