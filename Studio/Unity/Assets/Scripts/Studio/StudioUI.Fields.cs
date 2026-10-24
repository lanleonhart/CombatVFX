// The layer field table and the controls that edit a field: sliders with a typed value, the colour picker (hue,
// saturation, brightness, a palette, the base effect's own colour), choices, and the game VFX prefab picker.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GNFX
{
    public partial class StudioUI
    {
        private static readonly string[] KINDS = { "beam", "bolt", "trail", "pulse", "helix", "arc", "flash", "glow", "orb", "charge", "spray", "casing", "burst", "ring", "scorch", "smoke", "embers", "debris", "weapon", "part", "vfx", "light", "shake" };
        private static readonly string[][] KIND_GROUPS =
        {
            new[] { "Along the path", "beam", "bolt", "trail", "pulse", "helix", "arc" },
            new[] { "At the muzzle", "flash", "glow", "orb", "charge", "spray", "casing" },
            new[] { "At the impact", "burst", "ring", "scorch", "smoke", "embers", "debris" },
            new[] { "From the game's own effects", "weapon", "part", "vfx" },
            new[] { "Anywhere", "light", "shake" },
        };
        private static readonly Dictionary<string, string> KIND_NAMES = new Dictionary<string, string>
        {
            { "beam", "Beam" }, { "bolt", "Bolt" }, { "trail", "Trail" }, { "pulse", "Pulses" }, { "helix", "Helix" }, { "arc", "Lightning" },
            { "flash", "Muzzle flash" }, { "glow", "Glow" }, { "orb", "Orb" }, { "charge", "Charge-up" }, { "spray", "Spray" }, { "casing", "Shell casings" },
            { "burst", "Impact burst" }, { "ring", "Shock ring" }, { "scorch", "Scorch mark" }, { "smoke", "Smoke" }, { "embers", "Embers" }, { "debris", "Debris" },
            { "light", "Light" }, { "vfx", "Game VFX" }, { "part", "Game part" }, { "weapon", "Whole weapon effect" }, { "shake", "Camera shake" },
        };
        /// <summary>Timeline block colour per group: path, muzzle, impact, other.</summary>
        private static Color KindColor(string kind)
        {
            for (int g = 0; g < KIND_GROUPS.Length; g++) if (Array.IndexOf(KIND_GROUPS[g], kind) > 0) return g == 0 ? new Color(0.95f, 0.55f, 0.15f) : g == 1 ? new Color(0.95f, 0.85f, 0.3f) : g == 2 ? new Color(0.4f, 0.75f, 1f) : g == 3 ? new Color(0.75f, 0.5f, 1f) : new Color(0.7f, 0.7f, 0.65f);
            return Color.grey;
        }

        private struct F { public string K, L, T; public float Min, Max; public string[] O; }
        private static F R(string k, string l, float min, float max) => new F { K = k, L = l, T = "range", Min = min, Max = max };
        private static F C(string k, string l) => new F { K = k, L = l, T = "color" };
        private static F S(string k, string l, params string[] o) => new F { K = k, L = l, T = "select", O = o };
        private static F P(string k, string l) => new F { K = k, L = l, T = "prefab" };
        private static F E(string k, string l) => new F { K = k, L = l, T = "effect" };
        private static F Bo(string k, string l) => new F { K = k, L = l, T = "bool" };
        private static readonly F PHASE = S("phase", "When", "fire", "prefire");
        private static readonly Dictionary<string, F[]> FIELDS = new Dictionary<string, F[]>
        {
            { "beam", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), C("core", "Core"), R("width", "Width m", 0.05f, 6), R("glow", "Glow", 0, 6), R("duration", "Duration s", 0.05f, 3), S("fade", "Fade", "ease out", "linear", "hold", "pulse", "flicker"), S("reach", "Reach", "instant", "sweep"), R("count", "Pulses", 1, 8), R("gap", "Gap s", 0, 0.5f), R("jitter", "Jag m", 0, 8) } },
            { "bolt", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), S("shape", "Shape", "blade", "bolt", "ball", "ring"), R("length", "Length m", 0.2f, 20), R("width", "Width m", 0.05f, 4), R("speed", "Speed m/s", 20, 1200), R("glow", "Glow", 0, 6), R("spin", "Spin r/s", 0, 20), R("count", "Count", 1, 24), R("gap", "Gap s", 0, 0.5f), R("arc", "Arc m", 0, 200), R("scatter", "Spread m", 0, 30) } },
            { "trail", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), R("length", "Length m", 1, 120), R("width", "Width m", 0.05f, 4), R("opacity", "Opacity", 0, 1) } },
            { "flash", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), R("count", "Particles", 4, 400), R("size", "Size m", 0.2f, 12), R("speed", "Speed m/s", 0, 80), R("spread", "Spread °", 0, 180), R("life", "Lifetime s", 0.05f, 3), S("texture", "Texture", "soft", "star", "spark", "ring") } },
            { "burst", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), R("count", "Particles", 4, 600), R("size", "Size m", 0.2f, 12), R("speed", "Speed m/s", 0, 120), R("gravity", "Gravity", 0, 60), R("life", "Lifetime s", 0.05f, 4), S("texture", "Texture", "soft", "star", "spark", "ring"), R("ring", "Shock ring m", 0, 40) } },
            { "light", new[] { R("start", "Start s", 0, 3), PHASE, C("color", "Colour"), S("anchor", "Where", "muzzle", "impact", "bolt"), R("intensity", "Intensity", 0, 40), R("radius", "Radius m", 5, 200), R("duration", "Duration s", 0.05f, 3) } },
            { "weapon", new[] { R("start", "Start s", 0, 3), PHASE, E("effect", "Game effect"), R("count", "Shots", 1, 24), R("sx", "Scale x", 0.05f, 60), R("sy", "Scale y", 0.05f, 60), R("sz", "Scale z", 0.05f, 60), R("speed", "Speed ×", 0.1f, 5), S("tintMode", "Colour", "stock", "recipe", "custom"), C("color", "Custom colour"), Bo("withPrefire", "Its pre-fire"), Bo("withFlash", "Its muzzle flash"), Bo("withImpact", "Its impact") } },
            { "part", new[] { R("start", "Start s", 0, 3), PHASE, E("effect", "Game effect"), S("part", "Part", "prefire", "flash", "impact", "armor", "structure", "terrain", "building"), S("tintMode", "Colour", "stock", "recipe", "custom"), C("color", "Custom colour"), S("anchor", "Where", "muzzle", "impact", "ground", "bolt"), R("size", "Scale", 0.05f, 10), R("duration", "Lives s", 0.1f, 8), S("facing", "Facing", "beam", "flat") } },
            { "pulse", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), R("count", "Pulses", 1, 40), R("size", "Size m", 0.1f, 6), R("speed", "Speed m/s", 20, 1500), R("gap", "Gap s", 0.01f, 0.5f), R("duration", "Emit for s", 0.05f, 3), S("texture", "Texture", "soft", "star", "spark", "ring") } },
            { "helix", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), R("count", "Strands", 1, 6), R("width", "Width m", 0.05f, 2), R("radius", "Radius m", 0.1f, 12), R("turns", "Turns", 0.5f, 40), R("spin", "Spin r/s", 0, 10), R("duration", "Duration s", 0.05f, 3), S("reach", "Reach", "instant", "sweep"), S("fade", "Fade", "ease out", "linear", "hold", "pulse", "flicker") } },
            { "arc", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), C("core", "Core"), S("reach", "Path", "beam", "fork"), S("anchor", "Fork from", "muzzle", "impact", "bolt"), R("width", "Width m", 0.05f, 3), R("glow", "Glow", 0, 6), R("jitter", "Jag m", 0, 12), R("count", "Branches", 0, 12), R("length", "Branch m", 0.5f, 40), R("rate", "Re-roll /s", 1, 60), R("duration", "Duration s", 0.05f, 3), S("fade", "Fade", "flicker", "ease out", "linear", "hold", "pulse") } },
            { "glow", new[] { R("start", "Start s", 0, 3), PHASE, C("color", "Colour"), S("anchor", "Where", "muzzle", "impact", "ground", "bolt"), R("size", "Size m", 0.2f, 30), R("glow", "Flicker", 0, 6), R("duration", "Duration s", 0.05f, 4), S("fade", "Fade", "ease out", "linear", "hold", "pulse", "flicker"), S("texture", "Texture", "soft", "star", "ring"), S("facing", "Facing", "camera", "flat", "beam") } },
            { "orb", new[] { R("start", "Start s", 0, 3), PHASE, C("color", "Colour"), C("core", "Core"), S("anchor", "Where", "muzzle", "impact", "bolt"), R("size", "Size m", 0.1f, 20), R("glow", "Halo", 0, 6), R("duration", "Duration s", 0.05f, 4), S("fade", "Fade", "in", "ease out", "hold", "pulse", "flicker") } },
            { "charge", new[] { R("start", "Start s", 0, 3), PHASE, C("color", "Colour"), R("count", "Particles", 4, 400), R("size", "Size m", 0.05f, 3), R("radius", "Gather from m", 0.5f, 30), R("duration", "Duration s", 0.1f, 3), S("texture", "Texture", "soft", "star", "spark") } },
            { "spray", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), R("count", "Particles", 10, 1500), R("size", "Size m", 0.1f, 8), R("speed", "Speed m/s", 5, 300), R("spread", "Cone °", 0, 60), R("life", "Lifetime s", 0.05f, 3), R("duration", "Emit for s", 0.05f, 3), R("gravity", "Gravity", 0, 40), S("texture", "Texture", "soft", "smoke", "star", "spark") } },
            { "casing", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), R("count", "Casings", 1, 40), R("size", "Size m", 0.03f, 1), R("speed", "Speed m/s", 1, 20), R("gap", "Gap s", 0.02f, 0.5f), R("gravity", "Gravity", 0, 40), R("life", "Lifetime s", 0.2f, 5) } },
            { "ring", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), S("anchor", "Where", "muzzle", "impact", "ground", "bolt"), R("size", "Radius m", 0.2f, 60), R("duration", "Duration s", 0.05f, 3), S("fade", "Fade", "ease out", "linear", "hold", "pulse"), S("facing", "Facing", "camera", "flat", "beam") } },
            { "scorch", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), S("anchor", "Where", "impact", "ground"), R("size", "Size m", 0.2f, 20), R("opacity", "Opacity", 0, 1), R("duration", "Duration s", 0.5f, 20), S("texture", "Shape", "soft", "disc"), S("facing", "Facing", "beam", "flat", "camera") } },
            { "smoke", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), S("anchor", "Where", "muzzle", "impact", "ground"), R("count", "Puffs", 1, 200), R("size", "Size m", 0.3f, 15), R("speed", "Rise m/s", 0, 20), R("life", "Lifetime s", 0.3f, 8), R("opacity", "Opacity", 0, 1) } },
            { "embers", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), S("anchor", "Where", "muzzle", "impact", "ground"), R("count", "Embers", 4, 400), R("size", "Size m", 0.05f, 2), R("speed", "Speed m/s", 0, 60), R("gravity", "Gravity", 0, 40), R("life", "Lifetime s", 0.2f, 6) } },
            { "debris", new[] { R("start", "Start s", 0, 3), C("color", "Colour"), S("anchor", "Where", "muzzle", "impact", "ground"), R("count", "Chunks", 1, 200), R("size", "Size m", 0.05f, 3), R("speed", "Speed m/s", 1, 80), R("spread", "Cone °", 5, 90), R("gravity", "Gravity", 0, 40), R("life", "Lifetime s", 0.2f, 6) } },
            { "vfx", new[] { R("start", "Start s", 0, 3), PHASE, P("prefab", "Game VFX"), S("tintMode", "Colour", "stock", "recipe", "custom"), C("color", "Custom colour"), S("anchor", "Where", "muzzle", "impact", "ground", "bolt"), R("size", "Scale", 0.05f, 10), R("duration", "Lives s", 0.1f, 8), S("facing", "Facing", "beam", "flat") } },
            { "shake", new[] { R("start", "Start s", 0, 3), S("anchor", "When", "muzzle", "impact"), R("intensity", "Strength", 0, 20), R("duration", "Duration s", 0.05f, 2) } },
        };

        // GN and vanilla weapon colours, for the palette row
        private static readonly string[] PALETTE = { "#f00e93", "#d6361a", "#7cf2c8", "#2ad4ff", "#7fd0ff", "#ff3b2f", "#2fff6a", "#ffb060", "#ffd27a", "#9b5cff", "#ffffff", "#ff8a3d" };

        // ------------------------------------------------------------------ slider with a typed value

        private string _editControl, _editText;

        /// <summary>A labelled slider whose value can also be typed: click the number, type, press Enter.</summary>
        private float Slider(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _small, GUILayout.Width(118));
            float nv = GUILayout.HorizontalSlider(v, min, max);
            string name = "val_" + label;
            bool fine = max - min < 50f && Mathf.Abs(v) < 10f;
            string shown = _editControl == name ? _editText : v.ToString(fine ? "0.##" : "0.#", CultureInfo.InvariantCulture);
            GUI.SetNextControlName(name);
            string typed = GUILayout.TextField(shown, _field, GUILayout.Width(52));
            bool focused = GUI.GetNameOfFocusedControl() == name;
            if (focused)
            {
                if (_editControl != name) { _editControl = name; _editText = shown; }
                if (typed != _editText) _editText = typed;
                if (Event.current.isKey && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)) { float parsed; if (float.TryParse(_editText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) nv = parsed; GUI.FocusControl(null); _editControl = null; }
            }
            else if (_editControl == name) { float parsed; if (float.TryParse(_editText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) nv = parsed; _editControl = null; }
            GUILayout.EndHorizontal();
            return nv;
        }

        // ------------------------------------------------------------------ colour

        /// <summary>Swatch, hex, hue/saturation/brightness sliders, palette, and the base effect's own colour. Returns the new hex.</summary>
        private string _revertKey, _revertHex;

        private string ColorPicker(string label, string hex, string key = null)
        {
            key = key ?? label;
            if (_revertKey == key) { hex = _revertHex; _revertKey = null; }
            string live = PopupValue(key); if (live != null) hex = live;
            Color c = Recipe.Col(hex ?? "#ffffff");
            float h, s, v; Color.RGBToHSV(c, out h, out s, out v);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _small, GUILayout.Width(118));
            var sw = GUILayoutUtility.GetRect(26, 18, GUILayout.Width(26)); GUI.DrawTexture(sw, Solid(c));
            var hr = GUILayoutUtility.GetRect(74, 20, GUILayout.Width(74));
            Event ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.clickCount == 2 && (sw.Contains(ev.mousePosition) || hr.Contains(ev.mousePosition))) { OpenPopup(key, hex, ev.mousePosition); ev.Use(); }
            string nh = _popupKey == key ? hex : GUI.TextField(hr, hex ?? "", _field);
            if (_popupKey == key) GUI.Label(hr, hex ?? "", _field);
            GUILayout.FlexibleSpace();
            if (_recipe.HasBase && GUILayout.Button("base", _btnSmall, GUILayout.Width(44))) { Color? bc = Stock.ProjectileColor(_recipe.baseEffect); if (bc.HasValue) nh = "#" + ColorUtility.ToHtmlStringRGB(bc.Value).ToLowerInvariant(); else Toast("That effect has no coloured projectile to read"); }
            GUILayout.EndHorizontal();
            float nhue = Slider("   Hue", h, 0f, 1f), nsat = Slider("   Saturation", s, 0f, 1f), nval = Slider("   Brightness", v, 0f, 1f);
            if (Mathf.Abs(nhue - h) > 0.001f || Mathf.Abs(nsat - s) > 0.001f || Mathf.Abs(nval - v) > 0.001f) nh = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(nhue, nsat, nval)).ToLowerInvariant();
            GUILayout.BeginHorizontal(); GUILayout.Space(118);
            foreach (string p in PALETTE)
            {
                var r = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16));
                GUI.DrawTexture(r, Solid(Recipe.Col(p)));
                if (string.Equals(p, nh, StringComparison.OrdinalIgnoreCase)) GUI.DrawTexture(new Rect(r.x, r.yMax - 2, r.width, 2), _gold);
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition)) { nh = p; Event.current.Use(); }
            }
            GUILayout.EndHorizontal();
            return nh;
        }

        // ------------------------------------------------------------------ one field of a layer

        private void Field(Layer l, F f)
        {
            var fi = typeof(Layer).GetField(f.K);
            if (fi == null) return;
            if (f.T == "range")
            {
                float v = fi.FieldType == typeof(int) ? (int)fi.GetValue(l) : (float)fi.GetValue(l);
                float nv = Slider(f.L, v, f.Min, f.Max);
                if (Mathf.Abs(nv - v) > 1e-5f) { if (fi.FieldType == typeof(int)) fi.SetValue(l, Mathf.RoundToInt(nv)); else fi.SetValue(l, (float)Math.Round(nv, 2)); }
            }
            else if (f.T == "color")
            {
                string hex = (string)fi.GetValue(l) ?? "#ffffff";
                string nh = ColorPicker(f.L, hex);
                if (nh != hex) fi.SetValue(l, nh);
            }
            else if (f.T == "bool")
            {
                bool cur = (bool)fi.GetValue(l);
                bool nv = GUILayout.Toggle(cur, " " + f.L, _label);
                if (nv != cur) { fi.SetValue(l, nv); Fire(); }
            }
            else if (f.T == "effect")
            {
                // one of the game's 47 stock effects
                string cur = (string)fi.GetValue(l) ?? "";
                GUILayout.Label(f.L + "  ·  " + (string.IsNullOrEmpty(cur) ? "none" : cur.Replace("WeaponEffect-Weapon_", "").Replace("WeaponEffect-", "")), _small);
                _effectFilter = GUILayout.TextField(_effectFilter, _field);
                int n = 0;
                foreach (string e in _effects)
                {
                    if (!string.IsNullOrEmpty(_effectFilter) && e.IndexOf(_effectFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (++n > 10) { GUILayout.Label("… type above to narrow the list", _small); break; }
                    if (GUILayout.Button(e.Replace("WeaponEffect-Weapon_", "").Replace("WeaponEffect-", ""), e == cur ? _btnOn : _btn)) { fi.SetValue(l, e); Fire(); }
                }
                if (!string.IsNullOrEmpty(cur)) foreach (string line in Stock.Anatomy(cur)) GUILayout.Label(line, _small);
            }
            else if (f.T == "prefab")
            {
                string cur = (string)fi.GetValue(l) ?? "";
                GUILayout.Label(f.L + "  ·  " + (string.IsNullOrEmpty(cur) ? "none" : cur), _small);
                _prefabFilter = GUILayout.TextField(_prefabFilter, _field);
                if (Assets != null && Assets.Ready && _prefabFilter.Length >= 2)
                {
                    int n = 0;
                    foreach (string id in Assets.PrefabBundle.Keys)
                    {
                        if (!id.StartsWith("vfx", StringComparison.OrdinalIgnoreCase) || id.IndexOf(_prefabFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (++n > 10) { GUILayout.Label("… type more to narrow the list", _small); break; }
                        if (GUILayout.Button(id, id == cur ? _btnOn : _btn)) { fi.SetValue(l, id); Fire(); }
                    }
                }
                else GUILayout.Label("Type two or more letters of a vfx prefab name (vfxPrfPrtl_…, vfxPrfb_…).", _small);
            }
            else
            {
                string cur = (string)fi.GetValue(l);
                GUILayout.BeginHorizontal();
                GUILayout.Label(f.L, _small, GUILayout.Width(118));
                foreach (string o in f.O) if (GUILayout.Button(o, o == cur ? _btnOn : _btnSmall)) fi.SetValue(l, o);
                GUILayout.EndHorizontal();
            }
        }

        private string _prefabFilter = "", _effectFilter = "";
    }
}
