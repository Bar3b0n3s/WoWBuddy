# Activities and professions

Phase 9: doing more than one thing in a session, and making use of the professions a character
already has.

## Why mix activities at all

Not for variety's sake. The profitable thing to be doing changes during a session — bags fill,
a zone's nodes get picked over, a level is reached — and a character that grinds the same spot
for nine hours is both less useful and considerably more obvious than one that does not.

## An activity plan is a priority list, not a rota

Activities are tried in order and the first eligible one wins. That makes "go and sell when the
bags are full" just an activity with a condition on it, sitting above the others, rather than a
special case in the scheduler:

| Activity | Condition | Duration |
| --- | --- | --- |
| Sell | `BagsFullPercent >= 90` | — |
| Dungeon | `Level >= 60` | 1 hour |
| Quest | — | 2 hours |
| Grind | — | — |

Conditions use the same seven-term language as profiles, so anything a profile step can ask
about, an activity can too. See [profiles.md](profiles.md).

## Two rules stop it thrashing

**A minimum dwell.** An activity runs for at least `MinimumDwell` before anything may displace
it. Without that, an activity whose condition sits on a boundary — bags exactly at the
threshold, health hovering at a limit — is swapped in and out several times a second, and the
character stands still doing neither. This is the single most important setting in a plan.

**Pre-emption waits for the boundary.** A more-preferred activity that becomes eligible does not
cut in immediately; it waits for the dwell to elapse. Same reason.

The one thing that does *not* wait is a condition that stops holding. A dwell exists to stop
dithering between things worth doing, not to keep the character mining with full bags, so an
activity whose conditions fail ends at once.

**Expiry hands over to the next activity, not back to the top.** A plan of "an hour of this,
then an hour of that" would otherwise re-select the most preferred activity forever and never
reach the second hour.

When nothing is eligible the scheduler says so and the character stands still, rather than
picking something arbitrary. That is a plan to fix, not a state to paper over.

## Gathering on the way

`OpportunisticGathering` is the piece that makes a character look like a person playing rather
than a program executing a plan. A player questing through Elwynn who walks past a copper vein
mines it. One who walks past nine and mines none, or who abandons the quest to strip the zone,
looks like neither.

So the whole design is limits:

- **A short detour range** (40 yards by default). The point is "there is a vein on the way", not
  "abandon the quest and go mining" — that is what a gathering *activity* is for.
- **A cap on how many can be taken in a row**, then a rest. A dense field would otherwise hold a
  questing character indefinitely: every node it walks to puts it within range of the next.
- **A per-node cooldown**, so it does not loop between two respawning nodes.
- **Never in combat, and never with something targeted.** Stopping to mine while a wolf chews on
  the character is the single most obvious bot behaviour there is.
- **A node it cannot reach is given up on** rather than walked into all night.

Remove any one of them and this stops being opportunism and becomes a second bot base fighting
the first for control.

Node entries come from you. Nothing is assumed: ids differ between servers, and inventing a list
would be the same kind of unverified fact this project refuses elsewhere.

## Professions

**No recipe data ships with this project, and none is needed.** What a character can make is in
its own spellbook, and the client will list it when the trade skill window is open — names,
difficulty colours, and how many the materials in the bags allow. That makes crafting one of the
few things the bot does without asking you to extract anything.

`LuaTradeSkills` reads that list. `BestForSkillUp` picks the highest colour there are materials
for, and never picks grey: making a trivial recipe consumes materials and raises nothing, which
is worse than stopping.

It also reads what each recipe is made from, and how much of it the character is carrying, which
is what tells the crafting base it is short of something and by how much. The item ids come out
of the reagents' own hyperlinks, because the client gives a name and a count and no id — and a
name cannot be matched against a vendor table without knowing which language the client is in.

One thing it does not do: it does not open the window. Casting a profession is a spell like any
other and belongs with the casting that already exists.

## The crafting base

`CraftBotBase` opens a profession, works out what to make, and keeps making it. The client does
the hard part — it knows what the character can make and what the bags can make it from — so the
base only decides what is worth making and keeps asking.

Every way it can stop is a named reason rather than silence, because a crafting session that
quietly does nothing looks exactly like one that is working: no profession named, the window
would not open, the skill is at its training cap, the materials ran out, or the named recipe is
one the character does not know. Once stopped it stays stopped until reset — a session that
restarted itself would burn a night's materials.

### Buying materials

With a world data export loaded, running out of materials sends the character shopping instead
of stopping. The client says what the recipe takes and how much is in the bags; your export says
who sells the difference; `SupplyRun` walks there, buys it, and comes back to where the crafting
was happening — which matters more than it sounds, because a blacksmith crafts at an anvil and
one that buys its flux and stays in the shop has not finished the errand.

It stocks up for several batches rather than one. The walk is the expensive part: going to a
shop for two flux and walking back is most of a minute for twenty seconds of crafting. It buys
everything on its list that a shop stocks while it is standing there, keeps money back for the
repair bill, and buys what the money allows rather than nothing when it cannot afford the lot.

**Most materials cannot be bought at all.** Ore, herbs, leather and cloth come off the world,
not off a shelf, so stopping with `OutOfMaterials` is still the ordinary outcome — and with no
export it is the only outcome, exactly as before. What this buys is the other kind: flux,
thread, dye, salt, vials. Stopping for want of a stack of thread was never a good reason to end
a session.

Two shopping trips at most. A trip that comes back and leaves the recipe still unmakeable means
the bot has misunderstood something, and a third trip would misunderstand it again.

## What is not here

Raising a profession past its training cap. The bot can find a trainer, and it does visit a
*class* trainer to learn abilities — but a profession trainer's window is a different thing, and
from outside, learning the next rank of Blacksmithing and buying a recipe look identical. Rather
than guess and spend the character's money on the wrong row, the crafting base stops with
`Capped` and says a trainer is needed.

## Skinning

A corpse becomes skinnable once its loot has been taken, so the two lists never overlap and the
root tree works them in turn — skinning below looting, which means one trip to each body rather
than two. It sits above resting for the same reason looting does: a corpse is on a timer and the
character's health is not.

Skinning is a setting rather than something the bot reads. A character that walks to every
corpse and fails to skin it is worse than one that never tries.
