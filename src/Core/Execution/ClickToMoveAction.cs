namespace WoWBuddy.Core.Execution;

/// <summary>
/// Action codes written into the client's click-to-move block.
/// </summary>
/// <remarks>
/// <para>
/// The code tells the client's movement system what the destination means: walk there, walk
/// there and interact with a GUID, face a direction, stop. Used from phase 3.
/// </para>
/// <para>
/// <b>Single-sourced.</b> These come from one public table and none has been confirmed
/// against a client. <see cref="Attack"/> in particular is suspect: it sits at 0x10 while the
/// values around it run 0x9, 0xA, 0xB, which looks more like a transcription slip than a real
/// gap. Nothing in the bot uses these yet, and phase 3 must confirm each one it relies on by
/// writing it and watching what the character actually does.
/// </para>
/// </remarks>
public enum ClickToMoveAction : uint
{
    /// <summary>Turn to face the current target.</summary>
    FaceTarget = 0x1,

    /// <summary>Turn to face the stored destination.</summary>
    FaceDestination = 0x2,

    /// <summary>Stop moving.</summary>
    Stop = 0x3,

    /// <summary>Walk to the stored destination.</summary>
    Move = 0x4,

    /// <summary>Walk to a unit and interact with it.</summary>
    Interact = 0x5,

    /// <summary>Walk to a corpse and loot it.</summary>
    Loot = 0x6,

    /// <summary>Walk to a game object and use it. The gathering action.</summary>
    InteractObject = 0x7,

    /// <summary>Turn to face an arbitrary angle.</summary>
    FaceOther = 0x8,

    /// <summary>Walk to a corpse and skin it.</summary>
    Skin = 0x9,

    /// <summary>Attack whatever is at the stored destination.</summary>
    AttackPosition = 0xA,

    /// <summary>Attack the stored GUID.</summary>
    AttackGuid = 0xB,

    /// <summary>
    /// Attack. TODO: verify. The value breaks the sequence its neighbours follow and may be a
    /// transcription error in the single source it came from.
    /// </summary>
    Attack = 0x10,

    /// <summary>Walk to the destination while turning toward it.</summary>
    WalkAndRotate = 0x13,
}
