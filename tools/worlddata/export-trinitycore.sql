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
SELECT 'entry', 'name', 'npcflag', 'faction', 'minlevel', 'maxlevel', 'rank'
UNION ALL
SELECT entry, REPLACE(REPLACE(name, '\t', ' '), '\n', ' '),
       npcflag, faction, minlevel, maxlevel, rank
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
