# Offsets for WoW 3.3.5a build 12340

Every low-level fact the bot relies on lives in
[`src/Core/Offsets/Offsets335a.cs`](../src/Core/Offsets/Offsets335a.cs) and
[`src/Core/Offsets/UpdateFields335a.cs`](../src/Core/Offsets/UpdateFields335a.cs). Nothing
outside those files may contain a magic address.

## The rule

A wrong offset does not fail loudly. It returns a plausible-looking number, and the bot then
acts on it: walking to a garbage coordinate, or writing into a memory block that is not the
one it thinks it is. So every constant carries an `[OffsetInfo]` attribute recording where
the value came from and how to check it, and a unit test
(`OffsetCatalogueTests.EveryOffsetDeclaresWhereItCameFrom`) fails the build if one does not.

## Confidence levels

| Level | Meaning |
| --- | --- |
| `ProtocolDefined` | Derived from the 3.3.5 server's own field layout. A server and client that disagreed on these could not communicate, so agreement is strong evidence. The best available short of disassembling the client. |
| `Corroborated` | At least two independent public sources agree. |
| `SingleSource` | One source, unconfirmed. Only used behind a runtime check that would notice a bad value. |
| `Conflicted` | Sources disagree. Resolved against the live client at attach time. |
| `Unverified` | No source. A documented assumption, used only where a wrong value fails safe. |

## Sources

Community offset sites were unreachable from the environment this table was assembled in, so
the values were cross-checked against sources that were reachable. Each is identified in the
table below.

