# Changelog

## 0.2.2

- Anchored the SKIN COLOR button on the real swatch pair: the Unit Creator's visible
  red/blue squares are `UnitEditorTeamToggle` + `UnitEditorTeamButton` children — the
  injector now clones the Red swatch directly and tints it green, so the button sits
  exactly next to the vanilla swatches and matches their styling.

## 0.3.4

- **Fixed tint leaking between units**: the UC reuses the same body/TeamColor
  components when you load or create a different unit, and the previous unit's
  tinted materials stayed registered. Switching units now restores vanilla first,
  then re-seeds from the new unit's saved color (if any).
- The per-frame preview tint only runs when a color is actually wanted (saved, or
  being edited) — untouched units stay vanilla.

## 0.3.3

- UC preview now targets the manager UnitSpawnPoint (which always contains the
  body) instead of the spawnedBase array, which intermittently dropped the body
  during rebuilds — root cause of the remaining flicker.
- Equipment (CharacterItem clothes/weapons) is excluded from preview tinting.

## 0.3.2

- Unit Creator preview color is now re-applied every frame while the UC is open,
  making any flicker invisible regardless of what re-writes it.
- Spawn apply queue delay reduced 0.35s to 0.1s, and diagnostics added when
  SetTeamColor fires without a Unit parent.

## 0.3.1

- Saved colors now apply synchronously on spawn (inside the SetTeamColor hook),
  so units never show the original team color first — fixes the 1-2 s delay on
  TEST UNIT and in battles.
- The Unit Creator preview re-seeds from the saved color every time the UC is
  opened, instead of keeping the last unsaved editor color.

## 0.3.0

- **Fixed battle coloring**: Unit.spawnedObjects can contain body parts (hands,
  base meshes), and blanket-excluding that list prevented any battle apply. Props
  are now identified properly (CharacterItem equipment only).
- Hands and other body parts sharing the team palette materials (without their own
  TeamColor) are now tinted as well.

## 0.2.9

- Hooked TeamColor.UpdateTeamColors: after every game-driven re-tint, tinted
  materials are forced back into every renderer slot that carried a palette
  material (kills residual flicker from any code path).
- Applies to spawned units and the UC preview are now logged for diagnosis.

## 0.2.8

- **Fixed battle spawns for pooled units**: hooked TeamColor.SetTeamColor (runs on
  every spawn, unlike Unit.Awake which only fires once per pooled instance) so saved
  colors are re-applied to every spawned unit.
- **Fixed preview flashing**: hooked UnitEditorManager.RespawnUnit to re-tint the
  rebuilt preview body the same frame.
- Queue de-duplication so multiple team-color components on one unit only trigger
  a single apply.

## 0.2.7

- **Fixed team-color flicker**: hooked TeamColor.GetTeamMaterial so every game-driven
  re-tint (UC preview refresh, team switches, pooled battle-unit re-use) resolves to
  the tinted material instead of fighting the mod.
- **Fixed battle spawns**: units are pooled and Unit.Awake only fires once per pool
  instance; the GetTeamMaterial hook now keeps saved colors across all re-spawns.
- Save key falls back to the UC unit-name field when no blueprint is loaded
  (fixes TARGET showing "?").

## 0.2.6

- **Fixed the Unit Creator preview**: the gamepad-style UC spawns preview bodies as
  plain GameObjects (UnitEditorManager.spawnedBase), not Unit components, so the mod
  never saw them. The editor now targets those preview bodies directly, seeds the
  picker from the current team material, and re-applies the color through preview
  re-spawns. SAVE TO UNIT keys off the edited blueprint.

## 0.2.5

- **Fixed skin detection**: the body is now identified the same way the game does
  it — renderers carrying a TeamColor component — instead of matching renderer
  paths against the unit base prefab (which failed for custom units).

## 0.2.4

- **Fixed skin colors not applying at all**: the body color is driven by TeamColor
  components that swap whole shared palette materials, not by a per-unit _Color.
  The mod now creates per-unit tinted material instances and writes them into each
  TeamColor, so recoloring works in the Unit Creator preview and in battles, and
  survives team switches.
- Unit Creator preview is now detected correctly (target shows UNIT CREATOR).
- F9 dump now includes TeamColor material details.

## 0.2.3

- Fixed the injected button being placed off-screen: the clone is now parented
  inside the RedBlue swatch pair itself (same coordinate space as the vanilla
  swatches) and spaced using the Blue-Raw gap. Placement values are logged.

## 0.2.1

- Fixed the SKIN COLOR button not appearing in Unit Creator screens that don't use
  the standard team-swatch component (e.g. the GamepadUI scene): the injector now
  tries several anchor strategies and falls back to a standalone button pinned to
  the top-right of the screen.
- New config: `UcButtonEnabled`.
- New debug hotkey **F10**: dumps the full UI hierarchy to
  `BepInEx/plugins/TabsSkinColorMod/dump_ui_*.txt` for troubleshooting button placement.

## 0.2.0

- SKIN COLOR button is now injected directly into the Unit Creator's own UI canvas,
  next to the red/blue team color swatches (cloned from the swatches, so it matches
  the game's styling).
- Editor window opens via the canvas button or F8; battlefield unit picking added.
- Core: fixed a JSON parser hang when hand-edited color files contained non-string
  values.

## 0.1.0

- Initial release: per-unit Wobbler skin recoloring in the Unit Creator and battles,
  saved colors that persist across restarts, hotkey editor (F8), material dump (F9).
