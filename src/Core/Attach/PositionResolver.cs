using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;

namespace WoWBuddy.Core.Attach;

/// <summary>
/// Decides which of the candidate unit-position layouts the attached client actually uses.
/// </summary>
/// <remarks>
/// <para>
/// The public sources disagree about where a unit's position block lives, and one of them
/// disagrees with itself. Rather than pick a value and hope, the correct one is identified
/// empirically against the running client, using two properties that a wrong offset cannot
/// fake:
/// </para>
/// <list type="number">
/// <item>
/// A real coordinate is finite and inside the world grid. Garbage read from the wrong
/// offset is overwhelmingly likely to be denormal, enormous, or exactly zero.
/// </item>
/// <item>
/// The client only keeps objects that are near the player, so every unit in the manager
/// should be within a few hundred yards of the player. A wrong offset scatters them
/// across the coordinate space.
/// </item>
/// </list>
/// <para>
/// Requiring both, across many units at once, is enough to pick the right layout with high
/// confidence. If neither candidate clears the bar, the resolver reports failure and attach
/// stops, which is the correct outcome: acting on an unverified position offset is how a bot
/// walks a character into a wall for six hours.
/// </para>
/// </remarks>
public static class PositionResolver
{
    /// <summary>
    /// Fraction of sampled units that must yield a plausible nearby position for a candidate
    /// to be accepted.
    /// </summary>
    /// <remarks>
    /// Not 1.0, because objects genuinely do get destroyed underneath the walk, and units on
    /// a transport report transport-relative coordinates that legitimately look wrong.
    /// </remarks>
    public const double RequiredAgreement = 0.80;

    /// <summary>
    /// How far from the player a unit may be and still count as corroborating.
    /// </summary>
    /// <remarks>
    /// The client's own object update distance is well under this. The margin is generous
    /// so that a large battleground or a long sight line does not produce a false negative,
    /// while still being tiny next to the 34000-yard span of a map.
    /// </remarks>
    public const float MaxPlausibleUnitDistance = 2000f;

    /// <summary>Smallest number of units needed before the agreement ratio means anything.</summary>
    public const int MinimumSampleSize = 3;

    /// <summary>
    /// Picks the position layout that best matches the attached client.
    /// </summary>
    /// <param name="reader">Reader for the attached client.</param>
    /// <param name="objectManager">A manager already known to resolve.</param>
    /// <returns>The resolution outcome, successful or not.</returns>
    public static PositionResolution Resolve(IMemoryReader reader, ObjectManager objectManager)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(objectManager);

        GameObjectRef localPlayer = objectManager.FindLocalPlayer();
        if (!localPlayer.IsValid)
        {
            return PositionResolution.Failed(
                "The local player object could not be found, so position offsets cannot be resolved. " +
                "Log a character fully into the world before attaching.");
        }

        // One walk, reused for every candidate. Walking per candidate would sample a
        // different world each time and make the scores incomparable.
        List<nint> units = objectManager.EnumerateObjects()
            .Where(o => o.Type.IsUnitLike())
            .Select(o => o.Address)
            .Take(256)
            .ToList();

        var scored = new List<PositionCandidateScore>();

        foreach (Offsets335a.PositionLayout candidate in Offsets335a.UnitPosition.Candidates)
        {
            scored.Add(Score(reader, candidate, localPlayer.Address, units));
        }

        foreach (PositionCandidateScore score in scored)
        {
            Log.For(nameof(PositionResolver)).Information(
                "Position candidate {Layout}: player position {Player}, {Agree}/{Total} units agree",
                score.Layout, score.PlayerPosition, score.AgreeingUnits, score.SampledUnits);
        }

        PositionCandidateScore best = scored
            .Where(s => s.PlayerPositionPlausible)
            .OrderByDescending(s => s.Agreement)
            .ThenByDescending(s => s.SampledUnits)
            .FirstOrDefault();

        if (best.SampledUnits == 0 && !best.PlayerPositionPlausible)
        {
            return PositionResolution.Failed(
                "No candidate position layout produced a plausible position for the local player. " +
                "The offset table does not match this client. See docs/offsets.md for how to find the correct value.");
        }

