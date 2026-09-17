# TABS Skin Color Mod

Recolor the **skin (body) of Wobblers** — something the vanilla Unit Creator doesn't
let you do — with a button added **right next to the red/blue team color swatches**
in the Unit Creator (top-right of the BASE tab).

## Features

- **SKIN COLOR** button in the Unit Creator, next to the team swatches
- Color picker with RGB sliders, HEX input, preset swatches and a randomizer
- Works in the **Unit Creator** (live preview) and in **battles**
- **SAVE TO UNIT** — the color is remembered per unit and applied every time the
  unit spawns, even after restarting the game
- **RESET ORIGINAL** restores vanilla colors; **CLEAR SAVED** forgets the unit
- Battlefield targeting: last spawned unit, or click a unit to pick it
- F8 opens the editor anywhere; F9 dumps a unit's material info for debugging

## How to use

1. Open the **Unit Creator** (or spawn units in a battle).
2. Click the **green swatch** next to the red/blue team color squares (top-right),
   or press **F8**.
3. Pick a color, then hit **SAVE TO UNIT**. That's it — the unit keeps its skin.

Colors are stored in `BepInEx/plugins/TabsSkinColorMod/skin_colors.json` and can be
hand-edited.

## Notes & limitations

- Colors are cosmetic and **local only** — other players in multiplayer won't see them.
- Two custom units with the same name share a color entry (colors are keyed by name).
- If a particular unit's skin doesn't change, press **F9** while targeting it and
  check `BepInEx/plugins/TabsSkinColorMod/dump_*.txt`; the material name can be added
  to the `MaterialNameFilter` config option
  (`BepInEx/config/com.freebuff.tabs.skincolor.cfg`).

## Requirements

- Totally Accurate Battle Simulator (Steam)
- BepInEx (installed automatically as a dependency by r2modman / the Thunderstore manager)
