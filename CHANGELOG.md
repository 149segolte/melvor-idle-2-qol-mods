# Changelog

Every mod has a version of its own; this file lists the releases of the repository as a whole.

## Combat Simulator 0.2.0

- **Where** is a list you tick: one area, several, a whole category, or nothing for everything. "Best for"
  uses it too, so a search can be kept to the areas you care about.
- **Spells**: when the weapon casts spells, choose which one to fight with; two spells are compared like two
  sets of equipment. With unlimited supplies the copy is handed the runes.
- **History**: the last ten scans of a character are kept on disk and can be looked at again, table and
  details, without simulating anything.
- **Faster**: up to four fights are simulated side by side (about one and a half times as fast; `Workers` in
  `settings.json` changes it).
- **Fixed, important**: every simulated fight left about 120 MB of memory behind until the game was closed,
  so a scan of everything could use tens of gigabytes. The game's settings kept each copy alive; the copy's
  subscriptions are now removed when its fight is over. Measured: 152 fights now grow the game by under
  1 GB, where 38 used to add 5 GB.
- A weapon with a single attack stance is no longer simulated "per stance" in Best for, and such results are
  shared between the two modes.
- Dungeons were run for the first time (two dungeons, an hour each) and give sensible numbers.
- The Fight button also selects the spell of the result, when the result was simulated with what is worn.

## 0.1.0

First release. For Melvor Idle 2 0.11.6-alpha.1 with Melvor Mod Loader 0.1.3.

- **Combat Simulator 0.1.0**: simulates fights by running the game's own combat on a copy of the character.
  Areas, dungeons or everything unlocked; saved loadouts and per-slot item swaps with a search; a "Best for"
  scan that ranks every unlocked fight for a skill; a comparison with the same fight in other equipment; a
  button that starts the selected fight.
- **Item Sources 0.1.0**: on the bank's item panel, what is behind "Found in" and "Used in": enemies and
  where they live, recipes, chests, thieving targets, shop purchases, with quantities and chances.
- **Recipe Rates 0.1.0**: XP per hour on every recipe of a skill's list, and the whole hour in the XP
  tooltip.
- **Combat Goals 0.1.0**: XP per hour and time to a level goal for every combat skill being trained.
- **Kills in Combat 0.1.0**: the kill count of the current enemy next to its name.
- **Dungeon Completions 0.1.0**: the completion count of each dungeon in the combat areas menu.

Known gaps in this release: the simulator's dungeon runs, its "from bank" supplies and the Fight button
were built and rendered but not exercised; the simulator's numbers were compared with the game once
(4,188 XP per hour simulated against 4,125 on the game's ten minute average).