        // With too few units around, the agreement ratio is noise. The player's own position
        // being plausible is still real evidence, so accept but say so.
        if (best.SampledUnits < MinimumSampleSize)
        {
            return PositionResolution.Succeeded(
                best.Layout,
                confident: false,
                $"Only {best.SampledUnits} unit(s) were in view, too few to corroborate the layout. " +
                $"Accepted {best.Layout} on the strength of the local player's position alone " +
                $"({best.PlayerPosition}). Re-attach somewhere busier to confirm.");
        }

        if (best.Agreement < RequiredAgreement)
        {
            return PositionResolution.Failed(
                $"The best candidate layout ({best.Layout}) was corroborated by only " +
                $"{best.AgreeingUnits} of {best.SampledUnits} nearby units " +
                $"({best.Agreement:P0}, {RequiredAgreement:P0} required). " +
                "Refusing to attach rather than act on an unverified position offset.");
        }

        return PositionResolution.Succeeded(
            best.Layout,
            confident: true,
            $"Resolved unit positions to {best.Layout}; corroborated by " +
            $"{best.AgreeingUnits} of {best.SampledUnits} nearby units.");
    }

    private static PositionCandidateScore Score(
        IMemoryReader reader,
        Offsets335a.PositionLayout candidate,
        nint localPlayerAddress,
        IReadOnlyList<nint> unitAddresses)
    {
        Vector3 playerPosition = ReadPosition(reader, localPlayerAddress, candidate);
        bool playerPlausible = WorldBounds.IsPlausible(playerPosition);

        int sampled = 0;
        int agreeing = 0;

        foreach (nint address in unitAddresses)
        {
            if (address == localPlayerAddress)
            {
                continue;
            }

            sampled++;
            Vector3 position = ReadPosition(reader, address, candidate);

            if (WorldBounds.IsPlausible(position)
                && playerPlausible
                && position.Distance(playerPosition) <= MaxPlausibleUnitDistance)
            {
                agreeing++;
            }
        }

        return new PositionCandidateScore(candidate, playerPosition, playerPlausible, sampled, agreeing);
    }

    private static Vector3 ReadPosition(IMemoryReader reader, nint objectAddress, Offsets335a.PositionLayout layout) =>
        reader.TryReadVector3(objectAddress + (nint)layout.PositionBlock, out Vector3 position)
            ? position
            : Vector3.Zero;
}

/// <summary>How well one candidate layout matched the client.</summary>
internal readonly record struct PositionCandidateScore(
    Offsets335a.PositionLayout Layout,
    Vector3 PlayerPosition,
    bool PlayerPositionPlausible,
    int SampledUnits,
    int AgreeingUnits)
{
    /// <summary>Fraction of sampled units whose position corroborated this layout.</summary>
    public double Agreement => SampledUnits == 0 ? 0d : (double)AgreeingUnits / SampledUnits;
}

/// <summary>The outcome of resolving the unit position layout.</summary>
public readonly record struct PositionResolution
{
    private PositionResolution(bool success, Offsets335a.PositionLayout layout, bool confident, string message)
    {
        Success = success;
        Layout = layout;
        Confident = confident;
        Message = message;
    }

    /// <summary>True when a layout was identified.</summary>
    public bool Success { get; }

    /// <summary>The identified layout. Meaningless when <see cref="Success"/> is false.</summary>
    public Offsets335a.PositionLayout Layout { get; }

    /// <summary>
    /// False when the layout was accepted on weak evidence, typically because almost nothing
    /// was in view. The bot still runs, but the attach report says so.
    /// </summary>
    public bool Confident { get; }

    /// <summary>Human-readable explanation, suitable for the log and the UI.</summary>
    public string Message { get; }

    internal static PositionResolution Succeeded(Offsets335a.PositionLayout layout, bool confident, string message) =>
        new(true, layout, confident, message);

    internal static PositionResolution Failed(string message) =>
        new(false, default, false, message);
}
