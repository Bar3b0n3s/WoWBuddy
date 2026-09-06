using System.Xml;
using System.Xml.Linq;
using WoWBuddy.Common.Geometry;

namespace WoWBuddy.Profiles;

/// <summary>
/// Reads a WoWBuddy profile and says what is wrong with it before the bot runs a step.
/// </summary>
/// <remarks>
/// <para>
/// A questing profile is a long list of instructions that plays out over hours. Finding out
/// at 2am that step 340 names a quest id of "1234a" is the worst possible time, so the loader
/// checks everything up front and returns a list of problems rather than the first exception
/// it hits. Errors stop the profile being used; warnings are reported and the profile still
/// runs.
/// </para>
/// <para>
/// <b>The format is data, never code.</b> Conditions are the small language in
/// <see cref="ProfileCondition"/> and nothing else. Profiles get shared between strangers and
/// then run unattended against someone's account, and a format that could execute arbitrary
/// code would make that a much bigger act of trust than it looks like.
/// </para>
/// <para>
/// <b>Unknown elements are reported, not silently dropped.</b> A profile written for another
/// bot, or for a newer version of this one, loads with a warning naming each element that was
/// skipped, so the user can see exactly how much of it the bot understood.
/// </para>
/// </remarks>
public static class ProfileLoader
{
    /// <summary>Elements allowed directly under the root.</summary>
    private static readonly string[] KnownSections =
        ["QuestOrder", "Vendors", "Blackspots", "AvoidMobs"];

    /// <summary>Reads a profile from a file.</summary>
    /// <param name="path">The .xml file to read.</param>
    /// <returns>The profile and everything the loader found wrong with it.</returns>
    public static ProfileLoadResult LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string source = Path.GetFileName(path);

