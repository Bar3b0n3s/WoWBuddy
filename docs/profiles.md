# Profiles

A profile is a list of instructions the questing bot base walks from top to bottom. It is XML,
it is data rather than code, and it is checked when it loads rather than when a step is reached.

`profiles/example.xml` shows every element in one file.

## The format is data, deliberately

Profiles get shared between strangers and then run unattended against someone's account. A
format that could execute arbitrary code would make running a downloaded profile a much larger
act of trust than it looks like, so conditions are the small language described below and
nothing more. Anything a profile needs beyond that goes in a `CustomBehavior`, which names a
behaviour compiled into the bot — the profile chooses one, it does not supply one.

## No game data ships with WoWBuddy

Quest ids, creature entries and coordinates are Blizzard's data. None of it is in this
repository, `profiles/example.xml` included: its ids are invented and it will not run. Profiles
come from you, or from the community, or from converting an Honorbuddy profile you already have.

## Validation happens at load

A questing profile plays out over hours, so finding out at step 340 that a quest id was written
`1234a` is the worst possible time. `ProfileLoader` reads the whole file, collects everything
wrong with it, and returns a list:

- **Errors** stop the profile being used. A step with no position, a quest id that is not a
  number, a `While` with no condition, a coordinate outside the world.
- **Warnings** are reported and the profile still runs. An element the bot does not know, an
  attribute that means nothing here, a quest picked up and never handed in.

`ProfileLoadResult.Describe()` prints the lot, one issue per line, with the line number it was
written on.

Unknown elements are warned about rather than silently dropped. A profile written for another
bot, or for a newer version of this one, loads with a warning naming each thing that was
skipped, so you can see exactly how much of it the bot understood.

## The root element

```xml
<Profile Name="Example" Author="you" Faction="Alliance" MinLevel="1" MaxLevel="10">
```

| Attribute  | Meaning                                        | Default |
| ---------- | ---------------------------------------------- | ------- |
| `Name`     | Shown in the profile list                      | filename |
| `Author`   | Whoever wrote it                               | empty   |
| `Faction`  | `Alliance`, `Horde`, `Any` (`Both` also works) | `Any`   |
| `MinLevel` | Lowest level it is written for                 | 1       |
| `MaxLevel` | Highest level it is written for                | 80      |

## Sections

`<Vendors>`, `<Blackspots>` and `<AvoidMobs>` are optional; `<QuestOrder>` holds the steps.

### Vendors

`<Vendor>` and `<Mailbox>` both take `Name`, `Entry`, `Map` and `X`/`Y`/`Z`. `Vendor` also takes
`Repair="true"`. The errand system walks to the nearest one on the current map when bags fill
up or gear wears out.

### Blackspots

`<Blackspot Map="" X="" Y="" Z="" Radius="" Reason="" />` — somewhere never to walk, whatever
the pathfinder says. `Reason` is for your own benefit later.

### AvoidMobs

`<Mob Entry="" Name="" />` — a creature never to attack, even when it is in the way.

## Steps

Steps run in written order. The bot works the first one whose conditions hold and which is not
already done.

| Step              | What it does                                    | Needs                                    |
| ----------------- | ----------------------------------------------- | ---------------------------------------- |
| `PickUp`          | Takes a quest from a giver                      | `QuestId`, and `Entry` or a position      |
| `TurnIn`          | Hands a quest in                                | `QuestId`, and `Entry` or a position      |
| `Objective`       | Works on a quest's objective                    | `QuestId`, and a position or `<Hotspot>`s |
| `RunTo`           | Walks somewhere                                 | a position                                |
| `Grind`           | Kills things in an area                         | a position; a `Condition` to ever stop    |
| `If`              | Wraps a condition around the steps inside it    | `Condition`                               |
| `While`           | Repeats the steps inside it                     | `Condition`, and at least one step        |
| `CustomBehavior`  | Runs a behaviour compiled into the bot          | `Name`                                    |

`If` is not a step of its own. The loader folds its condition onto each child, so what the
runner walks is always a flat list and every step carries every condition that governs it.
`While` really does loop, which is why a missing condition is an error rather than a warning.

### Objective types

`Type` is one of `Kill`, `Collect`, `Interact`, `UseItem`, `Gossip`, `Explore`. Leave it out, or
write something the bot does not know, and the objective becomes `Unknown`: the bot works the
area and watches the quest log for progress instead of doing anything specific. That is a
warning, not an error — a profile that mostly works is more useful than one that refuses to
load.

Common attributes: `Entry` (what to kill or talk to), `ItemId` (what to collect or use),
`Index` (which of the quest's objectives, 1-based; 0 means all of them), `Count`, `Radius`, and
either `X`/`Y`/`Z` or a list of `<Hotspot X="" Y="" Z="" />` children.

## Conditions

A condition is a term the bot knows, an optional id, a comparison and a number:

```
Level >= 12
ItemCount(8600) >= 5
QuestCompleted(9001)          <!-- true when non-zero -->
!QuestInLog(9002)             <!-- true when zero -->
Level >= 5 &amp;&amp; !QuestCompleted(9002)
```

Clauses are joined with `&&` and all of them must hold. There is no `||`: a profile that needs
alternatives can write two steps.

| Term                 | Takes    | Is                                          |
| -------------------- | -------- | ------------------------------------------- |
| `Level`              | —        | the character's level                       |
| `Money`              | —        | copper carried                              |
| `BagsFullPercent`    | —        | how full the bags are, 0–100                |
| `QuestCompleted`     | quest id | whether the quest has been handed in        |
| `QuestInLog`         | quest id | whether it is in the log, finished or not   |
| `QuestReadyToTurnIn` | quest id | whether it is in the log and complete       |
| `ItemCount`          | item id  | how many are carried, across all bags       |

Anything else is an error naming the clause and listing the terms that do exist. Numbers are
read the invariant way, so a profile written on a machine that uses commas for decimal points
still loads the same everywhere.

## Writing one

Start from `profiles/example.xml`, then check it:

```
WoWBuddy.Inspector profile check my-profile.xml
```

That prints the same report the bot would, without needing the game running.
