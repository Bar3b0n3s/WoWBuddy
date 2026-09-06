# Contributing

## The rule that matters

**Never invent a low-level fact.**

Memory offsets, function addresses, descriptor indices and structure layouts are claims about
a binary none of us wrote. A wrong one does not throw — it returns a plausible number that
the bot acts on, and the resulting bug surfaces hours later as a character stuck in a wall.

So, when adding anything to `Offsets335a.cs` or `UpdateFields335a.cs`:

1. **Record where it came from.** Every constant needs an `[OffsetInfo]` attribute with a
   confidence level, a source, and a `HowToVerify`. A test fails the build without one.
2. **If you do not know, say so.** `OffsetConfidence.Unverified` plus a `TODO: verify` note
   describing how to find it is a perfectly good contribution. A guess dressed as a fact is
   not.
3. **Make wrong values fail safe.** Code using an unverified offset must degrade to doing
   nothing, not to doing something wrong. The name cache is the model: it can only return a
   name whose GUID it has already matched, so a bad structure offset yields *no* name rather
   than the wrong one.
4. **Prefer resolving to guessing.** Where sources conflict, add candidates and a runtime
   resolver that identifies the right one against the live client, as `PositionResolver`
   does. Refusing to attach beats acting on a coin flip.

## Licensing rules

- The project is MIT. Only MIT, BSD, Apache-2.0 and zlib dependencies may be linked.
- **Do not copy code from other bots.** Every 3.3.5a bot surveyed is GPL-3.0, unlicensed
  (all rights reserved), or proprietary. Reading one to learn a factual offset value is fine;
  copying its code, structure, naming or comments is not. See
  [docs/legal-and-licensing.md](docs/legal-and-licensing.md).
- Never commit Blizzard game files, extracted map data, or anything derived from a game
  client.
- Never commit credentials, account names, or realm logins.

## Code style

The codebase follows the conventions already in it rather than a separate style document:

- Nullable enabled, warnings are errors. Do not suppress; fix.
- XML documentation on public types and members. Say *why*, not what the signature already
  says.
- Comments explain decisions and constraints. If a number looks arbitrary, the comment must
  say where it came from.
- Prefer `TryX(out ...)` over exceptions for anything that fails routinely. Reading a freed
  object is routine.

## Tests

- Anything reading the client can be tested against `SimulatedClient`, which lays out a fake
  process using the real offset constants. Use it.
- Test the failure paths, not just the happy one. Most of the value in `OffsetVerifierTests`
  is in the cases where an offset is wrong.
- `dotnet test WoWBuddy.sln` on Windows, or `dotnet test WoWBuddy.NoUI.slnf` elsewhere.

## Before opening a pull request

```
dotnet build WoWBuddy.sln -c Release
dotnet test WoWBuddy.sln -c Release
```

If your change touches anything the bot reads from the client, also run the relevant manual
test script against a live client and say in the PR what you observed. Simulated tests cannot
prove an offset is right.

## Scope

The target is Honorbuddy parity for 3.3.5a, and nothing else. Archaeology, pet battles and
transmog are later expansions and are out of scope. So is anything intended to defeat
server-side anti-cheat.
