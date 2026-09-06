using WoWBuddy.Behavior;
using WoWBuddy.BotBases;
using WoWBuddy.BotBases.Battlegrounds;
using WoWBuddy.BotBases.Group;
using WoWBuddy.BotBases.Questing;
using WoWBuddy.BotBases.Support;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Profiles;
using WoWBuddy.WorldData;

namespace WoWBuddy.Presentation;

/// <summary>
/// The four things only the game client can do for the fishing base.
/// </summary>
/// <remarks>
/// Fishing is the one bot base that cannot be built from settings alone: it has to read the
/// bobber's state byte out of the client and click it, and neither has an equivalent anywhere
/// else in the bot. Passing them in keeps the factory free of the client while still letting it
/// refuse, with a sentence, when they are missing.
/// </remarks>
/// <param name="BobberState">Reads the bobber's state byte, or null when there is no bobber.</param>
/// <param name="CastLine">Casts the fishing spell.</param>
/// <param name="ApplyLure">Applies the configured lure.</param>
/// <param name="ClickBobber">Interacts with the bobber to collect the catch.</param>
public sealed record FishingHooks(
    Func<IBotState, byte?> BobberState,
    Func<IBotState, bool> CastLine,
    Func<IBotState, bool> ApplyLure,
    Func<IBotState, bool> ClickBobber);

/// <summary>What could not be built, and why.</summary>
/// <param name="Tree">The subtree to run, or null when it could not be built.</param>
/// <param name="Message">A sentence for the status bar.</param>
public readonly record struct BotBaseBuild(Node<IBotState>? Tree, string Message)
{
    /// <summary>True when there is something to run.</summary>
    public bool Success => Tree is not null;
}

/// <summary>
/// Turns the user's choices into the subtree the bot runs.
/// </summary>
/// <remarks>
/// <para>
/// This is where the window's choices meet the bot proper, and it lives here rather than in the
/// window for the same reason everything else does: it is the part most likely to be wrong, and
/// it is only testable if a test can reach it. "Which bot base did the user pick, and does the
/// profile they chose actually give it what it needs" is a decision, not a piece of plumbing.
/// </para>
/// <para>
/// Every base that needs data it has not been given refuses with a sentence saying what is
/// missing, rather than starting and doing nothing. A bot that appears to run and quietly
/// achieves nothing for an hour is worse than one that will not start.
/// </para>
/// </remarks>
public static class BotBaseFactory
{
    /// <summary>Builds the subtree for a bot base by name.</summary>
    /// <param name="name">One of the names in <see cref="BotBaseOption.All"/>.</param>
    /// <param name="profile">The chosen profile, where there is one.</param>
    /// <param name="memory">What the bot has learned about the world, for the gather base.</param>
    /// <param name="role">What the character is playing, for the dungeon base.</param>
    /// <param name="behaviors">Behaviours plugins offer, for a profile's custom steps.</param>
    public static BotBaseBuild Create(
        string name,
        Profile? profile = null,
        WorldMemory? memory = null,
        PartyRole role = PartyRole.None,
        IReadOnlyDictionary<string, Node<IBotState>>? behaviors = null,
        FishingHooks? fishing = null,
        ITradeSkills? tradeSkills = null,
        CraftSettings? crafting = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return name.ToUpperInvariant() switch
        {
            "GRIND" => Grind(profile),
            "GATHER" => Gather(profile, memory),
            "FISH" => Fish(profile, fishing),
            "QUESTING" => Questing(profile, behaviors),
            "DUNGEON" => Dungeon(profile, role),
            "BATTLEGROUND" => Battleground(profile),
            "CRAFT" => Craft(tradeSkills, crafting),
            _ => new BotBaseBuild(null, $"'{name}' is not a bot base this build knows about."),
        };
    }

    private static BotBaseBuild Grind(Profile? profile)
    {
        GrindSettings settings = new()
        {
            Hotspots = Places(profile),
            AvoidEntries = profile?.AvoidMobs ?? new HashSet<uint>(),
            Blackspots = Blackspots(profile),
        };

        return new BotBaseBuild(
            new GrindBotBase(settings).Build(),
            settings.Hotspots.Count > 0
                ? $"Grinding {settings.Hotspots.Count} spot(s)."
                : "Grinding where the character stands. Give it a profile to patrol an area.");
    }

