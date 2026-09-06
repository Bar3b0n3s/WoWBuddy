# Architecture

## The shape of the problem

The bot is an out-of-process .NET application that reads the memory of a running 32-bit game
client and, from phase 2, executes calls on the client's own game thread. Everything follows
from three facts:

1. **The client is 32-bit.** Every pointer it holds is 4 bytes, so client pointers are read
   as exactly four bytes regardless of what the bot itself was built as. The programs that
   attach to it (the shell and the inspector) are built x86, enforced by
   `Directory.Build.targets`, because from phase 2 the injected payload must match the
   client. Libraries stay AnyCPU: in .NET, process bitness comes from the entry executable.
2. **Reads are racy by nature.** The game thread mutates its object list while the bot walks
   it. A failed read is routine, not exceptional, so the memory layer returns `false` instead
   of throwing and the object walk is written to survive torn pointers.
3. **Protected Lua functions cannot be called from injected script.** Anything that affects
   gameplay (casting, targeting, moving) must go through the client's native C functions on
   the game thread. This is the single biggest constraint on the design and is the whole
   subject of phase 2.

## Projects

| Project | Target | Role |
| --- | --- | --- |
| `Common` | `net8.0` | Logging, configuration, geometry. No dependency on the client, so it is testable and portable. |
| `Core` | `net8.0-windows` | Offsets, memory access, process attach, object manager, attach-time verification. The only place that touches raw addresses. |
| `GameApi` | `net8.0-windows` | Typed model: `WoWUnit`, `WoWPlayer`, `WoWGameObject`. Everything above this line works in game terms, never in pointers. |
| `UI` | `net8.0-windows`, WPF, **x86 host** | The desktop shell. |
| `tools/Inspector` | `net8.0-windows`, **x86 host** | Console dev tool. Read-only. Used to prove the offset table against a real client. |

Later phases add `Navigation`, `Behavior`, `BotBases`, `CombatRoutines`, `Profiles` and
`Plugins` as the brief describes.

## The seam that matters

There is one sharp boundary in this codebase, and it is between `Core` and `GameApi`.

Below it, code works in addresses and unverified facts about someone else's binary. Above it,
code works in levels, health and positions. Keeping the boundary sharp means all the risky,
unverifiable material sits on one side of it and can be audited in one sitting — `Core` is a
few thousand lines, and the offsets are all in two files.

## Attach is a gate, not a step

You cannot obtain a `GameClient` without passing verification. `GameClient.Attach` runs
`OffsetVerifier` against the live client and returns a failed `AttachResult` if anything does
not check out. There is deliberately no way to skip it.

The checks are chosen so that a wrong offset makes them fail:

- The local player GUID read from the object manager header must resolve to exactly one
  object in the list, and that object's type must be `Player`.
- That object's descriptor array must independently report the same GUID. The two values live
  in different places and are written by different code paths, so they cannot agree by
  accident. This is what makes the single-sourced descriptor pointer offset safe to use.
- Level and health must be internally consistent (level 1-83, health at most max health).
- The object list must terminate, and every type tag must be in the legal 0-7 range.

## Resolving the conflicted offsets empirically

The public sources disagree about where a unit's position lives. Rather than pick one,
`PositionResolver` determines it against the running client using two properties a wrong
offset cannot fake: a real coordinate is finite and inside the 64x64 map grid, and the client
only keeps objects near the player, so every unit should be within a few hundred yards.
Requiring both across many units identifies the right layout confidently; if neither
candidate clears the bar, attach fails.

This pattern generalises. Where a fact cannot be established from sources, the bot should
work it out from the client and refuse to run if it cannot, rather than guessing.

## Execution model (phase 2 onward)

Reads go straight through `ReadProcessMemory`. Anything that must happen *on the game thread*
— Lua execution, `Interact`, `TraceLine`, `UnitReaction` — goes through an injected x86 hook
on the Direct3D `EndScene` call, which drains a queue of pending calls once per frame and
returns results through shared memory. The rest of the codebase sees only
`Game.Execute(() => ...)` and `Lua.Call<T>("...")`.

## Behaviour model (phase 4 onward)

Behaviour trees. The root is:

```
Handle death → Handle stuck → Combat → Rest → Loot/gather → Bot base → Idle
```

Bot bases and combat routines contribute subtrees; plugins hook `OnPulse` and the event bus.

## Failure policy

On an unhandled exception the bot stops and leaves the character standing. It never keeps
sending input in a state it does not understand. A character standing still in the world is
recoverable; a character running into a wall for six hours is not.
