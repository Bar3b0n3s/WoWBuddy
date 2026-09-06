-- Exports the slices of a TrinityCore 3.3.5 world database that WoWBuddy uses.
--
-- Run it against YOUR OWN server database, or against a public TrinityCore world dump you have
-- imported. No data from it is ever committed to this repository: it is derived from Blizzard's
-- game files, the same as navigation meshes.
--
--   mysql -u USER -p -N -B world < tools/worlddata/export-trinitycore.sql > worlddata.tsv
--
-- -N drops the column-name row that mysql would otherwise print per statement, and -B makes
-- the output tab-separated. The reader wants one header line per file, so run the statements
-- separately as shown in docs/world-data.md, or split the combined output.
--
-- AzerothCore users want export-azerothcore.sql instead: its creature table names its
-- template column id1 rather than id, so this script silently produces nothing there.

-- gameobject-spawns.tsv
SELECT 'guid', 'entry', 'map', 'x', 'y', 'z'
UNION ALL
SELECT guid, id, map, position_x, position_y, position_z
FROM gameobject;

-- gameobject-templates.tsv
SELECT 'entry', 'name', 'type', 'lockId'
UNION ALL
SELECT entry, REPLACE(REPLACE(name, '\t', ' '), '\n', ' '), type, Data0
FROM gameobject_template;

-- creature-spawns.tsv
SELECT 'guid', 'entry', 'map', 'x', 'y', 'z'
UNION ALL
SELECT guid, id, map, position_x, position_y, position_z
FROM creature;

-- creature-templates.tsv
--
-- The whole table now, not just the service NPCs. Rank is the reason: it is what tells the bot
-- an elite from an ordinary mob, and nothing it can read out of memory does. A grinding
-- character that pulls an elite of its own level dies, every time, and only the database knows
-- which is which before the fight starts.
--
-- The first three columns are what older WoWBuddy builds expect, so this file stays readable by
-- them; the rest are ignored by anything that does not know about them.
SELECT 'entry', 'name', 'npcflag', 'faction', 'minlevel', 'maxlevel', 'rank',
       'trainertype', 'trainerclass'
UNION ALL
SELECT entry, REPLACE(REPLACE(name, '\t', ' '), '\n', ' '),
       npcflag, faction, minlevel, maxlevel, rank,
       trainer_type, trainer_class
FROM creature_template;

-- item-templates.tsv
--
-- The whole table, because a loot decision is about an item the character is not carrying yet
-- and so could be about any of them. A few megabytes.
SELECT 'entry', 'name', 'quality', 'itemlevel', 'requiredlevel',
       'class', 'subclass', 'inventorytype', 'sellprice', 'stackable'
UNION ALL
SELECT entry, REPLACE(REPLACE(name, '\t', ' '), '\n', ' '),
       Quality, ItemLevel, RequiredLevel,
       class, subclass, InventoryType, SellPrice, stackable
FROM item_template;

-- npc-vendors.tsv
--
-- What each vendor sells. The bot uses it to work out where to buy crafting materials, which
-- is the difference between a crafting session that stops when the bags run dry and one that
-- goes and gets more.
SELECT 'entry', 'item'
UNION ALL
SELECT entry, item
FROM npc_vendor;

-- quest-templates.tsv
--
-- Objectives, so the questing base knows what a quest actually wants rather than watching the
-- log for a change it cannot interpret. Six item slots and four creature slots is what 3.3.5
-- quests have.
SELECT 'id', 'title', 'minlevel', 'questlevel',
       'npc1', 'npccount1', 'npc2', 'npccount2', 'npc3', 'npccount3', 'npc4', 'npccount4',
       'item1', 'itemcount1', 'item2', 'itemcount2', 'item3', 'itemcount3',
       'item4', 'itemcount4', 'item5', 'itemcount5', 'item6', 'itemcount6'
UNION ALL
SELECT Id, REPLACE(REPLACE(LogTitle, '\t', ' '), '\n', ' '), MinLevel, QuestLevel,
       RequiredNpcOrGo1, RequiredNpcOrGoCount1, RequiredNpcOrGo2, RequiredNpcOrGoCount2,
       RequiredNpcOrGo3, RequiredNpcOrGoCount3, RequiredNpcOrGo4, RequiredNpcOrGoCount4,
       RequiredItemId1, RequiredItemCount1, RequiredItemId2, RequiredItemCount2,
       RequiredItemId3, RequiredItemCount3, RequiredItemId4, RequiredItemCount4,
       RequiredItemId5, RequiredItemCount5, RequiredItemId6, RequiredItemCount6
FROM quest_template;
