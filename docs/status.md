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

**Deciding.** A behaviour tree; seven bot bases (grind, gather, fish, questing, dungeon,
battleground, craft); thirty combat routines, one per specialisation; profiles with a validating
loader and a Honorbuddy importer; loot rules, gear evaluation and errand planning; a session
scheduler with randomised breaks; group play — following, assisting, role-aware behaviour,
group healing; plugins.

**Getting back in after a dropped connection.** No credentials, and none stored: a disconnection
leaves the client at character select with the last character still chosen, and entering the
world from there needs no secret. A client at the login screen needs a person, and the bot stops
and says so.

**Noticing when it is spoken to.** A frame inside the client listens for whispers; the bot
drains it every tick and stands the character still — or stops for good, if you would rather.
It never replies.

**Keeping what it learns.** Every vendor, mailbox and node the character walks past is written
down and read back next session, one map per realm and character.

**Using a world data export.** Where the herbs and vendors are, which creatures are elites,
which trainer teaches which class, who sells a given item, and what each quest actually asks
for. Everything that depends on it degrades to how the bot behaved before it existed, and the
status line says which of the two is in use.

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

What the suite *does* cover, and did not until recently, is the seams. Every defect found in
this project since the bot bases were written has been at one — an errand nothing could finish,
a setting the window collected and the composition root never passed, a trainer errand waiting
for a merchant's window. None of those is visible from inside any single component, so there is
now a set of tests that runs the real runner over the real root tree over a real bot base
against a real live state, with only the client and pathfinding faked.

### Six things the bot will not work out for itself

The window asks, keeps the answer, and never guesses:

| Setting | Why it is a setting |
| --- | --- |
| Group role | The client may know; this project has not verified how to ask, and a bot wrong about it tanks in cloth |
| Skinning | A character that walks to every corpse and fails to skin it is worse than one that never tries |
| Mail recipient | There is no way to infer who your bank alt is |
| Mesh folder | Extracting them puts them wherever the extractor ran; copying gigabytes next to the bot is a poor default |
| Talent build | This project has no talent data. Unspent points cost nothing; wrongly spent ones cost gold to undo |
| Mount name | Which mounts a character owns is in its spellbook, but which is fastest or usable here needs spell data this project does not ship |
| Whisper ignore list | Nobody but you knows which of your guildmates already know what the character is doing |

Settings live beside the executable and hold nothing that could log a character in. This project
makes no network calls, and storing an account password to be typed into a game client is a
promise it is not in a position to keep safely. The original brief for this project asked for
auto-login with credentials kept under Windows DPAPI; it was considered and declined, and what
was built instead is the half that needs no secret — see **Getting back in** above. That covers
the ordinary disconnection, which is what an unattended session actually loses time to.

### Hostility is exact with faction data, approximate without

`FactionTemplate.dbc`, extracted from your own client into a `dbc` folder, makes hostility
exact: both faction template ids come from the units' own descriptors, so no server database is
involved. Without it the bot excludes players and anything wearing NPC flags and treats the rest
as fair game, which will occasionally offer a neutral critter as a target. The log says which is
in use.

### The crafting base buys materials, but only the buyable kind

With `npc-vendors.tsv` exported, running out of materials sends the character to a shop instead
of stopping: the client says what the recipe takes and what the bags hold, the export says who
sells the difference, and the bot walks there, buys, and comes back to the anvil.

Most trade materials cannot be bought at all — ore, herbs, leather and cloth come off the world
— so stopping is still the ordinary outcome, and with no export it is the only outcome. What
this fixes is the narrower case of a session ending for want of a stack of thread.

### Training works; profession trainers do not

With world data exported the bot finds a trainer for the character's own class — the creature
table says which trainer teaches which class, which the trainer flag alone does not — walks
there, and learns everything on offer it can afford, keeping money back for the repair bill. No
spell data is involved: the trainer knows what this character's class, level and money allow,
and it is asked.

Profession trainers are a different matter. The bot can find one, but it does not know what a
profession trainer's window means: learning the next rank of a profession and buying a recipe
look the same from outside, and the Train errand is written for a class trainer. So hitting a
skill cap still stops a crafting session rather than fixing itself.

### Objective steps know what a quest wants, given the export

`quest-templates.tsv` says which creature a quest wants killed and how many. Without it, an
objective step the profile did not describe kills whatever is nearest and finishes by luck;
with it, the step is specific. A profile that names its own entries needs none of this, and what
the profile says always wins.

The client's own objective order is assumed to match the database's column order, which is what
makes a step saying `Index="2"` mean something. That is unverified — see the assumptions table
below — and an index past the end falls back to using every requirement, so being wrong costs
precision rather than correctness.

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
| `GetTradeSkillReagentInfo` returns needed and carried in that order | `LuaTradeSkills` | [phase 9](phase-9-manual-test.md) §2 |
| `BuyMerchantItem` counts individual items rather than purchases | `LuaVendor` | [phase 9](phase-9-manual-test.md) §4 |
| The client's objective order matches the database's column order | `QuestObjectives` | [phase 9](phase-9-manual-test.md) §6 |
| `GetTrainerServiceInfo`'s third return is the availability category | `LuaTrainer` | [phase 5](phase-5-manual-test.md) §5 |
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
| [phase 9](phase-9-manual-test.md) | professions, buying materials, quest objectives |

**Botting violates the terms of service of Blizzard's servers and the rules of most private
servers. Accounts get banned for it.** This project does not attempt to defeat server-side
anti-cheat.
