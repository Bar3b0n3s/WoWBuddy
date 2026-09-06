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

## Execution model

Reads go straight through `ReadProcessMemory`. Anything that must happen *on the game thread*
goes through a hook on the Direct3D `EndScene` call, which runs once per frame.

**How the hook is installed.** The device's vtable entry for EndScene (entry 42, so byte
offset 0xA8) is pointed at a ~50 byte stub allocated in the client. The alternative —
overwriting the first instructions of EndScene with a jump — needs a length-disassembler to
avoid splitting an instruction and is awkward to undo; swapping one pointer needs neither,
and undoing it is a single write. The stub refuses to install unless the resolved EndScene
lands inside `d3d9.dll`, which is what catches a drifted pointer chain.

**How a call is made.** Two allocations: a read/write data block holding a handshake word and
the return registers, and a small RWX code page holding the stub and a per-call thunk. To
make a call, the bot compiles the exact push/call/store sequence for it, writes it to the
thunk, then sets the handshake word. The stub sees the request on the next frame, runs the
thunk, stores the results and marks it complete.

Generating a thunk per call, rather than a fixed dispatcher over a command enumeration, is
what lets one tiny stub serve any calling convention and any argument count. It also means
the injected code is a pure function of the call description, so it can be compiled and
inspected in a test with no client present.

**The machine code is generated in C#, not shipped as a native DLL.** That keeps the project
to one MIT-licensed repository with no C++ toolchain, and it makes the bytes testable: every
instruction has a golden test whose expectation came from disassembling the output with
`objdump`, and a small interpreter in the test suite executes the exact bytes to check that
the resulting program does the right thing.

**On protected functions.** WoW refuses to let untrusted Lua call `CastSpellByName`,
`TargetUnit`, `UseAction` and the rest. That protection lives in the script sandbox and
tracks taint through the Lua VM; the C functions those bindings wrap have no notion of it,
and neither does the render loop. So the bot's split is: **Lua for asking, native calls for
acting.** This is not a workaround for taint so much as a route that never enters it, and it
is why the bot will never try to cast a spell through Lua — that fails silently and
confusingly.

**Failure is latching.** If a call is not picked up within its deadline the executor marks
itself broken and refuses further work. It cannot tell a stalled game thread from a dead one,
and a thread that wakes up later would run whatever thunk is in the page at that moment. A
stopped bot leaves a character standing; a confused one does not.

## Behaviour model (phase 4 onward)

Behaviour trees. The root is:

```
Handle death → Handle stuck → Combat → Rest → Loot/gather → Bot base → Idle
```

Bot bases and combat routines contribute subtrees; plugins hook `OnPulse` and the event bus.

## The window, and what it is not

The UI is split in two. `src/Presentation` holds every decision the window makes — which
clients were found, whether attaching is allowed, whether the chosen profile loads, which bot
base to build — and mentions no WPF type at all. `src/UI` is the XAML and the four small classes
that translate between the core and the window.

That split is not tidiness. The rules about what the user may do next are exactly the part of a
UI that goes wrong, and they are checkable only if a test can reach them. Attaching twice,
attaching to a build the offsets do not fit, starting a questing base with a profile that will
not load — all of those are decisions with tests, and none of them needs Windows to run. This
repository's CI builds and tests the presentation layer on Linux; only the XAML needs a Windows
machine.

## The gap: nothing implements IBotState against a live client

**This is the largest remaining piece of work, and it is worth being blunt about.**

Six bot bases, thirty combat routines, the behaviour tree, profiles, questing, group play and
battlegrounds are all written against `IBotState` and tested against a fake implementation of
it. Nothing yet implements it against a running client.

Doing so means reading, from a real 3.3.5a client: the quest log and its objectives, the party
and its members' health and targets, bag contents and item counts, the battleground queue, and
the verbs that go with them. A good deal of that is Lua this project has not verified — see the
manual test scripts for [phase 7](phase-7-manual-test.md) and
[phase 8](phase-8-manual-test.md), which exist precisely to check it.

### Narrowing the unverified surface first

The adapter's problem is not the memory — position, health, target, nearby units and the object
manager are all verified already — it is the Lua. So before any of it is written, the bot now
asks the attached client which of the calls it needs actually exist.

`CapabilityProbes` runs once when execution is enabled and produces a report in the same spirit
as the offset verification one. It asks about existence rather than behaviour, deliberately: a
party call returns nothing when solo and a quest call returns nothing with an empty log, so
asking what a call *returns* would switch features off for the wrong reason. `type(x) ==
"function"` separates "this client cannot do that" from "you happen to have no quests".

It distinguishes two kinds of absence, and the difference matters to a user. A **known gap** is
something this project already believes 12340 lacks — `IsQuestFlaggedCompleted`, which arrived
in 4.0, and `UnitGroupRolesAssigned`, which may or may not exist — and the report explains the
consequence. A **surprise** is a call the bot expected to find and did not, which usually means
the client is not the build it claims to be; in that case the offset table is suspect too, and
the report says so.

What the probe does not establish is that a call returns what this bot expects — that
`GetQuestLogTitle` puts completion in the seventh slot, say. That needs a character with a quest
in the log and a person to look at the answer, which is what the manual test scripts are for.
The probe narrows the unverified surface; it does not close it.

Until that adapter exists, the Start button builds the tree — which is real, and is where a
profile that does not give a bot base what it needs is caught — and then says plainly that it
cannot play. **A start button that ticked a tree fed on invented values would look like progress
and be worth less than nothing.**

## Failure policy

On an unhandled exception the bot stops and leaves the character standing. It never keeps
sending input in a state it does not understand. A character standing still in the world is
recoverable; a character running into a wall for six hours is not.
