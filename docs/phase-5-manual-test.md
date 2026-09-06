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

## 5. Errands

The bot decides *that* it needs a vendor, a repair, a mailbox or a trainer. Where those are
comes from elsewhere: a profile's `<Vendor>` and `<Mailbox>` entries for the first three, and a
world data export for the trainer, because a profile has no way to say where a warrior trainer
stands.

**With neither, `BeginErrand` returns false, the branch fails, and the bot carries on grinding
with full bags.** You will see the decision in the log and no trip. That is the correct
outcome, not a fault: stalling instead would stop the bot doing anything at all.

With a profile that names a vendor, work through each errand:

1. Let the bags fill. **Expect a trip, a sell, and the bags emptier.** The log names how many
   stacks went.
2. Let durability drop below the threshold. **Expect a repair**, and expect it to refuse and
   walk away rather than half-repair if the character cannot afford it.
3. With a mail recipient set, **expect one letter of up to twelve attachments**, and expect
   quest items to stay in the bags.

### Training

Needs a world data export. Level the character past the training interval and:

1. **Expect it to walk to a trainer of its own class** — not a profession trainer, not a mount
   vendor, both of which wear the same trainer flag.
2. **Expect it to learn what it can afford, cheapest rows first**, and to keep a reserve back.
   The log names each ability and its cost.
3. **Expect it to stop asking.** This is the check that matters: the trip is recorded whether it
   worked or not, so the bot goes back to playing. Before that record existed the errand was
   re-decided every quarter of a second and the character walked to the trainer for the rest of
   the session.

A trainer that teaches nothing the character can afford is a finished errand, not a failed one.

## 5b. Whispers

The one thing an unattended session can least afford to get wrong. With the bot running, have
another character whisper it.

1. **Expect the character to stop where it is**, within a tick or so. The log names who spoke,
   and only the log's debug level records what they said.
2. **Expect it never to reply.** Answering automatically would be worse than silence: a bot that
   says "hi" to a game master has still shown exactly what it is.
3. **Expect it to carry on by itself** once the pause is up. A pause that needed clearing by
   hand would be a stop under another name.
4. Put a name on the ignore list and whisper from it. **Expect nothing to happen**, and the
   whisper still to appear in the log.
5. Reload the interface (`/console reloadui`) and whisper again. **Expect it still to work** —
   the listener is rebuilt on every drain precisely because a reload destroys it.

If it does not react at all, check the capability report for `Whispers`: without `CreateFrame`
the bot cannot listen, and it says so at attach rather than pretending.

## 5c. A dropped connection

The bot handles the half of logging in that needs no password. With it running, force a
disconnection — pull the network cable, or `/console reloadui` will not do it, so kill the
connection at the router or use the server's own disconnect if you run one.

1. **Expect nothing to happen for the first half-minute.** A loading screen and a zone change
   look exactly like a disconnection until enough time has passed, and entering the world during
   one would be wasted at best.
2. **Expect the character to come back in** as whoever was last played.
3. Log the client out to the character-select screen by hand and watch it come back the same way.
4. Now log out to the **login** screen. **Expect the bot to stop, with `Disconnected` as the
   reason**, and to say that a person is needed. It stores no credentials and cannot answer a
   login screen; a bot clicking at one all night would be worse than one that stopped.

If step 2 does nothing at all, the likely cause is the one recorded in `LuaWorldEntry`: 3.3.5a
runs the glue screens in a separate Lua state, and this project has no record of whether
`FrameScript_Execute` reaches it. Run `type(EnterWorld)` through the inspector's `lua` command
while the character-select screen is showing and report what you see — that single answer closes
the question.

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
| Buying food and water | The bot buys crafting materials but does not stock up on consumables; resting works with what is already in the bags. |
| Bind-on-pickup filtering | `GetItemInfo` does not report it on this client; tooltip scanning would be language-dependent. |
| Talent assignment without a build | This project has no talent data. Points are spent only against a build you write; see the talents setting. |
| Riding skill and mount purchase | The bot uses a mount you already own; buying one means a mount vendor's window, which is not read. |