        if (!File.Exists(path))
        {
            return new ProfileLoadResult(
                null,
                [new ProfileIssue(ProfileIssueSeverity.Error, $"There is no profile at '{path}'.")],
                source);
        }

        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException exception)
        {
            return new ProfileLoadResult(
                null,
                [new ProfileIssue(ProfileIssueSeverity.Error, $"'{path}' could not be read: {exception.Message}")],
                source);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new ProfileLoadResult(
                null,
                [new ProfileIssue(ProfileIssueSeverity.Error, $"'{path}' could not be read: {exception.Message}")],
                source);
        }

        return Parse(text, source);
    }

    /// <summary>Reads a profile from XML already in memory.</summary>
    /// <param name="xml">The profile's text.</param>
    /// <param name="source">Where it came from, for messages.</param>
    /// <returns>The profile and everything the loader found wrong with it.</returns>
    public static ProfileLoadResult Parse(string xml, string source = "profile")
    {
        List<ProfileIssue> issues = [];

        XDocument document;

        try
        {
            document = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            issues.Add(new ProfileIssue(
                ProfileIssueSeverity.Error,
                $"This is not valid XML: {exception.Message}",
                exception.LineNumber));

            return new ProfileLoadResult(null, issues, source);
        }

        XElement? root = document.Root;

        if (root is null)
        {
            issues.Add(new ProfileIssue(ProfileIssueSeverity.Error, "The file is empty."));
            return new ProfileLoadResult(null, issues, source);
        }

        if (!string.Equals(root.Name.LocalName, "Profile", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ProfileIssue(
                ProfileIssueSeverity.Error,
                $"The outermost element is <{root.Name.LocalName}>, but a WoWBuddy profile starts with "
                + "<Profile>. Honorbuddy profiles start with <HBProfile> and need the importer.",
                LineOf(root)));

            return new ProfileLoadResult(null, issues, source);
        }

        ProfileXml reader = new(root, issues);

        string name = reader.Text("Name");
        string author = reader.Text("Author");
        int minimumLevel = reader.Integer("MinLevel", 1);
        int maximumLevel = reader.Integer("MaxLevel", 80);
        ProfileFaction faction = ReadFaction(reader);
        reader.WarnAboutUnreadAttributes();

        if (name.Length == 0)
        {
            reader.Warn("has no Name, so it will be hard to tell apart in the profile list.");
            name = source;
        }

        if (minimumLevel > maximumLevel)
        {
            reader.Error($"has MinLevel={minimumLevel} above MaxLevel={maximumLevel}.");
        }

        List<ProfileStep> steps = [];
        List<ProfileVendor> vendors = [];
        List<ProfileBlackspot> blackspots = [];
        HashSet<uint> avoidMobs = [];

        foreach (XElement section in root.Elements())
        {
            switch (section.Name.LocalName.ToUpperInvariant())
            {
                case "QUESTORDER":
                    steps.AddRange(ReadSteps(section, issues, []));
                    break;

                case "VENDORS":
                    vendors.AddRange(ReadVendors(section, issues));
                    break;

                case "BLACKSPOTS":
                    blackspots.AddRange(ReadBlackspots(section, issues));
                    break;

                case "AVOIDMOBS":
                    foreach (uint entry in ReadAvoidMobs(section, issues))
                    {
                        avoidMobs.Add(entry);
                    }

                    break;

                default:
                    issues.Add(new ProfileIssue(
                        ProfileIssueSeverity.Warning,
                        $"<{section.Name.LocalName}> is not a section this bot knows, so it was ignored. "
                        + $"Sections it reads: {string.Join(", ", KnownSections)}.",
                        LineOf(section),
                        section.Name.LocalName));
                    break;
            }
        }

        Profile profile = new()
        {
            Name = name,
            Author = author,
            Faction = faction,
            MinimumLevel = minimumLevel,
            MaximumLevel = maximumLevel,
            Steps = steps,
            Vendors = vendors,
            Blackspots = blackspots,
            AvoidMobs = avoidMobs,
            Source = source,
        };

        CheckQuestOrderMakesSense(profile, issues);

        return new ProfileLoadResult(profile, issues, source);
    }

    private static ProfileFaction ReadFaction(ProfileXml reader)
    {
        string text = reader.Text("Faction");

        if (text.Length == 0)
        {
            return ProfileFaction.Any;
        }

        if (Enum.TryParse(text, ignoreCase: true, out ProfileFaction faction) && Enum.IsDefined(faction))
        {
            return faction;
        }

        if (string.Equals(text, "Both", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Neutral", StringComparison.OrdinalIgnoreCase))
        {
            return ProfileFaction.Any;
        }

        reader.Error($"Faction='{text}' is not Alliance, Horde or Any.");
        return ProfileFaction.Any;
    }

    private static List<ProfileStep> ReadSteps(
        XElement parent,
        List<ProfileIssue> issues,
        IReadOnlyList<ProfileCondition> inherited)
    {
        List<ProfileStep> steps = [];

        foreach (XElement element in parent.Elements())
        {
            ProfileXml reader = new(element, issues);

            switch (element.Name.LocalName.ToUpperInvariant())
            {
                case "IF":
                {
                    // An If is not a step: it is a condition wrapped around the steps inside
                    // it. Flattening it here means the runner only ever walks a list, and a
                    // step always carries every condition that governs it.
                    IReadOnlyList<ProfileCondition> conditions = reader.Conditions();
                    reader.WarnAboutUnreadAttributes();

                    if (conditions.Count == 0)
                    {
                        reader.Error("needs a Condition. Without one it does nothing but nest its contents.");
                    }

                    List<ProfileStep> inner = ReadSteps(element, issues, Combine(inherited, conditions));

                    if (inner.Count == 0)
                    {
                        reader.Warn("contains no steps.");
                    }

                    steps.AddRange(inner);
                    break;
                }

                case "WHILE":
                {
                    IReadOnlyList<ProfileCondition> conditions = reader.Conditions();
                    reader.WarnAboutUnreadAttributes();

                    if (conditions.Count == 0)
                    {
                        reader.Error("needs a Condition. Without one the bot would repeat it forever.");
                    }

                    List<ProfileStep> children = ReadSteps(element, issues, []);

                    if (children.Count == 0)
                    {
                        reader.Error("contains no steps, so there is nothing to repeat.");
                    }

                    steps.Add(new ProfileStep(
                        StepKind.Repeat,
                        Conditions: Combine(inherited, conditions),
                        Children: children,
                        LineNumber: reader.Line));
                    break;
                }

                case "PICKUP":
                    steps.Add(ReadQuestStep(reader, StepKind.PickUp, inherited));
                    break;

                case "TURNIN":
                    steps.Add(ReadQuestStep(reader, StepKind.TurnIn, inherited));
                    break;

                case "OBJECTIVE":
                    steps.Add(ReadObjective(reader, inherited));
                    break;

                case "RUNTO":
                {
                    Vector3 position = reader.Position(required: true);
                    int mapId = reader.Integer("Map");
                    IReadOnlyList<ProfileCondition> conditions = reader.Conditions();
                    reader.WarnAboutUnreadAttributes();

                    steps.Add(new ProfileStep(
                        StepKind.RunTo,
                        Position: position,
                        MapId: mapId,
                        Conditions: Combine(inherited, conditions),
                        LineNumber: reader.Line));
                    break;
                }

                case "GRIND":
                {
                    Vector3 position = reader.Position(required: true);
                    int mapId = reader.Integer("Map");
                    float radius = reader.Number("Radius", 100f);
                    IReadOnlyList<ProfileCondition> conditions = reader.Conditions();
                    IReadOnlyList<Vector3> hotspots = ReadHotspots(reader, issues);
                    reader.WarnAboutUnreadAttributes();

                    if (radius <= 0f)
                    {
                        reader.Error($"has Radius={radius}, which leaves nowhere to grind.");
                    }

                    if (conditions.Count == 0)
                    {
                        reader.Warn(
                            "has no Condition, so the bot will grind here until it is stopped by hand. "
                            + "Add one such as 'Level >= 12' to move on.");
                    }

                    steps.Add(new ProfileStep(
                        StepKind.Grind,
                        Position: position,
                        MapId: mapId,
                        Radius: radius,
                        Conditions: Combine(inherited, conditions),
                        Hotspots: hotspots,
                        LineNumber: reader.Line));
                    break;
                }

                case "CUSTOMBEHAVIOR":
                {
                    string behaviour = reader.Text("Name");
                    IReadOnlyList<ProfileCondition> conditions = reader.Conditions();
                    IReadOnlyDictionary<string, string> arguments =
                        reader.AllAttributes("Name", "Condition");

                    if (behaviour.Length == 0)
                    {
                        reader.Error("needs a Name saying which behaviour to run.");
                    }

                    steps.Add(new ProfileStep(
                        StepKind.CustomBehavior,
                        BehaviorName: behaviour,
                        Conditions: Combine(inherited, conditions),
                        Arguments: arguments,
                        LineNumber: reader.Line));
                    break;
                }

                default:
                    reader.Warn(
                        $"is not a step this bot knows, so it was skipped. The profile will run "
                        + "without whatever it did.");
                    break;
            }
        }

        return steps;
    }

    private static ProfileStep ReadQuestStep(
        ProfileXml reader,
        StepKind kind,
        IReadOnlyList<ProfileCondition> inherited)
    {
        uint questId = reader.Id("QuestId");
        string questName = reader.Text("QuestName");
        uint entry = reader.Id("Entry");
        int mapId = reader.Integer("Map");
        Vector3 position = reader.Position(required: false);
        IReadOnlyList<ProfileCondition> conditions = reader.Conditions();
        reader.WarnAboutUnreadAttributes();

        if (questId == 0)
        {
            reader.Error("needs a QuestId.");
        }

        if (entry == 0 && position.IsZero)
        {
            reader.Error(
                "needs either an Entry naming the quest giver or an X/Y/Z saying where it stands. "
                + "With neither, the bot has nothing to walk to.");
        }

        return new ProfileStep(
            kind,
            QuestId: questId,
            QuestName: questName,
            Entry: entry,
            Position: position,
            MapId: mapId,
            Conditions: Combine(inherited, conditions),
            LineNumber: reader.Line);
    }

    private static ProfileStep ReadObjective(
        ProfileXml reader,
        IReadOnlyList<ProfileCondition> inherited)
    {
        uint questId = reader.Id("QuestId");
        string questName = reader.Text("QuestName");
        uint entry = reader.Id("Entry");
        uint itemId = reader.Id("ItemId");
        int mapId = reader.Integer("Map");
        int index = reader.Integer("Index");
        int count = reader.Integer("Count");
        float radius = reader.Number("Radius", 100f);
        Vector3 position = reader.Position(required: false);
        IReadOnlyList<ProfileCondition> conditions = reader.Conditions();
        ObjectiveKind kind = ReadObjectiveKind(reader);
        IReadOnlyList<Vector3> hotspots = ReadHotspots(reader, reader.Element, kind);
        reader.WarnAboutUnreadAttributes();

        if (questId == 0)
        {
            reader.Error("needs a QuestId saying which quest it works on.");
        }

        if (index < 0)
        {
            reader.Error($"has Index={index}; objective numbers start at 1, and 0 means all of them.");
        }

        if (position.IsZero && hotspots.Count == 0)
        {
            reader.Error(
                "needs a position: either X/Y/Z, or one or more <Hotspot> elements. Without one "
                + "the bot does not know where to work.");
        }

        if (kind == ObjectiveKind.Collect && itemId == 0)
        {
            reader.Warn(
                "is a Collect objective with no ItemId, so the bot will watch the quest log for "
                + "progress instead of counting the item.");
        }

        if (kind == ObjectiveKind.UseItem && itemId == 0)
        {
            reader.Error("is a UseItem objective, so it needs an ItemId saying what to use.");
        }

        if (kind is ObjectiveKind.Kill or ObjectiveKind.Gossip && entry == 0)
        {
            reader.Warn(
                $"is a {kind} objective with no Entry, so the bot will work on whatever is hostile "
                + "in the area rather than a particular creature.");
        }

        return new ProfileStep(
            StepKind.Objective,
            QuestId: questId,
            QuestName: questName,
            Entry: entry,
            Position: position,
            MapId: mapId,
            Objective: kind,
            ObjectiveIndex: index,
            Count: count,
            ItemId: itemId,
            Radius: radius,
            Conditions: Combine(inherited, conditions),
            Hotspots: hotspots,
            LineNumber: reader.Line);
    }

    private static ObjectiveKind ReadObjectiveKind(ProfileXml reader)
    {
        string text = reader.Text("Type");

        if (text.Length == 0)
        {
            reader.Warn(
                "has no Type, so the bot will work the area and watch the quest log rather than "
                + "doing anything specific. Types it knows: "
                + string.Join(", ", Enum.GetNames<ObjectiveKind>()) + ".");

            return ObjectiveKind.Unknown;
        }

        if (Enum.TryParse(text, ignoreCase: true, out ObjectiveKind kind) && Enum.IsDefined(kind))
        {
            return kind;
        }

        reader.Warn(
            $"has Type='{text}', which the bot does not know, so it will work the area and watch "
            + "the quest log instead. Types it knows: "
            + string.Join(", ", Enum.GetNames<ObjectiveKind>()) + ".");

        return ObjectiveKind.Unknown;
    }

    private static IReadOnlyList<Vector3> ReadHotspots(ProfileXml reader, List<ProfileIssue> issues) =>
        ReadHotspots(reader, reader.Element, ObjectiveKind.Unknown);

    private static IReadOnlyList<Vector3> ReadHotspots(
        ProfileXml reader,
        XElement parent,
        ObjectiveKind _)
    {
        List<Vector3> hotspots = [];

        foreach (XElement element in parent.Elements())
        {
            if (!string.Equals(element.Name.LocalName, "Hotspot", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ProfileXml spot = new(element, []);
            Vector3 position = new(spot.Number("X"), spot.Number("Y"), spot.Number("Z"));

            if (!WorldBounds.IsPlausible(position))
            {
                reader.Report(
                    ProfileIssueSeverity.Error,
                    $"has a <Hotspot> at {position} on line {spot.Line}, which is not somewhere in the world.");
                continue;
            }

            hotspots.Add(position);
        }

        return hotspots;
    }

    private static List<ProfileVendor> ReadVendors(XElement parent, List<ProfileIssue> issues)
    {
        List<ProfileVendor> vendors = [];

        foreach (XElement element in parent.Elements())
        {
            ProfileXml reader = new(element, issues);
            string local = element.Name.LocalName;

            bool isMailbox = string.Equals(local, "Mailbox", StringComparison.OrdinalIgnoreCase);

            if (!isMailbox && !string.Equals(local, "Vendor", StringComparison.OrdinalIgnoreCase))
            {
                reader.Warn("is not a <Vendor> or <Mailbox>, so it was ignored.");
                continue;
            }

            string name = reader.Text("Name");
            uint entry = reader.Id("Entry");
            int mapId = reader.Integer("Map");
            Vector3 position = reader.Position(required: true);
            bool canRepair = !isMailbox && reader.Flag("Repair");
            reader.WarnAboutUnreadAttributes();

            if (entry == 0)
            {
                reader.Error("needs an Entry so the bot can recognise it when it gets there.");
            }

            vendors.Add(new ProfileVendor(name, entry, mapId, position, canRepair, isMailbox));
        }

        return vendors;
    }

    private static List<ProfileBlackspot> ReadBlackspots(XElement parent, List<ProfileIssue> issues)
    {
        List<ProfileBlackspot> blackspots = [];

        foreach (XElement element in parent.Elements())
        {
            ProfileXml reader = new(element, issues);

            if (!string.Equals(element.Name.LocalName, "Blackspot", StringComparison.OrdinalIgnoreCase))
            {
                reader.Warn("is not a <Blackspot>, so it was ignored.");
                continue;
            }

            Vector3 position = reader.Position(required: true);
            float radius = reader.Number("Radius", 20f);
            int mapId = reader.Integer("Map");
            string reason = reader.Text("Reason");
            reader.WarnAboutUnreadAttributes();

            if (radius <= 0f)
            {
                reader.Error($"has Radius={radius}, so it blocks nothing.");
                continue;
            }

            blackspots.Add(new ProfileBlackspot(position, radius, mapId, reason));
        }

        return blackspots;
    }

    private static List<uint> ReadAvoidMobs(XElement parent, List<ProfileIssue> issues)
    {
        List<uint> entries = [];

        foreach (XElement element in parent.Elements())
        {
            ProfileXml reader = new(element, issues);

            if (!string.Equals(element.Name.LocalName, "Mob", StringComparison.OrdinalIgnoreCase))
            {
                reader.Warn("is not a <Mob>, so it was ignored.");
                continue;
            }

            uint entry = reader.Id("Entry");
            reader.Ignore("Name");
            reader.WarnAboutUnreadAttributes();

            if (entry == 0)
            {
                reader.Error("needs an Entry naming the creature to leave alone.");
                continue;
            }

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Looks for quest orders that cannot play out: handing in something never picked up,
    /// or picking something up and never handing it in.
    /// </summary>
    /// <remarks>
    /// Both are warnings rather than errors. A profile meant to be run after another one can
    /// legitimately begin with a turn-in, and a profile can legitimately leave a long escort
    /// quest in the log. The point is to tell the author, not to refuse the file.
    /// </remarks>
    private static void CheckQuestOrderMakesSense(Profile profile, List<ProfileIssue> issues)
    {
        HashSet<uint> pickedUp = [];
        HashSet<uint> turnedIn = [];

        foreach (ProfileStep step in profile.AllSteps())
        {
            switch (step.Kind)
            {
                case StepKind.PickUp:
                    if (step.QuestId != 0 && !pickedUp.Add(step.QuestId))
                    {
                        issues.Add(new ProfileIssue(
                            ProfileIssueSeverity.Warning,
                            $"Quest {step.QuestId} is picked up more than once.",
                            step.LineNumber,
                            "PickUp"));
                    }

                    break;

                case StepKind.TurnIn:
                    if (step.QuestId == 0)
                    {
                        break;
                    }

                    turnedIn.Add(step.QuestId);

                    if (!pickedUp.Contains(step.QuestId))
                    {
                        issues.Add(new ProfileIssue(
                            ProfileIssueSeverity.Warning,
                            $"Quest {step.QuestId} is handed in without this profile ever picking it up. "
                            + "That is fine if an earlier profile did, but check the order.",
                            step.LineNumber,
                            "TurnIn"));
                    }

                    break;

                case StepKind.Objective:
                    if (step.QuestId != 0 && !pickedUp.Contains(step.QuestId))
                    {
                        issues.Add(new ProfileIssue(
                            ProfileIssueSeverity.Warning,
                            $"Work is done on quest {step.QuestId} before this profile picks it up.",
                            step.LineNumber,
                            "Objective"));
                    }

                    break;
            }
        }

        foreach (uint questId in pickedUp.Where(id => !turnedIn.Contains(id)))
        {
            issues.Add(new ProfileIssue(
                ProfileIssueSeverity.Warning,
                $"Quest {questId} is picked up but never handed in, so it will sit in the log."));
        }
    }

    private static IReadOnlyList<ProfileCondition> Combine(
        IReadOnlyList<ProfileCondition> outer,
        IReadOnlyList<ProfileCondition> inner)
    {
        if (outer.Count == 0)
        {
            return inner;
        }

        if (inner.Count == 0)
        {
            return outer;
        }

        List<ProfileCondition> combined = new(outer.Count + inner.Count);
        combined.AddRange(outer);
        combined.AddRange(inner);
        return combined;
    }

    private static int LineOf(XObject node) =>
        node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;
}
