# World data

The bot needs to know where things are: where herb nodes spawn, where vendors and repair NPCs
are. There are two ways it gets that, and **the first needs nothing from you at all**.

## The bot learns the world by playing in it

Every vendor the character walks past, every mailbox, every node it gathers is written down.
The next time an errand comes due it already knows where to go. A first lap of a zone is slow
and blind; by the third the bot knows the route, which is roughly how a person learns one.

The map lives in your settings folder, one per realm and character — a private server's world
is not necessarily the same as another's, and sharing one file would teach the bot to walk to
places that are not there. It is a plain JSON file you can read, edit or delete.

Nothing on this page is required. Everything below is an optional shortcut for the minority
of users who happen to have a server database.

## Seeding from a server database, if you have one

**Most people do not.** If you are botting on somebody else's realm you have no access to its
database, and the bot is built on that assumption. But if you run your own server, or a local
one for testing, you can hand the bot everything at once instead of waiting for it to learn.

**No data ships with this project and none ever can.** The spawn tables derive from Blizzard's
game files, and the databases that hold them are distributed under the GPL by TrinityCore and
AzerothCore — the same licence that stopped this project vendoring a navigation library. So
the export comes from your installation, the same posture as
[navigation meshes](navigation-data.md).

## TrinityCore and AzerothCore both work, with different scripts

The two schemas are almost identical, and the one place they differ matters:

| | TrinityCore | AzerothCore |
| --- | --- | --- |
| `creature` template column | `id` | `id1` (with `id2`, `id3` for alternatives) |
| `gameobject` template column | `id` | `id` |

AzerothCore lets one spawn point rotate between several creatures, so it needs three columns
where TrinityCore needs one. Running the wrong script fails with "unknown column" rather than
producing wrong data, which is the failure mode to prefer.

- TrinityCore: `tools/worlddata/export-trinitycore.sql`
- AzerothCore: `tools/worlddata/export-azerothcore.sql`

## Exporting

Run each statement separately so each file gets its own header line. From the repository root,
with your server's database name in place of `world`:

```bash
mkdir -p worlddata

mysql -u USER -p -N -B world -e "
  SELECT 'guid','entry','map','x','y','z'
  UNION ALL SELECT guid, id, map, position_x, position_y, position_z FROM gameobject
" > worlddata/gameobject-spawns.tsv
```

…and the same for the other three statements in the script, writing to
`gameobject-templates.tsv`, `creature-spawns.tsv` and `creature-templates.tsv`.

`-N` suppresses the column names mysql would otherwise add (the `SELECT 'guid',...` row is
the header the reader wants), and `-B` makes the output tab-separated.

The creature template export is filtered to creatures offering a service the bot can use,
which is a small fraction of the table. The rest is exported whole: a full `gameobject` table
is a few hundred thousand rows and around 20 MB.

## Checking it

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- worlddata ./worlddata
```

Needs no game client. It reports how much loaded and lets you search for node types by name,
which is how you find the entry ids to gather.

## Finding node ids

**The bot ships no list of herb or ore ids.** Inventing one would be exactly the kind of
unverified fact this project refuses, and ids differ between databases anyway. Search your own:

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- worlddata ./worlddata --find "vein"
dotnet run --project tools/WoWBuddy.Inspector -c Release -- worlddata ./worlddata --find "peacebloom"
```

Names come from your database, so this works as well as that database's language does.

## Why a file rather than connecting to MySQL

Two reasons. This project promises to make no network calls, and a database connection is one
even to localhost. And an export is more useful: it works when the server is not running, it
moves between machines, and it can be read in a text editor when something looks wrong.

## What actually reads a node's position

Neither the learned map nor a database export removes the need to read a game object's
position from the client: the bot has to see that a node is *there* before it gathers it, and
a spawn point only says one appears there sometimes.

No source consulted for this project states where a 12340 game object keeps its coordinates,
so the offset is worked out against the running client at attach — see
`GameObjectPositionResolver`. When that fails, gathering says so instead of walking to
fabricated coordinates, and the attach report records it as a warning.

## Licensing

The export comes from your database, which you installed to run your server. Nothing from it
is committed here. TrinityCore and AzerothCore are GPL projects used as external tools whose
output the bot consumes — the same arrangement as the map extractors, and the same one set
out in [legal-and-licensing.md](legal-and-licensing.md).
