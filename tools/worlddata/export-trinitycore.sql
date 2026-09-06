-- Exports the slices of a TrinityCore 3.3.5 world database that WoWBuddy uses.
--
-- Run it against YOUR OWN server database. No data from it is ever committed to this
-- repository: it is derived from Blizzard's game files, the same as navigation meshes.
--
--   mysql -u USER -p -N -B world < tools/worlddata/export-trinitycore.sql > worlddata.tsv
--
-- -N drops the column-name row that mysql would otherwise print per statement, and -B makes
-- the output tab-separated. The reader wants one header line per file, so run the four
-- statements separately as shown in docs/world-data.md, or split the combined output.
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
-- Only creatures that offer a service the bot can use, which is a small fraction of the table.
SELECT 'entry', 'name', 'npcflag'
UNION ALL
SELECT entry, REPLACE(REPLACE(name, '\t', ' '), '\n', ' '), npcflag
FROM creature_template
WHERE npcflag & (0x80 | 0x1000 | 0x2000 | 0x10000 | 0x20000 | 0x10 | 0x20 | 0x40) <> 0;
