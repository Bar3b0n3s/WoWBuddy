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

It is read when the bot starts and written when it stops or detaches, and closing the window
counts as detaching. Entries not seen for a month are dropped on the way in: a node that has
not been there for that long is more likely to have been a mistake than to still be there.

The realm half of the name comes from the client, through the one Lua call this project makes
purely to name a file. A client that cannot answer leaves it as "unknown", and then two
characters with the same name on different servers would share a map — worth knowing if you
play the same name in two places.

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

## What the tables buy that memory cannot

Most of what the bot needs it reads out of the client. Five things it cannot:

**Whether a creature is an elite.** This is the big one. Rank is not in a unit's descriptors, so
from memory an elite looks exactly like an ordinary mob of the same level — the bot finds out by
pulling it and dying, and an unattended session that does that at two in the morning is over.
The database knows before the fight starts, and `creature_template.rank` is the whole answer.

**What a creature's level range is.** The client says what the unit in front of the character
is; the export says what that spawn point can roll. A level-eight boar with a level-nine sibling
behind it matters when the character is level nine.

**What an item is before you pick it up.** `GetItemInfo` answers only about items already in the
bags, one round trip at a time. The export answers about any item, which is what a loot decision
actually needs.

**Who sells a thing.** The client will list what the merchant in front of the character is
selling and nothing else, so without the export the only way to find out who stocks Weak Flux is
to walk into every shop. `npc-vendors.tsv` is indexed by the item rather than by the vendor,
because the question the bot asks is always "who has this" — see the crafting base's shopping
trip in [activities.md](activities.md).

**What a quest is actually asking for.** The client says how far along an objective is — "3/8" —
but the words beside it are in whatever language the client is in. The bot can see that
something is three eighths done and not what the something is. `quest-templates.tsv` says which
creature and how many, which turns an objective step the profile did not describe from "kill
whatever is nearby and hope" into something specific.

Without the export the bot still runs — the target filter rejects nothing it cannot look up,
because refusing everything unknown would stop a bot that had been working perfectly well. It
just plays worse, and the status line says so.

## Faction data comes from the client, not the database

Hostility is the one thing the database cannot answer. `creature_template.faction` gives a
faction *template id*; what that template attacks lives in `FactionTemplate.dbc`, which is
client data.

Extract it with the same tool that produces the navigation meshes and put it in a `dbc` folder
beside WoWBuddy. With it, hostility is exact — both faction template ids come straight from the
units' own descriptors, so no database is involved at all. Without it, the bot falls back to an
approximation that excludes players and anything wearing NPC flags, and will occasionally offer
a neutral critter as a target. The log says which of the two is in use at start-up.

The reader refuses a file whose column count is not the 3.3.5a one, because reading the wrong
columns would produce a hostility table that looks entirely reasonable and is wrong.

## What else the tables carry

| Export | What it unlocks |
| --- | --- |
| `creature-templates.tsv` | elites and bosses, level ranges, and which trainer teaches which class |
| `item-templates.tsv` | loot decisions about an item before it is picked up |
| `npc-vendors.tsv` | who sells a given item, for buying crafting materials |
| `quest-templates.tsv` | what a quest actually asks for, rather than log text in the client's language |

The Train errand needs the trainer columns: the trainer flag alone is on profession trainers,
mount vendors and pet trainers too, and none of them teaches a warrior how to hit things.

A profile that names the creature for every objective needs none of `quest-templates.tsv`, and a
character crafting from materials it gathers itself needs none of `npc-vendors.tsv`. Both exports
are there for the cases where nothing else can answer.

## Public dumps, for users without a server

The export scripts run against any TrinityCore or AzerothCore world database, including one
imported from the SQL dumps both projects publish with their releases. You do not need to run a
server: importing the dump into a local MySQL, running the export, and throwing the database
away is enough, and the resulting TSV files are a few megabytes.

Nothing derived from those dumps is committed here, for the same reason no navigation mesh is:
it is Blizzard's data, and TrinityCore and AzerothCore are GPL-licensed besides.
