# World data from your server database

The bot needs to know where things are: where herb nodes spawn, where vendors and repair
NPCs are. All of that already exists in the database of any 3.3.5 server, which is far better
than having the bot wander until it stumbles across things.

**No data ships with this project and none ever can** — it derives from Blizzard's game files.
You export it from your own database, the same posture as
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

## What this unblocks

| | Before | Now |
| --- | --- | --- |
| Gathering | Blocked: game object positions could not be read from client memory and were not guessed | Node positions come from the database |
| Vendor, repair and trainer errands | The bot knew *that* it needed one but not *where* | Nearest service NPC by map and distance |

The object manager is still needed for one thing gathering cannot do without: whether a node
is actually up right now. A spawn point says a node appears there, not that it has respawned
since the last person emptied it.

## Licensing

The export comes from your database, which you installed to run your server. Nothing from it
is committed here. TrinityCore and AzerothCore are GPL projects used as external tools whose
output the bot consumes — the same arrangement as the map extractors, and the same one set
out in [legal-and-licensing.md](legal-and-licensing.md).
