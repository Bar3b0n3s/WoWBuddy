using WoWBuddy.Common.Geometry;
using WoWBuddy.Core.Client;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Attach;

/// <summary>
/// Checks the offset table against a live client before the bot is allowed to use it.
/// </summary>
/// <remarks>
/// <para>
/// The checks are ordered so that each one only runs when its prerequisites held, and each
/// is chosen because a wrong offset makes it fail. Confirming that a pointer is non-null
/// proves very little; confirming that the object manager contains exactly one Player object
/// whose GUID matches the one stored in the manager header, and whose descriptor array
/// independently reports the same GUID, proves that the whole chain is right.
/// </para>
/// </remarks>
public static class OffsetVerifier
{
    /// <summary>Highest level a 3.3.5a unit can be, allowing for the boss padding above 80.</summary>
    private const int MaxPlausibleLevel = 83;

    /// <summary>
    /// Runs every check against <paramref name="reader"/>.
    /// </summary>
    /// <param name="reader">Reader for the attached client.</param>
    /// <param name="build">What was determined about the client's version.</param>
    /// <param name="resolution">Receives the resolved unit position layout.</param>
    public static VerificationReport Verify(IMemoryReader reader, ClientBuild build, out PositionResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var report = new VerificationReport();
        resolution = default;

        if (!VerifyBuild(report, build))
        {
            return report;
        }

        var objectManager = new ObjectManager(reader);

        if (!VerifyPointerChain(report, reader, objectManager, out nint manager))
        {
            return report;
        }

        List<GameObjectRef> objects = VerifyObjectWalk(report, objectManager);
        if (objects.Count == 0)
        {
            return report;
        }

        GameObjectRef localPlayer = VerifyLocalPlayer(report, objectManager, manager, objects);
        if (!localPlayer.IsValid)
        {
            return report;
        }

        VerifyDescriptors(report, localPlayer);
        VerifyPositions(report, reader, objectManager, out resolution);

        return report;
    }

    private static bool VerifyBuild(VerificationReport report, ClientBuild build)
    {
        if (!build.Is32Bit)
        {
            report.Fail("Client bitness",
                "The selected process is 64-bit. WoW 3.3.5a is a 32-bit client, so this is not a 12340 client.");
            return false;
        }

        if (build.IsUnknown)
        {
            // Repacked private-server clients sometimes have their version resource stripped.
            // That is suspicious but not proof of the wrong build, and the checks that follow
            // will catch a genuinely wrong client far more reliably than a version string would.
            report.Warn("Client build",
                $"Could not determine the client build ({build.Source}). Continuing, but every offset check " +
                "below is now the only thing standing between the bot and a mismatched client.");
            return true;
        }

        if (build.Build != Offsets335a.SupportedBuild)
        {
            report.Fail("Client build",
                $"Client reports build {build.Build}, but this offset table only describes " +
                $"{Offsets335a.SupportedVersion} build {Offsets335a.SupportedBuild}. Every address would be wrong.");
            return false;
        }

        report.Pass("Client build", $"{build.Version} build {build.Build}, 32-bit ({build.Source}).");
        return true;
    }

    private static bool VerifyPointerChain(
        VerificationReport report,
        IMemoryReader reader,
        ObjectManager objectManager,
        out nint manager)
    {
        manager = 0;

        nint connection = reader.ReadPointerOrZero(objectManager.ClientConnectionAddress);
        if (connection == 0)
        {
            report.Fail("Client connection",
                $"Read a null pointer from 0x{objectManager.ClientConnectionAddress:X8}. " +
                "Either the client is at the login screen, or Offsets335a.ObjectManager.ClientConnection is wrong.");
            return false;
        }

        report.Pass("Client connection",
            $"0x{objectManager.ClientConnectionAddress:X8} -> 0x{connection:X8}.");

        manager = objectManager.ResolveManager();
        if (manager == 0)
        {
            report.Fail("Object manager",
                $"Client connection + 0x{Offsets335a.ObjectManager.CurMgr:X} was null. " +
                "Log a character all the way into the world (not the character-select screen) and re-attach.");
            return false;
        }

        report.Pass("Object manager", $"Resolved to 0x{manager:X8}.");
        return true;
    }

