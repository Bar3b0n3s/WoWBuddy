using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Execution;

/// <summary>
/// Redirects the client's Direct3D EndScene call through the bot's stub.
/// </summary>
/// <remarks>
/// <para>
/// The bot needs somewhere to run code on the game's own thread, and the render loop is the
/// one place guaranteed to be reached every frame while the game is alive. EndScene is the
/// conventional choice: it is called once per frame, late enough that the frame's work is
/// done, and it belongs to Direct3D rather than to the game, so standing in front of it
/// disturbs nothing the game itself is doing.
/// </para>
/// <para>
/// <b>The vtable entry is patched, not the function.</b> The alternative, overwriting the
/// first instructions of EndScene with a jump, needs a length-disassembler to avoid splitting
/// an instruction, leaves the client permanently modified while installed, and is awkward to
/// undo safely. Swapping one pointer in the device's vtable needs neither, and undoing it is
/// a single write of the value that was there before.
/// </para>
/// <para>
/// The vtable belongs to d3d9.dll and is shared by every device in the process. WoW creates
/// one, and a device reset reuses the same static table, so the hook survives alt-tabbing and
/// resolution changes. <see cref="IsInstalled"/> re-reads the slot rather than trusting a
/// flag, so a hook that was displaced by something else is still detected.
/// </para>
/// </remarks>
public sealed class EndSceneHook
{
    private readonly IProcessMemory _memory;
    private readonly IModuleResolver _modules;

    /// <summary>Module the EndScene pointer is expected to live in.</summary>
    private const string ExpectedModule = "d3d9.dll";

    public EndSceneHook(IProcessMemory memory, IModuleResolver modules)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _modules = modules ?? throw new ArgumentNullException(nameof(modules));
    }

    /// <summary>Address of the vtable slot holding the EndScene pointer, once resolved.</summary>
    public nint VTableSlotAddress { get; private set; }

    /// <summary>The original EndScene address, captured before the slot was overwritten.</summary>
    public nint OriginalEndScene { get; private set; }

    /// <summary>Address the slot currently points at while hooked.</summary>
    public nint InstalledStub { get; private set; }

    /// <summary>True when the vtable slot still points at our stub.</summary>
    public bool IsInstalled =>
        InstalledStub != 0
        && VTableSlotAddress != 0
        && _memory.ReadPointerOrZero(VTableSlotAddress) == InstalledStub;

    /// <summary>
    /// Follows the device pointer chain to the vtable slot that holds EndScene.
    /// </summary>
    /// <param name="failure">Why resolution failed, when it did.</param>
    /// <returns>True when the slot was found and looks like Direct3D.</returns>
    public bool TryResolve(out string failure)
    {
        VTableSlotAddress = 0;
        OriginalEndScene = 0;

        nint devicePointerAddress = Offsets335a.Rebase(
            Offsets335a.Execution.D3DDevicePointer1, _memory.ModuleBase);

        nint deviceOuter = _memory.ReadPointerOrZero(devicePointerAddress);
        if (deviceOuter == 0)
        {
            failure = $"The Direct3D device pointer at 0x{devicePointerAddress:X8} was null. " +
                      "The client may still be starting up, or D3DDevicePointer1 is wrong.";
            return false;
        }

        nint device = _memory.ReadPointerOrZero(deviceOuter + (nint)Offsets335a.Execution.D3DDevicePointer2);
        if (device == 0)
        {
            failure = $"No device at 0x{deviceOuter:X8} + 0x{Offsets335a.Execution.D3DDevicePointer2:X}. " +
                      "D3DDevicePointer2 is wrong for this client.";
            return false;
        }

        // A COM object's first field is its vtable pointer.
        nint vtable = _memory.ReadPointerOrZero(device);
        if (vtable == 0)
        {
            failure = $"The device at 0x{device:X8} had no vtable pointer.";
            return false;
        }

        nint slot = vtable + (nint)Offsets335a.Execution.D3DEndSceneVTableOffset;
        nint endScene = _memory.ReadPointerOrZero(slot);
        if (endScene == 0)
        {
            failure = $"Vtable slot 0x{slot:X8} was empty.";
            return false;
        }

        // The decisive check. If the chain drifted, this is where it shows up.
        string? module = _modules.ModuleContaining(endScene);
        if (!string.Equals(module, ExpectedModule, StringComparison.OrdinalIgnoreCase))
        {
            failure = $"The resolved EndScene at 0x{endScene:X8} is in " +
                      $"{module ?? "no loaded module"}, not {ExpectedModule}. " +
                      "The Direct3D pointer chain does not match this client; refusing to hook it.";
            return false;
        }

        VTableSlotAddress = slot;
        OriginalEndScene = endScene;
        failure = string.Empty;

        Log.For<EndSceneHook>().Information(
            "Resolved EndScene to 0x{EndScene:X8} in {Module} via vtable slot 0x{Slot:X8}",
            endScene, module, slot);

        return true;
    }

    /// <summary>
    /// Points the vtable slot at <paramref name="stubAddress"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="TryResolve"/> must have succeeded first; installing without it would mean
    /// writing to an address that was never checked.
    /// </remarks>
    public bool Install(nint stubAddress)
    {
        if (VTableSlotAddress == 0 || OriginalEndScene == 0)
        {
            throw new InvalidOperationException("Resolve the vtable slot before installing the hook.");
        }

        if (stubAddress == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stubAddress), "The stub address must be non-zero.");
        }

        bool written = false;

        // The vtable lives on a read-only page inside d3d9.dll.
        bool protectionChanged = _memory.WithWritableMemory(VTableSlotAddress, sizeof(uint), () =>
        {
            Span<byte> buffer = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)stubAddress);
            written = _memory.TryWriteBytes(VTableSlotAddress, buffer);
        });

        if (!protectionChanged || !written)
        {
            Log.For<EndSceneHook>().Error(
                "Could not write the vtable slot at 0x{Slot:X8}", VTableSlotAddress);
            return false;
        }

        InstalledStub = stubAddress;
        Log.For<EndSceneHook>().Information(
            "Hook installed: vtable slot 0x{Slot:X8} now points at 0x{Stub:X8} (was 0x{Original:X8})",
            VTableSlotAddress, stubAddress, OriginalEndScene);

        return true;
    }

    /// <summary>
    /// Puts the original EndScene pointer back.
    /// </summary>
    /// <remarks>
    /// Restores unconditionally rather than only when <see cref="IsInstalled"/> is true. If
    /// something else displaced our stub we still want the real function back in the slot,
    /// and if the slot already holds it the write is harmless.
    /// </remarks>
    public bool Uninstall()
    {
        if (VTableSlotAddress == 0 || OriginalEndScene == 0)
        {
            return true;
        }

        bool written = false;
        bool protectionChanged = _memory.WithWritableMemory(VTableSlotAddress, sizeof(uint), () =>
        {
            Span<byte> buffer = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)OriginalEndScene);
            written = _memory.TryWriteBytes(VTableSlotAddress, buffer);
        });

        if (!protectionChanged || !written)
        {
            // Worth shouting about: the client is now calling into memory the bot is about to
            // free, and there is nothing further this process can do to fix it.
            Log.For<EndSceneHook>().Fatal(
                "Could not restore the EndScene vtable slot at 0x{Slot:X8}. The client is still " +
                "pointing at bot memory and should be closed before this process exits.",
                VTableSlotAddress);
            return false;
        }

        InstalledStub = 0;
        Log.For<EndSceneHook>().Information("Hook removed; EndScene restored to 0x{Original:X8}", OriginalEndScene);
        return true;
    }
}
