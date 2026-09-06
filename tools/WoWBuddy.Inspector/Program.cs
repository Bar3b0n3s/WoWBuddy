using System.Globalization;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Attach;
using WoWBuddy.Core.Memory;
using WoWBuddy.Core.Client;
using WoWBuddy.Core.Execution;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;
using WoWBuddy.GameApi;
using WoWBuddy.GameApi.Objects;
using WoWBuddy.Navigation;
using WoWBuddy.Navigation.Data;
using WoWBuddy.Profiles;
using WoWBuddy.Profiles.Import;
using WoWBuddy.WorldData;

namespace WoWBuddy.Inspector;

/// <summary>
/// The phase 1 dev tool: attaches to a running client and shows what the bot can see.
/// </summary>
/// <remarks>
/// <para>
/// A console application rather than part of the WPF shell, because this is the tool used to
/// prove the offset table against a real client and it needs to be runnable with nothing else
/// working. It is also the tool that produces the evidence for the manual test script in
/// <c>docs/phase-1-manual-test.md</c>.
/// </para>
/// <para>
/// <para>
/// The phase 1 commands are strictly read-only. The phase 2 commands (<c>exec</c>,
/// <c>lua</c>) install a hook and are labelled as such in the help text, because writing
/// code into somebody's running game is not something a tool should do without saying so.
/// <c>ctm</c> reads the click-to-move block but never writes it.
/// </para>
/// </para>
/// </remarks>
public static class Program
{
    public static int Main(string[] args)
    {
        Log.Initialise();

        try
        {
            string command = args.Length > 0 ? args[0].ToLowerInvariant() : "inspect";

            return command switch
            {
                "list" => ListClients(),
                "offsets" => DumpOffsets(),
                "inspect" => Inspect(args),
                "watch" => Watch(args),
                "exec" => ExecSelfTest(args),
                "lua" => LuaConsole(args),
                "ctm" => ReadClickToMove(args),
                "nav" => CheckNavigationData(args),
                "worlddata" => CheckWorldData(args),
                "profile" => CheckProfile(args),
                "help" or "--help" or "-h" => ShowHelp(),
                _ => ShowUnknownCommand(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unhandled error: {ex}");
            return 1;
        }
        finally
        {
            Log.Shutdown();
        }
    }

    private static int ShowHelp()
    {
        Console.WriteLine("""
            WoWBuddy inspector - dev tool for an attached WoW 3.3.5a (12340) client.

            The first group is read-only. The second installs a hook in the client.

              list              List running clients and whether each is supported.
              inspect [pid]     Attach, verify offsets, and dump the world once.
              watch [pid]       Attach and print the local player's state once a second.
              offsets           Print the offset provenance table as Markdown.

            These install a hook in the client (phase 2). See docs/phase-2-manual-test.md:
              exec [pid]        Install game-thread execution, run the self-tests, remove it.
              lua [pid]         Interactive Lua console.
              ctm [pid]         Watch the click-to-move block. Read-only.

            Navigation data (phase 3). Needs no client:
              nav <mmaps-dir> [mapId...]
                                Check navigation data you extracted yourself.
              worlddata <dir> [--find TEXT]
                                Check world data exported from your server database,
                                and search it for node types by name.

              profile check <file.xml>
                                Read a profile and report everything wrong with it.
                                Needs no game client.

              profile import <file.xml> --map N [--out OUT.xml]
                                Convert an Honorbuddy profile, reporting what could
                                not be translated. --map is the map the profile is
                                for; Honorbuddy profiles do not record one.

              help              Show this text.

            With no pid, a single running supported client is used. If several are running,
            pass the pid explicitly rather than letting the tool guess.
            """);
        return 0;
    }

    private static int ShowUnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        ShowHelp();
        return 2;
    }

    private static int ListClients()
    {
        IReadOnlyList<WowClientCandidate> candidates = WowClientLocator.FindAll();

        if (candidates.Count == 0)
        {
            Console.WriteLine("No WoW client processes found.");
            return 1;
        }

        Console.WriteLine($"{candidates.Count} client process(es):");
        foreach (WowClientCandidate candidate in candidates)
        {
            string marker = candidate.Build.IsSupported ? "  supported" : "  UNSUPPORTED";
            Console.WriteLine($"{marker}  {candidate.Describe()}");
        }

        return candidates.Any(c => c.Build.IsSupported) ? 0 : 1;
    }

    private static int DumpOffsets()
    {
        Console.WriteLine(OffsetCatalogue.ToMarkdown());

        IReadOnlyList<CataloguedOffset> pending = OffsetCatalogue.NeedingVerification();
        if (pending.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{pending.Count} offset(s) still need first-hand verification:");
            foreach (CataloguedOffset entry in pending)
            {
                Console.WriteLine($"  [{entry.Confidence}] {entry.Path} = {entry.Value}");
            }
        }

        return 0;
    }

    private static int Inspect(string[] args)
    {
        using GameClient? client = AttachOrReport(args);
        if (client is null)
        {
            return 1;
        }

        var world = new World(client);
        WorldSnapshot snapshot = world.Snapshot();

        PrintLocalPlayer(snapshot.Me);
        PrintObjectBreakdown(snapshot);
        PrintNearbyUnits(snapshot);

        return 0;
    }

    private static int Watch(string[] args)
    {
        using GameClient? client = AttachOrReport(args);
        if (client is null)
        {
            return 1;
        }

        var world = new World(client);
        Console.WriteLine("Watching. Press Ctrl+C to stop.");
        Console.WriteLine();

        using var stop = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
        };

        while (!stop.IsSet)
        {
            if (!client.IsAttached)
            {
                Console.WriteLine("Lost the client: it exited, or a different character logged in.");
                return 1;
            }

            WoWLocalPlayer? me = world.Me;
            if (me is null)
            {
                Console.WriteLine("Not in the world.");
            }
            else
            {
                Vector3 position = me.Position;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{DateTime.Now:HH:mm:ss}  level {me.Level,2}  " +
                    $"hp {me.Health,6}/{me.MaxHealth,-6} ({me.HealthPercent,5:F1}%)  " +
                    $"{me.PowerType} {me.Power,5}/{me.MaxPower,-5}  " +
                    $"{position}  facing {me.Facing,5:F2}  " +
                    $"map {me.MapId} zone {me.ZoneId}  " +
                    $"{(me.IsInCombat ? "COMBAT" : "      ")} {(me.IsMounted ? "MOUNTED" : "")}"));
            }

