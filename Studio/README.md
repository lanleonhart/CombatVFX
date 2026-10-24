# Effects Studio

A standalone Windows app for designing GN weapon effects, built with the game's own Unity version (2018.4.2f1) so it
draws what the game draws. It loads the game's weapon effect prefabs, impact effects and 'Mech models straight from the
BATTLETECH install's asset bundles, including the CAB packs under Mods.

## Run it
`build\EffectsStudio.exe`. On first start it looks for BATTLETECH in the usual Steam folders; if it cannot find the game,
type the game folder into the box and press "Use this folder". Settings (game path, 'Mechs, backdrop, camera) are kept
in `build\studio.json`.

## The window
- **Top bar**: Game effects / Recipe / Library; the recipe name with a saved/unsaved mark, Undo, Redo, Save, New,
  Duplicate; FIRE and Loop; playback speed (×1, ×0.5, ×0.25, ×0.1), Pause and single-frame Step.
- **Left**: in Game effects, the 47 stock effects grouped by family. In Recipe, the recipe in firing order: 1 the base
  game effect, 2 its colour table, 3 its scale and speed, 4 the extra layers (on, solo, remove), then the grouped
  "add a layer" buttons and the recipe list.
- **Right**, four tabs: **Layer** (the selected layer's fields, with help text), **Shot** (shots, spread, indirect fire,
  hit location on the target's rig, armour or structure hit VFX, compare mode, sounds), **Look** (day / dusk / night /
  grey backdrop, bloom, deferred rendering, light scale), **Game** (tags, the CAC fields to paste, hide-stock, install
  the recipe for GN Core, write the recipe straight into a weapon file).
- **Timeline**: every layer as a block from its start to its end, coloured by group; the impact moment; where the game
  plays its sounds (♪); the current time. Drag a block to move its start; click or drag anywhere else to scrub.
  Scrubbing backward re-fires the shot and runs to the moment.
- **Bottom**: range, shooter and target, camera presets: Over the shoulder, At the muzzle, Follow the shot, Side, At the
  target. Drag to orbit, wheel to zoom.
- **Library**: recipe cards with a thumbnail (taken on save), name, family, base effect, layer count and tags; search
  and family filters; Fire, Edit, B (make it the comparison recipe), delete.

Keys: Space fires, P pauses, . steps one frame, Ctrl+Z / Ctrl+Y undo and redo, Ctrl+S saves.

## Two kinds of thing to design
- **Game effects** - the stock effects, played as the game plays them: pre-fire, muzzle flash, projectile (bullets,
  missiles on their curved paths, PPC lightning, flamer cone, laser beam with its fade curve), impact prefab, and on
  request the armour- or structure-hit VFX for the chosen hit location.
- **Recipes** - a recipe starts from one of those stock effects and reshapes it with the same knobs CustomAmmoCategories
  reads from a weapon's JSON: a colour table (each entry an html colour times an intensity, written into the effect's
  `_ColorBB` the way CAC does; beams take it as is, bullet trails get CAC's brightening), the colour rule (t0 picks an
  entry; Linear and Random cycle the table at the change speed; RandomOnce), the projectile scale and the projectile
  speed multiplier. Extra drawn layers go on top for things no stock effect does; GN Core plays those in the game.

## 21 layer kinds
Along the path: Beam, Bolt, Trail, Pulses, Helix, Lightning (beam, or forks from an anchor). At the muzzle: Muzzle flash,
Glow, Orb (charge-up with the "in" fade), Charge-up, Spray, Shell casings. At the impact: Impact burst, Shock ring,
Scorch mark, Smoke, Embers, Debris. Anywhere: Light, Game VFX (any of the game's vfx prefabs by id, at muzzle, impact,
ground or riding the bolt), Camera shake. Layers anchored at the impact wait for the bolt to land.

## Compare
Shot tab, Compare: a second copy of the shooter stands 18 m to the left and fires at the same target. "Stock base vs
recipe" fires the unmodified base effect beside the recipe; "Recipe A vs B" fires the recipe chosen with B in the
Library. Swap A and B from the Shot tab.

