# WoWBuddy

An open-source gameplay bot for **World of Warcraft 3.3.5a (build 12340)**, in the spirit of
the discontinued Honorbuddy. C# 12, .NET 8, WPF, Windows only. MIT licensed.

> **Botting violates the terms of service of Blizzard's servers and the rules of most private
> servers. Accounts get banned for it.** This project does not attempt to defeat server-side
> anti-cheat. Using it is entirely at your own risk.

## Status: phases 0 and 1 complete

The delivery plan runs to eleven phases. Two are done.

| Phase | State |
| --- | --- |
| 0 — Scaffold, CI, licensing, docs | **Done** |
| 1 — Attach and read: memory, offsets, object manager, typed objects, inspector | **Done** |
| 2 — Execute: game-thread hook, Lua, native calls | Next |
| 3-11 — Movement, combat, bot bases, profiles, plugins, release | Not started |

**What works today.** Find a running 12340 client, attach to it, verify the offset table
against that specific client, walk the object manager, and read the local player and every
unit, player, item, corpse and world object around it. Read-only: nothing is written to the
client and nothing is injected.

**What does not.** Anything that acts on the game. No movement, no casting, no Lua. That is
phase 2 onward.

## Try it

```
dotnet build WoWBuddy.sln -c Release

# with a character logged into the world:
dotnet run --project tools/WoWBuddy.Inspector -c Release -- inspect
```

See [docs/setup.md](docs/setup.md), then work through
[docs/phase-1-manual-test.md](docs/phase-1-manual-test.md) to confirm the offsets match your
client.

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
src/Core         Offsets, memory, process attach, object manager, verification
src/GameApi      Typed model: WoWUnit, WoWPlayer, WoWGameObject
src/UI           WPF shell
tools/Inspector  Read-only console dev tool
tests/           Unit tests, including a simulated client
docs/            Setup, architecture, offsets, licensing, manual test scripts
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). The one rule that matters: **never invent a
low-level fact.** If you do not know an offset, say so and mark it `Unverified`.

## Licence

[MIT](LICENSE).
