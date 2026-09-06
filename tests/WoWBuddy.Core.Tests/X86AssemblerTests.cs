using WoWBuddy.Core.Execution;
using Xunit;

namespace WoWBuddy.Core.Tests;

/// <summary>
/// Golden tests for the machine code the bot injects into the game client.
/// </summary>
/// <remarks>
/// <para>
/// The expected byte sequences were not copied out of the implementation. Each was produced
/// by emitting the instruction, writing the bytes to a file, and disassembling them with
/// <c>objdump -D -b binary -m i386 -M intel --adjust-vma=0x1000</c>. The disassembly is
/// quoted next to each case, so a future change that alters the encoding has to explain
/// itself against a real disassembler rather than against a hand-written guess.
/// </para>
/// <para>
/// This matters more than a normal golden test would. A wrong byte here does not throw or
/// return a bad value; it executes on the game's own thread, in the game's own process.
/// </para>
/// </remarks>
public sealed class X86AssemblerTests
{
    private const nint Base = 0x1000;

    private static byte[] Emit(Action<X86Assembler> build)
    {
        var assembler = new X86Assembler(Base);
        build(assembler);
        return assembler.ToArray();
    }

    private static void AssertBytes(string expectedHex, byte[] actual) =>
        Assert.Equal(expectedHex, Convert.ToHexString(actual));

    [Fact] // pusha
    public void PushAllRegisters() => AssertBytes("60", Emit(a => a.PushAllRegisters()));

    [Fact] // popa
    public void PopAllRegisters() => AssertBytes("61", Emit(a => a.PopAllRegisters()));

    [Fact] // pushf
    public void PushFlags() => AssertBytes("9C", Emit(a => a.PushFlags()));

    [Fact] // popf
    public void PopFlags() => AssertBytes("9D", Emit(a => a.PopFlags()));

    [Fact] // ret
    public void Return() => AssertBytes("C3", Emit(a => a.Return()));

    [Fact] // push 0xdeadbeef
    public void PushImmediate() => AssertBytes("68EFBEADDE", Emit(a => a.PushImmediate(0xDEADBEEF)));

    [Fact] // push DWORD PTR ds:0xca11d8
    public void PushFromMemory() => AssertBytes("FF35D811CA00", Emit(a => a.PushFromMemory(0x00CA11D8)));

    [Fact] // mov eax,0x819210
    public void MoveEaxImmediate() => AssertBytes("B810928100", Emit(a => a.MoveEaxImmediate(0x00819210)));

    [Fact] // mov ecx,0x11223344
    public void MoveEcxImmediate() => AssertBytes("B944332211", Emit(a => a.MoveEcxImmediate(0x11223344)));

    [Fact] // mov ecx,eax
    public void MoveEcxFromEax() => AssertBytes("89C1", Emit(a => a.MoveEcxFromEax()));

    [Fact] // mov DWORD PTR ds:0x500000,0x3
    public void StoreImmediate() =>
        AssertBytes("C7050000500003000000", Emit(a => a.StoreImmediate(0x00500000, 3)));

    [Fact] // mov ds:0x500008,eax
    public void StoreEax() => AssertBytes("A308005000", Emit(a => a.StoreEax(0x00500008)));

    [Fact] // mov DWORD PTR ds:0x50000c,edx
    public void StoreEdx() => AssertBytes("89150C005000", Emit(a => a.StoreEdx(0x0050000C)));

    [Fact] // fstp DWORD PTR ds:0x500010
    public void StoreFloatFromFpuStack() =>
        AssertBytes("D91D10005000", Emit(a => a.StoreFloatFromFpuStack(0x00500010)));

    [Fact] // inc DWORD PTR ds:0x500004
    public void IncrementMemory() => AssertBytes("FF0504005000", Emit(a => a.IncrementMemory(0x00500004)));

    [Fact] // cmp DWORD PTR ds:0x500000,0x1
    public void CompareMemoryWithImmediate() =>
        AssertBytes("833D0000500001", Emit(a => a.CompareMemoryWithImmediate(0x00500000, 1)));

    [Fact] // mov eax,0x819210 ; call eax
    public void CallAbsolute() => AssertBytes("B810928100FFD0", Emit(a => a.CallAbsolute(0x00819210)));

    [Fact] // call 0x2000, emitted at base 0x1000: 0x1005 + 0x0FFB == 0x2000
    public void CallRelativeIsMeasuredFromTheFollowingInstruction() =>
        AssertBytes("E8FB0F0000", Emit(a => a.CallRelative(0x2000)));

    [Fact] // jmp DWORD PTR ds:0x500014
    public void JumpIndirect() => AssertBytes("FF2514005000", Emit(a => a.JumpIndirect(0x00500014)));

    [Fact] // add esp,0xc
    public void AddToStackPointer() => AssertBytes("83C40C", Emit(a => a.AddToStackPointer(0x0C)));

    [Fact]
    public void AddToStackPointerEmitsNothingForZero() =>
        AssertBytes(string.Empty, Emit(a => a.AddToStackPointer(0)));

    [Fact] // jne 0x1003 ; pusha ; ret  -- the branch skips the one-byte pusha
    public void ForwardShortJumpIsPatchedToTheLabel() =>
        AssertBytes("750160C3", Emit(a =>
            a.JumpIfNotEqual("skip").PushAllRegisters().MarkLabel("skip").Return()));

    [Fact]
    public void CurrentAddressTracksEmittedLength()
    {
        var assembler = new X86Assembler(Base);
        Assert.Equal(Base, assembler.CurrentAddress);

        assembler.PushImmediate(0);

        Assert.Equal(Base + 5, assembler.CurrentAddress);
        Assert.Equal(5, assembler.Length);
    }

    [Fact]
    public void UndefinedLabelIsRejected()
    {
        var assembler = new X86Assembler(Base);
        assembler.JumpIfNotEqual("nowhere");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => assembler.ToArray());
        Assert.Contains("nowhere", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BranchTooFarForItsEncodingIsRejected()
    {
        // Better to fail loudly than to silently emit a jump to the wrong place inside
        // somebody else's process.
        var assembler = new X86Assembler(Base);
        assembler.JumpIfNotEqual("far");
        for (int i = 0; i < 200; i++)
        {
            assembler.PushAllRegisters();
        }

        assembler.MarkLabel("far");

        Assert.Throws<InvalidOperationException>(() => assembler.ToArray());
    }
}
