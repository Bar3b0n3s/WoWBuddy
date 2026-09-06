using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Execution;

/// <summary>
/// Turns a <see cref="RemoteCall"/> into the machine code that performs it.
/// </summary>
/// <remarks>
/// <para>
/// This and <see cref="StubCompiler"/> are the only places that decide what actually executes
/// inside somebody else's game. Both are pure functions from a description to a byte array,
/// which is the point: they can be tested exhaustively without a client, and their output can
/// be disassembled and read.
/// </para>
/// </remarks>
public static class ThunkCompiler
{
    /// <summary>
    /// Compiles <paramref name="call"/> into a thunk the resident stub can <c>call</c>.
    /// </summary>
    /// <param name="call">The call to perform.</param>
    /// <param name="dataAddress">Base of the data block, where results are stored.</param>
    /// <param name="thunkAddress">Address the thunk will live at.</param>
    /// <exception cref="ArgumentException">The call cannot be expressed as a thunk.</exception>
    public static byte[] Compile(RemoteCall call, nint dataAddress, nint thunkAddress)
    {
        ArgumentNullException.ThrowIfNull(call);

        var assembler = new X86Assembler(thunkAddress);

        if (call.Prelude is { } prelude)
        {
            if (prelude.Prelude is not null)
            {
                throw new ArgumentException("Only one level of prelude is supported.", nameof(call));
            }

            EmitCall(assembler, prelude, dataAddress, storeResults: false);
            // The prelude's return value becomes this call's `this`.
            assembler.MoveEcxFromEax();
        }

        EmitCall(assembler, call, dataAddress, storeResults: true, thisAlreadyInEcx: call.Prelude is not null);
        assembler.Return();

        byte[] code = assembler.ToArray();

        if (code.Length > CommandBlock.ThunkCapacity)
        {
            throw new ArgumentException(
                $"The compiled thunk is {code.Length} bytes, more than the {CommandBlock.ThunkCapacity} " +
                "available. Reduce the argument count rather than enlarging the code page.",
                nameof(call));
        }

        return code;
    }

    private static void EmitCall(
        X86Assembler assembler,
        RemoteCall call,
        nint dataAddress,
        bool storeResults,
        bool thisAlreadyInEcx = false)
    {
        if (call.Arguments.Count > 16)
        {
            throw new ArgumentException(
                "Refusing to build a call with more than 16 stack arguments.", nameof(call));
        }

        if (call.Convention == CallingConvention.ThisCall && !thisAlreadyInEcx)
        {
            if (call.ThisPointer == 0)
            {
                throw new ArgumentException(
                    "A thiscall needs either a ThisPointer or a Prelude that produces one.", nameof(call));
            }

            assembler.MoveEcxImmediate(call.ThisPointer);
        }

        // Right to left: the leftmost argument must end up at the lowest address.
        for (int i = call.Arguments.Count - 1; i >= 0; i--)
        {
            assembler.PushImmediate(call.Arguments[i]);
        }

        assembler.CallAbsolute(call.FunctionAddress);

        int cleanup = call.CallerCleanupBytes;
        if (cleanup > byte.MaxValue)
        {
            throw new ArgumentException("Stack cleanup does not fit a short encoding.", nameof(call));
        }

        assembler.AddToStackPointer((byte)cleanup);

        if (!storeResults)
        {
            return;
        }

        // Order matters. The integer stores clobber nothing the float store needs, but
        // reading edx after anything else has run would be wrong, so both happen immediately.
        if (call.Returns.HasFlag(ReturnKind.Int32) || call.Returns.HasFlag(ReturnKind.Int64))
        {
            assembler.StoreEax(dataAddress + CommandBlock.ReturnLowOffset);
        }

        if (call.Returns.HasFlag(ReturnKind.Int64))
        {
            assembler.StoreEdx(dataAddress + CommandBlock.ReturnHighOffset);
        }

        if (call.Returns.HasFlag(ReturnKind.Float))
        {
            assembler.StoreFloatFromFpuStack(dataAddress + CommandBlock.ReturnFloatOffset);
        }
    }
}

/// <summary>
/// Builds the resident stub that the client's render loop runs on every frame.
/// </summary>
/// <remarks>
/// <para>
/// The stub is the only code that stays installed. It has to be cheap, because it runs
/// several dozen times a second whether or not the bot wants anything, and it has to be
/// transparent, because the function it is standing in front of belongs to Direct3D.
/// </para>
/// <para>
/// Transparency is why it saves and restores every register and the flags, and why it hands
/// control to the real function with an indirect jump rather than a call: the original's own
/// <c>ret</c> then returns straight to Direct3D's caller, with the stack exactly as it was.
/// </para>
/// </remarks>
public static class StubCompiler
{
    /// <summary>
    /// Compiles the resident stub.
    /// </summary>
    /// <param name="dataAddress">Base of the data block.</param>
    /// <param name="stubAddress">Address the stub will live at.</param>
    /// <param name="thunkAddress">Address of the per-call thunk.</param>
    public static byte[] Compile(nint dataAddress, nint stubAddress, nint thunkAddress)
    {
        var assembler = new X86Assembler(stubAddress);

        assembler
            .PushAllRegisters()
            .PushFlags()

            // Nothing to do unless the bot has left a request.
            .CompareMemoryWithImmediate(dataAddress + CommandBlock.StateOffset, (byte)CommandState.Requested)
            .JumpIfNotEqual("done")

            // Claim the request before running it, so a bot that gives up waiting and a game
            // thread that is mid-call cannot both decide they own the block.
            .StoreImmediate(dataAddress + CommandBlock.StateOffset, (uint)CommandState.Running)
            .CallRelative(thunkAddress)

            // Sequence first, then state: a reader that sees Complete is then guaranteed to
            // see the matching sequence number, not the previous one.
            .IncrementMemory(dataAddress + CommandBlock.SequenceOffset)
            .StoreImmediate(dataAddress + CommandBlock.StateOffset, (uint)CommandState.Complete)

            .MarkLabel("done")
            .PopFlags()
            .PopAllRegisters()
            .JumpIndirect(dataAddress + CommandBlock.OriginalFunctionOffset);

        byte[] code = assembler.ToArray();

        if (code.Length > CommandBlock.ThunkOffset)
        {
            throw new InvalidOperationException(
                $"The stub grew to {code.Length} bytes and would overlap the thunk area at " +
                $"0x{CommandBlock.ThunkOffset:X}.");
        }

        return code;
    }
}
