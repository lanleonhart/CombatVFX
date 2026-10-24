// Left panel: in Game effects mode the list of the game's 47 stock effects; in Recipe mode the recipe itself, top to
// bottom in the order it fires: the base game effect, the CAC knobs on it, then the extra layers with on/solo/remove,
// and the grouped buttons that add a layer.
using System;
using System.IO;
using UnityEngine;

namespace GNFX
{
    public partial class StudioUI
    {
        private string _baseFilter = ""; private bool _pickBase;

        private void Left(Rect r)
        {
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 16));
            if (_mode == 0)
            {
                GUILayout.Label("THE GAME'S WEAPON EFFECTS · " + _effects.Count, _cap);
                _filter = GUILayout.TextField(_filter, _field);
                _sl = GUILayout.BeginScrollView(_sl);
                string family = null;
                foreach (string e in _effects)
                {
                    if (!string.IsNullOrEmpty(_filter) && e.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string fam = Family(e);
                    if (fam != family) { GUILayout.Label(fam.ToUpperInvariant(), _cap); family = fam; }
                    if (GUILayout.Button(e.Replace("WeaponEffect-Weapon_", "").Replace("WeaponEffect-", ""), e == _effect ? _btnOn : _btn)) { _effect = e; _shots = DefaultShots(e); _perShot = PerShot(e); ApplyHint(); Fire(); }
                }
                GUILayout.EndScrollView();
            }
            else
            {
                _sl = GUILayout.BeginScrollView(_sl);
                BaseEffectPanel();
                CacPanel();
                LayersPanel();
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }

        /// <summary>Weapon family of a stock effect id, for the headings in the list.</summary>
        private static string Family(string e)
        {
            string s = e.ToLowerInvariant();
            if (s.Contains("artillery") || s.Contains("thumper") || s.Contains("mortar")) return "Artillery";
            if (s.Contains("laser") || s.Contains("tag")) return "Lasers";
            if (s.Contains("ppc") || s.Contains("coil")) return "Particle cannons";
            if (s.Contains("lrm") || s.Contains("srm") || s.Contains("narc")) return "Missiles";
            if (s.Contains("ac") || s.Contains("lb") || s.Contains("gauss") || s.Contains("machinegun")) return "Ballistics";
            if (s.Contains("flamer")) return "Flamers";
            if (s.Contains("melee") || s.Contains("dfa")) return "Melee";
            return "Other";
        }

        private void BaseEffectPanel()
        {
            GUILayout.Label("1 · BASE GAME EFFECT", _cap);
            string shown = _recipe.HasBase ? _recipe.baseEffect.Replace("WeaponEffect-Weapon_", "").Replace("WeaponEffect-", "") : "none · layers only";
            if (GUILayout.Button(shown + (_pickBase ? "  ▲" : "  ▼"), _recipe.HasBase ? _btnOn : _btn)) _pickBase = !_pickBase;
            if (!_pickBase) return;
            _baseFilter = GUILayout.TextField(_baseFilter, _field);
            if (GUILayout.Button("none · layers only", _btn)) { _recipe.baseEffect = ""; _pickBase = false; ApplyHint(); }
            int n = 0;
            foreach (string e in _effects)
            {
                if (!string.IsNullOrEmpty(_baseFilter) && e.IndexOf(_baseFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++n > 14) { GUILayout.Label("… type above to narrow the list", _small); break; }
                if (GUILayout.Button(e.Replace("WeaponEffect-Weapon_", "").Replace("WeaponEffect-", ""), e == _recipe.baseEffect ? _btnOn : _btn)) { _recipe.baseEffect = e; _pickBase = false; ApplyHint(); Fire(); }
            }
        }

        /// <summary>The CustomAmmoCategories knobs on the base effect: colour table and rule, projectile scale, speed.</summary>
        private void CacPanel()
        {
            if (!_recipe.HasBase) return;
            GUILayout.Space(8); GUILayout.Label("2 · COLOUR · CAC ColorsTable" + (Directory.Exists(Path.Combine(Assets.GamePath ?? "", "Mods", "CustomAmmoCategories")) ? "" : " · needs CustomAmmoCategories"), _cap);
            for (int i = 0; i < _recipe.colors.Count; i++)
            {
                ColorEntry e = _recipe.colors[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label("Entry " + (i + 1), _small, GUILayout.Width(50));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("✕", _btnSmall, GUILayout.Width(26))) { _recipe.colors.RemoveAt(i); GUILayout.EndHorizontal(); break; }
                GUILayout.EndHorizontal();
                string nh = ColorPicker("Colour", e.C); if (nh != e.C) e.C = nh;
                float ni = Slider("Intensity (HDR)", e.I, 0.1f, 12f); if (Mathf.Abs(ni - e.I) > 1e-4f) e.I = (float)Math.Round(ni, 2);
            }
            if (GUILayout.Button("+ colour", _btnSmall, GUILayout.Width(80))) _recipe.colors.Add(new ColorEntry { C = "#f00e93", I = 5f });
            if (_recipe.colors.Count > 1)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Rule", _small, GUILayout.Width(118));
                int ri = Array.IndexOf(EffectMods.Rules, _recipe.colorRule); if (ri < 0) ri = 0;
                if (GUILayout.Button(EffectMods.Rules[ri] + "  ▸", _btnSmall, GUILayout.Width(110))) _recipe.colorRule = EffectMods.Rules[(ri + 1) % EffectMods.Rules.Length];
                GUILayout.EndHorizontal();
                _recipe.colorSpeed = (float)Math.Round(Slider("Change speed", _recipe.colorSpeed, 0f, 10f), 2);
            }
            else if (_recipe.colors.Count == 1) _recipe.colorRule = "t0";
            GUILayout.Space(8); GUILayout.Label("3 · SIZE AND SPEED · CAC ProjectileScale", _cap);
            Vector3 s = _recipe.scale;
            s.x = (float)Math.Round(Slider("Scale x", s.x, 0.1f, 60f), 1); s.y = (float)Math.Round(Slider("Scale y", s.y, 0.1f, 60f), 1); s.z = (float)Math.Round(Slider("Scale z", s.z, 0.1f, 60f), 1);
            if (GUILayout.Button("Same on all axes", _btnSmall, GUILayout.Width(120))) s.y = s.z = s.x;
            _recipe.scale = s;
            _recipe.speedMul = (float)Math.Round(Slider("Projectile speed ×", _recipe.speedMul, 0.1f, 5f), 2);
        }

        private void LayersPanel()
        {
            GUILayout.Space(8); GUILayout.Label((_recipe.HasBase ? "4 · " : "") + "EXTRA LAYERS · drawn by the CombatVFX mod on top", _cap);
            if (_recipe.layers.Count == 0) GUILayout.Label("None yet. Add one below; impact layers wait for the shot to land.", _small);
            for (int i = 0; i < _recipe.layers.Count; i++)
            {
                Layer l = _recipe.layers[i];
                GUILayout.BeginHorizontal();
                bool on = GUILayout.Toggle(l.on, "", GUILayout.Width(18)); if (on != l.on) l.on = on;
                var sw = GUILayoutUtility.GetRect(6, 18, GUILayout.Width(6)); GUI.DrawTexture(sw, Solid(l.on ? KindColor(l.kind) : new Color(0.3f, 0.3f, 0.3f)));
                if (GUILayout.Button(KIND_NAMES[l.kind] + "  ·  " + l.start.ToString("0.##") + " s", i == _layer ? _btnOn : _btn)) { _layer = i == _layer ? -1 : i; if (_layer >= 0) _tab = 0; }
                if (GUILayout.Button("S", _solo == i ? _btnOn : _btnSmall, GUILayout.Width(24))) { _solo = _solo == i ? -1 : i; Fire(); }
                if (GUILayout.Button("✕", _btnSmall, GUILayout.Width(26))) { _recipe.layers.RemoveAt(i); if (_solo == i) _solo = -1; _layer = Mathf.Min(_layer, _recipe.layers.Count - 1); GUILayout.EndHorizontal(); break; }
                GUILayout.EndHorizontal();
            }
            if (_solo >= 0) GUILayout.Label("Solo: only the marked layer plays. Press S again to hear them all.", _small);
            GUILayout.Space(8); GUILayout.Label("ADD A LAYER", _cap);
            foreach (string[] group in KIND_GROUPS)
            {
                GUILayout.Label(group[0], _small);
                GUILayout.BeginHorizontal();
                for (int i = 1; i < group.Length; i++)
                {
                    if (i > 1 && (i - 1) % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                    if (GUILayout.Button("+ " + KIND_NAMES[group[i]], _btnSmall)) { _recipe.layers.Add(Layer.New(group[i])); _layer = _recipe.layers.Count - 1; _tab = 0; Fire(); }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8);
            GUILayout.Label("RECIPES · " + _files.Count + "  ·  the Library tab has pictures and search", _cap);
            string family = null;
            foreach (string f in _files)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                string g = name.Contains("__") ? name.Split(new[] { "__" }, StringSplitOptions.None)[0] : "other";
                if (g != family) { GUILayout.Label(g.ToUpperInvariant(), _cap); family = g; }
                string label = name.Contains("__") ? name.Substring(name.IndexOf("__") + 2) : name;
                if (GUILayout.Button(label.Replace('_', ' '), f == _recipePath ? _btnOn : _btn)) { LoadRecipe(f); Fire(); }
            }
        }
    }
}
