# Status

What works, what does not, and every assumption still outstanding. This page is the one to
believe; anything cheerier elsewhere is out of date.

## In one sentence

WoWBuddy is complete end to end — it attaches, verifies itself against your client, reads the
world, decides what to do and ticks a behaviour tree that acts on it — and not one line of that
has ever run against a real 3.3.5a client.

## What works today

**Reading a client.** Finding a running 12340 process, verifying the offset table against that
specific client, walking the object manager, and reading typed objects: the local player, units,
players, game objects, items, corpses. Attach refuses rather than degrading if verification
fails.

**Running code inside it.** A hook on the client's render loop, an x86 code generator whose
every instruction has a golden test taken from `objdump`, a Lua bridge that proves the whole
round trip by making the client compute a value the bot did not supply, and native function
calls. Installing the hook is a separate, explicit step from attaching.

**Navigation.** Loading meshes extracted from your own client, in either the TrinityCore or the
AzerothCore format, pathfinding through them, smoothing, following a path, and detecting and
recovering from being stuck.

**Deciding.** A behaviour tree; six bot bases (grind, gather, fish, questing, dungeon,
battleground); thirty combat routines, one per specialisation; profiles with a validating
loader and a Honorbuddy importer; loot rules, gear evaluation and errand planning; a session
scheduler with randomised breaks; group play — following, assisting, role-aware behaviour,
group healing; plugins.

**The console tool.** `WoWBuddy.Inspector` exposes the reading and execution layers directly:
`list`, `inspect`, `watch`, `offsets`, `exec`, `lua`, `ctm`, `nav`, `worlddata`. It is the right
place to start, and the only thing in the project that is useful today without further work.

## What does not work

### None of it has run against a real client

**That is now the whole of the gap.** Every piece exists and is wired: attaching, verifying the
offsets, installing the hook, proving Lua, probing what the client supports, reading the world
into `IBotState`, building a tree from a bot base and a profile, and ticking it four times a
second with the character's movement advanced first.

What has never happened is any of it running against a 3.3.5a client, because this project has
never had access to one. The unit tests — 808 of them — check the bot against this project's own
understanding of the client, which is exactly the thing that could be wrong.

Specifically unproven:

| Piece | Rests on |
| --- | --- |
| `WorldCharacterView` | the Lua verbs `RepopMe`, `RetrieveCorpse`, `InteractUnit`, `LootSlot`, `SitStandOrDescendStart` |
| `LiveCombatContext` | `GetSpellCooldown`, `IsUsableSpell`, `UnitAura`, `CastSpellByName` with a unit id |
| `LuaQuestLog` | `GetQuestLogTitle`'s seventh return, and `GetQuestLink`'s hyperlink format |
| `LuaBattlegrounds` | the queueing indices, and `GetBattlefieldInstanceRunTime` as a started signal |
| Hostility | an approximation with no faction data behind it at all |

**Run the manual test scripts.** They exist for this, they are ordered so each one only needs
what the last one proved, and reporting what you saw is the most useful thing anyone can do for
this project right now.

### Four things the bot will not work out for itself

The window asks, keeps the answer, and never guesses:

| Setting | Why it is a setting |
| --- | --- |
| Group role | The client may know; this project has not verified how to ask, and a bot wrong about it tanks in cloth |
| Skinning | A character that walks to every corpse and fails to skin it is worse than one that never tries |
| Mail recipient | There is no way to infer who your bank alt is |
| Mesh folder | Extracting them puts them wherever the extractor ran; copying gigabytes next to the bot is a poor default |
| Talent build | This project has no talent data. Unspent points cost nothing; wrongly spent ones cost gold to undo |
| Mount name | Which mounts a character owns is in its spellbook, but which is fastest or usable here needs spell data this project does not ship |

Settings live beside the executable and hold nothing that could log a character in. This project
makes no network calls, and storing an account password to be typed into a game client is a
promise it is not in a position to keep safely.

### Hostility is guessed at

Whether a creature is an enemy needs its faction template, and resolving one needs a data file
this project does not ship. `HostilityRule` excludes players, anything wearing NPC flags, and
unselectable or pacified units, then treats the rest as fair game. It will offer a neutral
critter as a target. A profile's avoid list is the intended cover, and a user with faction data
can supply a better rule.

### Errands need a profile that names vendors, and cannot train

`ErrandHandler` walks to a vendor, repairs, sells the junk the loot rules pick, and posts
keepable items to another character. It needs somewhere to go, and vendor locations are game
data, so **errands only run when the loaded profile has a `Vendors` section**. Without one the
branch stays off — beginning an errand nothing can finish is worse than not starting one, and
that is exactly what used to happen.

**Training is not implemented.** It needs a trainer for the character's own class, which a
profile has no way to express and this project has no data for. The planner can ask for it; the
handler reports that it cannot and abandons the errand.

### Nothing buys materials, and nothing visits a trainer

The crafting base makes what the character is carrying and then stops. Working out what a recipe
needs takes item data this project does not ship. Likewise the `Train` errand: it needs a
trainer for the character's own class, which a profile cannot express.

## Outstanding assumptions

These are facts this project has *not* confirmed against a real client. Each is marked in the
code, and each has a manual test script section that checks it.

| Assumption | Where | Checked by |
| --- | --- | --- |
| `GetQuestLogTitle` returns completion in its seventh return value | `LuaQuestLog` | [phase 7](phase-7-manual-test.md) §3 |
| `GetQuestLink` yields a hyperlink containing `quest:<id>` | `LuaQuestLog` | [phase 7](phase-7-manual-test.md) §3 |
| `RequestBattlegroundInstanceInfo` and `JoinBattlefield` take the indices assumed | `LuaBattlegrounds` | [phase 8](phase-8-manual-test.md) §7 |
| `GetBattlefieldInstanceRunTime` above zero means the gates are open | `LuaBattlegrounds` | [phase 8](phase-8-manual-test.md) §7 |
| Whether `UnitGroupRolesAssigned` exists on 12340 at all | capability probe | [phase 8](phase-8-manual-test.md) §2 |
| Three offsets remain documented assumptions rather than verified | [offsets.md](offsets.md) | [phase 1](phase-1-manual-test.md) |

**The bot checks what it can, at attach time.** When execution is enabled it asks the client
which of the scripting calls it needs actually exist, and reports what is missing and what
breaks without it. That narrows the surface; it does not close it. Existence is not behaviour —
a call can be present and return something other than what this bot expects, and only a person
with a character logged in can tell.

**Running a manual test script and reporting what you saw is the single most useful thing anyone
can contribute right now.**

## Before you run it

Treat the first run as an experiment, on an account you do not care about, and work through the
manual test scripts in order rather than pressing Start. They are written so each only needs
what the previous one established:

| Script | Establishes |
| --- | --- |
| [phase 1](phase-1-manual-test.md) | the offset table matches your client |
| [phase 2](phase-2-manual-test.md) | code can run inside it, and Lua answers |
| [phase 3](phase-3-manual-test.md) | click-to-move and pathing |
| [phase 4](phase-4-manual-test.md) | casting and combat |
| [phase 5](phase-5-manual-test.md) | looting, gear, the scheduler |
| [phase 7](phase-7-manual-test.md) | the quest log assumptions |
| [phase 8](phase-8-manual-test.md) | group play, and the battleground queue |

**Botting violates the terms of service of Blizzard's servers and the rules of most private
servers. Accounts get banned for it.** This project does not attempt to defeat server-side
anti-cheat.
