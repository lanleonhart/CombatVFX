// Library: every recipe as a card with its thumbnail (taken on save), name, family and tags; a search box and family
// chips to filter; click to load and fire, Edit to go to the recipe, B to make it the comparison recipe.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GNFX
{
    public partial class StudioUI
    {
        private string _libFilter = "", _libFamily = "";
        private readonly Dictionary<string, Recipe> _libCache = new Dictionary<string, Recipe>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _libStamp = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private Recipe Peek(string path)
        {
            try
            {
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                Recipe r; DateTime seen;
                if (_libCache.TryGetValue(path, out r) && _libStamp.TryGetValue(path, out seen) && seen == stamp) return r;
                r = Recipe.Parse(File.ReadAllText(path)); _libCache[path] = r; _libStamp[path] = stamp; return r;
            }
            catch { return null; }
        }

        private static string FamilyOf(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return name.Contains("__") ? name.Split(new[] { "__" }, StringSplitOptions.None)[0] : "other";
        }

        private void Library(Rect r)
        {
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 16));
            GUILayout.BeginHorizontal();
            GUILayout.Label("LIBRARY · " + _files.Count + " recipes in " + RecipesDir, _cap);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open folder", _btnSmall, GUILayout.Width(90))) { try { Directory.CreateDirectory(RecipesDir); System.Diagnostics.Process.Start(RecipesDir); } catch { } }
            if (GUILayout.Button("Refresh", _btnSmall, GUILayout.Width(70))) { RefreshFiles(); _thumbs.Clear(); }
            GUILayout.EndHorizontal();
            _libFilter = GUILayout.TextField(_libFilter, _field);
            GUILayout.BeginHorizontal();
            var families = _files.Select(FamilyOf).Distinct().OrderBy(f => f).ToList();
            if (GUILayout.Button("All", _libFamily == "" ? _btnOn : _btnSmall)) _libFamily = "";
            foreach (string f in families) if (GUILayout.Button(f, _libFamily == f ? _btnOn : _btnSmall)) _libFamily = _libFamily == f ? "" : f;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            _slib = GUILayout.BeginScrollView(_slib);
            const float cardW = 192f, cardH = 170f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((r.width - 40f) / (cardW + 8f)));
            int n = 0;
            GUILayout.BeginHorizontal();
            foreach (string f in _files)
            {
                string fam = FamilyOf(f);
                if (_libFamily != "" && fam != _libFamily) continue;
                Recipe p = Peek(f);
                string name = p != null ? p.name : Path.GetFileNameWithoutExtension(f);
                string hay = name + " " + fam + " " + (p != null ? p.tags + " " + p.baseEffect : "");
                if (!string.IsNullOrEmpty(_libFilter) && hay.IndexOf(_libFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n > 0 && n % perRow == 0) { GUILayout.EndHorizontal(); GUILayout.Space(8); GUILayout.BeginHorizontal(); }
                n++;
                Card(f, p, name, fam, cardW, cardH);
                GUILayout.Space(8);
            }
            GUILayout.EndHorizontal();
            if (n == 0) GUILayout.Label("No recipe matches.", _small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void Card(string path, Recipe p, string name, string fam, float w, float h)
        {
            Rect card = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
            bool current = path == _recipePath, isB = path == _recipeBPath;
            GUI.DrawTexture(card, current ? Solid(new Color(0.22f, 0.2f, 0.12f)) : _tile);
            var pic = new Rect(card.x + 4, card.y + 4, w - 8, 100);
            Texture2D t = Thumb(path);
            if (t != null) GUI.DrawTexture(pic, t, ScaleMode.ScaleAndCrop);
            else { GUI.DrawTexture(pic, Solid(new Color(0.05f, 0.05f, 0.04f))); GUI.Label(new Rect(pic.x + 6, pic.y + 40, pic.width - 12, 20), "no picture yet · save to take one", _small); }
            if (p != null)
            {
                // colour chips: the CAC colours, or the layers' colours
                float cx = pic.x + 4;
                var chips = p.colors.Count > 0 ? p.colors.Select(c => c.C) : p.layers.Where(l => l.on).Select(l => l.color);
                foreach (string c in chips.Take(6)) { GUI.DrawTexture(new Rect(cx, pic.yMax - 10, 12, 6), Solid(Recipe.Col(c))); cx += 14; }
            }
            GUI.Label(new Rect(card.x + 6, pic.yMax + 4, w - 12, 18), name, new GUIStyle(_label) { fontStyle = FontStyle.Bold, clipping = TextClipping.Clip, wordWrap = false });
            string sub = fam + (p != null && p.HasBase ? " · " + p.baseEffect.Replace("WeaponEffect-Weapon_", "").Replace("WeaponEffect-", "") : "") + (p != null && p.HasLayers ? " · " + p.layers.Count(l => l.on) + " layers" : "");
            GUI.Label(new Rect(card.x + 6, pic.yMax + 22, w - 12, 16), sub, new GUIStyle(_small) { clipping = TextClipping.Clip, wordWrap = false });
            if (p != null && !string.IsNullOrEmpty(p.tags)) GUI.Label(new Rect(card.x + 6, pic.yMax + 36, w - 12, 16), p.tags, new GUIStyle(_small) { clipping = TextClipping.Clip, wordWrap = false, normal = { textColor = new Color(1f, 0.7f, 0f, 0.8f) } });
            var row = new Rect(card.x + 6, card.yMax - 24, w - 12, 20);
            if (GUI.Button(new Rect(row.x, row.y, 54, 20), "Fire", _btnSmall)) { LoadRecipe(path); Fire(); }
            if (GUI.Button(new Rect(row.x + 58, row.y, 54, 20), "Edit", _btnSmall)) { LoadRecipe(path); _mode = 1; Fire(); }
            if (GUI.Button(new Rect(row.x + 116, row.y, 30, 20), "B", isB ? _btnOn : _btnSmall)) { _recipeB = Peek(path); _recipeBPath = path; _compare = 2; Rig.SetCompare(true); Toast("B = " + name + " · compare is on"); Fire(); }
            if (GUI.Button(new Rect(row.x + 150, row.y, 30, 20), "✕", _btnSmall)) { try { File.Delete(path); string png = Path.ChangeExtension(path, ".png"); if (File.Exists(png)) File.Delete(png); RefreshFiles(); Toast("Deleted " + Path.GetFileName(path)); } catch (Exception e) { Toast(e.Message); } }
            if (current) GUI.DrawTexture(new Rect(card.x, card.y, w, 2), _gold);
        }
    }
}
