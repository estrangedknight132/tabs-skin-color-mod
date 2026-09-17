# Changelog

## 0.3.8

- **Clothes no longer get recolored**: team-colored clothes use the same palette
  materials as the body, and the "hands fix" sweep from 0.3.0 matched them too.
  Equipment renderers are now excluded both by the item registry AND by
  CharacterItem ancestry, including bone-attached pieces the game re-parents
  outside the item's transform (via the item's own GameObject list).
- **TEST UNIT enemies keep vanilla colors**: the game's own
  `UnitEditorManager.isTestingUnit` flag is now respected — only the unit being
  tested uses its saved color; the enemies spawned for the test are untouched.

## 0.3.7

- **Fixed the stuck green on new units for good** (F11 dump evidence): the UC's
  visible display body is a scene-root `Humanoid(Clone)` carrying a Unit with a
  stale template key, so the store hooks kept re-applying the previous unit's
  saved color to it. Preview bodies are now registered per UC session and every
  store-driven hook (GetTeamMaterial / SetTeamColor / tracker) skips them —
  preview colors come solely from the editor, battle units solely from the store.
- Restore now also recognizes Unity-instantiated material copies
  (`*_SkinTinted (Instance)`) by name, so a reset undoes tints even after the
  game has copied them onto a renderer.
- Unit switches restore vanilla BEFORE re-seeding, so the orphan sweep can no
  longer be fooled into skipping the preview body.

## 0.3.6

- **Fixed the persistent green on fresh units**: the UC's visible display model can
  carry the team palette materials *without* a `TeamColor` component (same path as
  hands), so it got tinted by one unit and vanilla-restore never touched it — every
  subsequently loaded unit inherited the color. Restore now covers **every** renderer
  slot holding a tinted instance, and a scene-wide orphan sweep returns any renderer
  outside the tracked roots to vanilla on unit switches/resets.
- New **F11** diagnostic: dumps every scene renderer using skin/palette materials,
  with its full transform path — pinpoints exactly where display bodies live.

## 0.2.2

- Anchored the SKIN COLOR button on the real swatch pair: the Unit Creator's visible
  red/blue squares are `UnitEditorTeamToggle` + `UnitEditorTeamButton` children — the
  injector now clones the Red swatch directly and tints it green, so the button sits
  exactly next to the vanilla swatches and matches their styling.

## 0.3.5

- **Fixed "reset" leaving wrong colors**: RESET ORIGINAL / CLEAR SAVED now restore
  vanilla visuals AND remove the saved color, so the Unit Creator preview and battle
  spawns both revert to the vanilla team look.
- **Fixed black seeding**: with no saved color the picker resets to the default tan
  instead of reading the palette material's `_Color` (unused by the game's shaders,
  so it read as black).
- **Fixed stale tints on the UC display model**: tinting covers the whole editor +
  preview manager roots, and all tints flow through one (source, color)-keyed cache
  driven by a single "wanted color" lookup — no color leaks between units, and a
  reset is instantly vanilla everywhere.

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
