using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Attach;
using WoWBuddy.Core.Client;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;
using WoWBuddy.Core.Tests.Fakes;
using Xunit;

namespace WoWBuddy.Core.Tests;

/// <summary>
/// Covers the attach gate: a correct client must pass, and each way of being wrong must be
/// caught rather than waved through.
/// </summary>
public sealed class OffsetVerifierTests
{
    private static readonly ClientBuild SupportedBuild =
        new("3.3.5", Offsets335a.SupportedBuild, Is32Bit: true, "test");

    /// <summary>Builds a client that should pass every check.</summary>
    private static SimulatedClient HealthyClient()
    {
        var builder = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithLocalPlayer(level: 80, health: 19000, maxHealth: 20000);

        // Enough nearby units to let the position resolver reach a confident conclusion.
        var origin = new Vector3(-8913.23f, 554.63f, 93.79f);
        for (int i = 0; i < 8; i++)
        {
            builder.WithCreature(
                (ulong)(i + 1),
                entry: 299,
                level: 10,
                new Vector3(origin.X + (i * 3f), origin.Y + (i * 2f), origin.Z));
        }

        return builder.Build();
    }

    [Fact]
    public void Verify_Passes_ForAConsistentClient()
    {
        SimulatedClient client = HealthyClient();

        VerificationReport report = OffsetVerifier.Verify(client, SupportedBuild, out PositionResolution resolution);

        Assert.True(report.Passed, report.ToString());
        Assert.True(resolution.Success);
        Assert.True(resolution.Confident);
    }

    [Fact]
    public void Verify_Fails_WhenTheClientIsTheWrongBuild()
    {
        SimulatedClient client = HealthyClient();
        var wrongBuild = new ClientBuild("4.3.4", 15595, Is32Bit: true, "test");

        VerificationReport report = OffsetVerifier.Verify(client, wrongBuild, out _);

        Assert.False(report.Passed);
        Assert.Contains(report.Checks, c => c.Name == "Client build" && c.Status == CheckStatus.Failed);
    }

    [Fact]
    public void Verify_Fails_WhenTheClientIs64Bit()
    {
        SimulatedClient client = HealthyClient();
        var wrongBitness = new ClientBuild("3.3.5", Offsets335a.SupportedBuild, Is32Bit: false, "test");

        VerificationReport report = OffsetVerifier.Verify(client, wrongBitness, out _);

        Assert.False(report.Passed);
        Assert.Contains(report.Checks, c => c.Name == "Client bitness" && c.Status == CheckStatus.Failed);
    }

    [Fact]
    public void Verify_Fails_WhenTheObjectManagerCannotBeResolved()
    {
        // A character at the login screen: the connection exists, the manager does not.
        SimulatedClient client = new SimulatedClientBuilder().Build();

        VerificationReport report = OffsetVerifier.Verify(client, SupportedBuild, out _);

        Assert.False(report.Passed);
        Assert.Contains(report.Checks, c => c.Name == "Client connection" && c.Status == CheckStatus.Failed);
    }

    [Fact]
    public void Verify_Fails_WhenTheLocalPlayerGuidIsNotAPlayerGuid()
    {
        var builder = new SimulatedClientBuilder().WithObjectManager().WithLocalPlayer();
        SimulatedClient client = builder.Build();

        // A creature GUID where the player GUID should be: what a wrong header offset looks like.
        client.WriteUInt64(
            builder.ManagerAddress + (nint)Offsets335a.ObjectManager.LocalPlayerGuid,
            0x1234 | ((ulong)WoWGuidType.Creature << 48));

        VerificationReport report = OffsetVerifier.Verify(client, SupportedBuild, out _);

        Assert.False(report.Passed);
        Assert.Contains(report.Checks, c => c.Name == "Local player GUID" && c.Status == CheckStatus.Failed);
    }

    [Fact]
    public void Verify_Fails_WhenTheDescriptorPointerIsWrong()
    {
        SimulatedClient client = HealthyClient();
        nint playerAddress = client.ObjectAddresses[0];
        client.WritePointer(playerAddress + (nint)Offsets335a.Object.Descriptors, client.Allocate(0x100));

        VerificationReport report = OffsetVerifier.Verify(client, SupportedBuild, out _);

        Assert.False(report.Passed);
        Assert.Contains(report.Checks, c => c.Name == "Descriptor pointer" && c.Status == CheckStatus.Failed);
    }

    [Fact]
    public void Verify_Fails_WhenDescriptorValuesAreImpossible()
    {
        var builder = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithLocalPlayer(level: 900, health: 1, maxHealth: 1);

        SimulatedClient client = builder.Build();

        VerificationReport report = OffsetVerifier.Verify(client, SupportedBuild, out _);

        Assert.False(report.Passed);
        Assert.Contains(report.Checks, c => c.Name == "Descriptor sanity" && c.Status == CheckStatus.Failed);
    }

    [Fact]
    public void Verify_Fails_WhenHealthExceedsMaximum()
    {
        SimulatedClient client = new SimulatedClientBuilder()
            .WithObjectManager()
            .WithLocalPlayer(level: 80, health: 500, maxHealth: 100)
            .Build();

        VerificationReport report = OffsetVerifier.Verify(client, SupportedBuild, out _);

        Assert.False(report.Passed);
        Assert.Contains(report.Checks, c => c.Name == "Descriptor sanity" && c.Status == CheckStatus.Failed);
    }

    [Fact]
    public void Verify_Warns_ButProceeds_WhenTheBuildCannotBeDetermined()
    {
        // Repacked private-server clients sometimes have no version resource. That is worth
        // saying out loud, but the structural checks are the ones that actually matter.
        SimulatedClient client = HealthyClient();
        var unknownBuild = new ClientBuild(string.Empty, null, Is32Bit: true, "version resource stripped");

        VerificationReport report = OffsetVerifier.Verify(client, unknownBuild, out _);

        Assert.True(report.Passed, report.ToString());
        Assert.Contains(report.Checks, c => c.Name == "Client build" && c.Status == CheckStatus.Warning);
    }

    [Fact]
    public void StillAttachedTo_IsFalse_WhenADifferentCharacterLogsIn()
    {
        var builder = new SimulatedClientBuilder().WithObjectManager().WithLocalPlayer(guidLow: 0x1111);
        SimulatedClient client = builder.Build();
        var objectManager = new ObjectManager(client);

        Assert.True(OffsetVerifier.StillAttachedTo(objectManager, builder.LocalPlayerGuid));

        client.WriteUInt64(builder.ManagerAddress + (nint)Offsets335a.ObjectManager.LocalPlayerGuid, 0x2222);

        Assert.False(OffsetVerifier.StillAttachedTo(objectManager, builder.LocalPlayerGuid));
    }
}
