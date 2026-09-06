-- Exports the slices of an AzerothCore world database that WoWBuddy uses.
--
-- Run it against YOUR OWN server database. No data from it is ever committed to this
-- repository: it is derived from Blizzard's game files, the same as navigation meshes.
--
--   mysql -u USER -p -N -B acore_world < tools/worlddata/export-azerothcore.sql > worlddata.tsv
--
-- The only difference from the TrinityCore script is the creature spawn table. AzerothCore
-- lets one spawn point rotate between several creatures, so its template column is id1 (with
-- id2 and id3 for the alternatives) where TrinityCore has a single id. Running the wrong
-- script produces an error about an unknown column rather than bad data, which is the
-- failure mode to prefer.

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
-- id1 is the first of the creature ids this spawn point may use.
SELECT 'guid', 'entry', 'map', 'x', 'y', 'z'
UNION ALL
SELECT guid, id1, map, position_x, position_y, position_z
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
