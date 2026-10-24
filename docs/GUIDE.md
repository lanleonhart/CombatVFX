# Effects Studio guide

This is the reference for making weapon effects with the studio and playing them with the CombatVFX mod. The README
has the five-minute version.

## 1. The window

- **Top bar**: Game effects / Recipe / Library. In Recipe: the name, a saved/unsaved mark, Undo, Redo, Save, New,
  Duplicate. FIRE (Space) and Loop. Playback speed ×1 to ×0.1, Pause (P), Step one frame (.).
- **Left panel**. Game effects: the 47 stock effects by family; click one to fire it. Recipe: the recipe in firing
  order: 1 base game effect, 2 colour, 3 size and speed, 4 extra layers (on / solo / remove), then the add-a-layer
  buttons by group and the recipe list. Library: cards with pictures, search and family filters.
- **Right panel**, four tabs. **Layer**: the selected layer's fields with help text. **Shot**: shots, spread, indirect
  fire, hit location, armour or structure hit VFX, compare, sounds. **Look**: backdrop, bloom, deferred rendering,
  light scale. **Game**: tags, the CustomAmmoCategories lines, hide-stock, install, write into a weapon file.
- **Timeline**: each layer is a block from its start to its end, coloured by group (orange path, yellow muzzle, blue
  impact, grey other). The red line is the impact moment; ♪ marks where the game plays its sounds; the gold line is now.
  Drag a block to move its start. Click or drag anywhere else to scrub. Scrubbing backward re-fires and runs to the moment.
- **Stage row**: range, shooter and target (any 'Mech the game and its mods have), camera presets. Drag to orbit, wheel
  to zoom.

Keys: Space fire, P pause, . step, Ctrl+Z undo, Ctrl+Y redo, Ctrl+S save.

## 2. The recipe

A recipe is a JSON file. The studio writes it; the CombatVFX mod reads it. Three parts:

1. **Base effect** (`baseEffect`): one of the game's stock effects, for example `WeaponEffect-Weapon_LaserPulse_Medium`.
   Empty means the layers are the whole effect.
2. **CustomAmmoCategories knobs** on the base effect. These only work in the game for players running CAC, and they
   live in the *weapon's* JSON, not in the recipe: the studio prints them (Game tab) or writes them into the weapon file.
   - `colors`: a list of `{ "C": "#rrggbb", "I": intensity }`. CAC writes colour × intensity into the effect's blackbody
     tint. Beams take it as is; bullet trails get CAC's brightening. PPCs and missiles are not recoloured by CAC.
   - `colorRule`: `t0`, `t1`, ... picks one entry; `Linear` and `Random` cycle the table at `colorSpeed`; `RandomOnce`.
   - `scale`: `{x, y, z}` projectile scale. `speedMul`: projectile speed multiplier.
3. **Base settings** (`baseSettings`): the stock effect's own numbers and pieces, edited before anything is added on
   top. Numbers default to the stock value and can be reset to it: pre-fire duration, projectile speed (beam time for
   lasers), light intensity and radius (lasers), pulse delay, spread, shot and burst delay (ballistics), firing and
   volley interval and missile curve (missiles), impact time (flamer), next-shot delays. Pieces: the pre-fire, muzzle
   flash, impact, armour hit and structure hit can each stay stock, be switched off, or be another weapon's (an impact
   can be one named variation). `matchColors` tints the pre-fire, flash and impact to the colour table's first colour
   while keeping their own brightness, since CustomAmmoCategories recolours only the beam. In the game CombatVFX
   writes these onto the weapon's effect the moment it fires and tints the spawned pieces as the game spawns them.
   **Components** (`components`): under Base settings, every particle system, line, trail and light inside the
   effect's pre-fire, muzzle flash, projectile and impact prefabs is listed by name with what it does now (count,
   size, lifetime, speed; width; light). Each can be switched off or given multipliers for size, lifetime, speed,
   count, gravity, width and light, and a colour of its own. The game applies them to the pooled piece as it spawns
   and to the effect's projectile as it is built, and restores the stock values when the pool hands the object on.
4. **Layers** (`layers`): what CombatVFX draws on top, in order. `hideStock: true` hides the stock projectile in the
   game so the layers replace it (damage still flows through the stock effect).

Also: `tags` (free text for the library), `prefire` (pre-fire phase length without a base), `sound` and `impactSound` (studio only).

## 3. The 21 layer kinds

Every layer has `start` (seconds after the shot) and `on`. Impact-anchored layers wait for the shot to land: their
start is counted from the impact moment, which the studio computes from the bolt's speed (or the beam's sweep).

