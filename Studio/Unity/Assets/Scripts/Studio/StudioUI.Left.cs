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
                BaseSettingsPanel();
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

        private string _piecePick, _pieceFilter = "";

        /// <summary>A stock number with its own slider: -1 means "as the game has it"; the stock value is the default shown.</summary>
        private float Over(string label, float cur, float stock, float min, float max)
        {
            GUILayout.BeginHorizontal();
            float shown = cur >= 0f ? cur : stock;
            float v = Slider(label, shown, min, Mathf.Max(max, stock * 2f));
            bool changed = Mathf.Abs(v - shown) > 1e-4f;
            if (GUILayout.Button(cur >= 0f ? "stock" : "·", cur >= 0f ? _btnSmall : _tabOff, GUILayout.Width(44))) { GUILayout.EndHorizontal(); return -1f; }
            GUILayout.EndHorizontal();
            return changed ? (float)Math.Round(v, 3) : cur;
        }

        /// <summary>Which weapon's piece plays for one slot of the base effect: stock, none, or another weapon's.</summary>
        private string Piece(string label, string cur, string part)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _small, GUILayout.Width(100));
            string shown = cur == "" ? "stock" : cur == "none" ? "none" : Pretty(cur.Split('|')[0]) + (cur.Contains("|") ? " · " + cur.Split('|')[1] : "");
            if (GUILayout.Button(shown + "  ▾", cur == "" ? _btnSmall : _btnOn)) { _piecePick = _piecePick == part ? null : part; _pieceFilter = ""; }
            GUILayout.EndHorizontal();
            if (_piecePick != part) return cur;
            GUILayout.BeginHorizontal(); GUILayout.Space(100);
            if (GUILayout.Button("stock", _btnSmall)) { _piecePick = null; return ""; }
            if (GUILayout.Button("none", _btnSmall)) { _piecePick = null; return "none"; }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUILayout.Space(100); _pieceFilter = GUILayout.TextField(_pieceFilter, _field); GUILayout.EndHorizontal();
            int n = 0;
            foreach (string e in _effects)
            {
                if (e == _recipe.baseEffect) continue;
                foreach (string[] p in PartsOf(e))
                {
                    bool fits = part == "impact" ? p[0].StartsWith("impact") : p[0] == part;
                    if (!fits) continue;
                    string label2 = Pretty(e) + (p[0].StartsWith("impact:") ? " · " + p[1] : "");
                    if (_pieceFilter.Length > 0 && label2.IndexOf(_pieceFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (++n > 14) { GUILayout.Label("… type to narrow", _small); goto done; }
                    GUILayout.BeginHorizontal(); GUILayout.Space(100);
                    if (GUILayout.Button(label2, _btnSmall)) { _piecePick = null; GUILayout.EndHorizontal(); return p[0].StartsWith("impact:") ? e + "|" + p[0].Substring(7) : e; }
                    GUILayout.EndHorizontal();
                }
            }
            done:
            return cur;
        }

        /// <summary>The stock effect's own numbers and pieces, before anything is added on top.</summary>
        private void BaseSettingsPanel()
        {
            if (!_recipe.HasBase) return;
            WeaponEffect st = Stock.Stock(_recipe.baseEffect);
            if (st == null) return;
            BaseSettings b = _recipe.baseSettings;
            GUILayout.Space(8); GUILayout.Label("1b · BASE SETTINGS · the stock effect's own numbers and pieces" + (b.AnySet ? "  ·  changed" : ""), _cap);
            b.preFireDuration = Over("Pre-fire s", b.preFireDuration, st.preFireDuration, 0f, 3f);
            b.projectileSpeed = Over(st is LaserEffect ? "Beam time s" : "Speed m/s", b.projectileSpeed, st.projectileSpeed, st is LaserEffect ? 0.05f : 10f, st is LaserEffect ? 4f : 1500f);
            var le = st as LaserEffect; if (le != null) { b.lightIntensity = Over("Light intensity", b.lightIntensity, le.lightIntensity, 0f, 8000000f); b.lightRadius = Over("Light radius m", b.lightRadius, le.lightRadius, 1f, 300f); }
            var pe = st as PulseLaserEffect; if (pe != null) b.pulseDelay = Over("Pulse delay s", b.pulseDelay, pe.pulseDelay, 0f, 1f);
            var be = st as BallisticEffect; if (be != null) { b.spreadAngle = Over("Spread m", b.spreadAngle, be.spreadAngle, 0f, 20f); b.shotDelay = Over("Shot delay s", b.shotDelay, be.shotDelay, 0f, 1f); b.burstDelay = Over("Burst delay s", b.burstDelay, be.burstDelay, 0f, 1f); }
            var me = st as MissileLauncherEffect; if (me != null) { b.firingInterval = Over("Firing interval s", b.firingInterval, me.firingInterval, 0f, 1f); b.volleyInterval = Over("Volley interval s", b.volleyInterval, me.volleyInterval, 0f, 2f); b.curveStrength = Over("Curve strength", b.curveStrength, me.missileCurveStrength, 0f, 200f); b.curveFrequency = Mathf.RoundToInt(Over("Curve points", b.curveFrequency, me.missileCurveFrequency, 0f, 12f)); }
            var fe = st as FlamerEffect; if (fe != null) b.impactTime = Over("Impact at s", b.impactTime, fe.impactTime, 0f, 3f);
            b.delayMin = Over("Next shot min s", b.delayMin, st.attackSequenceNextDelayMin, 0f, 3f); b.delayMax = Over("Next shot max s", b.delayMax, st.attackSequenceNextDelayMax, 0f, 3f);
            GUILayout.Space(4); GUILayout.Label("Pieces", _small);
            b.prefirePart = Piece("Pre-fire", b.prefirePart, "prefire");
            b.flashPart = Piece("Muzzle flash", b.flashPart, "flash");
            b.impactPart = Piece("Impact", b.impactPart, "impact");
            b.armorPart = Piece("Armour hit", b.armorPart, "armor");
            b.structurePart = Piece("Structure hit", b.structurePart, "structure");
            b.matchColors = GUILayout.Toggle(b.matchColors, " Match pre-fire, flash and impact to the colour table", _label);
            if (b.AnySet && GUILayout.Button("Back to stock on everything", _btnSmall, GUILayout.Width(200))) _recipe.baseSettings = new BaseSettings { matchColors = b.matchColors };
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
                if (GUILayout.Button(LayerLabel(l) + "  ·  " + l.start.ToString("0.##") + " s" + (l.phase == "prefire" ? "  ·  pre-fire" : ""), i == _layer ? _btnOn : _btn)) { _layer = i == _layer ? -1 : i; if (_layer >= 0) _tab = 0; }
                if (GUILayout.Button("S", _solo == i ? _btnOn : _btnSmall, GUILayout.Width(24))) { _solo = _solo == i ? -1 : i; Fire(); }
                if (GUILayout.Button("✕", _btnSmall, GUILayout.Width(26))) { _recipe.layers.RemoveAt(i); if (_solo == i) _solo = -1; _layer = Mathf.Min(_layer, _recipe.layers.Count - 1); GUILayout.EndHorizontal(); break; }
                GUILayout.EndHorizontal();
            }
            if (_solo >= 0) GUILayout.Label("Solo: only the marked layer plays. Press S again to hear them all.", _small);
            GUILayout.Space(8); GUILayout.Label("ADD A LAYER", _cap);
            foreach (string[] group in KIND_GROUPS)
            {
                if (group[1] == "part") continue;        // the game's own pieces are listed by name in the catalogue below
                GUILayout.Label(group[0], _small);
                GUILayout.BeginHorizontal();
                for (int i = 1; i < group.Length; i++)
                {
                    int perRow = _leftW < 330f ? 2 : 3;
                    if (i > 1 && (i - 1) % perRow == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                    if (GUILayout.Button("+ " + KIND_NAMES[group[i]], _btnSmall)) { _recipe.layers.Add(Layer.New(group[i])); _layer = _recipe.layers.Count - 1; _tab = 0; Fire(); }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8);
            Catalog();
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
