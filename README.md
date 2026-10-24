# CombatVFX · Effects Studio

Design weapon effects for BATTLETECH (HBS, 2018) in a standalone editor, then play them in the game with a small ModTek mod.

- **Effects Studio** (`Studio/`) is a Windows app built with the game's own Unity version. It loads the game's real weapon effect prefabs, impact effects and 'Mech models straight from your BATTLETECH install, including CAB packs under `Mods`, so what you see is what the game draws.
- **CombatVFX** (`Runtime/`) is the ModTek mod that plays your recipes in a mission: drop a recipe file into `Mods\CombatVFX\effects` and tag a weapon `vfx_<recipe>`. It also carries a universal ballistic deflection system (optional, tunable, off by setting the chances to zero).

A **recipe** starts from one of the game's 47 stock effects and reshapes it with the knobs CustomAmmoCategories reads from a weapon's JSON (colour table, colour rule, projectile scale and speed), then adds drawn layers on top from 21 kinds: beams, bolts, trails, pulses, helixes, lightning, muzzle flashes, glows, orbs, charge-ups, sprays, shell casings, impact bursts, shock rings, scorch marks, smoke, embers, debris, lights, any of the game's own VFX prefabs, and camera shake.

## Install (players and modders)
1. Get the latest release zip and unzip it anywhere.
2. Copy `Mods\CombatVFX` into your game's `Mods` folder (ModTek required).
3. Run `EffectsStudio\EffectsStudio.exe`. It finds the game through Steam's library list or GOG; if not, type the game folder in.

## Make an effect in five minutes
1. **Game effects** tab: pick a stock effect, press Space, orbit the camera. This is your baseline.
2. **Recipe** tab: choose the base effect, add a colour (the "base" button reads the stock colour), tune scale and speed, add layers. Each layer has sliders and help text; drag its block on the timeline to move it in time.
3. Pause (P), step (.), scrub, slow it down, compare it beside the stock effect, change the backdrop.
4. **Game** tab: read the CustomAmmoCategories lines for your weapon JSON, or let the studio write them into the weapon file (it finds every weapon def under `Mods` and keeps a backup). "Install recipe" copies it into `Mods\CombatVFX\effects`; the weapon gets the tag `vfx_<recipe>`.
5. In a mission the mod plays it. With `FxTestHotkey: true` in the mod's `settings.json` (or the game's test tools on), Ctrl+Shift+E plays the latest recipe from the selected 'Mech, so you can tune with the game running: save in the studio, press the key.

See **[docs/GUIDE.md](docs/GUIDE.md)** for every layer, field and file.

## What needs what
| You want | Needs |
|---|---|
| Preview stock effects and recipes | the studio and a BATTLETECH install |
| Recolour, rescale or speed up a stock effect in the game | CustomAmmoCategories (the fields go in the weapon JSON) |
| Drawn layers in the game | the CombatVFX mod |
| Sounds in the studio | a .wav or .ogg in `recipes\sounds` (studio only; the game's sounds are Wwise events) |

## Building from source
- Studio: `powershell -ExecutionPolicy Bypass -File Studio\build.ps1` with Unity 2018.4.2f1 installed (`-NoRun`, `-Out <folder>`). Output in `Studio\build`.
- Runtime: `dotnet build Runtime\CombatVFX.csproj -c Release -p:BattleTechPath="<your game folder>"`.
- Package: `powershell -ExecutionPolicy Bypass -File package.ps1` builds both and writes `dist\CombatVFX-Studio-<version>.zip`.

## How it works, briefly
The game's effect prefabs carry components of its own script classes. The studio declares classes with the same names and serialized fields, so a loaded prefab keeps its data and the studio drives the visuals from it; nothing from the game's assemblies is shipped. The game's VFX shaders need globals only its renderer provides, so the studio moves each material onto the matching built-in particle shader and folds the game's HDR blackbody colour into it, with a bloom pass standing in for the game's post-processing. The runtime hooks `WeaponEffect.Update` with Harmony, answers "not ours" once per effect with a hash lookup, and on the first firing frame plays the recipe between the effect's own start and end points. Damage still flows through the stock effect.

## Licence
MIT. Recipes you make are yours.