### Along the path (muzzle to target)
| Kind | What it draws | Main fields |
|---|---|---|
| `beam` | three strips: white core, coloured body, wide glow | width, glow, duration, fade, reach (instant / sweep), count (pulses), gap, jitter (jag) |
| `bolt` | a mesh (blade / bolt / ball / ring) flying to the target with glow strips | length, width, speed, glow, spin, count, gap, arc, scatter |
| `trail` | follows the first bolt (or lies on the beam line) | length, width, opacity |
| `pulse` | bright dots travelling down the line | count, size, speed, gap, duration, texture |
| `helix` | strands spiralling round the line | count (strands), width, radius, turns, spin, duration, reach, fade |
| `arc` | jagged lightning; path `beam` = muzzle to target, `fork` = random forks from the anchor | width, glow, jitter, count (branches), length, rate (re-rolls/s), duration, fade |

### At the muzzle
| Kind | What it draws | Main fields |
|---|---|---|
| `flash` | particles in a cone | count, size, speed, spread, life, texture |
| `glow` | a sprite that flickers | anchor, size, glow (flicker), duration, fade, texture, facing |
| `orb` | a core sphere with a halo; fade `in` = charge-up | core, anchor, size, glow (halo), duration, fade |
| `charge` | particles born round the muzzle and drawn into it | count, size, radius, duration, texture |
| `spray` | a cone emitted over time (flamer) | count, size, speed, spread, life, duration, gravity, texture |
| `casing` | brass thrown out of the gun's right side | count, size, speed, gap, gravity, life |

### At the impact
| Kind | What it draws | Main fields |
|---|---|---|
| `burst` | particles out of the impact, optional shock ring | count, size, speed, gravity, life, texture, ring |
| `ring` | an expanding ring | anchor, size (radius), duration, fade, facing (camera / flat / beam) |
| `scorch` | a dark mark that lingers | anchor (impact / ground), size, opacity, duration, texture (soft / disc), facing |
| `smoke` | rising, growing puffs | anchor, count, size, speed, life, opacity |
| `embers` | glowing dots falling under gravity | anchor, count, size, speed, gravity, life |
| `debris` | tumbling chunks thrown back toward the shooter | anchor, count, size, speed, spread, gravity, life |

### From the game's own effects
| Kind | What it draws | Main fields |
|---|---|---|
| `weapon` | a whole stock effect played as a layer, muzzle to target: its pre-fire, flash, projectile and impact, each switchable | effect, shots, scale x/y/z, speed, colour (stock / recipe / custom), with pre-fire / flash / impact |
| `part` | one piece of a stock effect: its pre-fire charge, muzzle flash, impact (a random variation or a named one), armour hit, structure hit, terrain hit or building hit | effect (pick one of the 47), part, phase, colour (stock / recipe / custom), anchor, size (scale), duration, facing |
| `vfx` | any of the game's vfx prefabs by name | prefab (type part of a name, pick from the list), colour (stock / recipe / custom), anchor, size (scale), duration, facing |

In Game effects mode the Layer tab shows each stock effect's **anatomy** (pre-fire, flash, projectile, light, impact
and its variations, armour / structure / terrain / building hits, sounds) with a button per part that adds it to the
current recipe, and one that adds the whole effect as a layer. So a recipe can take the large laser's charge, the PPC's flash and the AC/20's impact, or stack whole effects: a large laser inside a medium laser, one of them scaled.

### Anywhere
| Kind | What it draws | Main fields |
|---|---|---|
| `light` | a point light | anchor (muzzle / impact / bolt), intensity, radius, duration |
| `shake` | camera shake | anchor (when: muzzle = on fire, impact = on hit), intensity, duration |

### Phases
Every layer has a `phase`: `fire` (the default, timed from the shot) or `prefire` (timed from the start of the weapon's
charge-up, before the shot). The pre-fire length comes from the base effect's own pre-fire duration, or from the
recipe's `prefire` setting when there is no base. In the game CombatVFX plays pre-fire layers when the weapon effect
enters its pre-firing state and the rest when it fires. Parts added from a pre-fire are put in the pre-fire phase.

Anchors: `muzzle`, `impact`, `ground` (under the impact), `bolt` (rides the first bolt). Fades: `ease out`, `linear`,
`hold`, `pulse`, `flicker`, `in`. Textures: `soft`, `star`, `spark`, `ring`, `smoke`, `disc`.

## 3b. Jets: GN particles instead of jump-jet exhaust

