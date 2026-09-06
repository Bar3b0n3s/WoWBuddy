# Phase 8 manual test script

Phase 8 adds group play: following, assisting, healing someone other than yourself, and
battlegrounds. Complete phases [1](phase-1-manual-test.md) to [5](phase-5-manual-test.md)
first, and read [group-play.md](group-play.md) for why the rules are what they are.

Most of the decisions here are unit tested. What cannot be tested away from a client is
whether they are being fed the right facts, and — for battlegrounds — whether the Lua behind
them works at all.

## 1. Reading the group

With the character in a party of at least two:

```
dotnet run --project tools/WoWBuddy.Inspector -c Release -- lua
lua> GetNumPartyMembers()
```

**Expect** the number of *other* people in the party, so one fewer than the party size. 3.3.5a
counts the character itself separately, which is the opposite of what later versions do — a
group adapter that gets this wrong is off by one and never sees the last member.

```
lua> UnitName("party1")
lua> UnitGUID("party1")
lua> UnitIsUnit("party1", "party1")
```

**Expect** a name and a GUID string. If `UnitGUID` returns nil, party units are not resolving
and nothing else in this phase will work.

```
lua> UnitHealth("party1") .. "/" .. UnitHealthMax("party1")
```

**Expect** two numbers. The healer picks who to heal from these, so a maximum of zero — which
happens for members out of range on some clients — must be treated as "unknown", not as
"dead". Watch for a healer that fixates on someone across the map.

## 2. Roles

There is no role detection in this bot: the role is a setting, and it decides whether the
character pulls, holds back or stands at range.

Worth checking on your client, because if it works it would make a good default:

```
lua> UnitGroupRolesAssigned("party1")
```

**If this errors**, the function does not exist on your build and the setting is the only
source — which is what the bot assumes. **If it returns a string**, note it in an issue; it
could pre-fill the setting, though it should never override it. A player assigned a role by the
Dungeon Finder may be playing another one.

## 3. Following

Set the role to Damage, join a group, and have someone walk away.

Watch the distance at which the character sets off and the distance at which it stops. **Expect
two different distances** — moving at about twelve yards, stopping at about six.

**The failure to look for** is a character that shuffles forward and back on the spot. That
means the band has collapsed to a single distance, and it is both useless and one of the more
obvious things a watching player notices.

Then have the group member take a flight path or hearth. **Expect the bot to stop and say so**,
not to start walking across the continent.

## 4. Assisting

Still as Damage, with a tank in the group:

1. Have the tank attack something. **Expect** the bot to attack the same thing.
2. Have the tank target a friendly NPC. **Expect** the bot to attack nothing.
3. Have something attack the bot while the tank is on something else. **Expect** the bot to
   fight back — standing there being eaten helps nobody.

**Expect it never to pull anything by itself.** That is the rule the whole base is built
around.

## 5. Tanking

Set the role to Tank and give the profile a route.

1. **Expect** the character to walk the route and pull what it meets.
2. Have a group member fall a long way behind. **Expect the bot to stop pulling and wait.**
3. Have a group member die and stay dead. **Expect the bot to keep going** — waiting for a
   corpse run before every pull would stop the run entirely.

Without a route, **expect it to follow instead**, and to say once that it does not know where
the instance goes.

## 6. Healing

As a Holy Priest in a group:

1. Let a group member take damage while the character is at full health. **Expect a heal on
   them, not on the character.**
2. Let the character and a member both drop, the member lower. **Expect the heal to go to the
   member.**
3. Let the character drop lowest. **Expect it to heal itself** — a healer that never does dies
   with the group at full health.
4. Watch what happens the moment the tank pulls, before the healer is in combat. **Expect the
   routine to act anyway.** If the bot stands there until something hits it, the group-support
   branch is not firing.
5. **Expect it not to sit down to drink while the group is fighting.**

## 7. Battlegrounds: the unverified part

**This is the section that matters most, because the battleground verbs are written against Lua
this project has not confirmed.** Everything below is a check, not a demonstration.

Outside a battleground:

```
lua> GetBattlefieldStatus(1)
```

**Expect** one of `none`, `queued`, `confirm` or `active`. Note what your client returns and in
what argument order — the interface assumes a one-based index.

```
lua> RequestBattlegroundInstanceInfo(1)
lua> JoinBattlefield(0)
```

**Expect** to end up in a queue. Note which index each call wants; they are not necessarily the
same one, and this is the single most likely thing to be wrong.

When the invitation appears:

```
lua> AcceptBattlefieldPort(1, 1)
```

**Expect** to be ported in. And on the way out:

```
lua> LeaveBattlefield()
```

**If any of these are protected** — the call returns nothing and the client logs a taint error —
say so in an issue. The bot's Lua runs through the client's own entry point and carries no
taint (see [phase-2](phase-2-manual-test.md)), so this is unlikely, but it has not been
confirmed for these particular functions and should not be assumed.

## 8. Battlegrounds: playing one

With a plan whose posts you wrote from your own client's coordinates:

1. **Expect the character to move behind the gates rather than stand perfectly still.** Standing
   still is one of the few things a battleground actually watches for.
2. Once the gates open, **expect it to go to the first post**.
3. On a post with a flag or banner entry, **expect it to click it**.
4. **Expect it to stop chasing at about thirty yards from its post.** A bot that follows one
   runner to the other end of the map while its objective changes hands is the classic failure.
5. At the end, **expect it to leave and queue again**.

## 9. The role reaches the dungeon base

A short one, because it is the check that would have caught a real bug: the window collected a
role and the bot was never given it, so the Dungeon base refused to start whatever you chose.

1. Choose a role in the window.
2. Choose the Dungeon base and press Start.
3. **Expect the status line to name the role you chose**, not "Choose a role before running the
   dungeon base".

## What is still unverified after this script

- Every battleground Lua call in section 7, until you have run it.
- Whether `UnitGroupRolesAssigned` exists on 12340.
- Boss encounters. The dungeon base fights a boss exactly as it fights anything else, because
  the data that would drive anything better is per-encounter game data this project does not
  ship and cannot invent.
- Anything about which team holds what in a battleground. The bot has no idea, and does not
  pretend to.
