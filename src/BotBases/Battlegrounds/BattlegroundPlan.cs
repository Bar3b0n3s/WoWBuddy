using WoWBuddy.Common.Geometry;
using WoWBuddy.Profiles;

namespace WoWBuddy.BotBases.Battlegrounds;

/// <summary>Somewhere worth being in a battleground.</summary>
/// <param name="Position">Where it is.</param>
/// <param name="Radius">How far around it counts as being there.</param>
/// <param name="Entry">
/// A flag or banner to interact with on arrival, or 0 when the post is just a place to stand.
/// </param>
/// <param name="Name">What it is, for logs.</param>
public readonly record struct BattlegroundPost(
    Vector3 Position,
    float Radius = 20f,
    uint Entry = 0,
    string Name = "")
{
    /// <summary>True when there is something here to click.</summary>
    public bool HasObjective => Entry != 0;

    public override string ToString() =>
        Name.Length > 0 ? Name : Position.ToString();
}

/// <summary>
/// Where to go in a battleground, in order of preference.
/// </summary>
/// <remarks>
/// <para>
/// Battleground layouts are game data, so a plan comes from a profile the user supplies rather
/// than from this project. <see cref="FromProfile"/> reads one: every step that names a place
/// becomes a post, in written order, and the profile's blackspots and avoid list carry over.
/// That keeps one file format for users instead of inventing a second one.
/// </para>
/// <para>
/// Flags and banners are the one piece of battleground behaviour that generalises: capturing a
/// base, picking up a flag and returning one are all "walk to a thing and click it". A post
/// with an <see cref="BattlegroundPost.Entry"/> is that, and it works in every battleground
/// without a line of per-battleground scripting.
/// </para>
/// </remarks>
public sealed record BattlegroundPlan
{
    /// <summary>The battleground this plan is for, as the client names it.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Places worth being, most important first.</summary>
    public IReadOnlyList<BattlegroundPost> Posts { get; init; } = [];

    /// <summary>Creature entries never to attack.</summary>
    public IReadOnlySet<uint> AvoidEntries { get; init; } = new HashSet<uint>();

    /// <summary>Places never to walk into.</summary>
    public IReadOnlyList<ProfileBlackspot> Blackspots { get; init; } = [];

    /// <summary>How far to chase someone away from the current post.</summary>
    /// <remarks>
    /// The classic battleground bot failure is chasing one runner across the map while the
    /// objective it was standing on changes hands. A short leash is worth more than a kill.
    /// </remarks>
    public float ChaseRange { get; init; } = 30f;

    /// <summary>True when a position is inside a blackspot on this map.</summary>
    public bool IsBlacklisted(int mapId, Vector3 position) =>
        Blackspots.Any(spot => spot.Contains(mapId, position));

    /// <summary>Reads a plan out of a profile.</summary>
    /// <param name="profile">
    /// A profile whose steps are places worth being. Steps that name no position — a turn-in
    /// with only a creature entry, say — are skipped, because a battleground plan is only ever
    /// about where to stand.
    /// </param>
    public static BattlegroundPlan FromProfile(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        List<BattlegroundPost> posts = [];

        foreach (ProfileStep step in profile.AllSteps())
        {
            float radius = step.Radius > 0f ? step.Radius : 20f;

            if (!step.Position.IsZero)
            {
                posts.Add(new BattlegroundPost(step.Position, radius, step.Entry, step.QuestName));
            }

            foreach (Vector3 spot in step.Spots)
            {
                posts.Add(new BattlegroundPost(spot, radius, step.Entry, step.QuestName));
            }
        }

        return new BattlegroundPlan
        {
            Name = profile.Name,
            Posts = posts,
            AvoidEntries = profile.AvoidMobs,
            Blackspots = profile.Blackspots,
        };
    }
}
