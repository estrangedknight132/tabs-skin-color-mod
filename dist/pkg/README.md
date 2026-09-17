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
- **RESET ORIGINAL** / **CLEAR SAVED** restore the vanilla team look everywhere —
  in the editor preview *and* for future battle spawns (both remove the saved color)
- Battlefield targeting: last spawned unit, or click a unit to pick it
- Survives the game's team switches (red/blue), pooled unit re-use and the Unit
  Creator's preview rebuilds — no flicker, no color leaking between units
- F8 opens the editor anywhere; F9/F10/F11 dump debug info for troubleshooting

## How to use

1. Open the **Unit Creator** (or spawn units in a battle).
2. Click the **green swatch** next to the red/blue team color squares (top-right),
   or press **F8**.
3. Pick a color, then hit **SAVE TO UNIT**. That's it — the unit keeps its skin.
4. **SAVE TO UNIT** keys off the unit's **name** — name your custom unit first
   (units without a name can't be saved).

Colors are stored in `BepInEx/plugins/TabsSkinColorMod/skin_colors.json` and can be
hand-edited.

## Hotkeys

| Key | Action |
|-----|--------|
| **F8** | Toggle the skin color editor window |
| **F9** | Dump the targeted unit's material/TeamColor info to a file |
| **F10** | Dump the UI hierarchy (button placement debugging) |
| **F11** | Dump every scene renderer using skin materials (with its location) |

Dumps land in `BepInEx/plugins/TabsSkinColorMod/dump_*.txt`.

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
