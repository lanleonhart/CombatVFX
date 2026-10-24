// Right panel, four tabs:
//   Layer - the selected layer's fields (or, with none selected, what the recipe is)
//   Shot  - how the shot is fired: shots, spread, hit location, armour VFX, compare, sounds
//   Look  - backdrop, bloom, lights, deferred rendering
//   Game  - the CAC fields, writing them into a weapon file, installing the recipe for CombatVFX, tags
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GNFX
{
    public partial class StudioUI
    {
        private static readonly string[] TABS = { "Layer", "Shot", "Look", "Game" };
        private string _weaponFilter = ""; private WeaponDefs.Def _weaponPick; private string _exportNote = "";

        private void Right(Rect r)
        {
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 16));
            GUILayout.BeginHorizontal();
            for (int i = 0; i < TABS.Length; i++) if (GUILayout.Button(TABS[i], _tab == i ? _tabOn : _tabOff)) _tab = i;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            _sr = GUILayout.BeginScrollView(_sr);
            switch (_tab)
            {
                case 0: LayerTab(); break;
                case 1: ShotTab(); break;
                case 2: LookTab(); break;
                default: GameTab(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void LayerTab()
        {
            if (_mode == 0)
            {
                GUILayout.Label("THIS EFFECT", _cap);
                GUILayout.Label(Stock.Status ?? "", _small);
                GUILayout.Space(8); GUILayout.Label("MAKE A RECIPE FROM IT", _cap);
                if (GUILayout.Button("New recipe with this as its base", _btn)) { _recipe = new Recipe { name = (_effect ?? "").Replace("WeaponEffect-Weapon_", "").Replace("WeaponEffect-", "").Replace("_", " "), baseEffect = _effect }; _recipePath = null; _layer = -1; _solo = -1; _undo.Clear(); _redo.Clear(); MarkSaved(); _mode = 1; ApplyHint(); }
                GUILayout.Space(8); GUILayout.Label("ASSETS", _cap);
                foreach (string line in Assets.Log.Take(4)) GUILayout.Label(line, _small);
                return;
            }
            if (_layer >= 0 && _layer < _recipe.layers.Count)
            {
                Layer l = _recipe.layers[_layer];
                GUILayout.BeginHorizontal();
                GUILayout.Label(KIND_NAMES[l.kind].ToUpperInvariant() + "  ·  layer " + (_layer + 1) + " of " + _recipe.layers.Count, _cap);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Up", _btnSmall) && _layer > 0) { _recipe.layers.RemoveAt(_layer); _recipe.layers.Insert(_layer - 1, l); _layer--; }
                if (GUILayout.Button("Down", _btnSmall) && _layer < _recipe.layers.Count - 1) { _recipe.layers.RemoveAt(_layer); _recipe.layers.Insert(_layer + 1, l); _layer++; }
                if (GUILayout.Button("Duplicate", _btnSmall)) { _recipe.layers.Insert(_layer + 1, l.Clone()); _layer++; }
                GUILayout.EndHorizontal();
                foreach (F f in FIELDS[l.kind]) Field(l, f);
                GUILayout.Space(6);
                GUILayout.Label(LayerHelp(l.kind), _small);
                return;
            }
            GUILayout.Label("THE RECIPE", _cap);
            GUILayout.Label((_recipe.HasBase ? "Starts from " + _recipe.baseEffect.Replace("WeaponEffect-", "") + (_recipe.colors.Count > 0 ? ", recoloured" : "") + (_recipe.scale != Vector3.one ? ", rescaled" : "") + "." : "No base game effect: the layers are the whole effect.") + (_recipe.HasLayers ? " " + _recipe.layers.Count(x => x.on) + " layers on top." : ""), _label);
            GUILayout.Space(6);
            GUILayout.Label("Pick a layer on the left to edit its fields, or drag a block on the timeline to move its start.", _small);
            GUILayout.Space(6);
            GUILayout.Label("Shot length " + _recipe.Length(Rig.Range).ToString("0.0") + " s at " + Mathf.RoundToInt(Rig.Range) + " m.", _small);
        }

        private static string LayerHelp(string kind)
        {
            switch (kind)
            {
                case "beam": return "Three strips (core, body, glow) muzzle to target. Pulses repeat it; Jag breaks it into re-rolled segments.";
                case "bolt": return "A mesh flying muzzle to target with glow strips. Arc lifts the path; Spread scatters each bolt.";
                case "trail": return "Follows the first bolt; without a bolt it lies along the beam line.";
                case "pulse": return "Bright dots travelling down the line, one every Gap seconds while Emit for runs.";
                case "helix": return "Strands spiralling round the line. Sweep grows them out from the muzzle.";
                case "arc": return "Jagged lightning. Path beam = muzzle to target; fork = random forks from the anchor. Branches hang off the main bolt.";
                case "flash": return "Particles out of the muzzle in a cone.";
                case "glow": return "A flat sprite at the anchor. Flicker wobbles its size.";
                case "orb": return "A growing core sphere with a halo. Fade in = charge-up; the others = flash and die.";
                case "charge": return "Particles born in a shell round the muzzle and drawn into it, arriving as the duration ends.";
                case "spray": return "A cone of particles emitted over time, flamer style.";
                case "casing": return "Brass thrown out of the right side of the gun, one per Gap.";
                case "burst": return "Particles out of the impact. Shock ring adds an expanding ring.";
                case "ring": return "An expanding ring facing the camera, flat on the ground, or facing the beam.";
                case "scorch": return "A dark mark on the armour (facing beam) or ground (flat), fading over Duration.";
                case "smoke": return "Puffs rising from the anchor, growing as they fade.";
                case "embers": return "Glowing dots thrown up and falling under gravity.";
                case "debris": return "Tumbling chunks thrown back toward the shooter.";
                case "light": return "A point light at the anchor, fading over Duration.";
                case "vfx": return "One of the game's own vfx prefabs (impacts, fire, smoke, dust...) at the anchor.";
                case "shake": return "Shakes the camera when fired or when the shot lands.";
            }
            return "";
        }

        private void ShotTab()
        {
            GUILayout.Label("FIRING", _cap);
            if (_mode == 0)
            {
                _shots = Mathf.RoundToInt(Slider("Shots", _shots, 1, 24));
                _perShot = Mathf.RoundToInt(Slider("Projectiles per shot", _perShot, 1, 12));
            }
            Stock.Spread = Stock2.Spread = Slider("Extra spread m", Stock.Spread, 0, 10);
            Stock.Indirect = Stock2.Indirect = GUILayout.Toggle(Stock.Indirect, " Indirect fire (missiles climb)", _label);
            GUILayout.Space(8); GUILayout.Label("HIT LOCATION", _cap);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < SceneRig.Locations.Length; i++)
            {
                if (i == 4) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                if (GUILayout.Button(SceneRig.LocationNames[i], Rig.HitLocation == SceneRig.Locations[i] ? _btnOn : _btnSmall)) { Rig.HitLocation = SceneRig.Locations[i]; Rig.Refresh(); Fire(); }
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Also play", _small, GUILayout.Width(118));
            foreach (var o in new[] { new[] { "none", "impact only" }, new[] { "armor", "armour hit VFX" }, new[] { "structure", "structure hit VFX" } })
                if (GUILayout.Button(o[1], Stock.ArmorVfx == o[0] ? _btnOn : _btnSmall)) { Stock.ArmorVfx = Stock2.ArmorVfx = o[0]; }
            GUILayout.EndHorizontal();
            if (_mode != 0)
            {
                GUILayout.Space(8); GUILayout.Label("COMPARE · a second shooter stands 18 m to the left", _cap);
                GUILayout.BeginHorizontal();
                foreach (var o in new[] { new[] { "0", "Off" }, new[] { "1", "Stock base vs recipe" }, new[] { "2", "Recipe A vs B" } })
                    if (GUILayout.Button(o[1], _compare.ToString() == o[0] ? _btnOn : _btnSmall)) { _compare = int.Parse(o[0]); Rig.SetCompare(_compare != 0); Fire(); }
                GUILayout.EndHorizontal();
                if (_compare == 2)
                {
                    GUILayout.Label("B: " + (_recipeB != null ? _recipeB.name : "pick one in the Library (the B button on a card)"), _small);
                    if (_recipeB != null && GUILayout.Button("Swap A and B", _btnSmall, GUILayout.Width(110))) { Recipe t = _recipe; string tp = _recipePath; _recipe = _recipeB; _recipePath = _recipeBPath; _recipeB = t; _recipeBPath = tp; MarkSaved(); Fire(); }
                }
                GUILayout.Space(8); GUILayout.Label("SOUND · studio only, files in recipes\\sounds", _cap);
                var files = Audio != null ? Audio.Files() : new System.Collections.Generic.List<string>();
                if (files.Count == 0) GUILayout.Label("Drop .wav or .ogg files into " + (Audio != null ? Audio.Dir : "recipes\\sounds") + ". The game's own weapon sounds are Wwise events the studio cannot play; in the game Unity audio is silent, so sounds are a timing aid here only.", _small);
                else
                {
                    _recipe.sound = SoundPick("On fire", _recipe.sound, files);
                    _recipe.impactSound = SoundPick("On impact", _recipe.impactSound, files);
                    Audio.Volume = Slider("Volume", Audio.Volume, 0f, 1f);
                }
                if (GUILayout.Button("Open sounds folder", _btnSmall, GUILayout.Width(140))) { try { Directory.CreateDirectory(Audio.Dir); System.Diagnostics.Process.Start(Audio.Dir); } catch { } }
            }
        }

        private string SoundPick(string label, string cur, System.Collections.Generic.List<string> files)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _small, GUILayout.Width(118));
            int i = files.IndexOf(cur ?? "");
            string shown = i < 0 ? "none" : files[i];
            if (GUILayout.Button(shown + "  ▸", _btnSmall)) { i = i + 1 >= files.Count ? -1 : i + 1; cur = i < 0 ? "" : files[i]; }
            GUILayout.EndHorizontal();
            return cur ?? "";
        }

        private void LookTab()
        {
            GUILayout.Label("BACKDROP", _cap);
            GUILayout.BeginHorizontal();
            foreach (var o in new[] { new[] { "day", "Day" }, new[] { "dusk", "Dusk" }, new[] { "night", "Night" }, new[] { "grey", "Grey reference" } })
                if (GUILayout.Button(o[1], Rig.Backdrop == o[0] ? _btnOn : _btnSmall)) { Rig.SetBackdrop(o[0]); Boot.Instance.Config.backdrop = o[0]; }
            GUILayout.EndHorizontal();
            GUILayout.Space(8); GUILayout.Label("BLOOM · stands in for the game's post-processing", _cap);
            Bloom.On = GUILayout.Toggle(Bloom.On, " Bloom", _label);
            if (Bloom.On)
            {
                Bloom.Threshold = Slider("Threshold", Bloom.Threshold, 0.2f, 3f);
                Bloom.Intensity = Slider("Strength", Bloom.Intensity, 0f, 2f);
                GUILayout.BeginHorizontal(); GUILayout.Label("Presets", _small, GUILayout.Width(118));
                if (GUILayout.Button("Game-like", _btnSmall)) { Bloom.Threshold = 1.0f; Bloom.Intensity = 0.6f; }
                if (GUILayout.Button("Subtle", _btnSmall)) { Bloom.Threshold = 1.4f; Bloom.Intensity = 0.3f; }
                if (GUILayout.Button("Heavy", _btnSmall)) { Bloom.Threshold = 0.7f; Bloom.Intensity = 1.1f; }
                GUILayout.EndHorizontal();
                GUILayout.Label("The game's own bloom profile is not readable from its data files, so these are matched by eye.", _small);
            }
            GUILayout.Space(8); GUILayout.Label("LIGHTS", _cap);
            bool deferred = GUILayout.Toggle(Rig.Deferred, " Deferred rendering (the game's path: unlimited lights, no light count cap)", _label);
            if (deferred != Rig.Deferred) Rig.Deferred = deferred;
            BattleTech.Rendering.BTLight.Scale = Slider("Light scale ×1e-6", BattleTech.Rendering.BTLight.Scale * 1e6f, 0.2f, 12f) * 1e-6f;
            GUILayout.Label("The game's BTLight intensities run into the millions for its own deferred light system; this scale maps them onto Unity lights.", _small);
        }

        private void GameTab()
        {
            if (_mode == 0) { GUILayout.Label("Switch to a recipe to see what goes into the game.", _small); return; }
            GUILayout.Label("TAGS · for the library", _cap);
            _recipe.tags = GUILayout.TextField(_recipe.tags ?? "", _field);
            GUILayout.Space(8); GUILayout.Label("IN THE GAME", _cap);
            if (_recipe.HasBase)
            {
                GUILayout.Label("CustomAmmoCategories fields for the weapon JSON:", _small);
                if (!Directory.Exists(Path.Combine(Assets.GamePath ?? "", "Mods", "CustomAmmoCategories"))) GUILayout.Label("CustomAmmoCategories is not installed in this game: the colour, scale and speed knobs need it. Layers do not.", _small);
                GUILayout.TextArea(_recipe.CacJson(), _field);
            }
            if (_recipe.HasLayers)
            {
                GUILayout.Label("Layers: the CombatVFX mod reads Mods\\CombatVFX\\effects\\" + Slug(_recipe.name) + ".json and plays it on weapons tagged vfx_" + Slug(_recipe.name) + ".", _small);
                _recipe.hideStock = GUILayout.Toggle(_recipe.hideStock, " Hide the stock projectile in the game (the layers replace it)", _label);
                if (GUILayout.Button("Install recipe into Mods\\CombatVFX\\effects", _btn)) InstallRecipe();
                if (!Directory.Exists(Path.Combine(Assets.GamePath ?? "", "Mods", "CombatVFX"))) GUILayout.Label("The CombatVFX mod is not installed in this game yet; the layers need it to play. It ships with the studio (Mods\\CombatVFX).", _small);
            }
            if (!_recipe.HasBase && !_recipe.HasLayers) GUILayout.Label("Pick a base game effect or add a layer first.", _small);
            GUILayout.Space(8); GUILayout.Label("WRITE INTO A WEAPON FILE", _cap);
            if (!Weapons.Done && !Weapons.Indexing) { if (GUILayout.Button("Find weapon files under Mods", _btn)) Weapons.Index(Assets.GamePath); }
            else if (Weapons.Indexing) GUILayout.Label("Indexing the weapon files…", _small);
            else
            {
                GUILayout.Label(Weapons.Defs.Count + " weapon files. Type part of a name or id:", _small);
                _weaponFilter = GUILayout.TextField(_weaponFilter, _field);
                if (_weaponFilter.Length >= 2)
                {
                    int n = 0;
                    lock (Weapons.Defs)
                        foreach (WeaponDefs.Def d in Weapons.Defs)
                        {
                            if (d.Id.IndexOf(_weaponFilter, StringComparison.OrdinalIgnoreCase) < 0 && d.Name.IndexOf(_weaponFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (++n > 12) { GUILayout.Label("… type more to narrow the list", _small); break; }
                            if (GUILayout.Button(d.Label, d == _weaponPick ? _btnOn : _btn)) _weaponPick = d;
                        }
                }
                if (_weaponPick != null)
                {
                    GUILayout.Label(_weaponPick.Path.Replace(Assets.GamePath ?? "", ""), _small);
                    if (GUILayout.Button("Write the recipe into this weapon (keeps a .bak)", _btnOn))
                    {
                        try { _exportNote = WeaponDefs.Apply(_recipe, _weaponPick.Path); Toast(_exportNote); } catch (Exception e) { _exportNote = "Failed: " + e.Message; Toast(_exportNote); }
                    }
                    if (!string.IsNullOrEmpty(_exportNote)) GUILayout.Label(_exportNote, _small);
                }
            }
        }

        private void InstallRecipe()
        {
            try
            {
                string dir = Path.Combine(Assets.GamePath, "Mods", "CombatVFX", "effects");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, Slug(_recipe.name) + ".json");
                File.WriteAllText(file, _recipe.ToJson());
                Toast("Installed " + file + (Process("BattleTech") ? " — the game is running; CombatVFX reloads it on the spot" : ""));
            }
            catch (Exception e) { Toast("Could not install: " + e.Message); }
        }

        private static bool Process(string name) { try { return System.Diagnostics.Process.GetProcessesByName(name).Length > 0; } catch { return false; } }
    }
}
