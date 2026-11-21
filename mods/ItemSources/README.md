<h1 align="center"><img src="../../docs/img/art/h-item-sources.svg" alt="Item Sources" width="720"></h1>

<p align="center">
  <a href="../../README.md"><img src="../../docs/img/art/btn-all-mods.svg" alt="All mods" height="46"></a>
  &nbsp;
  <a href="https://github.com/Springs-Tea/melvor-2-mods/releases"><img src="../../docs/img/art/btn-download.svg" alt="Download" height="46"></a>
</p>

> Where does this drop, and what is it for?

The game's item panel says "Found in" and "Used in" with a few skill icons. Hover either row and this
lists what is behind them: in the bank, and on any item's tooltip anywhere in the game once you press Alt
to keep it open.

<p>
  <img src="../../docs/img/item-sources.png" alt="Where Big Bones come from" width="400">
  &nbsp;
  <img src="../../docs/img/item-sources-used.png" alt="What Copper Bars are used in" width="330">
</p>

## Anywhere in the game: Alt

The game shows the same small tooltip for an item wherever the pointer is on one: a recipe's materials, a
shop, loot, equipment. It follows the pointer, so its "Found in" and "Used in" rows cannot be reached.

Press **Alt** while that tooltip is up and it stays where it is. Move onto its two rows, and they work like
the ones in the bank. Alt again, Esc, or a click somewhere else lets it go; Alt on another item moves it
there.

So while you look at a recipe: point at the material you are short of, press Alt, and see where it comes
from.

![A tooltip kept open, with what the item is found in](../../docs/img/item-sources-pin.png)

## What it shows

| Row | You see |
| --- | --- |
| **Found in** | Which enemy drops the item and where that enemy lives; which thieving target, hunting animal, chest, recipe, quest or shop purchase gives it; how many, and how likely. |
| **Used in** | Every recipe, upgrade, shop purchase or animal that uses it, and how many it takes. |

Sources are grouped (Combat, Hunting, Opening, Thieving, the skill of a recipe...) with the likeliest on
top. A long group is cut short so the others stay in sight.

## How it works

The game keeps this knowledge spread over its content: an enemy's drop tables, a recipe's products and
ingredients, a thieving target's loot, what a chest holds. Rather than know each of those classes by name,
the mod walks every content registry once when a character is opened.

```mermaid
flowchart LR
    A["Every content registry<br/>enemies, recipes, items,<br/>shop purchases..."] --> B["Walk each object once<br/>about 150 ms"]
    B --> C{"Does it hold<br/>an item, an amount,<br/>a chance?"}
    C -->|"Products, Drops,<br/>Rewards..."| D["Found in"]
    C -->|"Costs, Ingredients,<br/>Eats..."| E["Used in"]
    D --> F["Index by item"]
    E --> F
    F --> G["Tooltip on hover"]
```

- Chances are worked out from the game's own drop tables: the table's chance times the item's weight in it.
- Because nothing is hard-coded, items and enemies a game update adds show up by themselves.

## Good to know

- A few relations are left out on purpose, because they neither give nor take the item: quest steps and
  Mastery Guild tasks that ask for it, and the pieces of an equipment set.
- The chances are the base ones from the game's data; bonuses to drop chance are not applied.
- The tooltip that Alt keeps open is a second copy of the game's own item panel, so it looks and reads
  exactly like the original. If a game update changes that panel, only this part switches itself off, and
  says so in the log.

## Install

1. Install the [Melvor Mod Loader](https://www.nexusmods.com/melvoridle2/mods/2).
2. Extract the mod's zip into the game folder, so the file ends up as `mods\ItemSources\ItemSources.dll`.

To remove it, delete `mods\ItemSources`. Your save is never changed.
