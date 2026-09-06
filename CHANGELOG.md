# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
uses [semantic versioning](https://semver.org/spec/v2.0.0.html). While the major version is
zero, anything may change.

## [Unreleased]

### Missing

- The live `IBotState` adapter that would let the behaviour tree run against a real client. See
  [docs/status.md](docs/status.md).
- Phase 9: professions and the mixed-activity scheduler.

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
