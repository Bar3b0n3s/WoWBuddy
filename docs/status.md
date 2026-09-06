# Status

What works, what does not, and every assumption still outstanding. This page is the one to
believe; anything cheerier elsewhere is out of date.

## In one sentence

WoWBuddy can read a 3.3.5a client thoroughly and run code inside it safely, and everything that
decides what a bot should do is written and tested — but nothing yet joins those two halves, so
it cannot play unattended.

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

### The live state adapter is missing

Every bot base and combat routine is written against `IBotState` and tested against a fake. Four
adapters that read a real client now exist:

| Adapter | Reads |
| --- | --- |
| `LuaQuestLog` | the quest log, its objectives, and accepting and handing in |
| `LuaPartyState` | group membership, health, targets; positions from the object manager |
| `LuaBattlegrounds` | the queue, the match state, joining and leaving |
| `LuaInventory` | bag space, gear wear, money, item counts, using an item |

What does not exist is the class that composes those with the memory-derived state — position,
health, target, nearby units — and drives the behaviour tree on a timer.

**The consequence is visible in the UI.** Start validates your choices, builds the tree, catches
a profile that does not give its bot base what it needs, and then says plainly that it cannot
play it. That is deliberate. A start button that ticked a tree fed on invented values would look
like progress and be worth less than nothing.

It also means `IPlugin.Pulse` is a contract nothing calls yet. A plugin that ships a combat
routine works today; one that does per-tick work does not.

### Phase 9 was skipped

Professions and the mixed-activity scheduler — a character that mines while questing, or
alternates gathering and grinding on a schedule — were never built. The session scheduler
handles time; it does not handle mixing activities.

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

## What has never been run against a live client

All of it. Every manual test script in `docs/` is written and none has been executed — there has
been no Windows machine with a 3.3.5a client in this project's history. The unit tests are
thorough (a simulated client, an x86 interpreter that executes the real injected bytes, both
navigation mesh formats built byte-for-byte) but they test the bot against this project's own
understanding of the client, which is exactly the thing that could be wrong.

Treat the first run as an experiment, on an account you do not care about.
