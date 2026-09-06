# Phase 5 manual test script

Phase 5 adds what keeps an unattended run alive: looting, vendor and repair errands, gear
upgrades, and a scheduler with breaks. Complete phases
[1](phase-1-manual-test.md) to [4](phase-4-manual-test.md) first.

Most of this phase is decision logic and is already covered by unit tests. What cannot be
tested away from a client is whether the decisions are being fed the right facts, which is
what this script checks.

## 1. Item information

Everything in this phase depends on `GetItemInfo` returning what the bot expects. In the Lua
console, with something in your bags:

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- lua
lua> select(3, GetItemInfo(6948))
```

**Expect `1`** — that is the quality of a Hearthstone, which every character has.

The bot reads name, quality, item level, required level, class, subclass, stack size, equip
location and vendor price from this call, in that order. If your client returns them in a
different order — a heavily modified private server, say — everything downstream is wrong in
ways that look like bad loot rules.

**Known limitation:** `GetItemInfo` on 3.3.5 does not report whether an item is
bind-on-pickup. The loot rules therefore do not consider binding at all. That is a small loss
— soulbound items can still be sold — but it means "ignore bind-on-pickup" from the brief is
not implemented and cannot be without tooltip scanning, which depends on the client's
language.

## 2. Localisation

Several rules compare against strings the client returns in its own language:

| What | Where it matters |
| --- | --- |
| `"Quest"` item class | Quest items are always kept |
| `"Armor"` / `"Weapon"` classes, and subclasses like `"Plate"` | Gear evaluation refuses armour the class cannot wear |
| Stat names such as `"Strength"` | Stat weights score items |

On a non-English client these silently do not match. The symptom is a bot that equips the
wrong armour class and vendors quest items. Check with:

```
lua> select(6, GetItemInfo(6948))
```

If that does not read `Miscellaneous` in English, the gear and loot rules need their strings
adjusted for your client.

## 3. Looting

Kill something that drops loot and watch.

**Expect:** the fight finishes, the character walks to the corpse, loots it, and only then
sits down to eat if it needs to.

**The ordering matters and is worth confirming.** Looting is above resting in the tree
because a corpse expires on a timer and the character's health does not. A bot that drinks
for two minutes first arrives at a corpse that has gone.

**Watch for:** looting being attempted during combat (it should be interrupted), or the
character standing next to a corpse doing nothing (loot range, or the corpse is not being
reported as lootable).

## 4. Bag filling and the wedged case

Let the bags fill up.

| Expected | If it does not happen |
| --- | --- |
| Vendor fodder stops being taken once free slots reach the reserve | Reserved slots setting, or free-slot counting |
| Good items are still taken with almost no space left | The reserve is being applied to everything, not just fodder |
| An errand becomes due | Errand thresholds |

Then deliberately fill the bags with things no vendor will buy — quest items work. The bot
should recognise this as **wedged**: full bags with nothing sellable is a state a vendor trip
cannot fix. It should say so rather than walking to a vendor repeatedly.

## 5. Errands have nowhere to go yet

**This is expected and is the honest state of the phase.** The bot decides *that* it needs a
vendor, a repair or a trainer. It does not know *where* any of those are: that comes from
profiles in phase 7.

So `BeginErrand` returns false, the errand branch fails, and the bot carries on grinding with
full bags. You will see the decision in the log and no trip. Stalling instead would stop the
bot doing anything at all, which would be worse.

## 6. The scheduler

Set a short work interval and break length to watch it without waiting ninety minutes.

**Expect:** the character stops where it stands, does nothing for the break, then resumes.

Two behaviours are worth confirming deliberately, because they were the wrong way round in a
first draft:

- **Die during a break.** The character should still release and run back rather than lying
  dead for the whole break and resuming to a corpse run it could have already done.
- **Get attacked during a break.** The character should fight back. Standing still while
  something eats it is not a break.

What it should *not* do during a break is loot, eat, run errands or look for a new fight.

## 7. Gear

Loot an upgrade and watch what the bot decides. The log records the score of the candidate
against the equipped item and why.

**Watch particularly for the wrong armour class.** A warrior equipping cloth is the classic
failure, and it is quiet: the character simply kills things more slowly for hours. The
evaluator refuses armour subclasses outside the specialisation's list, and rings, trinkets,
necks and cloaks are exempt because the client reports no meaningful subclass for them.

## What is deliberately not here

| Thing | Why |
| --- | --- |
| Actually walking to a vendor, trainer or mailbox | The bot has no idea where they are until profiles arrive in phase 7. |
| Buying food and water | Same reason. Resting works only with what is already in the bags. |
| Bind-on-pickup filtering | `GetItemInfo` does not report it on this client; tooltip scanning would be language-dependent. |
| Talent assignment on level-up | Needs per-specialisation talent data, which belongs with the other 26 routines in phase 10. |
| Whisper alerts and pause-on-whisper | Needs chat event plumbing that no other part of the bot uses yet. |
| Riding skill and mount purchase | Follows from trainers and vendors having locations. |
