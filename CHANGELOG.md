# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
uses [semantic versioning](https://semver.org/spec/v2.0.0.html). While the major version is
zero, anything may change.

## [Unreleased]

### Added

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

### Missing

- **Evidence.** None of this has run against a real 3.3.5a client. See
  [docs/status.md](docs/status.md) for what that means in practice.
- A crafting bot base, and buying materials for one.

### Added

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
