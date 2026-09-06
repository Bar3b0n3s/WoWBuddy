# Navigation data

The bot needs a navigation mesh to path anywhere. **None ships with this project and none
ever can**: it is derived from Blizzard's game files. You generate it from your own client
using an extractor from a server project, and the bot reads what you generated.

## TrinityCore and AzerothCore are both supported — but do not mix them

Both projects ship an `mmaps_generator`, and both write a `mmaps/` folder that looks
identical from the outside. The files are not interchangeable:

| | TrinityCore (3.3.5 branch) | AzerothCore |
| --- | --- | --- |
| Generator version | 15 | 20 |
| Tile header size | 20 bytes | 56 bytes |
| Extra header content | — | 36-byte record of the Recast settings used |

The extra 36 bytes sit between the header and the navigation mesh. A reader that assumes the
wrong one takes Recast configuration as the start of the mesh, so getting this wrong does not
produce an error — it produces nonsense.

WoWBuddy detects which one you have from the version field and reads the mesh from the right
place, so **either works**. What it will not do is load a folder containing both: that means
two partial extractions were overwritten together, the halves were built with different
settings, and the resulting mesh would have silent holes in it. If you see

```
... was written by the AzerothCore extractor but earlier tiles for this map came from
TrinityCore. Navigation data from the two cannot be mixed
```

then delete the `mmaps` folder and extract it once, with one extractor.

## Generating the data

You need the extractor binaries from **either** project, and your own WoW 3.3.5a client. The
extractors are GPL-licensed programs run as separate tools; WoWBuddy consumes their output
and contains no code from them. See [legal-and-licensing.md](legal-and-licensing.md).

1. Build (or download) `mapextractor`, `vmap4extractor`, `vmap4assembler` and
   `mmaps_generator` from the project you are using.
2. Copy them into your WoW folder — the one with `Wow.exe` and `Data/`.
3. Run them in order. `mmaps_generator` is the slow one; expect hours and several gigabytes.
   ```
   mapextractor
   vmap4extractor
   vmap4assembler Buildings vmaps
   mmaps_generator
   ```
4. You now have `maps/`, `vmaps/` and `mmaps/`. **The bot only needs `mmaps/`.**

To extract a single continent while testing, `mmaps_generator` takes a map id: `0` Eastern
Kingdoms, `1` Kalimdor, `530` Outland, `571` Northrend.

## Checking what you produced

Before pointing the bot at it:

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- nav "C:\Games\WoW\mmaps"
```

This needs no game client. Expect something like:

```
  [ok    ] map   0 Eastern Kingdoms    4096 tiles, TrinityCore data
  [ok    ] map   1 Kalimdor            3970 tiles, TrinityCore data
  [FAILED] map 530 Outland             unusable
             530.mmap is missing from C:\Games\WoW\mmaps.
```

| Message | Meaning |
| --- | --- |
| `NNN.mmap is missing` | That continent was not extracted. Re-run `mmaps_generator` for it. |
| `no tiles were extracted` | The extraction started and stopped. Re-run it. |
| `is shorter than its own header` / `claims more tile data than the file contains` | Truncated file, usually from an interrupted run. Delete it and re-extract. |
| `written by an extractor this bot does not recognise` | A generator version other than 15 or 20. Probably a much newer AzerothCore or a different expansion's tool. |
| `declares a Detour tile version other than 7` | Data built for a different expansion. |
| `cannot be mixed` | Both extractors' output in one folder. Delete and re-extract with one. |
| `too many to be incidental` | More than a handful of unreadable tiles: the extraction is broken, not merely incomplete. |

A few rejected tiles among thousands is survivable — those squares simply cannot be pathed
through. Dozens means something is wrong with the data.

## What the bot does with it

Paths are found in-process using [DotRecast](https://github.com/ikpil/DotRecast), a zlib-licensed
C# port of Recast/Detour. There is no separate navigation server to install or keep running.

The usual reason bots use a separate server is that Detour is C++ while the bot is C#; a
permissively licensed C# port removes that reason, and with it a second executable, a socket
protocol, and a process that can die independently of the bot. The obvious alternative,
AmeisenNavigation, is GPL-3.0 and could not be linked into an MIT-licensed program at all.

## Coordinates

Worth knowing if you are reading the code. The game uses X north, Y west, Z up. Recast assumes
Y is up, so the extractor stores positions as `(worldY, worldZ, worldX)` and the bot swaps
them back on the way in and out. That conversion lives in `DetourCoordinates` and has its own
tests, because a swapped path is a perfectly well-formed path to the wrong place.
