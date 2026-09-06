# Phase 2 manual test script

Phase 2 is where the bot stops only watching and starts acting. It writes a small amount of
machine code into the client and redirects one Direct3D vtable entry through it, so that the
bot can run calls on the game's own thread.

**This can crash the client if an address is wrong.** Every address involved is checked
before use and the self-tests below are the check, but do this on a character you do not mind
disconnecting, and read [what to do if it goes wrong](#if-something-goes-wrong) first.

Complete [phase-1-manual-test.md](phase-1-manual-test.md) before starting. If the phase 1
offsets do not match your client, nothing here can work.

## What is actually installed

| Thing | Where | Undone by |
| --- | --- | --- |
| A ~50 byte stub and a per-call thunk | Two pages allocated in the client | Freed on exit |
| One pointer, in the `IDirect3DDevice9` vtable, at entry 42 (EndScene) | Inside `d3d9.dll` | Original written back on exit |

Nothing is patched inside the game's own code, and no DLL is loaded into it. Closing the bot
cleanly puts the vtable entry back and frees the pages. Killing the bot process without
letting it clean up leaves the client pointing at freed memory: see below.

## 1. Install and self-test

With a character logged into the world:

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- exec
```

**Expect** the phase 1 report, then:

```
Offset verification passed.
  [Passed ] Install hook: EndScene redirected via vtable slot 0x........; stub at 0x........, data at 0x........
  [Passed ] Game thread: The render loop executed an injected call (1 completed).
  [Passed ] Native call: The client and the object manager agree the local player is at 0x........
  [Passed ] Lua round trip: Lua round trip confirmed: the client computed "wowbuddy1234".
```

Then the optional targeting check, which briefly changes your target and puts it back.

What each one proves:

| Check | Proves |
| --- | --- |
| Install hook | The Direct3D pointer chain resolved and landed inside `d3d9.dll`. |
| Game thread | The client is rendering and the render loop reached the injected stub. |
| Native call | A call really executed and its return value came back intact — and `GetActivePlayerObject` agrees with the object manager about where the player is. |
| Lua round trip | `FrameScript_Execute` ran a script, the script assigned a global, and `GetLocalizedText` read it back. The value is a sum the bot picks at random, so a wrong address cannot pass by luck. |
| Targeting | `GameUiTarget` really targets, judged by the target descriptor changing. |

**If "Game thread" fails but the hook installed**, the client is not rendering: minimised,
on a loading screen, or in the background with a frame limiter. Bring it to the foreground
and try again. This is the most common cause and is not an offset problem.

## 2. Lua console

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- lua
```

Try these and compare with the game:

| Type | Expect |
| --- | --- |
| `UnitLevel("player")` | Your level |
| `UnitName("player")` | Your character name |
| `GetMoney()` | Your money in copper |
| `select(2, GetNetStats())` | Your world latency |
| `GetZoneText()` | Your current zone |
| `UnitHealth("player")` | Should match the health the `inspect` command reads from memory |

That last one is the useful one: it is the same number arriving by two completely
independent routes, memory reads and the Lua interpreter. If they agree, both are right.

Then settle the protected-function question for your client. Type:

```
issecure()
```

**Expect `true`.** The client tracks taint through the script VM and refuses protected
functions to *addon* code; a script handed straight to the client's own entry point from the
render-loop hook belongs to no addon and carries no taint. `issecure()` asks the client
directly whether the bot's scripts are trusted, which is a side-effect-free answer to exactly
the question that matters.

If it returns `true`, casting through Lua will work and the bot enables it. If it returns
`false`, or `issecure` does not exist, the bot leaves casting disabled and says so rather
than attempting it blindly — a refused cast is silent, and a rotation that silently does
nothing is maddening to debug.

Then target something and confirm the practical answer matches:

```
CastSpellByName("Fireball")
```

## 3. Click-to-move block (read-only)

Phase 3 will write here. Before it does, confirm the offsets by watching:

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- ctm
```

Right-click-move around in game. **Expect:**

- The destination to change to roughly where you clicked, matching the coordinates the
  `watch` command shows for your character once you arrive.
- The action code to change as you move, interact and stop.
- Interacting with an NPC to put that NPC's GUID in the guid column.

Record what action codes you actually observe. The
[action code list](../src/Core/Execution/ClickToMoveAction.cs) is single-sourced and one
value in it looks like a transcription error; this is how that gets settled.

## 4. Clean removal

Let the tool exit normally. Then, still in game:

- Alt-tab, resize the window, and change resolution. The client must not crash.
- Run `exec` again. It must install and pass cleanly a second time.

Repeat install and removal a few times. A leak or a botched restore shows up here.

## If something goes wrong

**The client crashes when the bot exits.** The hook was not removed before the memory was
freed. Report the log — the bot logs a fatal message if it could not restore the vtable, and
in that case it deliberately leaks the pages rather than freeing memory the client is still
pointing at.

**The bot is killed without cleaning up** (task manager, a debugger stop, a power cut). The
client is now calling into freed memory and will crash on its next frame or behave strangely.
Close and restart the game. Nothing is permanently damaged: no game file is touched.

**A self-test fails.** Nothing is left installed — a failed install removes its own hook. The
report names which address is wrong. Do not loosen the check to get past it.

## What is deliberately not here

| Thing | Why |
| --- | --- |
| A *native* cast function | The two published `CastSpell` addresses conflict (0x0080DA40 vs 0x0080B210) and neither has been confirmed, so neither is used. Casting goes through Lua instead, gated on the `issecure()` check above. A verified native address would be better, since it does not depend on taint at all. |
| `Interact` and `TraceLine` | No reachable source gives their addresses for 12340. Interaction will go through click-to-move in phase 3; line of sight needs a real address and is a phase 3 prerequisite. |
| Movement | Phase 3. The click-to-move writer exists but nothing calls it. |
| Creature names | Needs the client's own name lookup, which needs an address that is not yet confirmed. |
