using System.Buffers.Binary;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;

namespace WoWBuddy.Core.Attach;

/// <summary>One offset that might hold a game object's position, and how well it held up.</summary>
/// <param name="Offset">Byte offset from the object's base.</param>
/// <param name="Plausible">Objects whose value there looked like a world position near the player.</param>
/// <param name="Sampled">Objects examined.</param>
/// <param name="Example">One position read at this offset, for eyeballing.</param>
public readonly record struct PositionOffsetCandidate(
    uint Offset,
    int Plausible,
    int Sampled,
    Vector3 Example)
{
    /// <summary>Fraction of sampled objects that agreed.</summary>
    public double Agreement => Sampled == 0 ? 0d : (double)Plausible / Sampled;

    public override string ToString() =>
        $"0x{Offset:X3}  {Plausible}/{Sampled} ({Agreement:P0})  {Example}";
}

/// <summary>The outcome of resolving where game objects keep their position.</summary>
/// <param name="Offset">The offset to use, or null when none could be established.</param>
/// <param name="Scan">The full scan, for diagnostics.</param>
/// <param name="Detail">A sentence for the attach report.</param>
public readonly record struct GameObjectPositionResolution(
    uint? Offset,
    GameObjectPositionScan Scan,
    string Detail)
{
    /// <summary>True when game object positions are usable.</summary>
    public bool Success => Offset is not null;
}

/// <summary>What a scan found.</summary>
/// <param name="Candidates">Every offset that agreed on most objects, best first.</param>
/// <param name="ObjectsSampled">How many game objects were examined.</param>
/// <param name="Message">A sentence for the user.</param>
public sealed record GameObjectPositionScan(
    IReadOnlyList<PositionOffsetCandidate> Candidates,
    int ObjectsSampled,
    string Message)
{
    /// <summary>The offset most likely to be the position, or null when nothing convincing was found.</summary>
    public uint? BestOffset => Candidates.Count > 0 ? Candidates[0].Offset : null;

    /// <summary>True when exactly one offset stood out, which is the unambiguous case.</summary>
    public bool IsUnambiguous => Candidates.Count == 1;
}

/// <summary>
/// Works out where a game object keeps its position, by looking.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1 left game object positions unimplemented because no source consulted stated the
/// offset and guessing one would have sent a gathering bot to fabricated coordinates. Both
/// reference bots surveyed had the same gap: one removed its game object offsets, the other
/// left them commented out and colliding with the object list's own next-pointer.
/// </para>
/// <para>
/// There is no way around it. A bot that gathers has to know where a node is, and the only
/// place that fact exists is the client's own memory: no Lua call exposes it, and a server
/// database is something a person botting on somebody else's realm will never have.
/// </para>
/// <para>
/// So rather than guess, this searches, in the same spirit as the resolver that settles unit
/// positions. A game object's position has properties almost nothing else in its memory
/// shares: three consecutive finite floats, inside the world grid, and close to the player,
/// because the client only keeps objects that are nearby. Testing every aligned offset
/// against many objects at once makes the real one stand out, and something that merely
/// happens to look like a coordinate on one object does not survive being checked against
/// thirty.
/// </para>
/// <para>
/// Ambiguity is reported rather than resolved arbitrarily. Objects often hold more than one
/// copy of their position, so several offsets can pass; the bot takes the lowest, because the
/// first such block is the one the client itself reads from, and says how many others matched
/// so a wrong choice is visible rather than silent.
/// </para>
/// </remarks>
public static class GameObjectPositionResolver
{
    /// <summary>How far into an object to look.</summary>
    /// <remarks>
    /// Generous. The published unit position offsets sit near 0x800, so a game object's is
    /// unlikely to be far past that, and reading a block this size costs one call per object.
    /// </remarks>
    public const int ScanLength = 0x1000;

    /// <summary>
    /// How far from the player a game object may be and still count as corroborating.
    /// </summary>
    /// <remarks>
    /// The client's own object update distance is well under this. Being generous costs
    /// little: a wrong offset produces coordinates scattered across a 34000-yard map, so
    /// almost anything nearby is evidence.
    /// </remarks>
    public const float MaxPlausibleDistance = 500f;

    /// <summary>Fraction of sampled objects that must agree before an offset is reported.</summary>
    public const double RequiredAgreement = 0.80;

    /// <summary>Fewest game objects worth drawing a conclusion from.</summary>
    /// <remarks>
    /// Below this the odds of a coincidence are real. Standing in a city or a field of nodes
    /// gives plenty; standing in an empty room gives none, and the scan says so rather than
    /// guessing from two.
    /// </remarks>
    public const int MinimumObjects = 8;