    private static BotBaseBuild Gather(Profile? profile, WorldMemory? memory)
    {
        if (profile is null)
        {
            return new BotBaseBuild(null, "The gather base needs a profile naming which nodes to gather.");
        }

        // Node ids differ between servers, so nothing is assumed: a profile's avoid list is
        // the wrong place for them, and the entries come from the steps that name one.
        HashSet<uint> nodes = [.. profile.AllSteps()
            .Where(step => step.Entry != 0)
            .Select(step => step.Entry)];

        if (nodes.Count == 0)
        {
            return new BotBaseBuild(
                null,
                "That profile names no nodes to gather. Add steps with an Entry, which is the "
                + "game object id of the herb or vein — the inspector reports it when you stand "
                + "next to one.");
        }

        GatherSettings settings = new()
        {
            NodeEntries = nodes,
            Route = Places(profile),
            Blackspots = Blackspots(profile),
        };

        return new BotBaseBuild(
            new GatherBotBase(settings, memory ?? new WorldMemory()).Build(),
            $"Gathering {nodes.Count} node type(s) around {settings.Route.Count} point(s).");
    }

    private static BotBaseBuild Fish(Profile? profile, FishingHooks? hooks)
    {
        if (hooks is null)
        {
            return new BotBaseBuild(
                null,
                "The fishing base needs to be able to read the bobber out of the client, which "
                + "means attaching first.");
        }

        FishSettings settings = new() { Spots = Places(profile) };

        return new BotBaseBuild(
            new FishBotBase(settings).Build(
                hooks.BobberState, hooks.CastLine, hooks.ApplyLure, hooks.ClickBobber),
            settings.Spots.Count > 0
                ? $"Fishing at {settings.Spots.Count} spot(s)."
                : "Fishing where the character stands.");
    }

    private static BotBaseBuild Questing(
        Profile? profile,
        IReadOnlyDictionary<string, Node<IBotState>>? behaviors)
    {
        if (profile is null)
        {
            return new BotBaseBuild(null, "The questing base needs a profile.");
        }

        // Running out of quests is what success looks like for a levelling profile, so the
        // fallback is built from the same profile rather than left empty.
        Node<IBotState> fallback = Grind(profile).Tree!;

        return new BotBaseBuild(
            new QuestingBotBase(profile, fallback, behaviors).Build(),
            $"Questing through {profile.Name}, with {profile.AllSteps().Count()} step(s).");
    }

    private static BotBaseBuild Dungeon(Profile? profile, PartyRole role)
    {
        if (role == PartyRole.None)
        {
            return new BotBaseBuild(
                null,
                "Choose a role before running the dungeon base. This bot does not detect one, "
                + "and guessing wrong means tanking in cloth.");
        }

        DungeonSettings settings = new()
        {
            Route = Places(profile),
            AvoidEntries = profile?.AvoidMobs ?? new HashSet<uint>(),
        };

        string message = role == PartyRole.Tank && settings.Route.Count == 0
            ? "Tanking without a route, so the bot will follow instead of leading."
            : $"Playing {role} in a group.";

        return new BotBaseBuild(new DungeonBotBase(settings).Build(), message);
    }

    private static BotBaseBuild Battleground(Profile? profile)
    {
        if (profile is null)
        {
            return new BotBaseBuild(
                null,
                "The battleground base needs a profile saying where to go. Battleground layouts "
                + "are game data this project does not ship.");
        }

        BattlegroundPlan plan = BattlegroundPlan.FromProfile(profile);

        if (plan.Posts.Count == 0)
        {
            return new BotBaseBuild(null, "That profile names nowhere to go.");
        }

        BattlegroundSettings settings = new() { Queue = profile.Name, Plan = plan };

        return new BotBaseBuild(
            new BattlegroundBotBase(settings).Build(),
            $"Queueing for {profile.Name}, working {plan.Posts.Count} post(s).");
    }

    private static BotBaseBuild Craft(ITradeSkills? skills, CraftSettings? settings)
    {
        if (skills is null)
        {
            return new BotBaseBuild(
                null,
                "The crafting base reads the character's professions out of the client, which "
                + "means attaching and enabling execution first.");
        }

        if (settings is not { IsUsable: true })
        {
            return new BotBaseBuild(
                null,
                "Name a profession to work on, as the client spells it.");
        }

        return new BotBaseBuild(
            new CraftBotBase(settings).Build(skills),
            settings.Recipe.Length > 0
                ? $"Making {settings.Recipe} until the materials run out."
                : $"Working {settings.Profession} up while the materials last.");
    }

    /// <summary>Every place a profile names, in written order.</summary>
    private static IReadOnlyList<Vector3> Places(Profile? profile)
    {
        if (profile is null)
        {
            return [];
        }

        List<Vector3> places = [];

        foreach (ProfileStep step in profile.AllSteps())
        {
            places.AddRange(step.AllPositions());
        }

        return places;
    }

    private static IReadOnlyList<(Vector3 Centre, float Radius)> Blackspots(Profile? profile) =>
        profile is null
            ? []
            : [.. profile.Blackspots.Select(spot => (spot.Position, spot.Radius))];
}
