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
   process boundary is not a derivative work. This decision is deferred to phase 3, where the
   alternative is a permissively licensed Recast/Detour server of our own.
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
| xunit, xunit.runner.visualstudio (test only) | Apache-2.0 |
| Microsoft.NET.Test.Sdk (test only) | MIT |

`.github/workflows/licence-check.yml` fails the build if a denied package enters the
dependency graph.

## GPL tools used as external programs

These are **never linked or vendored**. The user installs and runs them; WoWBuddy consumes
their output or talks to them over a socket.

| Tool | Licence | Role |
| --- | --- | --- |
| TrinityCore / AzerothCore map extractors | GPL-2.0 | The user runs these against **their own** game client to produce `maps`, `vmaps` and `mmaps` navigation data. |
| AzerothCore / TrinityCore server | GPL-2.0 | Used as an integration-test target, and as the reference for protocol facts such as descriptor field indices. |

## What is never in this repository

- Blizzard game files, art, models, maps, DBC data, or MPQ archives.
- Extracted navigation data. Users generate it from their own client.
- Any account credential. The optional auto-login feature stores credentials encrypted with
  Windows DPAPI, in the user's profile, never in the repository.

## Risk to the user

Automating World of Warcraft violates the terms of service of Blizzard's own servers and the
rules of most private servers. Accounts get banned for it. This project does not attempt to
defeat server-side anti-cheat, and using it is entirely at the user's own risk. The
humanization options exist because Honorbuddy had them, not because they make detection
unlikely.
