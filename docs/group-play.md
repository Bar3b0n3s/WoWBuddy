# Group play

Phase 8 teaches the bot to be in a party: to follow, to assist, to heal someone other than
itself, and to not be the reason a run fails.

That last one is the design brief. A bot in a party is judged by four other people in real
time, and every way it can go wrong is social: pulling extra packs, breaking crowd control,
standing in the wrong place, being three rooms behind. So the rules are deliberately
conservative.

## Roles are set by you, not detected

`PartyRole` decides whether the bot pulls, holds back, or stands at range. Getting it wrong is
the difference between a run and a wipe, so the setting is authoritative.

The 3.3.5a client does know about roles — the Dungeon Finder assigns them — but this project has
not verified how to read them, and it does not guess. If `UnitGroupRolesAssigned` turns out to
exist on 3.3.5a it could pre-fill the setting; it should still not override it, because a player
can be assigned a role by the finder and be playing another.

## Assist, never pull

| Role     | What it attacks                                                     |
| -------- | ------------------------------------------------------------------- |
| Tank     | the nearest thing it is allowed to fight, once the group is together |
| Healer   | nothing, unless something is already hitting it                      |
| Damage   | whatever the tank is on; failing that, whatever is hitting it        |

The anchor is the tank, or the leader when nobody has been told their role — which is most
groups. If the anchor is targeting something the bot cannot see as an enemy, such as a friendly
NPC it is talking to, the answer is to attack nothing.

A tank will not pull while anyone alive and connected is further away than
`GroupWaitDistance`. It does not wait for the dead: waiting for a corpse run before every pull
would stop the run entirely, and the group's own decision to keep going is not the bot's to
override.

## Parties and raids

Two sets of unit ids, and they do not behave the same way. 3.3.5a counts the party as the
members *other than* the character — the opposite of later versions — so `party1` to `partyN`
never includes them. A raid counts everybody, so `raid1` to `raidN` does, and the character has
to be skipped explicitly or it ends up in its own group list, where a healer would cheerfully
heal it twice.

A character in a raid is also in a party as far as `GetNumPartyMembers` is concerned, so the
raid check comes first. The other way round reports four people out of twenty-five.

Everything above the reader — following, assisting, healing, the dungeon base — sees the same
shape of data either way and does not know which it came from.

## Following

Two distances, not one. The bot starts moving past `FollowDistance` and stops at
`StopDistance`, and the gap between them is the whole point: following on a single distance
produces a character that starts and stops many times a second, which wastes the tick, fights
the movement controller, and looks exactly like a bot.

Past `GiveUpDistance` it stops and says so. Someone that far away has zoned, hearthed, taken a
flight path or died somewhere else, and pathing across a continent to catch up is worse than
stopping.

It prefers the client's own follow, which keeps a sensible distance, handles doorways and looks
like a person following someone. Pathing to a moving target is the fallback.

Healers stand off at `StandOffRange` on the line back towards wherever they already are —
deliberately not a fixed compass offset, which puts a healer in the fire as often as out of it.

## Healing

`ICombatContext` gained `Group` and `CastOn`, both defaulted, so every routine written before
groups existed compiles and behaves exactly as it did. A rotation that never mentions the group
is a solo rotation, which is what most of them are.

`CastOn` is separate from `Cast` because healing someone must not disturb the character's own
target: retargeting to heal and back loses casts, breaks channels, and on a damage character
means the next attack goes at whatever was healed.

`GroupHealing.MostHurt` picks lowest health first and counts the character itself as a group
member. The failure that kills groups is not the wrong spell, it is the wrong person — topping
up a damage dealer at 70 per cent while the tank drops from 30 to nothing. A healer that never
heals itself dies with the group at full health, which helps nobody either.

The root tree gained one branch for this. A healer stood behind the tank is not in combat and
has nothing targeted, so the combat branch passes it over entirely — and the tank dies while the
bot considers what to do next. `HandleGroupSupport` fills that gap, and reports failure even
when the routine acted, so that a damage character still reaches the bot base that picks it
something to assist with. Resting is also now blocked while anyone in the group is fighting.

## Battlegrounds

The battleground base queues, accepts the port, goes to the places its plan names, clicks the
flags it finds there, fights what it meets, and leaves when the match ends.

**Stated plainly: it fights and moves and does not get thrown out for standing still. It does
not play Warsong Gulch well.** It has no idea which team holds what, it does not call incoming,
it will not decide the flag carrier needs help, and it cannot tell a defended base from an empty
one. Everything it knows about a battleground is a list of places worth being, in order,
supplied by you.

That is a deliberate stopping point rather than an unfinished one. Real battleground play is
per-battleground logic driven by the score and objective state, and pretending to have it would
produce a bot that looks competent for thirty seconds.

Two things in it are worth more than they look:

- **Flags generalise.** Capturing a base, taking a flag and returning one are all "walk to a
  thing and click it", so a post with a game object entry covers all of them in every
  battleground without a line of per-battleground scripting.
- **The leash.** The classic battleground bot failure is chasing one runner to the other end of
  the map while the objective it was standing on changes hands behind it. The bot will not
  chase past `ChaseRange` from its current post.

Plans come from a profile — `BattlegroundPlan.FromProfile` reads every step that names a place —
so there is one file format for users rather than a second one invented for battlegrounds.
Battleground layouts are game data, so no plan ships with this project.

The Lua behind queueing is **not verified**. Section 7 of
[phase-8-manual-test.md](phase-8-manual-test.md) is how to check it, and it is the first thing
to run before trusting any of this.

## What is deliberately not here

**Boss encounters.** Every fight worth scripting is scripted differently, and the data that
would drive it — which spell to run from, where the safe ground is, when the add spawns — is
per-encounter game data this project does not ship and cannot invent. A bot that pretended
otherwise would look competent right up until it stood in the fire. The dungeon base fights
bosses exactly as it fights anything else.

**Leading a run without a route.** The bot does not know where an instance goes. A route is
per-dungeon data and comes from a profile; without one, a tank follows instead of guessing, and
says so once.
