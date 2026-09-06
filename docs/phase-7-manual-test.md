# Phase 7 manual test script

Phase 7 adds profiles and the questing base. Complete phases
[1](phase-1-manual-test.md) to [5](phase-5-manual-test.md) first.

Two of the checks below need no game client at all, which makes them the cheapest useful
verification in the project so far — do those first.

## 1. Validate a profile (no client needed)

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- profile check profiles/example.xml
```

**Expect** `0 error(s), 0 warning(s)`, a summary naming the profile, and a count of each kind
of step.

Now break it deliberately. Change one `QuestId` to `12x4` and run it again.

**Expect** an error naming the line, and a non-zero exit code. If a mistake in a profile only
shows up when the bot reaches that step, the loader is not doing its job — see
[profiles.md](profiles.md).

## 2. Convert an Honorbuddy profile (no client needed)

Use one of your own; none ships with this project.

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- profile import your-profile.xml --map 0 --out converted.xml
```

**Expect** a report with four counts — steps converted, steps not understood, conditions
translated, conditions lost — followed by a line per problem, and a written file.

**Read the lost conditions.** Each one is a step that used to be gated and now is not. The exit
code is non-zero while any remain, deliberately: a conversion that looks clean and quietly
dropped a condition produces a bot that does the wrong thing an hour in.

Then check the result loads:

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- profile check converted.xml
```

**If the importer skipped a lot**, the vocabulary it recognises is in
`src/Profiles/Import/HonorbuddyVocabulary.cs`, in one file, documented as observational rather
than specified. Adding a name there is the intended fix.

## 3. Quest log reading

This is the part of phase 7 that cannot be tested away from a client, and the part most likely
to be wrong.

With a character that has at least one quest in its log:

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- lua
lua> GetNumQuestLogEntries()
```

**Expect** two numbers: entries including headers, then quests.

```
lua> GetQuestLogTitle(1)
```

**Expect** the first log line's title, level, tag, suggested group, whether it is a header,
whether it is collapsed, whether it is complete, whether it is daily — **and no quest id**.
3.3.5a does not return one here, which is why the id has to come from the hyperlink:

```
lua> GetQuestLink(2)
```

**Expect** something containing `|Hquest:<id>:<level>|h`. If your client returns nil for a
line, that line is a header rather than a quest — walk past headers when reading the log.

```
lua> SelectQuestLogEntry(2)
lua> GetQuestLogLeaderBoard(1, 2)
```

**Expect** the objective's text, its type, and whether it is finished.

If any of these differ on your client, the quest log adapter is reading the wrong thing, and
the questing base will believe objectives are done when they are not.

## 4. The thing that is not solved

```
lua> IsQuestFlaggedCompleted(2)
```

**Expect this to fail** — the function arrived in 4.0 and does not exist on 3.3.5a. That is the
point: there is no way to ask a 3.3.5a client whether this character ever handed a quest in.

The bot therefore remembers what it hands in and assumes nothing else, and when a quest giver
turns out not to have a quest on offer it records that quest as done and moves on. The
observable consequence is worth confirming on a character with history: **start a profile whose
first quests you have already done, and expect the bot to walk to each of those givers once,
then move on.** Once per quest is correct. Repeatedly walking back to the same NPC is a bug.

The proper fix is the client's completed-quest bitmask, which is marked as unverified in
`IQuestLog` rather than guessed at. Finding it would remove this whole section.

## 5. Take, work and hand in a quest

Write a three-step profile for a quest you know, on a character that has not done it:

```xml
<Profile Name="Check" Map="0">
  <QuestOrder>
    <PickUp QuestId="..." Entry="..." X="" Y="" Z="" />
    <Objective QuestId="..." Type="Kill" Entry="..." X="" Y="" Z="" Radius="80" />
    <TurnIn QuestId="..." Entry="..." X="" Y="" Z="" />
  </QuestOrder>
</Profile>
```

Run it and watch, in order:

1. The character walks to the giver, talks to it once, and the quest appears in the log.
2. It walks to the objective area and fights only the creature the step names.
3. The quest completes and it walks back and hands in.

**The interesting failure** is step 1 taking several ticks before the quest appears. That is
correct — the quest window takes a moment to open, and the bot keeps interacting until it does.
What is not correct is the bot walking away from the giver after one attempt: that means a slow
window is being read as a refusal.

## 6. Stopping

Let the profile finish. **Expect** the character to start grinding where it stands rather than
standing still, and a debug line saying the profile is finished. Running out of quests is what
success looks like for a levelling profile, not an error.

## What is still unverified after this script

- The completed-quest bitmask (section 4).
- Reward selection beyond taking the index the profile names. The bot does not choose rewards
  by value: that needs item statistics it does not have when the window opens, and taking the
  wrong reward cannot be undone.
- Anything an imported profile's custom behaviours did. Those are code from another bot and are
  reported, never converted.
