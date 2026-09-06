# WoWBuddy

An open-source gameplay bot for **World of Warcraft 3.3.5a (build 12340)**, in the spirit of
the discontinued Honorbuddy. C# 12, .NET 8, WPF, Windows only. MIT licensed.

> **Botting violates the terms of service of Blizzard's servers and the rules of most private
> servers. Accounts get banned for it.** This project does not attempt to defeat server-side
> anti-cheat. Using it is entirely at your own risk.

## Status: complete, and entirely unproven

All eleven phases are done. Every piece is written, wired and tested — and none of it has ever
run against a real client. Both halves of that sentence matter, so this section is specific.

| Phase | State |
| --- | --- |
| 0 — Scaffold, CI, licensing, docs | **Done** |
| 1 — Attach and read: memory, offsets, object manager, typed objects, inspector | **Done** |
| 2 — Execute: game-thread hook, Lua bridge, native calls, Lua console | **Done** |
| 3 — Move: navigation meshes, path following, stuck handling | **Done** |
| 4 — Fight: behaviour trees, combat routines, grind bot base | **Done** |
| 5 — Live: loot, gear, errand planning, scheduler, humanization | **Done** |
| 6 — Gather and Fish bases, world data | **Done** |
| 7 — Questing base, profile schema and importer | **Done** |
| 8 — Dungeons and battlegrounds, group play | **Done** |
| 9 — Professions and the mixed-activity scheduler | **Done** |
| 10 — All thirty combat routines, plugins, UI | **Done** |
| 11 — Docs, packaging, release | **Done** |

### What you can actually do today

**Everything that reads a client works.** Find a running 12340 client, attach, verify the
offset table against that specific client, and walk the object manager to read the local player
and everything around it. Optionally install a hook on the render loop and run code on the
client's own thread: execute Lua, read values back, and call native functions. The
`WoWBuddy.Inspector` console tool exposes all of it, and it is the thing to run first.

**Everything that decides works, and is tested.** Six bot bases, thirty combat routines, the
behaviour tree, navigation over TrinityCore or AzerothCore meshes, profiles, questing, group
play, battlegrounds, loot and gear rules, the session scheduler, plugins. Seven hundred–odd
tests cover it, including a simulated client and an x86 interpreter that executes the exact
bytes the bot would inject.

### What is missing

**Nothing structural. Everything is wired.** `LiveBotState` reads a real client into the
interface every bot base was written against, `BotRunner` ticks the tree four times a second
with movement advanced first, and the Start button composes the lot.

**What is missing is evidence.** Not one line of this has ever run against a 3.3.5a client —
there has never been a Windows machine with one in this project's history. The 808 tests check
the bot against this project's own understanding of the client, which is exactly the thing that
could be wrong. Several Lua behaviours are assumed rather than confirmed, and hostility is an
outright approximation because faction data is not shipped.

See [docs/status.md](docs/status.md) for the detail, including exactly which assumptions are
outstanding.

## Try it

