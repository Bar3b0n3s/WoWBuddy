# Phase 9 manual test script

Phase 9 is professions: reading what a character can make, making it, and — with a world data
export loaded — walking to a vendor for the materials it has run out of. Complete phases
[1](phase-1-manual-test.md) to [5](phase-5-manual-test.md) first, and read the professions
section of [activities.md](activities.md) for why the rules are what they are.

Every decision here is unit tested. What cannot be tested away from a client is whether the Lua
returns what the bot believes it does, and that is the whole of this script. The capability
probe checks at attach that each of these functions *exists*; existence is not the same as
returning what is expected, and the difference is what you are checking below.

## 1. The trade skill window

Open a profession by hand, then:

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- lua
lua> GetTradeSkillLine()
```

**Expect** the profession's name, your current skill, and the cap for your current training, in
that order. The bot uses the third to decide it needs a trainer rather than more materials.

```
lua> select(2, GetTradeSkillInfo(2))
```

**Expect** one of `optimal`, `medium`, `easy`, `trivial`, or `header` — the client's own word
for the colour. Anything else means the bot's difficulty parsing is reading a different value,
and it will make grey recipes until the materials are gone.

## 2. Reagents

With the same window open, and an index that is a recipe rather than a header:

```
lua> GetTradeSkillNumReagents(2)
lua> GetTradeSkillReagentInfo(2, 1)
```

**Expect** a count, and then a name, a texture, how many one craft takes, and how many the
character is carrying — in that order. The third and fourth are what tell the crafting base it
is short and by how much; if they are swapped, it will believe it is short of things it has.

```
lua> GetTradeSkillReagentItemLink(2, 1)
```

**Expect** an item hyperlink containing `item:` followed by a number. That number is the whole
reason this call is used: `GetTradeSkillReagentInfo` gives a name, and a name cannot be matched
against a vendor table without knowing which language your client is in. A reagent whose link
the client has not cached yet reports an id of zero, and the bot skips it rather than guessing.

## 3. A merchant's shelves

Standing at any vendor, with its window open:

```
lua> GetMerchantNumItems()
lua> GetMerchantItemInfo(1)
```

**Expect** a name, a texture, a price in copper, how many items one purchase gives, how many are
left (`-1` for an endless supply), whether it is usable, and whether it has an extended cost.
The bot skips anything with an extended cost: those are bought with honour, marks or tokens, and
buying one would either fail or spend something you were saving.

```
lua> GetMerchantItemLink(1)
```

**Expect** an item hyperlink, as in section 2.

## 4. Buying

**Do this on a character you do not mind spending a few copper on.** With a vendor open and
something cheap on the first shelf:

```
lua> BuyMerchantItem(1, 2)
```

**Expect two items to arrive, not two stacks.** This is the one thing in this script the project
has marked `// TODO: verify` — see `LuaVendor.Buy`. If two stacks arrive instead, the bot will
over-buy on its first trip and then stop asking, because it counts the bags again afterwards and
finds itself no longer short. That costs money and a trip; it does not break anything.

## 5. A shopping trip end to end

You need a world data export with `npc-vendors.tsv` in it — see
[world-data.md](world-data.md) — and a character whose profession uses a reagent a vendor
actually sells. Weak Flux for a blacksmith is the usual example; ore, herbs and leather are not,
because nobody sells them.

1. Empty the bags of that reagent.
2. Start the Craft base on that profession.
3. **Expect the status line to say it will buy materials.** If it says it cannot, the export has
   no vendor table loaded, and nothing below will happen.
4. **Expect the character to stop crafting, walk to a vendor, and buy.** The log names the shop
   and what it bought.
5. **Expect it to walk back to where it was crafting.** This matters: a blacksmith crafts at an
   anvil, and one that buys its flux and stays in the shop has not finished the errand.
6. **Expect it to carry on crafting.**

If nothing sells what the recipe needs, expect it to stop with `OutOfMaterials` and say so —
that is the ordinary outcome, not a fault.

## 6. Quest objectives from the export

You need `quest-templates.tsv` in the export, and a profile with an `Objective` step that gives
a `QuestId` and no `Entry`.

1. Take the quest and start the Questing base.
2. **Expect a log line naming what the quest wants**, said once per quest.
3. **Expect the character to fight only the creatures the quest asks for**, even when something
   else is nearer. Without the export it would attack the nearest thing.

For a `Collect` objective on a quest that asks for exactly one item, put enough of that item in
the bags by hand and **expect the step to be treated as finished** without the quest log having
to be read.

## What is still unverified after this script

- The order of `GetQuestLogLeaderBoard`'s objectives against the database's column order. The
  bot lines the two up so that a step saying `Index="2"` means something specific; an index past
  the end falls back to using every requirement, so being wrong costs precision rather than
  correctness. Marked `// TODO: verify` in `QuestObjectives`.
- Whether `BuyMerchantItem` counts items or purchases, until you have run section 4.
- Anything about profession trainers. The bot can find a class trainer from world data, but it
  does not read a profession trainer's window, so it cannot tell learning a new rank from buying
  a recipe — which is why hitting the skill cap stops the session rather than fixing itself.
