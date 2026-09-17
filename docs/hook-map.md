# TABS internals discovered for the skin color mod

Everything below was extracted offline from `Assembly-CSharp.dll` (Steam build,
Unity Mono) with `tools/MetaDump` (Mono.Cecil). No game launch required. Re-run:

```sh
dotnet run --project tools/MetaDump -- --asm third_party/game_assemblies/Assembly-CSharp.dll --il --out docs/meta
```

## Color pipeline

- **`UnitColorHandler`** (`UnityEngine.MonoBehaviour`, sits on unit prefabs)
  - `List<UnitColorInstance> colors` (public)
  - private: `Renderer[] rends`, `MaterialPropertyBlock[] propertyBlocks`, `Color[][] rendColors`
  - `Init()` — collects ALL renderers under `transform.root`, captures each material's
    `_Color` into `rendColors` (from shared materials, via `TFBGames.RendererExtensions.GetSharedMaterialsNonAlloc`),
    and creates property blocks only for materials whose shader is one of:
    `TFBG/SimpleVertexColorUnit`, `TFBG/SimpleTintDiffuseUnit`, `TFBG/VertexColorNormalUnit`
  - `SetColor(UnitColorInstance, float currentValue)` — finds the instance by
    `colorName`, updates `color`/`currentValue`, then `UpdateColors()`
  - `UpdateColors()` — for every renderer/material slot:
    `material.color = Color.Lerp(rendColors[i][j], effectColor, totalValue)` and, for
    TFBG-shader renderers, property-block `_TintColor` = `effectColor`, `_TintLerp` = `totalValue`
  - `effectColor`/`totalValue` are the **death-gray fade** (see coroutine `GoGray(Renderer)`),
    not team colors
- **`UnitColorInstance`** — `colorName: string`, `color: Color`, `currentValue: float`, `acceptWhenDead: bool`
- All `SetColor` callers in the assembly are status effects (ice arrow, potions,
  damage-over-time, crow color) — **nothing re-applies team colors to the body**;
  team identity is shown by the `Landfall.TABS.TeamEdge` outline (`_RedColor`/`_BlueColor`
  shader props in `TeamEdgeTypeSettings`) and UI.

## Units

- **`Landfall.TABS.Unit`** (`BatchedMonobehaviour`)
  - public: `UnitBlueprint unitBlueprint`, `Team Team`, `DataHandler data`,
    `GameObject[] spawnedObjects` (spawned clothes/weapons!), `bool dead`,
    `WeaponHandler WeaponHandler`, `UnitAPI api`
  - `Awake()`, `Start()`, `InitializeUnit(Team)`, `GetAllRenderers()`, `DestroyUnit()`
- **`Landfall.TABS.UnitBlueprint`** (`Sirenix SerializedScriptableObject`)
  - `GameObject m_unitBase` (private) — the unit base prefab; its renderer paths are
    the body/skin renderers
  - `DatabaseEntity m_entity` (private) — has public `Name` + `GUID`; used as the save key
  - `PropItemData[] m_propData` — clothes/weapons data
- **`Landfall.TABS.PropItemData`** — `int[] m_colors` (palette indexes), `bool[] m_isTeamColor`
- **`Landfall.TABS.CharacterItem`** — gear asset; `RendererMaterialWrapper[] m_defaultColors/m_materials`

## Unit Creator

- **`UnitEditorHandler`** (singleton `Instance`)
  - `GameObject UnitRoot` — the preview unit
  - `UnitBlueprint CurrentLoadedCustomUnit`
  - `SetTeamColors(Team)`, `SaveUnit(...)`, `SaveClothes(...)`, `LoadUnit(...)`
- **`UnitEditorTeamButtons`** — the red/blue team toggle swatches (top-right of the
  BASE tab); static `_CurrentTeam`, static event `_OnTeamChanged`
- Color UI: `UnitEditorColorPalette`, `UIColorPicker`, `UIColorButton`,
  `ColorPaletteData`, `TeamColorPaletteData`, `ZorrosColorDatabase` (palette database)

## Mod design decisions

1. Hook `Unit.Awake` (postfix) — exists on `Landfall.TABS.Unit`, fires for battle
   units and UC preview units alike.
2. Defer application ~0.35 s so the game's own color init completes first.
3. Skin materials = unit-base prefab renderer paths, excluding `spawnedObjects`
   subtrees, filtered to `TFBG/*` shaders with `_Color`. Optional name-pattern filter
   (`MaterialNameFilter`) for edge cases.
4. Per-unit tinting via `renderer.materials` (instantiates), plus updating
   `UnitColorHandler.rendColors` so `UpdateColors()` (death fade) lerps from our color.
5. Sticky re-apply (1 Hz) guards against external overwrites; skipped while `unit.dead`
   so the gray fade plays out.
6. UI: IMGUI button pinned top-right next to the team swatches + draggable picker
   window; persistence in `skin_colors.json` keyed by `DatabaseEntity.Name`.