Download a release from the [releases page](https://github.com/Bar3b0n3s/WoWBuddy/releases),
unzip it, and run `WoWBuddy.Inspector.exe` with a character logged into the world:

```
WoWBuddy.Inspector.exe inspect
```

Or build it yourself:

```
dotnet build WoWBuddy.sln -c Release
dotnet run --project tools/WoWBuddy.Inspector -c Release -- inspect
```

Read [docs/setup.md](docs/setup.md) first. Then work through the manual test scripts in order:
[1](docs/phase-1-manual-test.md) confirms the offsets match your client,
[2](docs/phase-2-manual-test.md) confirms code can run inside it safely,
[3](docs/phase-3-manual-test.md) covers movement and click-to-move,
[4](docs/phase-4-manual-test.md) combat, [5](docs/phase-5-manual-test.md) looting and the
scheduler, [7](docs/phase-7-manual-test.md) questing, and
[8](docs/phase-8-manual-test.md) group play and battlegrounds.

### Reference documentation

| Document | Covers |
| --- | --- |
| [status.md](docs/status.md) | What works, what does not, and every outstanding assumption |
| [setup.md](docs/setup.md) | Getting a build running against your client |
| [architecture.md](docs/architecture.md) | How the pieces fit, and why the seams are where they are |
| [offsets.md](docs/offsets.md) | Every address, its source and its confidence |
| [navigation-data.md](docs/navigation-data.md) | Extracting meshes from your own client |
| [world-data.md](docs/world-data.md) | How the bot learns where things are |
| [profiles.md](docs/profiles.md) | The profile format, and importing Honorbuddy ones |
| [combat-routines.md](docs/combat-routines.md) | The thirty rotations and how to edit them |
| [group-play.md](docs/group-play.md) | Parties, following, assisting, healing, battlegrounds |
| [activities.md](docs/activities.md) | Mixing activities in a session, and professions |
| [plugins.md](docs/plugins.md) | What a plugin can do, and why it is trusted differently |
| [troubleshooting.md](docs/troubleshooting.md) | When something does not work |
| [legal-and-licensing.md](docs/legal-and-licensing.md) | What may and may not be linked in |

## How this project handles low-level facts

Memory offsets are claims about someone else's binary. A wrong one does not fail loudly — it
returns a plausible-looking number that the bot then acts on. So:

- **Every offset records where it came from.** A `[OffsetInfo]` attribute carries the source,
  a confidence level, and how to re-verify it. A unit test fails the build if any constant
  lacks one. The generated table is [docs/offsets.md](docs/offsets.md).
- **Attach is a gate.** You cannot get a `GameClient` without the offset table being checked
  against the live client first. A failed check stops attach; it never degrades quietly.
- **Conflicts are resolved empirically, not by guessing.** The sources disagree about where a
  unit's position lives, so the bot works it out from the running client and refuses to
  start if it cannot.
- **Unverified things say so and fail safe.** Three values are still documented assumptions.
  They are listed in [docs/offsets.md](docs/offsets.md), and the code that uses them returns
  nothing rather than something wrong.
- **Injected code is tested as code.** Every x86 instruction the bot generates has a golden
  test whose expected bytes came from `objdump`, and a small interpreter in the test suite
  executes the exact bytes that would be injected, to check the resulting program behaves as
  intended. Installing the hook is itself gated on a self-test that makes the client compute
  an answer the bot did not supply.

## Licensing

MIT. No code from any other bot, no Blizzard files, no proprietary data.

**Note on AmeisenBotX:** the project brief assumed it was MIT and suggested adapting it.
It is **GPL-3.0**, as is AmeisenNavigation, so neither can be linked or vendored into an
MIT-licensed program. Nothing has been copied from either; they were treated as references
for approach only. The details, and the policy that follows from them, are in
[docs/legal-and-licensing.md](docs/legal-and-licensing.md).

You must extract navigation data from your **own** game client. None ships here, and none
ever may.

## Layout

```
src/Common       Logging, configuration, geometry
src/Core         Offsets, memory, process attach, object manager, verification,
                 game-thread execution (x86 codegen, EndScene hook, Lua bridge)
src/GameApi      Typed model: WoWUnit, WoWPlayer, WoWGameObject
src/Navigation   Navigation mesh loading, pathfinding, movement, stuck handling
src/WorldData    The world map the bot learns by playing, and optional database seeding
src/Behavior     Behaviour tree engine
src/CombatRoutines  Rotation engine and per-specialisation routines
src/BotBases     Root behaviour tree and the grind base
src/Profiles     Profile model, loader and validator
src/Plugins      Plugin contract, loader and manager
src/Presentation View models and the bot base factory, testable off Windows
src/UI           WPF shell
tools/Inspector  Read-only console dev tool
tests/           Unit tests, including a simulated client
docs/            Setup, architecture, offsets, licensing, manual test scripts
profiles/        Profile format reference (no game data; ids are invented)
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). The one rule that matters: **never invent a
low-level fact.** If you do not know an offset, say so and mark it `Unverified`.

## Licence

[MIT](LICENSE).