## Look
The game's effect materials use the game's own shaders, which depend on fog-of-war, biome and depth data only the game's
renderer provides, so the studio moves each material onto the matching built-in particle shader and folds the game's
tint and HDR blackbody colour (`_ColorBB`) into it. The game's effects are authored bright (a laser's `_ColorBB` is 5, an
AC round's is in the hundreds) and rely on bloom to glow, so the studio applies a bloom of the same shape. The game's
own bloom profile is not readable from its data files; the Game-like preset is matched by eye. Deferred rendering is a
toggle (the game's path); the game's BTLight intensities run into the millions for its own light system and are mapped
onto Unity lights by the light scale.

## Into the game
- **CAC fields**: the Game tab prints the lines for the weapon JSON, and "Write the recipe into this weapon" finds the
  weapon files under Mods (type part of a name or id), writes the fields at the top level keeping the file's formatting,
  adds the gn_fx_<name> tag to ComponentTags when the recipe has layers, and leaves a .bak.
- **Layers**: "Install recipe into Mods\GN Core\effects" copies the recipe where GN Core reads it. GN Core
  (Combat\Fx in GNCoreDLL) plays the layers on weapons tagged gn_fx_<name>, hides the stock projectile when the recipe
  says so, reloads the folder when a file changes, and with the game's test tools on, Ctrl+Shift+E plays the latest
  recipe from the selected 'Mech to its target (Ctrl+Shift+R cycles recipes).
- **Sounds**: .wav or .ogg files in `build\recipes\sounds` play at the muzzle on fire and at the impact. Studio only: the
  game's weapon sounds are Wwise events, and Unity audio is silent in the game.

## Building
`powershell -ExecutionPolicy Bypass -File build.ps1` runs Unity in batch mode on the `Unity` project and writes
`build\EffectsStudio.exe` (log in `build\unity-build.log`). `-NoRun` skips launching it; `-Out <folder>` builds somewhere
else, for when the app is open (the exe is locked while it runs).

## How the game's prefabs load here
The game's effect prefabs carry components of its own script classes (LaserEffect, MissileLauncherEffect, BTLight and so
on). The project declares classes with the same names and serialized fields (`Assets\Scripts\Stubs`), so a loaded prefab
keeps its data and the studio drives the visuals from it. Nothing from the game's assemblies is shipped.

## Recipe file (version 2)
```
{ "name": "GN Rifle", "version": 2, "tags": "gn, laser",
  "baseEffect": "WeaponEffect-Weapon_LaserPulse_Medium",
  "colors": [ { "C": "#f00e93", "I": 5 } ], "colorRule": "t0", "colorSpeed": 0,
  "scale": { "x": 40, "y": 40, "z": 20 }, "speedMul": 0.75,
  "sound": "", "impactSound": "", "hideStock": false,
  "layers": [] }
```
`baseEffect` empty means layers only. Version 1 files (layers only) still load. The browser page `index.html` is the
earlier, layers-only studio and does not know these fields.

## Code layout (Unity\Assets\Scripts)
Studio: Boot (start-up and settings), GameAssets (bundle index and loading), SceneRig (stage, 'Mechs, muzzles, hit
locations, cameras, backdrops), StockEffectPlayer (plays the game's effects), EffectMods (CAC knobs), ShaderFix (game
shaders onto built-in ones), Bloom, Playback (speed, pause, step, seek), Recipe (the file format), RecipePlayer (the 21
layer kinds), Sounds, WeaponDefs (weapon file index and writing), StudioUI + StudioUI.Fields / .Left / .Right /
.Bottom / .Library (the interface). Stubs: the game's effect and light classes. Editor: BuildStudio.

## Limits
- Missiles follow the game's path rules but not its terrain data; artillery and mortars fly a plain arc.
- Distortion-only materials (heat haze) are hidden rather than drawn wrong.
- The game's own sounds are not played.