    /// <summary>
    /// Scans game objects for an offset that behaves like a position.
    /// </summary>
    /// <param name="reader">Reader for the attached client.</param>
    /// <param name="gameObjects">Game objects to sample.</param>
    /// <param name="playerPosition">Where the character is, to judge closeness against.</param>
    public static GameObjectPositionScan Scan(
        IMemoryReader reader,
        IEnumerable<GameObjectRef> gameObjects,
        Vector3 playerPosition)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(gameObjects);

        if (!WorldBounds.IsPlausible(playerPosition))
        {
            return new GameObjectPositionScan(
                [], 0,
                "The character's own position is not usable, so nothing can be judged against it. " +
                "Resolve unit positions first.");
        }

        // One read per object rather than one per candidate offset: a thousand aligned
        // offsets across thirty objects would otherwise be thirty thousand calls.
        var blocks = new List<byte[]>();
        foreach (GameObjectRef gameObject in gameObjects)
        {
            byte[] block = new byte[ScanLength];
            if (reader.TryReadBytes(gameObject.Address, block))
            {
                blocks.Add(block);
            }
        }

        if (blocks.Count < MinimumObjects)
        {
            return new GameObjectPositionScan(
                [], blocks.Count,
                $"Only {blocks.Count} game object(s) could be read, and at least {MinimumObjects} are " +
                "needed before a match means anything. Stand somewhere with more around: a town, or " +
                "a field with plenty of nodes.");
        }

        var candidates = new List<PositionOffsetCandidate>();

        for (uint offset = 0; offset + 12 <= ScanLength; offset += 4)
        {
            int plausible = 0;
            Vector3 example = Vector3.Zero;

            foreach (byte[] block in blocks)
            {
                Vector3 position = ReadVector3(block, (int)offset);

                if (!WorldBounds.IsPlausible(position)
                    || position.Distance(playerPosition) > MaxPlausibleDistance)
                {
                    continue;
                }

                plausible++;

                if (example.IsZero)
                {
                    example = position;
                }
            }

            if ((double)plausible / blocks.Count >= RequiredAgreement)
            {
                candidates.Add(new PositionOffsetCandidate(offset, plausible, blocks.Count, example));
            }
        }

        candidates.Sort((left, right) => right.Plausible.CompareTo(left.Plausible));

        string message = candidates.Count switch
        {
            0 => $"No offset in the first 0x{ScanLength:X} bytes held a plausible nearby position across " +
                 $"{blocks.Count} game objects. Either game objects keep their position further in, or " +
                 "behind a pointer rather than inline.",
            1 => $"One offset matched across {blocks.Count} game objects: 0x{candidates[0].Offset:X3}. " +
                 "Confirm it against a node you are standing next to before trusting it.",
            _ => $"{candidates.Count} offsets matched across {blocks.Count} game objects. That is normal: " +
                 "objects often hold more than one copy of their position. Confirm which one tracks a " +
                 "node you are standing next to.",
        };

        Log.For(nameof(GameObjectPositionResolver)).Information(
            "Game object position scan over {Count} objects found {Matches} candidate offset(s)",
            blocks.Count, candidates.Count);

        return new GameObjectPositionScan(candidates, blocks.Count, message);
    }

    /// <summary>
    /// Resolves the offset to use, or reports why it could not be.
    /// </summary>
    /// <remarks>
    /// Called during attach. Failing is not fatal to the bot as a whole: reading the world,
    /// fighting and moving all work without it, and only gathering and anything else that
    /// needs to walk to a world object is blocked. The attach report therefore records this
    /// as a warning rather than a failure.
    /// </remarks>
    public static GameObjectPositionResolution Resolve(
        IMemoryReader reader,
        IEnumerable<GameObjectRef> gameObjects,
        Vector3 playerPosition)
    {
        GameObjectPositionScan scan = Scan(reader, gameObjects, playerPosition);

        if (scan.BestOffset is not { } offset)
        {
            return new GameObjectPositionResolution(null, scan, scan.Message);
        }

        // Several offsets passing is normal rather than a problem: an object commonly holds
        // more than one copy of its position. The lowest is taken because the first such
        // block is the one the client reads from itself.
        uint lowest = scan.Candidates.Min(candidate => candidate.Offset);

        string detail = scan.IsUnambiguous
            ? $"Game object positions are at 0x{lowest:X3}, agreed by " +
              $"{scan.Candidates[0].Plausible} of {scan.ObjectsSampled} objects."
            : $"Game object positions taken as 0x{lowest:X3}, the lowest of " +
              $"{scan.Candidates.Count} matching offsets across {scan.ObjectsSampled} objects. " +
              "Confirm it against a node you are standing next to.";

        return new GameObjectPositionResolution(lowest, scan, detail);
    }

    private static Vector3 ReadVector3(ReadOnlySpan<byte> block, int offset) =>
        new(
            BinaryPrimitives.ReadSingleLittleEndian(block[offset..]),
            BinaryPrimitives.ReadSingleLittleEndian(block[(offset + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(block[(offset + 8)..]));
}
