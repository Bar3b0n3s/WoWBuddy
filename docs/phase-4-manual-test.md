# Phase 4 manual test script

Phase 4 makes the bot fight. Complete phases
[1](phase-1-manual-test.md), [2](phase-2-manual-test.md) and [3](phase-3-manual-test.md)
first — combat depends on all three.

**Do this on a character you do not mind dying.** The point of the exercise is to find out
where the routines and the recovery logic are wrong, which mostly means dying a few times.

## 1. Confirm casting is available

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- exec
```

Look for the **Spell casting** line in the report:

```
  [Passed ] Spell casting: The client reports the bot's scripts run in a secure context, so
            protected functions will be accepted.
```

If it is a warning instead, casting is disabled and nothing in this phase will work. The
message says which of the three cases you have:

| Message | Meaning |
| --- | --- |
| tainted context | The client refuses protected functions from the bot's scripts. Casting through Lua is not possible on this client; it would need a verified native cast address, which this project does not have. |
| no `issecure()` | The check could not run. Casting is left disabled rather than attempted blindly. |
| no `CastSpellByName` | Not a client the bot knows how to cast in. |

## 2. Confirm the rotation casts what you expect

In the Lua console, cast one spell by hand first to confirm the mechanism end to end:

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- lua
lua> CastSpellByName("Frostbolt");
```

**Expect the character to cast it.** If nothing happens, the self-test above was wrong about
your client and everything below will fail silently.

## 3. One fight, watched

Pick something well below your level, somewhere you can retreat from. Start the grind base
with a single hotspot where you are standing.

Watch for, in order:

| Should happen | If it does not |
| --- | --- |
| The character buffs before pulling | The buff rotation's aura names do not match your client's |
| It walks to within its routine's pull range, then stops | Pull range is wrong for the specialisation, or movement is not enabled |
| It opens with the pull spell, not a filler | The pull rotation is being skipped |
| It casts one spell at a time, not several at once | Something is wrong in the rotation's cast-in-progress check |
| It keeps fighting until the target dies | The combat branch is returning Success rather than Running |
| It stops and rests afterwards if hurt | The routine's rest thresholds |

**Watch particularly for the character standing still doing nothing.** That is the signature
of a rotation whose spell names do not match the client: every rule is skipped because no
spell is ever "ready". Check with `GetSpellInfo("Frostbolt")` in the Lua console.

## 4. Spell names are the thing most likely to be wrong

The routines use English spell names. On a non-English client, or where a private server has
renamed something, every rule referring to it silently never fires.

Check a few from your rotation:

```
lua> GetSpellInfo("Bloodthirst")
```

An empty result means the name is wrong for your client. Spell names are ordinary data in the
rotation and are meant to be edited.

## 5. Death and recovery

Deliberately pull something that will kill you.

**Expect:** the character dies, releases, walks back to its corpse, reclaims it, waits out
resurrection sickness, and carries on. This is the sequence that decides whether an overnight
run works, and it is worth watching all the way through at least once.

**Watch for:** a ghost trying to fight (the death branch is not pre-empting the bot base), or
a ghost standing still forever (the corpse position is not being found).

## 6. An hour unattended

The real test. Leave it grinding somewhere safe and come back.

| Symptom | Likely cause |
| --- | --- |
| Standing still, alive, no target | Nothing acceptable in range and no hotspots, or every spell name is wrong |
| Standing still as a ghost | Corpse position not found |
| Dead repeatedly at the same place | The level range or avoid list needs adjusting for that spot |
| Full health, never fighting | Rest thresholds too conservative for the class |
| Fighting at very low health, never resting | Rest thresholds too aggressive |

## What is deliberately not here

| Thing | Why |
| --- | --- |
| Looting | Phase 5. The bot kills things and leaves them. |
| Vendor, repair, mail runs | Phase 5. |
| Buying food and water | Phase 5. Resting works only with what is already in the bags. |
| Mounts between hotspots | Needs casting a mount, which works only if the self-test above passes, and needs travel logic that is not written yet. |
| The other 26 combat routines | Phase 10. Four exist: one melee, one caster, one healer, one pet class, chosen to exercise the different shapes of routine. |
