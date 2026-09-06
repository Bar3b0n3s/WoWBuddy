using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Execution;

/// <summary>
/// Calls into the client's own C functions, on the client's own thread.
/// </summary>
/// <remarks>
/// <para>
/// This is how the bot acts on the world. The Lua bindings for these operations are
/// protected and refuse untrusted callers; the C functions underneath them have no such
/// notion, because the protection is a property of the script sandbox rather than of the
/// game logic. Calling them from the render loop is therefore not a trick played on the
/// protection so much as a path that never meets it.
/// </para>
/// <para>
/// Each function here is single-sourced and each has a self-test that proves it by its
/// effect on the game rather than by its return value.
/// </para>
/// </remarks>
public sealed class NativeFunctions
{
    private readonly GameThreadExecutor _executor;
    private readonly IMemoryReader _memory;
    private readonly ObjectManager _objects;

    public NativeFunctions(GameThreadExecutor executor, IMemoryReader memory, ObjectManager objects)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    }

    /// <summary>
    /// Sets the player's current target.
    /// </summary>
    /// <remarks>
    /// The native equivalent of the protected Lua <c>TargetUnit</c>. Pass
    /// <see cref="WoWGuid.Zero"/> to clear the target.
    /// </remarks>
    public bool Target(WoWGuid guid)
    {
        var call = new RemoteCall(
            Offsets335a.Rebase(Offsets335a.Execution.GameUiTarget, _memory.ModuleBase),
            [(uint)(guid.Value & 0xFFFFFFFF), (uint)(guid.Value >> 32)],
            CallingConvention.Cdecl,
            ReturnKind.None);

        ExecutionResult result = _executor.Execute(call);
        if (!result.Success)
        {
            Log.For<NativeFunctions>().Error("Target({Guid}) failed: {Error}", guid, result.Error);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the local player object address as the client itself reports it.
    /// </summary>
    /// <remarks>
    /// Interesting mainly as a cross-check: the address the client returns must be the same
    /// one the object-manager walk arrives at independently. When those disagree, one of the
    /// two chains is wrong.
    /// </remarks>
    public nint GetActivePlayerObject()
    {
        var call = RemoteCall.To(
            Offsets335a.Rebase(Offsets335a.Execution.GetActivePlayerObject, _memory.ModuleBase));

        ExecutionResult result = _executor.Execute(call);
        return result.Success ? result.Pointer : 0;
    }

    /// <summary>
    /// Checks that the client's own idea of the local player matches the bot's.
    /// </summary>
    /// <remarks>
    /// Safe to run at attach: it reads, changes nothing, and is not visible in game. It
    /// proves that the injected call mechanism really executes, that its return value comes
    /// back intact, and that <c>GetActivePlayerObject</c> is what its single source claims.
    /// </remarks>
    public bool VerifyActivePlayerObject(out string detail)
    {
        GameObjectRef expected = _objects.FindLocalPlayer();
        if (!expected.IsValid)
        {
            detail = "No local player object, so the client's answer cannot be checked. Log a character in.";
            return false;
        }

        nint actual = GetActivePlayerObject();
        if (actual == 0)
        {
            detail = "GetActivePlayerObject returned null while a character was in the world.";
            return false;
        }

        if (actual != expected.Address)
        {
            detail = $"The client reports the local player at 0x{actual:X8}, but walking the object " +
                     $"manager finds it at 0x{expected.Address:X8}. One of the two chains is wrong.";
            return false;
        }

        detail = $"The client and the object manager agree the local player is at 0x{actual:X8}.";
        return true;
    }

    /// <summary>
    /// Proves <see cref="Target"/> works, by using it and watching the result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This one is visible in game</b>: it changes the player's target and puts it back.
    /// It is therefore not part of the attach sequence and has to be asked for. The check is
    /// the only honest one available — a targeting function that returns nothing can only be
    /// verified by whether the target actually changed.
    /// </para>
    /// <para>
    /// The previous target is restored even when the check fails, so running it never leaves
    /// the character targeting something the user did not choose.
    /// </para>
    /// </remarks>
    public bool SelfTestTargeting(out string detail)
    {
        GameObjectRef player = _objects.FindLocalPlayer();
        if (!player.IsValid)
        {
            detail = "No local player object. Log a character in.";
            return false;
        }

        WoWGuid original = player.Descriptors.ReadGuid(UpdateFields335a.Unit.Target);

        // Targeting yourself is always legal, needs nothing nearby, and is trivially undone.
        if (!Target(player.Guid))
        {
            detail = "The Target call did not execute.";
            return false;
        }

        WoWGuid observed = _objects.FindLocalPlayer().Descriptors.ReadGuid(UpdateFields335a.Unit.Target);
        bool changed = observed == player.Guid;

        Target(original);

        detail = changed
            ? "Targeting confirmed: the player's target descriptor changed to the requested GUID and was restored."
            : $"The target descriptor read {observed} after asking for {player.Guid}. " +
              "Offsets335a.Execution.GameUiTarget is wrong for this client.";

        return changed;
    }
}
