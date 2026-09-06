# WoWBuddy

An open-source gameplay bot for **World of Warcraft 3.3.5a (build 12340)**, in the spirit of
the discontinued Honorbuddy. C# 12, .NET 8, WPF, Windows only. MIT licensed.

> **Botting violates the terms of service of Blizzard's servers and the rules of most private
> servers. Accounts get banned for it.** This project does not attempt to defeat server-side
> anti-cheat. Using it is entirely at your own risk.

## Status: phases 0, 1 and 2 complete

The delivery plan runs to eleven phases. Three are done.

| Phase | State |
| --- | --- |
| 0 — Scaffold, CI, licensing, docs | **Done** |
| 1 — Attach and read: memory, offsets, object manager, typed objects, inspector | **Done** |
| 2 — Execute: game-thread hook, Lua bridge, native calls, Lua console | **Done** |
| 3 — Move: navigation meshes, path following, stuck handling | **Done** |
| 4 — Fight: behaviour trees, four combat routines, grind bot base | **Done** |
| 5 — Live: loot, gear, errand planning, scheduler, humanization | **Done** |
| 6 — Gather and Fish bases, world data from your server database | **Done** |
| 7 — Questing base, profile schema and importer | Next |
| 8-11 — Dungeons, battlegrounds, professions, plugins, release | Not started |

**What works today.** Find a running 12340 client, attach, verify the offset table against
that specific client, and walk the object manager to read the local player and everything
around it. Then, optionally, install a hook on the client's render loop and run code on its
own thread: execute Lua and read values back, and call the client's native functions.

It can also load the navigation meshes you extract from your own client — from **either
TrinityCore or AzerothCore**, whose formats differ — find paths through them, and walk the
character along one with stuck detection and recovery.

On top of that sits a behaviour tree, four combat routines (Fury Warrior, Frost Mage, Holy
Priest, Beast Mastery Hunter) and a grind bot base: pick a target, close, pull, fight, loot,
rest, and recover from dying. A scheduler runs sessions with randomised breaks and stop
conditions, and loot rules, gear evaluation and errand planning decide what to keep, what to
wear and when to go to town.

**What does not.** No *native* cast function is used: the two published addresses for it
disagree and neither has been confirmed. Casting goes through the client's own script entry
point instead, which existing bots for this build rely on and which the bot verifies against
your client before using — it asks whether its scripts run in a secure context and stays
disabled if the answer is no.

**Reading is separate from acting.** Attaching is read-only and cannot destabilise the
client. Running code inside it is a second, explicit step (`EnableExecution`), so a user who
only wants to inspect the object manager never has anything injected into their game.

## Try it

```
dotnet build WoWBuddy.sln -c Release

# with a character logged into the world:
dotnet run --project tools/WoWBuddy.Inspector -c Release -- inspect
```

See [docs/setup.md](docs/setup.md), then work through
[docs/phase-1-manual-test.md](docs/phase-1-manual-test.md) to confirm the offsets match your
client, and [docs/phase-2-manual-test.md](docs/phase-2-manual-test.md) before letting
anything run inside it. For movement, [docs/navigation-data.md](docs/navigation-data.md)
covers extracting navigation data and [docs/phase-3-manual-test.md](docs/phase-3-manual-test.md)
covers confirming click-to-move before anything writes to it,
[docs/phase-4-manual-test.md](docs/phase-4-manual-test.md) covers combat, and
[docs/phase-5-manual-test.md](docs/phase-5-manual-test.md) covers looting, gear and the
scheduler. [docs/world-data.md](docs/world-data.md) covers how the bot learns where things are, and
[docs/profiles.md](docs/profiles.md) covers the profile format the questing base reads,
with [docs/phase-7-manual-test.md](docs/phase-7-manual-test.md) for questing.
[docs/group-play.md](docs/group-play.md) covers parties, following, assisting and healing.

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
