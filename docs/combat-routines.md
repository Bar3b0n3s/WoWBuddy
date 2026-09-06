# Combat routines

One routine per 3.3.5a specialisation — thirty of them, ten classes by three. They are ordinary
data, and they are meant to be edited.

## What a routine is

A priority list, not a sequence. Each decision walks the list from the top and casts the first
rule that applies, which is how players actually think about a rotation: keep this up, use that
on cooldown, otherwise filler. A proc or a low-health emergency displaces the filler without any
explicit state.

Four lists per routine: buffs to keep up out of combat, how to open a fight, how to fight, and
what to do while recovering. A routine that needs more than that — a hunter keeping its pet
alive, say — overrides a method instead.

One cast per decision, deliberately. The client has a global cooldown and queuing a second spell
in the same instant cancels the first, so a rotation that fired several rules at once would cast
fewer spells, not more.

## The priorities are conventional, not optimal

These are the ordinary Wrath priorities, not something squeezed out of a simulator. They are a
starting point that plays each specialisation sensibly; if you know a tree better than this,
edit it, and the edit is one file.

**A wrong spell name fails safely.** Rules are matched against the spellbook by name, so a spell
the character does not know — or one this project has spelled wrong — is never ready and the
rule is simply skipped. The rotation falls through to the next rule rather than jamming.

## What they optimise for

Surviving being played unattended, not damage. A character that kills half as fast and never
dies finishes the night far ahead of one that kills quickly and spends it running back from a
graveyard. That is why the defaults are what they are:

| Class        | Default             | Why                                                     |
| ------------ | ------------------- | ------------------------------------------------------- |
| Warrior      | Fury                | fast, and Victory Rush keeps it standing                |
| Paladin      | Retribution         | melee damage with a heal attached                       |
| Hunter       | Beast Mastery       | the pet takes the damage the character would            |
| Rogue        | Combat              | no positional requirements to get wrong                 |
| Priest       | Shadow              | Vampiric Embrace means it rarely has to stop            |
| Death Knight | Blood               | Death Strike heals it by fighting                       |
| Shaman       | Enhancement         | self-healing melee                                      |
| Mage         | Frost               | the only mage tree that stops things reaching it        |
| Warlock      | Affliction          | Drain Life, and a pet holding what the dots are killing |
| Druid        | Feral               | cat form, and no casting to interrupt                   |

The user overrides this; it exists so picking a class does something sensible before anyone has
chosen.

## Things deliberately left out

- **Positional abilities.** Backstab, Ambush and Shred all require standing behind the target,
  and this bot does not control its facing well enough to promise that. A rotation built on them
  would spend the night failing to cast, so Hemorrhage and Mangle are the fillers instead —
  worse, and they work.
- **Rune tracking for death knights.** The client already refuses a strike whose runes are
  spent, so asking whether the spell is ready answers the question. A rune tracker would be a
  second, worse copy of something the client is authoritative about.
- **Bear form for druids.** Tanking is a different rotation in a different form, and switching
  mid-fight is a decision this routine is not in a position to make well.
- **Trap placement for hunters.** Laying one usefully means knowing where something will walk.
  Traps are used defensively instead — something is on the character and needs to stop being.

## Healers

Healing routines set `NeedsTargetToFight` to false, because they have plenty to do with nothing
targeted. Everything else returns early without a target: casting a damage spell at nothing
burns the global cooldown on something the client will refuse, and since the group-support
branch arrived in phase 8 a melee character can be asked to act with no target at all.

`GroupHealing` picks who: lowest health first, counting the character itself. See
[group-play.md](group-play.md).

## The catalogue

`RoutineCatalogue` finds routines by looking at the assembly rather than from a hand-written
list, so adding one is a single file and nothing else. `Add(assembly)` is how a plugin
contributes its own; a routine that is not public, concrete and constructible without arguments
is skipped with a line saying why, rather than silently.