            stop.Wait(TimeSpan.FromSeconds(1));
        }

        return 0;
    }

    private static GameClient? AttachOrReport(string[] args)
    {
        AttachResult result = TryParsePid(args, out int pid)
            ? GameClient.Attach(pid)
            : GameClient.AttachToSingleClient();

        Console.WriteLine(result.Report.ToString());

        if (!result.Success)
        {
            Console.Error.WriteLine("Attach failed. Nothing was read beyond the checks above.");
            return null;
        }

        return result.Client;
    }

    private static bool TryParsePid(string[] args, out int pid)
    {
        pid = 0;
        return args.Length > 1 && int.TryParse(args[1], CultureInfo.InvariantCulture, out pid);
    }

    /// <summary>
    /// Installs game-thread execution, reports what the self-tests found, and removes it.
    /// </summary>
    private static int ExecSelfTest(string[] args)
    {
        using GameClient? client = AttachOrReport(args);
        if (client is null)
        {
            return 1;
        }

        Console.WriteLine("Installing game-thread execution. This writes a small amount of code");
        Console.WriteLine("into the client and redirects one Direct3D vtable entry through it.");
        Console.WriteLine();

        ExecutionInstallResult result = client.EnableExecution();
        Console.WriteLine(result.Report.ToString());

        if (!result.Success)
        {
            Console.Error.WriteLine("Execution is not available. Nothing was left installed.");
            return 1;
        }

        ExecutionSession session = result.Session!;

        Console.WriteLine("Optional check (changes your target briefly, then puts it back):");
        Console.WriteLine(session.Native.SelfTestTargeting(out string targeting)
            ? $"  [Passed ] Targeting: {targeting}"
            : $"  [Failed ] Targeting: {targeting}");
        Console.WriteLine();

        Console.WriteLine($"Calls completed on the game thread: {session.Executor.CompletedCallCount}");
        Console.WriteLine("Removing the hook.");
        return 0;
    }

    /// <summary>
    /// An interactive Lua prompt against the attached client.
    /// </summary>
    /// <remarks>
    /// Expressions are evaluated and printed; anything ending in a semicolon is run as a
    /// statement. Protected functions will refuse to run from here, which is a property of
    /// the client rather than a limitation of the console: the bot uses native calls for
    /// anything that acts on the world.
    /// </remarks>
    private static int LuaConsole(string[] args)
    {
        using GameClient? client = AttachOrReport(args);
        if (client is null)
        {
            return 1;
        }

        ExecutionInstallResult result = client.EnableExecution();
        if (!result.Success)
        {
            Console.WriteLine(result.Report.ToString());
            Console.Error.WriteLine("Cannot open a Lua console without game-thread execution.");
            return 1;
        }

        ExecutionSession session = result.Session!;

        Console.WriteLine("Lua console. Expressions are evaluated; lines ending in ';' are run as");
        Console.WriteLine("statements. Protected functions will refuse to run. Blank line or 'exit' quits.");
        if (!session.Lua.CanReadResults)
        {
            Console.WriteLine($"Results unavailable: {session.Lua.ResultsUnavailableReason}");
        }

        Console.WriteLine();

        while (true)
        {
            Console.Write("lua> ");
            string? line = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(line) || line.Trim() is "exit" or "quit")
            {
                return 0;
            }

            if (!session.IsUsable)
            {
                Console.Error.WriteLine("Game-thread execution stopped working; not sending anything further.");
                return 1;
            }

            string input = line.Trim();

            if (input.EndsWith(';'))
            {
                Console.WriteLine(session.Lua.Execute(input) ? "ok" : "failed");
                continue;
            }

            string? value = session.Lua.Evaluate(input);
            Console.WriteLine(value is null ? "(no value)" : value);
        }
    }

    /// <summary>
    /// Prints the click-to-move block once a second, without writing to it.
    /// </summary>
    /// <remarks>
    /// The way to confirm the click-to-move offsets before phase 3 relies on them: move
    /// normally in game and watch the destination match where you clicked.
    /// </remarks>
    private static int ReadClickToMove(string[] args)
    {
        using GameClient? client = AttachOrReport(args);
        if (client is null)
        {
            return 1;
        }

        var writer = new ClickToMoveWriter((IProcessMemory)client.Memory);

        Console.WriteLine($"Click-to-move block at 0x{writer.BaseAddress:X8}. Read-only.");
        Console.WriteLine("Right-click-move in game: the destination should match where you clicked.");
        Console.WriteLine("Press Ctrl+C to stop.");
        Console.WriteLine();

        using var stop = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
        };

        while (!stop.IsSet)
        {
            ClickToMoveState state = writer.Read();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{DateTime.Now:HH:mm:ss}  action {state.Action,-16} dest {state.Destination}  " +
                $"stop {state.StopDistance,5:F2}  guid {(state.InteractGuid.IsZero ? "-" : state.InteractGuid.ToString())}"));

            stop.Wait(TimeSpan.FromSeconds(1));
        }

        return 0;
    }

    /// <summary>
    /// Checks a folder of extracted navigation data and says what is in it.
    /// </summary>
    /// <remarks>
    /// Needs no game client, because the commonest navigation problem is the data rather than
    /// the bot: a partial extraction, the wrong continent, or files from both server projects
    /// mixed together. Finding that out should not require logging in.
    /// </remarks>
    private static int CheckNavigationData(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: nav <mmaps-directory> [mapId ...]");
            Console.Error.WriteLine("Defaults to the four 3.3.5a continents when no map ids are given.");
            return 2;
        }

        string directory = args[1];
        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"No such directory: {directory}");
            return 1;
        }

        // Eastern Kingdoms, Kalimdor, Outland, Northrend.
        int[] maps = args.Length > 2
            ? args[2..].Select(a => int.TryParse(a, CultureInfo.InvariantCulture, out int id) ? id : -1)
                       .Where(id => id >= 0).ToArray()
            : [0, 1, 530, 571];

        Console.WriteLine($"Navigation data in {directory}");
        Console.WriteLine();

        var loader = new NavMeshLoader(directory);
        int usable = 0;

        foreach (int mapId in maps)
        {
            NavMeshLoadResult result = loader.Load(mapId);
            string name = MapName(mapId);

            if (result.Success)
            {
                usable++;
                Console.WriteLine(
                    $"  [ok    ] map {mapId,3} {name,-18} {result.TilesLoaded,5} tiles, {result.Flavour} data" +
                    (result.TilesRejected > 0 ? $", {result.TilesRejected} rejected" : string.Empty));
            }
            else
            {
                Console.WriteLine($"  [FAILED] map {mapId,3} {name,-18} unusable");
            }

            foreach (string problem in result.Problems.Take(5))
            {
                Console.WriteLine($"             {problem}");
            }

            if (result.Problems.Count > 5)
            {
                Console.WriteLine($"             ... and {result.Problems.Count - 5} more");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{usable} of {maps.Length} map(s) usable.");
        Console.WriteLine("See docs/navigation-data.md if anything above failed.");
        return usable > 0 ? 0 : 1;
    }

    /// <summary>
    /// Checks a world data export and searches it for node types.
    /// </summary>
    /// <remarks>
    /// Needs no game client. The search is how a user finds the node entry ids to gather:
    /// the bot ships no list, because inventing one would be an unverified fact and the ids
    /// differ between databases anyway.
    /// </remarks>
    private static int CheckWorldData(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: worlddata <export-directory> [--find TEXT]");
            Console.Error.WriteLine("See docs/world-data.md for how to produce the export.");
            return 2;
        }

        string directory = args[1];
        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"No such directory: {directory}");
            return 1;
        }

        var set = new WorldDataSet();
        IReadOnlyList<string> problems = set.LoadFrom(directory);

        Console.WriteLine($"World data in {directory}");
        Console.WriteLine();
        Console.WriteLine($"  Game object spawns    {set.GameObjectSpawnCount,9}");
        Console.WriteLine($"  Game object templates {set.GameObjectTemplateCount,9}");
        Console.WriteLine($"  Creature spawns       {set.CreatureSpawnCount,9}");
        Console.WriteLine($"  Service creatures     {set.CreatureTemplateCount,9}");

        if (problems.Count > 0)
        {
            Console.WriteLine();
            foreach (string problem in problems)
            {
                Console.WriteLine($"  {problem}");
            }
        }

        int findIndex = Array.FindIndex(args, a => a == "--find");
        if (findIndex >= 0 && findIndex + 1 < args.Length)
        {
            string text = args[findIndex + 1];
            Console.WriteLine();
            Console.WriteLine($"Gatherable objects matching \"{text}\":");

            IReadOnlyList<GameObjectTemplate> found = set.FindTemplatesByName(text);
            if (found.Count == 0)
            {
                Console.WriteLine("  Nothing matched. Names come from your database, so try its language.");
            }

            foreach (GameObjectTemplate template in found.Take(40))
            {
                Console.WriteLine($"  {template.Entry,8}  {template.Name}");
            }

            if (found.Count > 40)
            {
                Console.WriteLine($"  ... and {found.Count - 40} more");
            }
        }

        Console.WriteLine();
        Console.WriteLine("See docs/world-data.md if anything above is missing.");
        return set.IsLoaded ? 0 : 1;
    }

    private static string MapName(int mapId) => mapId switch
    {
        0 => "Eastern Kingdoms",
        1 => "Kalimdor",
        530 => "Outland",
        571 => "Northrend",
        _ => string.Empty,
    };

    private static void PrintLocalPlayer(WoWLocalPlayer? me)
    {
        Console.WriteLine("Local player");
        Console.WriteLine("------------");

        if (me is null)
        {
            Console.WriteLine("  Not in the world.");
            Console.WriteLine();
            return;
        }

        string name = me.Name;
        Console.WriteLine($"  Name          {(string.IsNullOrEmpty(name) ? "(name cache unavailable)" : name)}");
        Console.WriteLine($"  GUID          {me.Guid}");
        Console.WriteLine($"  Race / class  {me.Race} {me.Class}");
        Console.WriteLine($"  Level         {me.Level}");
        Console.WriteLine($"  Health        {me.Health}/{me.MaxHealth} ({me.HealthPercent:F1}%)");
        Console.WriteLine($"  {me.PowerType,-13} {me.Power}/{me.MaxPower} ({me.PowerPercent:F1}%)");
        Console.WriteLine($"  Experience    {me.Experience}/{me.NextLevelExperience} ({me.ExperiencePercent:F1}%)");
        Console.WriteLine($"  Money         {me.Gold:F4}g");
        Console.WriteLine($"  Position      {me.Position}  facing {me.Facing:F3} rad");
        Console.WriteLine($"  Map / zone    {me.MapId} / {me.ZoneId}");
        Console.WriteLine($"  Stand state   {me.StandState}");
        Console.WriteLine($"  Flags         {me.Flags}");
        Console.WriteLine($"  In combat     {me.IsInCombat}");
        Console.WriteLine($"  Mounted       {me.IsMounted}");
        Console.WriteLine($"  Target        {(me.TargetGuid.IsZero ? "(none)" : me.TargetGuid.ToString())}");
        Console.WriteLine();
    }

    private static void PrintObjectBreakdown(WorldSnapshot snapshot)
    {
        Console.WriteLine($"Objects ({snapshot.Objects.Count} total)");
        Console.WriteLine("-------");

        foreach ((WoWObjectType type, int count) in snapshot.CountByType.OrderByDescending(kv => kv.Value))
        {
            Console.WriteLine($"  {type,-14} {count,5}");
        }

        Console.WriteLine();
    }

    private static void PrintNearbyUnits(WorldSnapshot snapshot)
    {
        if (snapshot.Me is not { } me)
        {
            return;
        }

        Vector3 origin = me.Position;

        List<(WoWUnit Unit, float Distance)> nearby = snapshot.Units
            .Where(u => u.Guid != me.Guid)
            .Select(u => (Unit: u, Distance: u.Position.Distance(origin)))
            .Where(x => WorldBounds.IsPlausible(x.Unit.Position))
            .OrderBy(x => x.Distance)
            .Take(20)
            .ToList();

        Console.WriteLine($"Nearest units ({nearby.Count} shown)");
        Console.WriteLine("-------------");

        if (nearby.Count == 0)
        {
            Console.WriteLine("  Nothing in range.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"  {"Distance",8}  {"Type",-6}  {"Level",5}  {"Health",13}  {"Entry",6}  GUID");
        foreach ((WoWUnit unit, float distance) in nearby)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {distance,8:F1}  {unit.Type,-6}  {unit.Level,5}  " +
                $"{unit.Health,6}/{unit.MaxHealth,-6}  {unit.Entry,6}  {unit.Guid}"));
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Reads a profile and prints the loader's report.
    /// </summary>
    /// <remarks>
    /// Needs no game client. A questing profile plays out over hours, so being able to check
    /// one in a second before starting is worth a command of its own.
    /// </remarks>
    private static int CheckProfile(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: profile check <file.xml>");
            Console.Error.WriteLine("       profile import <file.xml> --map N [--out OUT.xml]");
            Console.Error.WriteLine("See docs/profiles.md for the format.");
            return 2;
        }

        if (string.Equals(args[1], "import", StringComparison.OrdinalIgnoreCase))
        {
            return ImportProfile(args);
        }

        if (!string.Equals(args[1], "check", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Unknown profile command '{args[1]}'. Use check or import.");
            return 2;
        }

        string path = args[2];
        ProfileLoadResult result = ProfileLoader.LoadFile(path);

        Console.WriteLine(result.Describe());
        Console.WriteLine();

        if (result.Profile is null)
        {
            Console.WriteLine("The profile could not be read at all.");
            return 1;
        }

        Profile profile = result.Profile;

        Console.WriteLine($"Name       : {profile.Name}");
        Console.WriteLine($"Author     : {(profile.Author.Length > 0 ? profile.Author : "(none given)")}");
        Console.WriteLine($"Faction    : {profile.Faction}");
        Console.WriteLine($"Levels     : {profile.MinimumLevel}-{profile.MaximumLevel}");
        Console.WriteLine($"Steps      : {profile.AllSteps().Count()} ({profile.Steps.Count} at the top level)");
        Console.WriteLine($"Vendors    : {profile.Vendors.Count}");
        Console.WriteLine($"Blackspots : {profile.Blackspots.Count}");
        Console.WriteLine($"Avoid mobs : {profile.AvoidMobs.Count}");
        Console.WriteLine();

        foreach (IGrouping<StepKind, ProfileStep> group in profile.AllSteps().GroupBy(step => step.Kind))
        {
            Console.WriteLine($"  {group.Key,-15} {group.Count(),4}");
        }

        int unknownObjectives = profile.AllSteps()
            .Count(step => step.Kind == StepKind.Objective && step.Objective == ObjectiveKind.Unknown);

        if (unknownObjectives > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"{unknownObjectives} objective(s) have no type the bot knows. It will work the "
                + "area and watch the quest log for those.");
        }

        Console.WriteLine();
        Console.WriteLine(result.Success
            ? "The profile is usable."
            : "The profile cannot be run until the errors above are fixed.");

        return result.Success ? 0 : 1;
    }


    /// <summary>
    /// Converts an Honorbuddy profile and prints what could not be carried across.
    /// </summary>
    /// <remarks>
    /// The report matters more than the file. A conversion that quietly dropped a condition
    /// produces a profile that looks right and does the wrong thing an hour in, so the exit
    /// code is non-zero whenever anything was lost, even though a usable file was still written.
    /// </remarks>
    private static int ImportProfile(string[] args)
    {
        string path = args[2];
        int mapId = 0;
        bool mapGiven = false;
        string? output = null;

        for (int index = 3; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--map", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                mapId = parsed;
                mapGiven = true;
            }
            else if (string.Equals(args[index], "--out", StringComparison.OrdinalIgnoreCase))
            {
                output = args[index + 1];
            }
        }

        if (!mapGiven)
        {
            Console.Error.WriteLine(
                "Pass --map N. Honorbuddy profiles do not record which map they are for, and a "
                + "converted profile on the wrong map navigates nowhere.");
            Console.Error.WriteLine("Eastern Kingdoms is 0, Kalimdor 1, Outland 530, Northrend 571.");
            return 2;
        }

        ProfileImportResult result = HonorbuddyImporter.ImportFile(path, mapId);

        Console.WriteLine(result.Describe());
        Console.WriteLine();

        if (!result.ProducedSomething)
        {
            return 1;
        }

        string destination = output ?? Path.ChangeExtension(path, ".wowbuddy.xml");

        try
        {
            File.WriteAllText(destination, result.ConvertedXml);
            Console.WriteLine($"Written to {destination}");
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"Could not write {destination}: {exception.Message}");
            return 1;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"Could not write {destination}: {exception.Message}");
            return 1;
        }

        return result.Success ? 0 : 1;
    }

}