| Key | Source | Notes |
| --- | --- | --- |
| Source A | [`AzDeltaQQ/WotLKRotations`](https://github.com/AzDeltaQQ/WotLKRotations) — `offsets.py`, `WowInjectDLL/offsets.h` | Unlicensed, so **all rights reserved**: read for factual values only, no code used. Internally inconsistent in places, which is noted where it matters. |
| Source B | [`micenote/WoW-3.3.5a-Bot`](https://github.com/micenote/WoW-3.3.5a-Bot) — `AmeisenBot.Utilities/Offsets.cs` | GPL-3.0: read for factual values only, no code used. |
| AzerothCore | [`azerothcore/azerothcore-wotlk`](https://github.com/azerothcore/azerothcore-wotlk) — `UpdateFields.h`, `ObjectGuid.h`, `UnitDefines.h`, `SharedDefines.h` | GPL-2.0: used as the authority for **protocol** constants (descriptor indices, GUID type tags, flag bits), not for client memory offsets. No code used. |

See [legal-and-licensing.md](legal-and-licensing.md) for why reading these for factual values
is acceptable while copying their code is not.

## Still needing first-hand verification

Three entries are not yet backed by evidence and are called out here rather than buried:

| Offset | Status | Plan |
| --- | --- | --- |
| `Offsets335a.UnitPosition.Candidates` | `Conflicted` — source A states 0x798 in one file and 0x9B8 in another | **Resolved automatically at attach.** `PositionResolver` reads the local player through each candidate and keeps the one that yields a finite, in-bounds coordinate that nearby units also agree with. If neither works, attach fails rather than proceeding. |
| `Offsets335a.NameCache.BucketStride` | `Unverified` | Dump the bucket array with the inspector and measure the spacing between successive node pointers. Until then the name cache is advisory: a wrong stride yields *no* name rather than a wrong one, because the node's GUID must still match. |
| `Offsets335a.NameCache.NodeGuid` | `Unverified` | Dump 0x40 bytes at a bucket's first node and look for the local player's low GUID dword. |

Two more things are deliberately absent rather than guessed. The client's `CastSpell`
function has two conflicting published addresses (0x0080DA40 and 0x0080B210) and neither
could be confirmed, so casting is not implemented. `Interact`, `TraceLine` and `UnitReaction`
have no address in any reachable source; interaction will go through click-to-move in phase
3, and line of sight is a phase 3 prerequisite that needs a real address first.

The phase 2 addresses that *are* present are mostly single-sourced, and are covered by the
attach-time self-test in `ExecutionSession`: it makes the client compute a value the bot did
not supply and reads it back, which no wrong address can pass by accident. See
[phase-2-manual-test.md](phase-2-manual-test.md).

Game object positions are also **not implemented**: no reachable source states where a 12340
game object stores its coordinates, so `WoWGameObject.Position` returns zero and says so,
rather than sending a gathering bot to a fabricated location. This is a prerequisite for the
phase 6 gathering base; the method's documentation describes how to find it.

## How to regenerate this table

```
dotnet run --project tools/WoWBuddy.Inspector -- offsets
```

The table below is that command's output, so it cannot drift from the values the bot uses.

## The table

| Offset | Value | Confidence | Source | How to verify |
| --- | --- | --- | --- | --- |
| `Offsets335a.SupportedBuild` | `12340` | Corroborated | The build number of the 3.3.5a client, stated by the client's own version resource and by every source consulted. | Right-click WoW.exe, Properties, Details: the file version reads 3.3.5.12340. ClientBuildDetector reads the same resource at attach. |
| `Offsets335a.DefaultImageBase` | `0x400000` | Corroborated | The standard PE image base for the 2010 client, which is not ASLR-aware; consistent with every published address in the community tables being expressed against it. | Compare against the module base reported by the process at attach. When they differ, Rebase translates every static address, so a relocated client still works. |
| `Offsets335a.ObjectManager.ClientConnection` | `0xC79CE0` | Corroborated | Two independent public 12340 tables agree (source A and source B, docs/offsets.md); matches the value cited in the project brief. | Dereference it: the result must be a readable pointer, and +CurMgr must itself be a readable pointer while a character is in the world. Both are asserted during attach. |
| `Offsets335a.ObjectManager.CurMgr` | `0x2ED0` | Corroborated | Sources A and B agree; matches the brief. | Walking the resulting list must yield an object whose GUID equals the local player GUID at +LocalPlayerGuid. |
| `Offsets335a.ObjectManager.FirstObject` | `0xAC` | Corroborated | Sources A and B agree; matches the brief. | The walk terminates and produces objects whose type values are all within the legal 0-7 range. |
| `Offsets335a.ObjectManager.NextObject` | `0x3C` | Corroborated | Sources A and B agree; matches the brief. | Same as FirstObject: a wrong value produces an immediate walk failure or a cycle, both of which the enumerator detects. |
| `Offsets335a.ObjectManager.LocalPlayerGuid` | `0xC0` | Corroborated | Sources A and B agree; matches the brief. | The GUID read here must match exactly one object of type Player in the enumerated list. Asserted during attach. |
| `Offsets335a.Object.Descriptors` | `0x8` | SingleSource | Source A. Source B does not state it directly for units. | Self-validating and checked at attach: descriptor index OBJECT_FIELD_GUID (0) must contain the same 64-bit GUID as the object's own GUID field at +Guid. If the descriptor pointer is wrong those two cannot agree. |
| `Offsets335a.Object.Type` | `0x14` | Corroborated | Sources A and B agree. | Every enumerated object must report a value in 0-7. Asserted during attach. |
| `Offsets335a.Object.Guid` | `0x30` | Corroborated | Sources A and B agree. | Must equal descriptor field OBJECT_FIELD_GUID for the same object. |
| `Offsets335a.UnitPosition.Candidates` | `pos=0x798, facing=0x7A8 | pos=0x9B8, facing=0x9C4` | Conflicted | Source A states 0x798 in one file and 0x9B8 in another; source B does not state it. **Conflicts:** 0x798 (facing +0x10) vs 0x9B8 (facing +0xC) | Resolved at runtime by PositionResolver: the candidate must yield a finite in-bounds coordinate for the local player that also tracks movement. If neither candidate resolves, attach fails. |
| `Offsets335a.NameCache.Store` | `0xC5D940` | Corroborated | Sources A and B agree, both expressed as 0xC5D938 + 0x8. | Looking up the local player's own GUID must return the character name shown in-game. Asserted by the inspector tool. |
| `Offsets335a.NameCache.Mask` | `0x24` | Corroborated | Sources A and B agree. | A sane mask is a small power-of-two-minus-one; a wrong read produces an absurd value and the lookup is skipped. |
| `Offsets335a.NameCache.Base` | `0x1C` | Corroborated | Sources A and B agree. | See Store. |
| `Offsets335a.NameCache.NodeName` | `0x20` | Corroborated | Sources A and B agree. | See Store. |
| `Offsets335a.NameCache.NodeNext` | `0xC` | SingleSource | Source A only; source B does not state it. | Bucket walks are bounded and abandoned on an implausible pointer, so a wrong value degrades to 'name unavailable' rather than a bad read. |
| `Offsets335a.NameCache.NodeGuid` | `0x0` | Unverified | No source. TODO: verify. | Attach the inspector, dump 0x40 bytes at a bucket's first node, and look for the local player's low GUID dword. The name string at NodeName gives a known-good anchor to dump from. |
| `Offsets335a.NameCache.BucketStride` | `0xC` | Unverified | No source. TODO: verify. | Dump the bucket array and measure the spacing between successive non-null node pointers. Until then the name cache is advisory: a wrong stride yields no name rather than a wrong name, because the GUID in the node must still match. |
| `Offsets335a.ClientState.IsInWorld` | `0xBD0792` | SingleSource | Source B only. | Cross-checked against the object manager: 'in world' must coincide with a resolvable local player object. Attach relies on the object-manager check, not on this flag alone. |
| `Offsets335a.ClientState.IsLoadingScreen` | `0xB6AA38` | SingleSource | Source B only. | Should be non-zero exactly while zoning. Advisory only. |
| `Offsets335a.ClientState.MapId` | `0xAB63BC` | SingleSource | Source B only. | Compare against the known map ids: 0 Eastern Kingdoms, 1 Kalimdor, 530 Outland, 571 Northrend. The inspector prints it so it can be eyeballed against the character's actual location. |
| `Offsets335a.ClientState.ZoneId` | `0xBD080C` | SingleSource | Source B only. | Compare with the zone shown in-game via the inspector. |
| `Offsets335a.Execution.FrameScriptExecute` | `0x819210` | Corroborated | Sources A and B agree (A names it FRAMESCRIPT_EXECUTE, B names it luaDoString). Both call it cdecl with three arguments. | The attach self-test runs a script with a known answer and reads the result back. A wrong address fails that outright. |
| `Offsets335a.Execution.GetActivePlayerObject` | `0x4038F0` | SingleSource | Source B only. | Its return value must equal the local player object address the object manager walk finds independently. The self-test asserts exactly that. |
| `Offsets335a.Execution.GetLocalizedText` | `0x7225E0` | SingleSource | Source B only, which calls it as thiscall with the active player object in ecx, arguments (name, -1), and no caller stack cleanup. | The attach self-test assigns a known string to a global from Lua and reads it back through this function. Nothing else in the bot uses it until that passes. |
| `Offsets335a.Execution.GameUiTarget` | `0x524BF0` | SingleSource | Source B only, which pushes the GUID as two dwords, high first, and cleans up eight bytes. | Call it with a known unit's GUID and confirm the local player's UNIT_FIELD_TARGET descriptor changes to match. NativeFunctions.TargetSelfTest does this on demand. |
| `Offsets335a.Execution.D3DDevicePointer1` | `0xC5DF88` | Corroborated | Sources A and B agree. | The resolved EndScene pointer must lie inside d3d9.dll's loaded address range. EndSceneHook refuses to install if it does not. |
| `Offsets335a.Execution.D3DDevicePointer2` | `0x397C` | Corroborated | Sources A and B agree. | See D3DDevicePointer1. |
| `Offsets335a.Execution.D3DEndSceneVTableOffset` | `0xA8` | Corroborated | Sources A and B agree, and it matches the documented IDirect3DDevice9 vtable layout: EndScene is method 42, so 42 * 4 = 0xA8. | See D3DDevicePointer1. |
| `Offsets335a.ClickToMove.Base` | `0xCA11D8` | SingleSource | Source B only. | Right-click-move in-game and read the block: the destination floats must match where you clicked, before anything is ever written. ClickToMoveWriter.Read exists for exactly this check. |
| `Offsets335a.ClickToMove.Action` | `0x1C` | SingleSource | Source B only. | See Base. |
| `Offsets335a.ClickToMove.InteractGuid` | `0x20` | SingleSource | Source B only. | See Base. |
| `Offsets335a.ClickToMove.DestinationX` | `0x8C` | SingleSource | Source B only. | See Base. |
| `Offsets335a.ClickToMove.StopDistance` | `0xC` | SingleSource | Source B only. | See Base. |
| `UpdateFields335a.Object.Guid` | `0x0` | ProtocolDefined | AzerothCore 3.3.5 EObjectFields; agrees with source A. | Must equal the object's inline GUID at object+0x30. Asserted at attach. |
| `UpdateFields335a.Object.Type` | `0x2` | ProtocolDefined | AzerothCore 3.3.5 EObjectFields. | Bit 0 (Object) is set for every object. |
| `UpdateFields335a.Object.Entry` | `0x3` | ProtocolDefined | AzerothCore 3.3.5 EObjectFields. | For a creature it matches the creature_template id on the server. |
| `UpdateFields335a.Object.ScaleX` | `0x4` | ProtocolDefined | AzerothCore 3.3.5 EObjectFields. | A float that is normally 1.0. |
| `UpdateFields335a.Unit.SummonedBy` | `0xE` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields; agrees with source A (0x0E). | For a hunter pet it equals the owner's GUID. |
| `UpdateFields335a.Unit.Target` | `0x12` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields; agrees with source A (0x12). | Target something in-game and confirm the local player's value matches the target's GUID. |
| `UpdateFields335a.Unit.Bytes0` | `0x17` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields (byte offset 0x5C agrees with source A). | Byte 0 is race, byte 1 class; compare against the character actually logged in. |
| `UpdateFields335a.Unit.Health` | `0x18` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields; agrees with source A (0x18). | Compare with the health shown on the player frame. |
| `UpdateFields335a.Unit.Power1` | `0x19` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields; agrees with source A (0x19). | Read at the index given by the unit's own power type from Bytes0 and compare with the player frame. |
| `UpdateFields335a.Unit.MaxHealth` | `0x20` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields; agrees with source A (0x20). | Compare with the player frame. |
| `UpdateFields335a.Unit.MaxPower1` | `0x21` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields; agrees with source A (0x21). | Compare with the player frame. |
| `UpdateFields335a.Unit.Level` | `0x36` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields; agrees with source A (0x36). | Compare with the character's level. Must be 1-83 for anything the bot will meet. |
| `UpdateFields335a.Unit.FactionTemplate` | `0x37` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields. | Alliance and Horde player characters report different values; compare two characters. |
| `UpdateFields335a.Unit.Flags` | `0x3B` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields (byte offset 0xEC agrees with source A). | Sit down in-game and confirm the Sitting/standing state bit changes. |
| `UpdateFields335a.Unit.Flags2` | `0x3C` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields. | See Flags. |
| `UpdateFields335a.Unit.BoundingRadius` | `0x41` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields. | A small positive float, typically well under 5.0. |
| `UpdateFields335a.Unit.CombatReach` | `0x42` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields. | A small positive float. |
| `UpdateFields335a.Unit.DisplayId` | `0x43` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields. | Changes when the unit shapeshifts or mounts. |
| `UpdateFields335a.Unit.MountDisplayId` | `0x45` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields. | Mount up in-game and confirm it becomes non-zero. |
| `UpdateFields335a.Unit.DynamicFlags` | `0x4F` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields. | Kill a mob and confirm the lootable bit appears. Needed from phase 5 (looting). |
| `UpdateFields335a.Unit.NpcFlags` | `0x52` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields. | Compare a known vendor against a known trainer. Needed from phase 5. |
| `UpdateFields335a.Unit.Bytes1` | `0x4A` | ProtocolDefined | AzerothCore 3.3.5 EUnitFields. | Byte 0 is the stand state; sit down and confirm it changes. |
| `UpdateFields335a.Player.Bytes` | `0x99` | ProtocolDefined | AzerothCore 3.3.5 EPlayerFields. | Cosmetic only; a wrong read is harmless. |
| `UpdateFields335a.Player.Xp` | `0x27A` | ProtocolDefined | AzerothCore 3.3.5 EPlayerFields. | Compare with the XP bar; gain XP and confirm it rises. Drives the XP/hour readout. |
| `UpdateFields335a.Player.NextLevelXp` | `0x27B` | ProtocolDefined | AzerothCore 3.3.5 EPlayerFields. | Compare with the XP bar maximum. |
| `UpdateFields335a.Player.Coinage` | `0x492` | ProtocolDefined | AzerothCore 3.3.5 EPlayerFields. | Compare with the backpack gold display. Drives the gold/hour readout. |
| `UpdateFields335a.Player.QuestLog1_1` | `0x9E` | ProtocolDefined | AzerothCore 3.3.5 EPlayerFields. | Field 0 of an entry is the quest id; compare with the quest log in-game. Needed from phase 7. |
| `UpdateFields335a.Player.QuestLogEntrySize` | `0x5` | ProtocolDefined | AzerothCore 3.3.5 quest log layout (id, state, counts, timer). | Entry n's quest id sits at QuestLog1_1 + n * QuestLogEntrySize. |
| `UpdateFields335a.Player.InvSlotHead` | `0x144` | ProtocolDefined | AzerothCore 3.3.5 EPlayerFields. | Head slot GUID must match the equipped helm's item GUID. Needed from phase 5. |
| `UpdateFields335a.Item.Owner` | `0x6` | ProtocolDefined | AzerothCore 3.3.5 EItemFields. | For an item in the player's bags it equals the local player GUID. |
| `UpdateFields335a.Item.StackCount` | `0xE` | ProtocolDefined | AzerothCore 3.3.5 EItemFields. | Compare with the stack count shown on the bag icon. |
| `UpdateFields335a.Item.Durability` | `0x3C` | ProtocolDefined | AzerothCore 3.3.5 EItemFields. | Compare with the item tooltip. Drives repair runs from phase 5. |
| `UpdateFields335a.Container.NumSlots` | `0x40` | ProtocolDefined | AzerothCore 3.3.5 EContainerFields. | Compare with the bag's actual size. |
| `UpdateFields335a.Container.Slot1` | `0x42` | ProtocolDefined | AzerothCore 3.3.5 EContainerFields. | Slot GUIDs must resolve to Item objects in the object manager. |
| `UpdateFields335a.GameObject.DisplayId` | `0x8` | ProtocolDefined | AzerothCore 3.3.5 EGameObjectFields. | Distinct per node type; herb and ore nodes differ. |
| `UpdateFields335a.GameObject.Flags` | `0x9` | ProtocolDefined | AzerothCore 3.3.5 EGameObjectFields. | A locked chest has the locked bit set. |
| `UpdateFields335a.GameObject.Bytes1` | `0x11` | ProtocolDefined | AzerothCore 3.3.5 EGameObjectFields. | Open a door and confirm byte 0 changes. Needed from phase 6 (gathering). |
| `UpdateFields335a.DynamicObject.Caster` | `0x6` | ProtocolDefined | AzerothCore 3.3.5 EDynamicObjectFields; agrees with source B (0x06). | Cast a ground-targeted spell and confirm it equals the local player GUID. |
| `UpdateFields335a.DynamicObject.SpellId` | `0x9` | ProtocolDefined | AzerothCore 3.3.5 EDynamicObjectFields; agrees with source B (0x09). | Compare with the cast spell's id. Needed for ground-effect avoidance in later phases. |
| `UpdateFields335a.DynamicObject.Radius` | `0xA` | ProtocolDefined | AzerothCore 3.3.5 EDynamicObjectFields; agrees with source B (0x0A). | A positive float matching the spell's radius. |
| `UpdateFields335a.Corpse.Owner` | `0x6` | ProtocolDefined | AzerothCore 3.3.5 ECorpseFields. | After dying, exactly one corpse reports the local player GUID. Needed for corpse runs in phase 4. |
| `UpdateFields335a.Corpse.DisplayId` | `0xA` | ProtocolDefined | AzerothCore 3.3.5 ECorpseFields. | Cosmetic; a wrong read is harmless. |