    private static List<GameObjectRef> VerifyObjectWalk(VerificationReport report, ObjectManager objectManager)
    {
        List<GameObjectRef> objects = objectManager.EnumerateObjects().ToList();

        if (objects.Count == 0)
        {
            report.Fail("Object list walk",
                "The object manager's list was empty. A character in the world always has at least its own " +
                "player object, so either FirstObject or NextObject is wrong.");
            return objects;
        }

        if (objects.Count >= ObjectManager.MaxObjects)
        {
            report.Fail("Object list walk",
                $"The walk hit its {ObjectManager.MaxObjects}-object cap, which means the list did not terminate. " +
                "NextObject is almost certainly wrong.");
            return [];
        }

        string breakdown = string.Join(", ",
            objects.GroupBy(o => o.Type).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"));

        report.Pass("Object list walk", $"{objects.Count} objects, list terminated cleanly. {breakdown}.");
        return objects;
    }

    private static GameObjectRef VerifyLocalPlayer(
        VerificationReport report,
        ObjectManager objectManager,
        nint manager,
        List<GameObjectRef> objects)
    {
        WoWGuid guid = objectManager.GetLocalPlayerGuid();
        if (guid.IsZero)
        {
            report.Fail("Local player GUID",
                $"Object manager + 0x{Offsets335a.ObjectManager.LocalPlayerGuid:X} held a zero GUID at 0x{manager:X8}.");
            return default;
        }

        if (!guid.IsPlayer)
        {
            report.Fail("Local player GUID",
                $"The GUID read from the object manager ({guid}) is not a player GUID: its high word is " +
                $"0x{(ushort)guid.Type:X4}, and player GUIDs have a zero high word. " +
                "Offsets335a.ObjectManager.LocalPlayerGuid is pointing at something else.");
            return default;
        }

        List<GameObjectRef> matches = objects.Where(o => o.Guid == guid).ToList();
        if (matches.Count == 0)
        {
            report.Fail("Local player object",
                $"The local player GUID {guid} does not appear anywhere in the object list. " +
                "The manager header offset and the list walk disagree, so at least one of them is wrong.");
            return default;
        }

        GameObjectRef player = matches[0];
        if (player.Type != WoWObjectType.Player)
        {
            report.Fail("Local player object",
                $"The object matching the local player GUID reports type {player.Type}, not Player. " +
                "Offsets335a.Object.Type is wrong.");
            return default;
        }

        report.Pass("Local player object",
            $"GUID {guid} resolves to a Player object at 0x{player.Address:X8}.");
        return player;
    }

    private static void VerifyDescriptors(VerificationReport report, GameObjectRef localPlayer)
    {
        DescriptorTable descriptors = localPlayer.Descriptors;
        if (!descriptors.IsValid)
        {
            report.Fail("Descriptor pointer",
                $"Object + 0x{Offsets335a.Object.Descriptors:X} did not hold a readable pointer.");
            return;
        }

        // The decisive check. The object's inline GUID and the GUID in its descriptor array
        // are stored in different places by different code paths. If the descriptor pointer
        // were wrong, they could not agree.
        WoWGuid descriptorGuid = descriptors.ReadGuid(UpdateFields335a.Object.Guid);
        if (descriptorGuid != localPlayer.Guid)
        {
            report.Fail("Descriptor pointer",
                $"The descriptor array reports GUID {descriptorGuid} but the object itself reports " +
                $"{localPlayer.Guid}. Offsets335a.Object.Descriptors is wrong.");
            return;
        }

        report.Pass("Descriptor pointer",
            $"Descriptor array at 0x{descriptors.BaseAddress:X8} reports the same GUID as the object.");

        int level = descriptors.ReadInt32(UpdateFields335a.Unit.Level);
        uint health = descriptors.ReadUInt32(UpdateFields335a.Unit.Health);
        uint maxHealth = descriptors.ReadUInt32(UpdateFields335a.Unit.MaxHealth);

        if (level is < 1 or > MaxPlausibleLevel)
        {
            report.Fail("Descriptor sanity",
                $"The local player's level read as {level}, which is outside 1-{MaxPlausibleLevel}. " +
                "The unit descriptor indices do not match this client.");
            return;
        }

        if (maxHealth == 0 || health > maxHealth)
        {
            report.Fail("Descriptor sanity",
                $"The local player's health read as {health}/{maxHealth}, which is impossible. " +
                "The unit descriptor indices do not match this client.");
            return;
        }

        report.Pass("Descriptor sanity",
            $"Level {level}, health {health}/{maxHealth}. Consistent with a real character.");
    }

    private static void VerifyPositions(
        VerificationReport report,
        IMemoryReader reader,
        ObjectManager objectManager,
        out PositionResolution resolution)
    {
        resolution = PositionResolver.Resolve(reader, objectManager);

        if (!resolution.Success)
        {
            report.Fail("Unit position layout", resolution.Message);
            return;
        }

        if (!resolution.Confident)
        {
            report.Warn("Unit position layout", resolution.Message);
            return;
        }

        report.Pass("Unit position layout", resolution.Message);
    }

    /// <summary>
    /// A cheap re-check for use while the bot is running.
    /// </summary>
    /// <remarks>
    /// Full verification walks the whole object list and is far too heavy for a per-tick
    /// check. This confirms the two things that actually change when a client is closed and
    /// a different one is opened underneath the bot: the manager still resolves, and the
    /// local player is still the character the bot started on.
    /// </remarks>
    public static bool StillAttachedTo(ObjectManager objectManager, WoWGuid expectedPlayerGuid)
    {
        ArgumentNullException.ThrowIfNull(objectManager);
        return objectManager.ResolveManager() != 0
            && objectManager.GetLocalPlayerGuid() == expectedPlayerGuid;
    }
}
