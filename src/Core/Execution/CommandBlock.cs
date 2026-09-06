namespace WoWBuddy.Core.Execution;

/// <summary>
/// Layout of the small block of client memory the bot and the game thread talk through.
/// </summary>
/// <remarks>
/// <para>
/// The bot cannot call into the client directly: the client's functions expect to run on its
/// own thread, and most of them touch state that is only safe to touch there. So the bot
/// leaves work in this block, and code injected into the render loop picks it up on the next
/// frame and leaves the answer behind.
/// </para>
/// <para>
/// Two allocations rather than one. The data block is read/write; the code block is the only
/// executable page, and it is kept as small as possible. Leaving a large RWX region in
/// someone's game process for hours is worth avoiding.
/// </para>
/// <para>
/// The handshake is deliberately a single word. The bot writes the call, then writes
/// <see cref="State"/>; the game thread reads <see cref="State"/>, does the work, then writes
/// it back. There is no lock, because there are only ever two participants and each writes
/// the word only when the other is not looking at anything else. The ordering is what makes
/// it safe: the request must be fully written before the state word says so.
/// </para>
/// </remarks>
public static class CommandBlock
{
    /// <summary>Total size of the data allocation.</summary>
    public const int DataSize = 0x1000;

    /// <summary>Total size of the code allocation.</summary>
    public const int CodeSize = 0x1000;

    // ---- Data block layout -------------------------------------------------------------

    /// <summary>Handshake word. See <see cref="CommandState"/>.</summary>
    public const int StateOffset = 0x00;

    /// <summary>
    /// Incremented by the game thread every time it completes a call.
    /// </summary>
    /// <remarks>
    /// Proof of life. If the state word never reaches Complete the bot cannot tell a frozen
    /// game apart from a hook that was never reached; a moving sequence number says the
    /// injected code is running even when a particular call is slow.
    /// </remarks>
    public const int SequenceOffset = 0x04;

    /// <summary>Low 32 bits of the return value (eax).</summary>
    public const int ReturnLowOffset = 0x08;

    /// <summary>High 32 bits of a 64-bit return value (edx).</summary>
    public const int ReturnHighOffset = 0x0C;

    /// <summary>Floating-point return value, popped from the x87 stack.</summary>
    public const int ReturnFloatOffset = 0x10;

    /// <summary>
    /// Holds the address of the real EndScene, so the stub can jump to it without clobbering
    /// a register.
    /// </summary>
    public const int OriginalFunctionOffset = 0x14;

    /// <summary>Start of the area used for call arguments and strings.</summary>
    public const int ScratchOffset = 0x40;

    /// <summary>Bytes available for arguments and strings.</summary>
    public const int ScratchSize = DataSize - ScratchOffset;

    // ---- Code block layout -------------------------------------------------------------

    /// <summary>Offset of the resident stub within the code allocation.</summary>
    public const int StubOffset = 0x00;

    /// <summary>
    /// Offset of the per-call thunk within the code allocation.
    /// </summary>
    /// <remarks>
    /// Rewritten for every call rather than being a fixed dispatcher over a command
    /// enumeration. That is what lets one tiny stub serve any calling convention and any
    /// number of arguments: the argument pushes, the call and the result stores are all
    /// generated for the specific call being made. The stub only ever has to know how to
    /// call one address.
    /// </remarks>
    public const int ThunkOffset = 0x80;

    /// <summary>Bytes available for the per-call thunk.</summary>
    public const int ThunkCapacity = CodeSize - ThunkOffset;
}

/// <summary>Values of the handshake word.</summary>
public enum CommandState : uint
{
    /// <summary>Nothing to do. The game thread does no work in this state.</summary>
    Idle = 0,

    /// <summary>The bot has written a call and the game thread should run it.</summary>
    Requested = 1,

    /// <summary>The game thread is running the call right now.</summary>
    Running = 2,

    /// <summary>The call finished and its results are readable.</summary>
    Complete = 3,
}
