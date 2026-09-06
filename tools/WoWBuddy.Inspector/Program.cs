using System.Globalization;
using WoWBuddy.Common.Geometry;
using WoWBuddy.Common.Logging;
using WoWBuddy.Core.Attach;
using WoWBuddy.Core.Client;
using WoWBuddy.Core.Objects;
using WoWBuddy.Core.Offsets;
using WoWBuddy.GameApi;
using WoWBuddy.GameApi.Objects;

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
/// Strictly read-only. Nothing here writes to the client.
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
            WoWBuddy inspector - read-only view of an attached WoW 3.3.5a (12340) client.

              list              List running clients and whether each is supported.
              inspect [pid]     Attach, verify offsets, and dump the world once.
              watch [pid]       Attach and print the local player's state once a second.
              offsets           Print the offset provenance table as Markdown.
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
}
