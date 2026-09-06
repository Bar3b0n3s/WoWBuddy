# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
uses [semantic versioning](https://semver.org/spec/v2.0.0.html). While the major version is
zero, anything may change.

## [Unreleased]

### Added

- **The crafting base buys its materials.** Running out no longer ends the session: the client
  says what a recipe takes and what the bags hold, `npc-vendors.tsv` says who sells the
  difference, and `SupplyRun` walks there, buys everything on its list that shop stocks, and
  comes back to the anvil. It stocks up for several batches, keeps money back for repairs, buys
  what the money allows rather than nothing, and gives up after two trips that do not help.
  Most trade materials cannot be bought at all, so stopping with a reason is still the ordinary
  outcome — and the only one without an export.
- **Objective steps know what the quest wants.** With `quest-templates.tsv` exported, an
  `Objective` step that names no creature works from the quest's own requirements instead of
  killing whatever is nearest, and a `Collect` step can be finished by the bags without reading
  log text in a language the bot cannot parse. What the profile says always wins.
- **The bot stops when a person speaks to it.** A small frame inside the client listens for
  whispers and the bot drains it every tick, standing the character still for ten minutes by
  default — or stopping for good, or doing nothing, as you choose — with an ignore list so a
  guild's ordinary chatter does not end every session. It never replies: a bot that says "hi" to
  a game master has still shown exactly what it is. This is the failure an unattended session
  can least afford, and it was the last thing on the list that could ruin an account rather than
  an evening.
- **Learning abilities from a trainer.** `ITrainerActions` and `LuaTrainer` read what a trainer
  will teach — filtered to what is actually available, so an index cannot point at something the
  character already knows — and learn it. No spell data ships: the trainer already knows what
  this character's class, level and money allow.
- **Three more capability probes.** `Reagents`, `Buying` and `Trainer`, listing exactly the
  calls the new scripts make, so a client without them switches those features off rather than
  failing four hours in.
- **[docs/phase-9-manual-test.md](docs/phase-9-manual-test.md)** — professions, reagents, a
  merchant's shelves, buying, and a shopping trip end to end.
- **End-to-end tests.** The real runner over the real root tree over a real bot base against a
  real live state, with only the client and pathfinding faked. Every defect found in this
  project since the bot bases were written has been at a seam, and none of them was visible from
  inside a single component.
- **Mixing activities within a session.** A plan is a priority list of activities with
  conditions and time limits; a minimum dwell and boundary-aligned pre-emption stop a condition
  sitting on its threshold from swapping activities several times a second.
- **Opportunistic gathering.** Picking up a node passed on the way, bounded by a short detour
  range, a cap on consecutive nodes, a rest, a per-node cooldown, and never in combat.
- **Trade skills.** Reading what a character can make from the client's own window — no recipe
  data ships — and choosing the highest-colour recipe there are materials for.

- **The live adapter.** `LiveBotState` implements `IBotState` against a real client,
  `WorldCharacterView` reads the verified half from the object manager, `LiveCombatContext`
  gives a rotation what it needs, and `BotRunner` ticks the tree with movement advanced first.
  The Start button composes all of it.

### Fixed

- **The Train errand trained nothing, and would not stop trying.** It walked to a class trainer
  and then waited for a *merchant* window, which was never going to open, so it clicked for ten
  seconds and gave up — and because nothing recorded the attempt, the planner decided the same
  trip was due again on the next tick. A character past the training interval would have walked
  to the trainer for the rest of the session instead of playing. It now opens the trainer's own
  window, learns everything on offer it can afford in the trainer's own order, keeps money back
  for the repair bill, and records the trip whether it worked or not.
- **Three things the window collected and the bot never received.** The composition root built
  every bot base without the chosen group role, without the learned world map, and without the
  behaviours plugins offer — so the Dungeon base refused to start however the role was set,
  gathering relearned the same nodes every session, and a profile's `CustomBehavior` steps were
  all reported missing. Plugins were loaded, listed in the window and never given a tick either.
  All four are now passed, and the factory's handling of each is pinned by a test that ticks the
  tree it returns.
- **The learned map is actually saved.** `WorldMemory` could load and save itself and nothing
  called either, so a session's learning died with the process. It is now read on start and
  written on stop, detach, or closing the window — one file per realm and character, in the
  settings folder, with entries older than a month dropped on the way in.

### Changed

- `IVendorActions` gained `MerchantStock` and `Buy`; `ITradeSkills` gained `ReagentsFor`.
- `BotController` takes the plugin manager and the config store. It is the composition root, and
  the things it failed to pass on were all things it was never given.
- One more capability probe, `Realm`, for the single call used to tell one server's world from
  another's when naming the map file.
- The walk-to-a-vendor-and-open-its-window step is now `VendorApproach`, shared by the errand
  handler and the supply run rather than written twice.

### Missing

- **Evidence.** None of this has run against a real 3.3.5a client. See
  [docs/status.md](docs/status.md) for what that means in practice.
- Taking a character to a profession trainer when a skill hits its cap.

### Added

- **Exact hostility, from the client's own faction data.** A DBC reader and
  `FactionTemplate.dbc` replace the approximation entirely. Extract it into a `dbc` folder; the
  log says which is in use.
- **The Train errand works.** The creature export now carries trainer type and class, which the
  trainer flag alone does not distinguish.
- **Vendor inventories and quest objectives** are in the export.
- **The bot can tell an elite from an ordinary mob.** The world data export now carries
  `creature_template`'s rank, faction and level range, and the grind base leaves elites, bosses
  and anything far above the character alone. Rank is not in a unit's descriptors, so without
  the export the bot found this out by dying.
- **Item templates in the export**, so a loot decision can be made about an item before it is
  picked up rather than after.
- **Raid groups.** Read from the raid unit ids when there are any, with the character skipped
  because raid units include it and party units do not.
- **Mounts.** The character gets on for a long journey and off for combat, looting, skinning
  or arrival. Name the mount; blank walks everywhere.
- **Talent points are spent from a build you write.** `1:3, 1:3, 2:5` — one point per tick, out
  of combat, in the order given, stopping the moment the client refuses one. No build means no
  spending, which costs nothing.
- **The window remembers what you chose.** Bot base, routine, profile, group role, skinning,
  mail recipient and where the navigation meshes are. Nothing that could log a character in.
- **Skinning.** A corpse becomes skinnable once looted, so the root tree works looting and
  skinning in turn and the character makes one trip to each body.
- **A crafting bot base.** Opens a profession, makes whatever raises the skill fastest or a
  named recipe, and stops with a named reason rather than in silence.
- **Errands are now carried out, not just decided.** `ErrandHandler` walks to a vendor from the
  profile's `Vendors` section, repairs, sells what the loot rules pick, and posts keepable items
  to another character. `LuaVendor` does the client half.

### Fixed

- **Errands no longer deadlock the bot.** An errand began, the branch above the bot base
  reported it was still running, and nothing anywhere ever cleared it — so the character stood
  still permanently the first time its bags filled or its gear wore down. `IBotState` gained
  `EndErrand`, the errand branch now requires a handler that carries the errand out, and every
  path through it ends the errand and hands control back.
- **The bot no longer walks to town with nothing to sell.** The "has anything worth selling"
  question was hardcoded to yes, making the check in `ErrandPlanner` dead code. `IBotState`
  gained `HasSellableItems`, answered from the bags.
- A flaky logging test. The logger is process-wide static state and the test tore it down while
  other tests in the same assembly were writing to it.

## [0.9.0] — 2026-09-06

First packaged release. **A preview: the bot can read a client and decide what to do, but
cannot yet play unattended.** See [docs/status.md](docs/status.md) for exactly what that means.

### Added

- **Reading a client.** Process discovery, build detection, an offset table where every address
  records its source and confidence, verification of that table against the attached client, the
  object manager, and typed objects.
- **Running code inside it.** An x86 code generator with `objdump`-verified output, a render-loop
  hook, a game-thread executor, a Lua bridge gated on a self-test that makes the client compute
  a value the bot did not supply, and native function calls.
- **Navigation.** Mesh loading for both the TrinityCore and AzerothCore formats, pathfinding,
  path smoothing, movement, and stuck detection and recovery.
- **Deciding.** A behaviour tree; grind, gather, fish, questing, dungeon and battleground bases;
  thirty combat routines, one per specialisation; loot rules, gear evaluation and errand
  planning; a session scheduler with randomised breaks.
- **Profiles.** A native XML format with a validating loader that reports every problem at load
  rather than at run time, and a best-effort Honorbuddy importer that reports what it could not
  translate.
- **Group play.** Party reading, following with hysteresis, assist targeting, role-aware dungeon
  behaviour, group healing, and a battleground base.
- **Plugins.** A contract for contributing combat routines, named behaviours and per-tick work,
  with faults isolated so a broken plugin costs its own feature and not the session.
- **The Inspector.** A read-only console tool exposing the reading and execution layers.
- **Capability probing.** At execution time the bot asks the client which scripting calls it
  has, and reports what is missing and what breaks without it.
- **Live adapters** for the quest log, party, battleground queue and bags — the pieces the
  missing state adapter will compose.

### Known limitations

- No manual test script has ever been run against a live client.
- Several Lua behaviours are assumed rather than confirmed; they are listed in
  [docs/status.md](docs/status.md).
- Three offsets remain documented assumptions; the code using them returns nothing rather than
  something wrong.