The **Jets** tab works on the stage's shooter 'Mech. It finds the game's own jump-jet components for that prefab
(`chrPrfComp_<base>_centertorso_jumpjet` and the leg ones, attached to the j_COCKPIT and j_LCalf / j_RCalf bones),
lets you remove them, and places one GN particle emitter on a bone of the model: pick the bone, slide the offset
(left/right, down/up, back/forward in metres), choose which way it blows and how wide. Then tune the particles
(colours with a gold-spark mix, density, size, thrust speed, drift, twinkle, lifetime), the idle stream that runs all
the time, the drive glow and the jump burst length. Space jumps. Save writes `recipes\jets\<name>.json`; "Install"
copies it to `Mods\CombatVFX\jets`.

In the game, CombatVFX puts the emitter on every 'Mech whose chassis carries the ChassisTag `vfx_jets_<name>`, idles
it, bursts it when the game starts the jets, and stops the stock exhausts if the recipe says so. The folder hot-reloads
for the next 'Mech that spawns.

Colour fields anywhere in the studio open a full picker on a double-click of the swatch or the hex.

## 4. From the studio to the game

1. **Install the mod**: copy `Mods\CombatVFX` into the game's `Mods` folder. It needs ModTek. It does nothing until it
   finds a recipe and a tagged weapon.
2. **Install the recipe**: Game tab, "Install recipe into Mods\CombatVFX\effects". The file name becomes the tag:
   `my_laser.json` is played on weapons tagged `vfx_my_laser`. The mod watches the folder and reloads on change, so
   you can save in the studio with the game running.
3. **Tag the weapon**: add `vfx_<recipe>` to the weapon def's `ComponentTags.items`. "Write the recipe into this
   weapon" does it for you, together with the CAC fields, and leaves a `.bak`.
4. **CAC fields**: `WeaponEffectID`, `ColorsTable`, `ColorChangeRule`, `ColorSpeedChange`, `ProjectileScale`,
   `ProjectileSpeedMultiplier` at the top level of the weapon JSON. Only with CustomAmmoCategories installed.
5. **Test in a mission**: with `FxTestHotkey: true` in `Mods\CombatVFX\settings.json` (or the game's test tools on),
   select a 'Mech, target something, press Ctrl+Shift+E: the most recently saved recipe plays from the 'Mech to the
   target. Ctrl+Shift+R cycles through the recipes. The mod's log is `Mods\CombatVFX\CombatVFX_log.txt`; it reports
   recipes loaded, bad files, and the first time each recipe plays.

Settings in `settings.json`: `FxTestHotkey` (default false), `FxTagPrefix` (default `vfx_`). The rest belong to the
ballistic deflection system; see its comments in the file.

## 5. Shipping a recipe with your mod

Your mod can carry recipes itself: put them in your own folder and copy them to `Mods\CombatVFX\effects` in your
install instructions, or depend on CombatVFX in your `mod.json` and ship the files into that folder. Keep the file name
and the tag in step. Recipes are plain JSON under the MIT licence of this project; what you make is yours.

## 6. Why the studio looks like the game, and where it does not

- The 'Mechs, projectiles, impacts and lights are the game's own prefabs from its asset bundles.
- The game's VFX shaders need fog-of-war, biome and depth data that only the game's renderer provides, so the studio
  moves each material onto the matching built-in particle shader and folds the game's HDR blackbody colour into it.
- The game's effects are authored bright and rely on bloom to glow. The studio has its own bloom; the game's post
  profile is not readable from its files, so the "Game-like" preset is matched by eye.
- Missiles follow the game's path rules but not its terrain. Artillery flies a plain arc. Heat-haze materials are hidden.
- Sounds: the game's weapon sounds are Wwise events the studio cannot play, and Unity audio is silent inside the game,
  so recipe sounds are a studio-only timing aid.

## 7. Files

- `Studio\build\EffectsStudio.exe`, `studio.json` (settings), `recipes\*.json` + `.png` thumbnails, `recipes\sounds\`.
- `Runtime\` sources: `Fx\FxRecipe.cs` (the file format), `Fx\FxPlayer.cs` (the 21 layers), `Fx\FxLibrary.cs` (folder,
  watcher, prefab preload, settings), `Fx\FxWeaponPatch.cs` (the Harmony hook and the hotkey), `Combat\` (deflection).
- Studio sources under `Studio\Unity\Assets\Scripts`: `Studio\` (the app) and `Stubs\` (the game's effect classes
  declared by name so prefabs deserialize).
