// A proper colour picker in a floating panel: a saturation/brightness square for the current hue, a hue strip, the
// hex, HSV readouts and the palette. Opened by double-clicking a colour swatch or its hex; edits apply live to the
// field it was opened for and Done closes it.
using System;
using System.Globalization;
using UnityEngine;

namespace GNFX
{
    public partial class StudioUI
    {
        private string _popupKey;              // which colour field the popup edits
        private string _popupHex = "#ffffff";
        private float _ph, _ps = 1f, _pv = 1f;
        private Rect _popupRect;
        private Texture2D _svTex, _hueTex; private float _svTexHue = -1f;
        private int _popupDrag;                // 1 square, 2 hue strip

        private void OpenPopup(string key, string hex, Vector2 at)
        {
            _popupKey = key; _popupHex = hex ?? "#ffffff"; _popupStart = _popupHex;
            Color c = Recipe.Col(_popupHex); Color.RGBToHSV(c, out _ph, out _ps, out _pv);
            float W = Screen.width, H = Screen.height;
            _popupRect = new Rect(Mathf.Clamp(at.x - 20f, 8f, W - 300f), Mathf.Clamp(at.y - 40f, T + 4f, H - 360f), 292f, 350f);
        }

        /// <summary>The popup's current colour for a field, or null when the popup is not editing that field.</summary>
        private string PopupValue(string key) => _popupKey == key ? _popupHex : null;

        private void BuildPickerTextures()
        {
            if (_hueTex == null)
            {
                _hueTex = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int x = 0; x < 256; x++) _hueTex.SetPixel(x, 0, Color.HSVToRGB(x / 255f, 1f, 1f));
                _hueTex.Apply();
            }
            if (_svTex == null) _svTex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            if (Mathf.Abs(_svTexHue - _ph) > 0.002f)
            {
                _svTexHue = _ph;
                var px = new Color[64 * 64];
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) px[y * 64 + x] = Color.HSVToRGB(_ph, x / 63f, y / 63f);
                _svTex.SetPixels(px); _svTex.Apply();
            }
        }

        /// <summary>Drawn last in OnGUI so it sits over everything.</summary>
        private void ColorPopup()
        {
            if (_popupKey == null) return;
            BuildPickerTextures();
            Rect r = _popupRect;
            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            MouseOverPanel = MouseOverPanel || r.Contains(mouse);
            GUI.Box(r, GUIContent.none, _box);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2), _gold);
            var sq = new Rect(r.x + 12, r.y + 30, 200, 200);
            var hue = new Rect(r.x + 222, r.y + 30, 18, 200);
            GUI.Label(new Rect(r.x + 12, r.y + 8, 200, 18), "COLOUR", _cap);
            GUI.DrawTexture(sq, _svTex);
            // the square's marker and the hue strip (drawn rotated: hue runs top to bottom)
            GUIUtility.RotateAroundPivot(90f, new Vector2(hue.x + hue.width / 2f, hue.y + hue.height / 2f));
            GUI.DrawTexture(new Rect(hue.x + hue.width / 2f - hue.height / 2f, hue.y + hue.height / 2f - hue.width / 2f, hue.height, hue.width), _hueTex);
            GUI.matrix = Matrix4x4.identity;
            float mx = sq.x + _ps * sq.width, my = sq.y + (1f - _pv) * sq.height;
            GUI.DrawTexture(new Rect(mx - 5, my - 5, 10, 10), _white); GUI.DrawTexture(new Rect(mx - 3, my - 3, 6, 6), Solid(Color.HSVToRGB(_ph, _ps, _pv)));
            float hy = hue.y + _ph * hue.height;
            GUI.DrawTexture(new Rect(hue.x - 3, hy - 1, hue.width + 6, 3), _white);
            Event e = Event.current;
            if (e.type == EventType.MouseDown) { if (sq.Contains(e.mousePosition)) _popupDrag = 1; else if (hue.Contains(e.mousePosition)) _popupDrag = 2; }
            if (e.type == EventType.MouseUp) _popupDrag = 0;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && _popupDrag != 0)
            {
                if (_popupDrag == 1) { _ps = Mathf.Clamp01((e.mousePosition.x - sq.x) / sq.width); _pv = 1f - Mathf.Clamp01((e.mousePosition.y - sq.y) / sq.height); }
                else _ph = Mathf.Clamp01((e.mousePosition.y - hue.y) / hue.height);
                _popupHex = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(_ph, _ps, _pv)).ToLowerInvariant();
                e.Use();
            }
            // readouts
            var row = new Rect(r.x + 12, r.y + 240, r.width - 24, 22);
            GUI.DrawTexture(new Rect(row.x, row.y, 40, 22), Solid(Recipe.Col(_popupHex)));
            GUI.SetNextControlName("popup_hex");
            string typed = GUI.TextField(new Rect(row.x + 48, row.y, 90, 22), _popupHex, _field);
            if (typed != _popupHex && typed.Length >= 7) { Color c; if (ColorUtility.TryParseHtmlString(typed, out c)) { _popupHex = typed.ToLowerInvariant(); Color.RGBToHSV(c, out _ph, out _ps, out _pv); } }
            GUI.Label(new Rect(row.x + 146, row.y + 2, 130, 20), "H " + Mathf.RoundToInt(_ph * 360f) + "°  S " + Mathf.RoundToInt(_ps * 100f) + "  B " + Mathf.RoundToInt(_pv * 100f), _small);
            // palette
            float px = r.x + 12, py = r.y + 272;
            foreach (string p in PALETTE)
            {
                var pr = new Rect(px, py, 18, 18); GUI.DrawTexture(pr, Solid(Recipe.Col(p)));
                if (e.type == EventType.MouseDown && pr.Contains(e.mousePosition)) { _popupHex = p; Color c = Recipe.Col(p); Color.RGBToHSV(c, out _ph, out _ps, out _pv); e.Use(); }
                px += 22;
            }
            if (GUI.Button(new Rect(r.x + r.width - 72, r.y + 306, 60, 26), "Done", _btnOn)) { _popupKey = null; }
            if (GUI.Button(new Rect(r.x + 12, r.y + 306, 90, 26), "Cancel", _btnSmall)) { _revertKey = _popupKey; _revertHex = _popupStart; _popupKey = null; }
            // click outside closes and keeps the colour
            if (e.type == EventType.MouseDown && !r.Contains(e.mousePosition)) { _popupKey = null; }
        }

        private string _popupStart = "#ffffff";
    }
}
