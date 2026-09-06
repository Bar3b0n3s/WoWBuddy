# Troubleshooting

## No client is found

The bot looks for a running process whose executable is the game. It needs the game to be
running and a character logged into the world — not sitting at the login or character-select
screen, where the object manager is empty.

If the game is running as administrator and WoWBuddy is not, WoWBuddy cannot see it. Run both
the same way.

## "This build is not supported"

WoWBuddy is for **3.3.5a, build 12340**, and refuses anything else. That refusal is not
caution — every address in the offset table is specific to that build, and against another one
they point at whatever happens to be there. The bot would read plausible-looking rubbish and act
on it.

The build is read from the executable's version information, not from memory, so a client that
reports a different build is a different client.

## Attach fails with a verification error

The offset table was checked against your client and something did not line up. The report says
which check failed. Common causes:

- **A modified client.** Private-server clients are sometimes patched. If the object manager
  pointer chain does not resolve, the client is not laid out the way this project expects.
- **The character is not in the world.** Several checks need a local player to exist.

Work through [phase 1](phase-1-manual-test.md); it isolates each link in the chain.

## The Lua self-test fails

Reading a value back out of the client depends on three addresses that this project has from a
single source each. The self-test asks the client to compute something the bot did not supply,
so it cannot pass by accident — and if it fails, one of those three is wrong for your client.

Everything that needs to read a result stays switched off. Reading memory still works.

## The capability report says something is missing

When execution is enabled the bot asks your client which scripting calls it has. Two absences
are expected and harmless:

- `IsQuestFlaggedCompleted` — a 4.0 function. The bot remembers what it hands in instead, so on
  a character with history it will walk to some quest givers once and find nothing on offer.
- `UnitGroupRolesAssigned` — may or may not exist on 12340. Nothing depends on it; the role is a
  setting either way.

**Anything else missing is a surprise, and the report says so.** It usually means the client is
not the build it claims to be — in which case the offset table is suspect too, and nothing
should be run against it until that is understood.

## The bot will not start

Start is gated on four things, and the button's tooltip says which one is missing:

1. Attached to a client.
2. Execution enabled — attaching only reads; acting on the game is a separate step.
3. A combat routine chosen.
4. For the gather, questing and battleground bases, a profile that loads without errors.

If all four are satisfied and it still refuses, it will say that the live state adapter does not
exist yet. That is the honest state of the project rather than a fault: see
[status.md](status.md).

## A profile will not load

The loader reports every problem at once, with line numbers, rather than stopping at the first.
Errors prevent the profile running; warnings do not.

The most common errors are a step with no position where one is required, a condition the bot
does not understand, and a coordinate outside the world. The most common warning is a quest
picked up and never handed in, which is usually a genuine mistake in the profile.

## The character walks into walls, or stops moving

Navigation needs mesh data extracted from **your own** client. None ships with WoWBuddy and none
ever will. Without it the bot has no idea what is walkable.

Check [navigation-data.md](navigation-data.md), and that the meshes are all from the same
extractor — the TrinityCore and AzerothCore formats differ, and the loader rejects a mixture
rather than reading one as the other.

## A plugin is not running

The plugin list shows its state and the last error. A plugin that threw while starting is kept
in a faulted state so you can see why; one that threw on a tick repeatedly was switched off, and
the log says when.

A plugin that does not appear at all is usually not public, not concrete, or has no
parameterless constructor. The loader reports each of those by name rather than skipping
silently — check the log at start-up.

## Something else

The log is in `logs/` next to the executable, one file per day, and it is far more detailed than
the window. Anything unexpected is worth an issue, with that log attached.
