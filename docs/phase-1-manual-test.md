# Phase 1 manual test script

Everything in phase 1 is automatically tested against a simulated client (57 tests). What
those tests **cannot** prove is that the offsets in the table are the ones the real 12340
client uses — no test running away from a copy of the game can prove that. This script is
that proof, and it needs about ten minutes and a running client.

Phase 1 is strictly read-only. Nothing here writes to the game or injects anything, so
nothing in this script can destabilise the client.

## Before you start

- Windows 10 or 11.
- WoW 3.3.5a build 12340. Check with right-click `WoW.exe` → Properties → Details; the file
  version must read `3.3.5.12340`.
- .NET 8 SDK.
- Build: `dotnet build WoWBuddy.sln -c Release`
- **Log a character all the way into the world.** The character-select screen is not enough:
  the object manager does not exist until the world is loaded, and the tool will say so.

## 1. Client discovery

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- list
```

**Expect:** one line per running client, with `supported` against the 12340 one.

| If instead | It means |
| --- | --- |
| `No WoW client processes found` | The client's process name is not one the locator knows. Add it to `WowClientLocator.KnownProcessNames`. |
| `UNSUPPORTED` on a client you believe is 12340 | The version resource was stripped or altered by a private-server launcher. `inspect` will still run and will warn rather than refuse. |

## 2. Attach and verify

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- inspect
```

The first thing printed is the verification report. **This is the actual test.** Every line
must read `Passed` (a `Warning` on the position layout is acceptable if you are standing
somewhere empty):

```
Offset verification passed.
  [Passed ] Client build: 3.3.5 build 12340, 32-bit (file version resource).
  [Passed ] Client connection: 0x00C79CE0 -> 0x........
  [Passed ] Object manager: Resolved to 0x........
  [Passed ] Object list walk: N objects, list terminated cleanly. Player 1, Unit 12, ...
  [Passed ] Local player object: GUID 0x................ resolves to a Player object at 0x........
  [Passed ] Descriptor pointer: Descriptor array at 0x........ reports the same GUID as the object.
  [Passed ] Descriptor sanity: Level 80, health 19000/20000. Consistent with a real character.
  [Passed ] Unit position layout: Resolved unit positions to pos=0x798, facing=0x7A8; corroborated by 11 of 12 nearby units.
```

**Record which position layout was resolved.** That is the answer to the one genuine conflict
in the offset table, and it belongs in `docs/offsets.md` once confirmed on a few machines.

If any line reads `Failed`, the offset table does not match your client. The message names
which offset is wrong. Do not "fix" it by loosening the check.

## 3. Check the values against the game

The tool then prints the local player. Compare each against what the game shows:

| Field | Check against |
| --- | --- |
| Level | Character sheet |
| Health | Player frame |
| Power | Player frame. The type must match your class: a rogue must read Energy, a warrior Rage. |
| Experience | XP bar. `/script print(UnitXP("player"))` in-game gives the exact number. |
| Money | Backpack gold display. Compare the copper value with `/script print(GetMoney())`. |
| Race / class | Character sheet |
| Map / zone | 0 Eastern Kingdoms, 1 Kalimdor, 530 Outland, 571 Northrend |
| Position | `/script local x,y = GetPlayerMapPosition("player") print(x,y)` gives map-relative coordinates, not world ones, so use this only as a sanity check that you have not teleported. The real test is step 4. |

Every one of these must match. A single mismatch means a descriptor index is wrong.

## 4. Confirm positions actually track movement

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- watch
```

This prints the player's state once a second. **Walk your character around.**

- The position must change smoothly and in the direction you are walking.
- Facing must change when you turn, and stay within `0` to `6.28`.
- Turning a full circle must wrap through zero, not jump to a wild value.
- Standing still must hold the position steady, not jitter.

This is what separates a correct position offset from one that merely happens to contain
plausible-looking floats. Do it before trusting any of phase 3.

## 5. Confirm the object list is real

Still in `inspect`, the nearest-units table should:

- List units in ascending distance order.
- Put the closest mob at a distance matching what you see on screen (a mob you are standing
  next to should be a few yards, not a few hundred).
- Show sensible levels for the zone.
- Grow when you run into a busy area and shrink in an empty one.

Target something in-game and re-run `inspect`: the `Target` line must show a non-zero GUID
that matches one of the listed units.

## 6. Confirm detach safety

With `watch` running, log out to the character-select screen.

**Expect:** `Lost the client: it exited, or a different character logged in.` and a clean
exit. It must not hang, spin, or print garbage.

Log back in on a *different* character and re-run: the tool must attach to the new character
cleanly, and its GUID must differ from the first one's.

## What is known not to work yet

| Thing | Status |
| --- | --- |
| Player names | Advisory. May print `(name cache unavailable)`; two of the name-cache structure offsets are unverified assumptions. Harmless. |
| Game object positions | Not implemented. Returns zero deliberately rather than a fabricated offset. Blocks phase 6. |
| Creature names | Not implemented. Needs the client's own name-getter, which needs phase 2's game-thread execution. |
| Anything that writes to the client | Phase 2. Nothing in phase 1 writes. |

## Reporting a failure

Include the whole verification report, your client's file version, and whether the client was
launched by a private-server launcher. The report names the failing offset, which is almost
always enough to identify the problem.
