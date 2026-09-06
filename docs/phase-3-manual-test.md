# Phase 3 manual test script

Phase 3 makes the character move. Complete
[phase-1](phase-1-manual-test.md) and [phase-2](phase-2-manual-test.md) first, and generate
navigation data as described in [navigation-data.md](navigation-data.md).

**The click-to-move offsets have one published source and have not been verified first-hand.**
The movement controller therefore refuses to write anything until `Enable()` is called, and
step 2 below is what earns that call. Do not skip it: an unverified destination write sends
somebody's character somewhere nobody asked for.

## 1. Navigation data

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- nav "C:\Games\WoW\mmaps"
```

Needs no client. Every continent you intend to bot on must report `ok`, and the flavour line
must name **one** extractor. See [navigation-data.md](navigation-data.md) for what each
failure means.

## 2. Confirm click-to-move before anything writes to it

With a character in the world:

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- ctm
```

Right-click-move around. **All of these must hold before movement is enabled:**

| Check | Why it matters |
| --- | --- |
| The destination changes when you click, and matches roughly where you clicked | Confirms `DestinationX` and that the block is the right one |
| The coordinates agree with what `watch` reports for your character once you arrive | Confirms the destination is in world coordinates, in the same frame as everything else |
| The action code changes as you move, interact and stop | Confirms `Action` |
| Interacting with an NPC puts that NPC's GUID in the guid column | Confirms `InteractGuid` |

**Record the action codes you actually see.** The
[action code list](../src/Core/Execution/ClickToMoveAction.cs) is single-sourced and the
`Attack` value looks like a transcription error — it sits at `0x10` while its neighbours run
`0x9`, `0xA`, `0xB`. Watching what the client really writes is how that gets settled.

If any of the above fails, stop. The offsets are wrong for your client and nothing further in
this phase is safe.

## 3. Pathing without moving

Before anything walks, check that paths make sense. Pick two points in the same zone, note
their coordinates from `watch`, and confirm a route between them:

- The path should have a handful of waypoints, not one and not hundreds.
- Its total length should be somewhat more than the straight-line distance, and not wildly
  more — a path three times the direct distance across open ground means the mesh is wrong.
- A destination inside a building or across water should produce a route that goes around,
  not through.
- A destination on another continent should fail cleanly with `NoDataForMap` or
  `EndOffMesh`, not produce a path.

## 4. Short walk, watched

Somewhere flat and open, with nothing hostile nearby. Start with a destination about 30 yards
away.

**Expect:** the character walks there the way it would if you had right-clicked, stops, and
the controller reports `Arrived`.

**Watch for:** running past the destination and coming back, stopping short and standing
still, or turning on the spot. All three mean the tolerances or the destination writes are
wrong, and all three are much easier to see over 30 yards than over 300.

## 5. Longer route, with obstacles

A few hundred yards, across terrain with buildings, slopes and water.

- The character should follow the terrain rather than trying to walk through it.
- Slopes should not cause it to stop: arrival is judged on flat distance for this reason.
- Water should be entered and swum if the path goes that way.

Long legs are split so the bot checks its progress along the way; you should not see it
commit to a single destination hundreds of yards off.

## 6. Getting stuck on purpose

Walk the character into a corner or a fence and give it a destination on the far side.

**Expect, in order:** it keeps trying briefly, then re-issues the destination, then skips the
blocked waypoint, then gives up and reports `Failed` with `Stuck`.

**Giving up is the correct outcome.** A bot that retries forever is indistinguishable from a
hung one. What must not happen is silent, indefinite pushing against scenery.

## What is deliberately not here

| Thing | Why |
| --- | --- |
| Mounts | Mounting is a protected action, so it needs a verified `CastSpell` address. The two published addresses conflict and neither is confirmed; see [phase-2-manual-test.md](phase-2-manual-test.md). |
| Jumping and strafing to get unstuck | Both are movement *commands* rather than destinations, and click-to-move cannot express them. Faking them with a nearby destination usually walks the character further into whatever it is stuck on. They need an input layer. |
| Flight paths | Needs the taxi UI, which needs interaction with a flight master, which needs a verified interact path. |
| Transports, boats and zeppelins | Positions aboard one are relative to the transport, which the object layer does not model yet. |
| Multi-continent routing | Follows from flight paths and portals. |
