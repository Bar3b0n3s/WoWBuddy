using WoWBuddy.Core.Execution;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Offsets;
using WoWBuddy.Core.Tests.Fakes;
using Xunit;

namespace WoWBuddy.Core.Tests;

/// <summary>Module table with whatever ranges a test wants.</summary>
internal sealed class FakeModuleResolver : IModuleResolver
{
    private readonly List<(nint Start, nint End, string Name)> _modules = [];

    public FakeModuleResolver Add(string name, nint start, int size)
    {
        _modules.Add((start, start + size, name));
        return this;
    }

    public string? ModuleContaining(nint address) =>
        _modules.FirstOrDefault(m => address >= m.Start && address < m.End).Name;
}

/// <summary>
/// Covers the one write that reaches outside the bot's own allocations.
/// </summary>
/// <remarks>
/// Patching a vtable entry inside d3d9.dll is the single most consequential thing the bot
/// does to the client. These tests are about the guards around it: that it refuses to install
/// against anything that is not Direct3D, that it always restores what it found, and that it
/// notices when the slot no longer holds its stub.
/// </remarks>
public sealed class EndSceneHookTests
{
    private const nint D3DBase = 0x6D000000;
    private const nint EndSceneAddress = D3DBase + 0x12340;
    private const nint StubAddress = 0x02100000;

    private static (SimulatedClient Client, nint SlotAddress) BuildClientWithDevice(nint endScene = EndSceneAddress)
    {
        var client = new SimulatedClient();

        nint deviceOuter = client.Allocate(0x8000);
        nint device = client.Allocate(0x100);
        nint vtable = client.Allocate(0x200);

        client.WritePointer(
            Offsets335a.Rebase(Offsets335a.Execution.D3DDevicePointer1, client.ModuleBase), deviceOuter);
        client.WritePointer(deviceOuter + (nint)Offsets335a.Execution.D3DDevicePointer2, device);
        client.WritePointer(device, vtable);

        nint slot = vtable + (nint)Offsets335a.Execution.D3DEndSceneVTableOffset;
        client.WritePointer(slot, endScene);

        return (client, slot);
    }

    private static FakeModuleResolver D3DModules() =>
        new FakeModuleResolver().Add("d3d9.dll", D3DBase, 0x100000);

    [Fact]
    public void ResolveFollowsTheDeviceChainToTheVtableSlot()
    {
        (SimulatedClient client, nint slot) = BuildClientWithDevice();
        var hook = new EndSceneHook(client, D3DModules());

        Assert.True(hook.TryResolve(out string failure), failure);
        Assert.Equal(slot, hook.VTableSlotAddress);
        Assert.Equal(EndSceneAddress, hook.OriginalEndScene);
    }

    [Fact]
    public void ResolveRefusesWhenTheTargetIsNotInDirect3D()
    {
        // The decisive guard. A drifted pointer chain lands on some other readable address,
        // and redirecting that would corrupt the client in a way nobody could diagnose.
        (SimulatedClient client, _) = BuildClientWithDevice(endScene: 0x00401000);
        var hook = new EndSceneHook(client, D3DModules().Add("Wow.exe", 0x00400000, 0x800000));

        Assert.False(hook.TryResolve(out string failure));
        Assert.Contains("Wow.exe", failure, StringComparison.Ordinal);
        Assert.Contains("refusing to hook", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveRefusesWhenTheTargetIsInNoModuleAtAll()
    {
        (SimulatedClient client, _) = BuildClientWithDevice(endScene: 0x0BAD0000);
        var hook = new EndSceneHook(client, D3DModules());

        Assert.False(hook.TryResolve(out string failure));
        Assert.Contains("no loaded module", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveFailsCleanlyWhenTheDeviceIsNotReadyYet()
    {
        // Normal during startup: the client exists but Direct3D has not been set up.
        var client = new SimulatedClient();
        client.WritePointer(
            Offsets335a.Rebase(Offsets335a.Execution.D3DDevicePointer1, client.ModuleBase), 0);

        var hook = new EndSceneHook(client, D3DModules());

        Assert.False(hook.TryResolve(out string failure));
        Assert.Contains("null", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallPointsTheSlotAtTheStubAndKeepsTheOriginal()
    {
        (SimulatedClient client, nint slot) = BuildClientWithDevice();
        var hook = new EndSceneHook(client, D3DModules());
        Assert.True(hook.TryResolve(out _));

        Assert.True(hook.Install(StubAddress));

        Assert.Equal(StubAddress, client.ReadPointerOrZero(slot));
        Assert.Equal(EndSceneAddress, hook.OriginalEndScene);
        Assert.True(hook.IsInstalled);
    }

    [Fact]
    public void InstallChangesPageProtectionBecauseTheVtableIsReadOnly()
    {
        (SimulatedClient client, nint slot) = BuildClientWithDevice();
        var hook = new EndSceneHook(client, D3DModules());
        hook.TryResolve(out _);

        hook.Install(StubAddress);

        Assert.Contains(client.ProtectionChanges, change => change.Address == slot);
    }

    [Fact]
    public void UninstallPutsTheOriginalPointerBack()
    {
        (SimulatedClient client, nint slot) = BuildClientWithDevice();
        var hook = new EndSceneHook(client, D3DModules());
        hook.TryResolve(out _);
        hook.Install(StubAddress);

        Assert.True(hook.Uninstall());

        Assert.Equal(EndSceneAddress, client.ReadPointerOrZero(slot));
        Assert.False(hook.IsInstalled);
    }

    [Fact]
    public void UninstallRestoresEvenWhenSomethingElseDisplacedTheHook()
    {
        // Another program hooked EndScene after us. Our stub is about to be freed either way,
        // so the real function has to go back into the slot regardless.
        (SimulatedClient client, nint slot) = BuildClientWithDevice();
        var hook = new EndSceneHook(client, D3DModules());
        hook.TryResolve(out _);
        hook.Install(StubAddress);

        client.WritePointer(slot, 0x07000000);
        Assert.False(hook.IsInstalled);

        Assert.True(hook.Uninstall());
        Assert.Equal(EndSceneAddress, client.ReadPointerOrZero(slot));
    }

    [Fact]
    public void IsInstalledReadsTheSlotRatherThanTrustingAFlag()
    {
        (SimulatedClient client, nint slot) = BuildClientWithDevice();
        var hook = new EndSceneHook(client, D3DModules());
        hook.TryResolve(out _);
        hook.Install(StubAddress);

        Assert.True(hook.IsInstalled);

        client.WritePointer(slot, EndSceneAddress);

        Assert.False(hook.IsInstalled);
    }

    [Fact]
    public void InstallingWithoutResolvingIsRejected()
    {
        (SimulatedClient client, _) = BuildClientWithDevice();
        var hook = new EndSceneHook(client, D3DModules());

        Assert.Throws<InvalidOperationException>(() => hook.Install(StubAddress));
    }

    [Fact]
    public void UninstallIsSafeBeforeAnythingWasInstalled()
    {
        (SimulatedClient client, _) = BuildClientWithDevice();
        var hook = new EndSceneHook(client, D3DModules());

        Assert.True(hook.Uninstall());
    }
}
