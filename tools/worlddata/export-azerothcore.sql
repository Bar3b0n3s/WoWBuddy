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
SELECT 'entry', 'name', 'npcflag'
UNION ALL
SELECT entry, REPLACE(REPLACE(name, '\t', ' '), '\n', ' '), npcflag
FROM creature_template
WHERE npcflag & (0x80 | 0x1000 | 0x2000 | 0x10000 | 0x20000 | 0x10 | 0x20 | 0x40) <> 0;
