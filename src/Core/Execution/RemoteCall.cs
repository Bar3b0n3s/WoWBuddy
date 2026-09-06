namespace WoWBuddy.Core.Execution;

/// <summary>How the called function expects its arguments and who cleans up the stack.</summary>
public enum CallingConvention
{
    /// <summary>Arguments pushed right to left; the caller cleans up.</summary>
    Cdecl,

    /// <summary>Arguments pushed right to left; the callee cleans up.</summary>
    StdCall,

    /// <summary>
    /// Like <see cref="StdCall"/>, but with the <c>this</c> pointer in ecx rather than on the
    /// stack. The client's C++ member functions use this.
    /// </summary>
    ThisCall,
}

/// <summary>What kind of value the called function returns.</summary>
[Flags]
public enum ReturnKind
{
    /// <summary>Nothing worth reading back.</summary>
    None = 0,

    /// <summary>A 32-bit value in eax.</summary>
    Int32 = 1,

    /// <summary>A 64-bit value in edx:eax.</summary>
    Int64 = 2,

    /// <summary>
    /// A float left on the x87 stack. Only set this when the function really does return
    /// one: popping a value that was never pushed corrupts the client's FPU state.
    /// </summary>
    Float = 4,
}

/// <summary>
/// One call for the game thread to make.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a description rather than a delegate. The bot never runs this code itself;
/// it is compiled to machine code, written into the client, and executed there. Keeping the
/// call as plain data means the thing that gets injected can be inspected and tested as data
/// too.
/// </para>
/// <para>
/// <paramref name="ThisPointer"/> applies only to <see cref="CallingConvention.ThisCall"/>.
/// Where it is 0 and the convention is thiscall, ecx is taken from eax instead, which is how
/// a member function is chained onto the call that produced its object.
/// </para>
/// </remarks>
/// <param name="FunctionAddress">Address of the function in the client.</param>
/// <param name="Arguments">Stack arguments, in source order (leftmost first).</param>
/// <param name="Convention">Calling convention.</param>
/// <param name="Returns">Which result registers to save.</param>
/// <param name="ThisPointer">The <c>this</c> pointer for a thiscall, if known up front.</param>
/// <param name="Prelude">
/// An optional call to make first, whose result becomes the <c>this</c> pointer for this one.
/// </param>
public sealed record RemoteCall(
    nint FunctionAddress,
    IReadOnlyList<uint> Arguments,
    CallingConvention Convention = CallingConvention.Cdecl,
    ReturnKind Returns = ReturnKind.Int32,
    uint ThisPointer = 0,
    RemoteCall? Prelude = null)
{
    /// <summary>A call with no arguments.</summary>
    public static RemoteCall To(
        nint functionAddress,
        CallingConvention convention = CallingConvention.Cdecl,
        ReturnKind returns = ReturnKind.Int32) =>
        new(functionAddress, [], convention, returns);

    /// <summary>A cdecl call with the given arguments.</summary>
    public static RemoteCall Cdecl(nint functionAddress, params uint[] arguments) =>
        new(functionAddress, arguments);

    /// <summary>
    /// Bytes of stack the caller must reclaim after the call, which is zero unless the
    /// convention makes cleanup the caller's job.
    /// </summary>
    public int CallerCleanupBytes =>
        Convention == CallingConvention.Cdecl ? Arguments.Count * 4 : 0;
}
