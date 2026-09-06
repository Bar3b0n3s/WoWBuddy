# Licensing, clean-room policy, and risk

## Summary

WoWBuddy is MIT licensed. It contains no code taken from any other bot, no Blizzard game
files, and no proprietary data. This document records the licence findings that shaped the
project and the rules that keep it that way.

## Finding: AmeisenBotX is GPL-3.0, not MIT

The project brief assumed AmeisenBotX was MIT licensed and suggested adapting its memory
layer, Lua execution and navigation server "with attribution". **That assumption is wrong.**
Both repositories are GPL-3.0:

| Project | Licence | Checked |
| --- | --- | --- |
| [`Jnnshschl/AmeisenBotX`](https://github.com/Jnnshschl/AmeisenBotX) | GNU GPL v3 | 2026-09-06, `LICENSE` at `master` |
| [`Jnnshschl/AmeisenNavigation`](https://github.com/Jnnshschl/AmeisenNavigation) | GNU GPL v3 | 2026-09-06, `LICENSE` at `master` |

GPL-3.0 is incompatible with shipping an MIT-licensed binary: linking or vendoring any part
of it would place the whole of WoWBuddy under the GPL. The brief anticipated this case and
specified the fallback, which is what the project follows:

> where it doesn't [permit], treat it as a reference for the *approach* only.

**Consequences.**

1. **No code is copied or adapted from AmeisenBotX.** Not the memory layer, not the object
   manager, not the Lua bridge. Everything in this repository was written for it.
2. **AmeisenNavigation cannot be vendored either.** It can still be used the same way the
   brief already permits for the map extractors: as a **separate external program** the user
   installs and runs themselves, which WoWBuddy talks to over localhost. Data crossing a
   process boundary is not a derivative work. **This turned out not to be needed.** Phase 3
   uses [DotRecast](https://github.com/ikpil/DotRecast), a zlib-licensed C# port of
   Recast/Detour, in-process. Its NuGet package carries no licence expression, so it was
   checked against `LICENSE.txt` in the repository: the zlib licence, with the original
   Recast copyright plus the porters'. That removes the separate navigation server entirely,
   along with the licence problem that made it awkward.
3. **Offset *values* are a different matter.** A memory offset is a measurement of someone
   else's binary. Facts are not copyrightable; the source code that records them is. So the
   values in `Offsets335a.cs` are treated as facts, gathered from multiple sources,
   cross-checked, and recorded with provenance in our own structure. No file layout, naming
   scheme, comment or algorithm was copied from any GPL source.

The same reasoning applies to the other bots surveyed. Every 3.3.5a bot found during this
work was GPL-3.0, unlicensed (and therefore all rights reserved), or under a bespoke
proprietary licence. None can be used as a code source.

## Dependency policy

Only MIT, BSD, Apache-2.0 and zlib licensed packages may be referenced. Current dependencies:

| Package | Licence |
| --- | --- |
| Serilog, Serilog.Sinks.Console, Serilog.Sinks.File | Apache-2.0 |
| DotRecast.Detour, DotRecast.Core | zlib |
| xunit, xunit.runner.visualstudio (test only) | Apache-2.0 |
| Microsoft.NET.Test.Sdk (test only) | MIT |

This is enforced by `build/check-licences.sh`, run in CI by
`.github/workflows/licence-check.yml`. It is an **allow-list**, not a deny-list: every
package in the graph, transitive ones included, must appear in `build/allowed-packages.txt`
with the licence it was checked under. A deny-list would only catch the copyleft packages
someone thought to name in advance; an allow-list forces every new dependency past a human
who has looked at its licence.

Run it yourself with `./build/check-licences.sh`.

## GPL tools used as external programs

These are **never linked or vendored**. The user installs and runs them; WoWBuddy consumes
their output or talks to them over a socket.

| Tool | Licence | Role |
| --- | --- | --- |
| TrinityCore / AzerothCore map extractors | GPL-2.0 | The user runs these against **their own** game client to produce `maps`, `vmaps` and `mmaps` navigation data. |
| AzerothCore / TrinityCore server | GPL-2.0 | Used as an integration-test target; as the reference for protocol facts such as descriptor field indices; and as the source of the world data (node and NPC locations) the user exports from their own installation. See [world-data.md](world-data.md). |

## What is never in this repository

- Blizzard game files, art, models, maps, DBC data, or MPQ archives.
- Extracted navigation data. Users generate it from their own client.
- World data exported from a server database. Users export it from their own installation.
- Any account credential. The optional auto-login feature stores credentials encrypted with
  Windows DPAPI, in the user's profile, never in the repository.

## Risk to the user

Automating World of Warcraft violates the terms of service of Blizzard's own servers and the
rules of most private servers. Accounts get banned for it. This project does not attempt to
defeat server-side anti-cheat, and using it is entirely at the user's own risk. The
humanization options exist because Honorbuddy had them, not because they make detection
unlikely.
