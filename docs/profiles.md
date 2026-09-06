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
| `Map`      | Default map for every step that omits one      | 0       |

Most profiles cover one continent, so `Map` on the root saves writing it on every step. A step,
vendor or blackspot that names its own `Map` overrides it. Eastern Kingdoms is 0, Kalimdor 1,
Outland 530, Northrend 571.

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

`TurnIn` also takes `Reward="2"` — which of the quest's rewards to take, numbered from 1.
Leave it out when the quest offers no choice. The bot does not pick for you: choosing well needs
item statistics it does not have at the moment the window opens, and taking the wrong reward is
not undoable, so the profile decides.

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

**`Entry` and `ItemId` can be left out if you have a world data export.** The bot then reads the
creature ids and item counts out of your own `quest-templates.tsv` — which is what an imported
profile usually needs, since Honorbuddy profiles describe an area and leave the objective to the
quest log. Without an export a `Kill` objective with no `Entry` kills whatever is nearest and
finishes by luck; with one it kills what the quest asked for.

What the profile says always wins. It was written by someone looking at the quest, and a quest
that wants "any beast in the valley" is written as an entry no database can derive.

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

## Importing Honorbuddy profiles

```
WoWBuddy.Inspector profile import old-profile.xml --map 1 --out new-profile.xml
```

Years of questing profiles exist for the bot this one replaces, and rewriting them by hand is
not realistic. The importer converts what it can and — more importantly — tells you what it
could not.

**Read the report, not just the file.** The importer's exit code is non-zero whenever anything
was lost, even though it still writes a usable profile. The report counts steps converted,
steps it did not recognise, conditions translated and conditions lost, then lists each problem
with the line it was on.

### What does not survive, and why

- **Custom behaviours are code.** Honorbuddy's `CustomBehavior` runs a compiled C# class shipped
  with the profile. Nothing here can run one. The step is kept with its name and arguments so
  it is visible and can be written as a WoWBuddy behaviour, but it does nothing until one
  exists under that name.
- **Most conditions.** Honorbuddy conditions are arbitrary C#; WoWBuddy's are seven terms on
  purpose. `Me.Level < 10`, `HasQuest(id)`, `IsQuestCompleted(id)`, `HasItem(id)` and
  `GetItemCount(id) >= n` translate, including `!` and `== false` and clauses joined by `&&`.
  Everything else is reported with its original text. `||` has no equivalent — split the step
  in two.
- **Steps the importer does not know.** Named, counted, and missing from the result.
- **Attributes it does not know.** Listed one per line, because that is where a conversion
  quietly loses behaviour.

A step whose condition could not be translated is **kept, not dropped** — dropping it loses
quest progress — but it now runs unconditionally, which is why the import is never called clean
while any remain. Those are the steps to check first. A `While` is the exception: one whose
condition was lost is left out entirely, because emitting a loop that never ends is worse than
emitting nothing.

### The map has to be told

Honorbuddy profiles do not record which map they are for; the bot knew it from where the
character was standing. `--map` is therefore required, and the converted profile carries it as
`Map` on the root. Get it wrong and the profile navigates nowhere.

### How much of this is guesswork

Honorbuddy's format was never published as a specification. The element and attribute names the
importer recognises were derived from the shape of profiles the community wrote, and they live
in one file — `HonorbuddyVocabulary` — so a mapping that turns out to be wrong has one place to
fix. Expect the importer to be incomplete; that is what the report is for.

No Honorbuddy code or profile is included in this repository. The importer reads files you
already have.

## What the questing base does with a profile

On every tick it asks, from the top of the list, which is the first step whose conditions hold
and which is not already done. There is no cursor and no "step 43 of 200". That costs a walk
over the list and buys back everything that goes wrong with a cursor: a quest abandoned by hand,
an objective finished by a passing player, a session resumed a day later against a character
that moved on. All of those resolve to a different answer next tick instead of leaving the bot
working a step the world has passed.

A step counts as done when the game says so, never when the bot remembers doing it:

| Step        | Done when                                                             |
| ----------- | --------------------------------------------------------------------- |
| `PickUp`    | the quest is in the log, or recorded as handed in                     |
| `TurnIn`    | the quest is recorded as handed in                                    |
| `Objective` | the quest is complete, that objective is done, or the items are carried\* |
| `RunTo`     | the character is within five yards                                    |
| `Grind`     | never — its conditions are what stop it                               |
| `Repeat`    | its condition stops holding                                           |

\* "The items are carried" needs to know which item and how many. The profile can say; failing
that, a world data export can, as long as the quest asks for exactly one thing — sending the
character after the wrong one of three collections means the step never finishes, which is worse
than not knowing.

Running out of steps is not an error. A profile written for levels 1 to 10 stops having
anything to do at 10, and a character standing still is worse than one levelling slowly, so the
base falls back to grinding.

### The one thing 3.3.5a cannot tell us

There is no way to ask a 3.3.5a client whether a character has ever handed in a given quest.
`IsQuestFlaggedCompleted` arrived in 4.0, and no offset for the client's completed-quest bitmask
is recorded in this project — it is marked as a thing to find, not guessed at.

So the bot remembers what it hands in, per character, and assumes nothing else. On a character
with history that means it will walk to a quest giver and find the quest is not on offer. When
that happens it records the quest as done and moves on, so the cost is one walk rather than a
stuck session — but it does mean a profile started on an old character will do some pointless
walking early on.
