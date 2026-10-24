// The studio's interface, drawn with Unity's immediate-mode GUI in the console look. This file holds the state, the
// skin, the frame layout and the actions (fire, load, save, undo); the panels are in the other StudioUI.*.cs files:
//   Left     - the game's effects list, or the recipe (base effect, CAC knobs, layers), or the library grid
//   Right    - tabs: Layer (the selected layer's fields), Shot, Look, Game
//   Bottom   - stage row (range, 'Mechs, camera, playback) and the timeline strip
//   Fields   - the layer field table, sliders with typed values, the colour picker
//   Library  - recipe cards with thumbnails, search and family filters
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GNFX
{
    public partial class StudioUI : MonoBehaviour
    {
        public GameAssets Assets;
        public SceneRig Rig;
        public StockEffectPlayer Stock, Stock2;
        public RecipePlayer Recipes, Recipes2;
        public Sounds Audio;
        public string RecipesDir;
        public readonly WeaponDefs Weapons = new WeaponDefs();
        public static bool MouseOverPanel;

        private int _mode;            // 0 game effects, 1 recipe, 2 library
        private int _tab;             // right panel: 0 layer, 1 shot, 2 look, 3 game
        private List<string> _effects = new List<string>(), _mechs = new List<string>();
        private string _effect, _filter = "", _mechFilter = "";
        private int _shots = 1, _perShot = 1;
        private bool _loop;
        private float _nextLoop;
        private Recipe _recipe = new Recipe { name = "Untitled" };
        private string _recipePath;
        private int _layer = -1, _solo = -1;
        private List<string> _files = new List<string>();
        private Vector2 _sl, _sr, _sm, _slib;
        private string _toast; private float _toastUntil;
        private bool _pickShooter, _pickTarget;
        private GUIStyle _box, _btn, _btnOn, _btnSmall, _label, _cap, _field, _small, _tabOn, _tabOff;
        private Texture2D _dark, _tile, _gold, _line, _white;
        // compare
        private int _compare;         // 0 off, 1 stock base vs recipe, 2 recipe A vs recipe B
        private Recipe _recipeB; private string _recipeBPath;
        // undo
        private readonly List<string> _undo = new List<string>(), _redo = new List<string>();
        private string _lastJson = "", _savedJson = "";
        // layout
        private const float L = 280f, Rw = 360f, T = 44f, B = 48f, TL = 64f;   // panels: left, right, top, bottom stage row, timeline
        private const float LibW = 640f;
        private Rect _viewport;

        private bool Dirty => JsonUtility.ToJson(_recipe) != _savedJson;

        public void Init()
        {
            if (Assets != null && Assets.Ready) { _effects = Assets.EffectIds; _mechs = Assets.MechIds; }
            _effect = _effects.FirstOrDefault(e => e.Contains("Laser_Medium")) ?? _effects.FirstOrDefault();
            RefreshFiles();
            string first = _files.FirstOrDefault(f => f.Contains("blue_pulse")) ?? _files.FirstOrDefault(f => f.Contains("examples__")) ?? _files.FirstOrDefault();
            if (first != null) LoadRecipe(first);
            else { _recipe = new Recipe { name = "Blue pulse laser", baseEffect = "WeaponEffect-Weapon_LaserPulse_Medium", tags = "example, laser" }; _recipe.colors.Add(new ColorEntry { C = "#2ad4ff", I = 5f }); MarkSaved(); }
            ApplyHint();
        }

        private void RefreshFiles()
        {
            _files.Clear();
            try { if (Directory.Exists(RecipesDir)) _files.AddRange(Directory.GetFiles(RecipesDir, "*.json").OrderBy(f => f)); } catch { }
        }

        private void LoadRecipe(string path)
        {
            try
            {
                _recipe = Recipe.Parse(File.ReadAllText(path)); _recipePath = path;
                _layer = -1; _solo = -1; _undo.Clear(); _redo.Clear(); MarkSaved();
                ApplyHint();
                Toast("Loaded " + Path.GetFileName(path));
            }
            catch (Exception e) { Toast("Could not read " + Path.GetFileName(path) + ": " + e.Message); }
        }

        private void MarkSaved() { _savedJson = JsonUtility.ToJson(_recipe); _lastJson = _savedJson; }

        private void SaveRecipe()
        {
            try
            {
                Directory.CreateDirectory(RecipesDir);
                string file = Path.Combine(RecipesDir, Slug(_recipe.name) + ".json");
                File.WriteAllText(file, _recipe.ToJson());
                _recipePath = file; MarkSaved(); RefreshFiles(); Toast("Saved " + Path.GetFileName(file));
                StartCoroutine(Thumbnail(Path.ChangeExtension(file, ".png")));
            }
            catch (Exception e) { Toast("Could not save: " + e.Message); }
        }

        public static string Slug(string s) => System.Text.RegularExpressions.Regex.Replace((s ?? "untitled").ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');

        private void Toast(string s) { _toast = s; _toastUntil = Time.unscaledTime + 3f; }

        /// <summary>Point the shooter's muzzle at the hardpoint of the weapon family in play.</summary>
        private void ApplyHint()
        {
            if (Rig == null) return;
            string id = _mode == 0 ? _effect : _recipe.baseEffect;
            Rig.WeaponHint = SceneRig.HintFor(id);
            Rig.Refresh();
        }

        public void SelectEffect(string id) { if (_effects.Contains(id)) { _mode = 0; _effect = id; _shots = DefaultShots(id); _perShot = PerShot(id); ApplyHint(); } }
        public void SelectRecipe(string file) { string f = _files.FirstOrDefault(x => string.Equals(Path.GetFileName(x), file, StringComparison.OrdinalIgnoreCase)) ?? (File.Exists(file) ? file : null); if (f != null) { _mode = 1; LoadRecipe(f); } }

        private static int DefaultShots(string e)
        {
            var m = System.Text.RegularExpressions.Regex.Match(e ?? "", @"(LRM|SRM)(\d+)");
            if (m.Success) return int.Parse(m.Groups[2].Value);
            if (e != null && e.Contains("LB")) return 10;
            return 1;
        }
        private static int PerShot(string e) => e != null && e.Contains("MachineGun") ? 10 : 1;

        // ------------------------------------------------------------------ firing

        public void Fire()
        {
            if (Rig == null) return;
            Stock.Clear(); Recipes.Clear(); Stock2.Clear(); Recipes2.Clear();
            Playback.SeekTo = -1f;
            if (_mode == 0)
            {
                if (string.IsNullOrEmpty(_effect)) return;
                Stock.Mods = null; Stock.Play(_effect, Rig.Muzzle, Rig.Impact, _shots, _perShot);
                Playback.Fired(2f);
                return;
            }
            Recipe r = _solo >= 0 && _solo < _recipe.layers.Count ? _recipe.Solo(_solo) : _recipe;
            float pre = Pre();
            PlayRecipe(Stock, Recipes, r, Rig.Muzzle, Rig.Impact, pre);
            Vector3 impact2 = Rig.Impact + new Vector3(-2.5f, 0f, 0f);
            if (_compare == 1 && r.HasBase) { Stock2.Mods = null; Stock2.Play(r.baseEffect, Rig.Muzzle2, impact2, DefaultShots(r.baseEffect), PerShot(r.baseEffect)); }
            else if (_compare == 2 && _recipeB != null) PlayRecipe(Stock2, Recipes2, _recipeB, Rig.Muzzle2, impact2, _recipeB.HasBase ? Stock.PrefireOf(_recipeB.baseEffect) : _recipeB.prefire);
            Playback.Fired(r.Length(Rig.Range, pre));
        }

        /// <summary>The pre-fire phase length: the base effect's own, or the recipe's setting when there is no base.</summary>
        private float Pre() => _recipe.HasBase ? Stock.PrefireOf(_recipe.baseEffect) : (_recipe.HasPrefire ? _recipe.prefire : 0f);

        private static void PlayRecipe(StockEffectPlayer stock, RecipePlayer recipes, Recipe r, Vector3 muzzle, Vector3 impact, float pre)
        {
            stock.Mods = r;
            if (r.HasBase) stock.Play(r.baseEffect, muzzle, impact, DefaultShots(r.baseEffect), PerShot(r.baseEffect));
            if (r.HasLayers) recipes.Play(r, muzzle, impact, pre);
        }

        private bool AnyPlaying => Stock.Playing || Recipes.Playing || Stock2.Playing || Recipes2.Playing;

        private void Update()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool typing = GUIUtility.keyboardControl != 0 && !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl());
            if (Input.GetKeyDown(KeyCode.Space) && !typing) Fire();
            if (ctrl && Input.GetKeyDown(KeyCode.Z)) Undo();
            if (ctrl && Input.GetKeyDown(KeyCode.Y)) Redo();
            if (ctrl && Input.GetKeyDown(KeyCode.S) && _mode != 0) SaveRecipe();
            if (Input.GetKeyDown(KeyCode.P) && !typing) Playback.Paused = !Playback.Paused;
            if (Input.GetKeyDown(KeyCode.Period) && !typing) Playback.Step();
            if (_loop && !AnyPlaying && !Playback.Paused && Time.unscaledTime > _nextLoop) { Fire(); _nextLoop = Time.unscaledTime + 1.2f; }
            if (AnyPlaying) _nextLoop = Time.unscaledTime + 1.2f;
            TrackUndo();
        }

        // ------------------------------------------------------------------ undo

        private void TrackUndo()
        {
            string json = JsonUtility.ToJson(_recipe);
            if (json == _lastJson || Input.GetMouseButton(0)) return;      // a slider drag collapses into one step
            _undo.Add(_lastJson); if (_undo.Count > 100) _undo.RemoveAt(0);
            _redo.Clear();
            _lastJson = json;
        }

        private void Undo()
        {
            if (_undo.Count == 0) { Toast("Nothing to undo"); return; }
            _redo.Add(JsonUtility.ToJson(_recipe));
            string json = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1);
            try { _recipe = Recipe.Parse(json); _lastJson = json; _layer = Mathf.Min(_layer, _recipe.layers.Count - 1); Toast("Undone"); } catch { }
        }

        private void Redo()
        {
            if (_redo.Count == 0) return;
            string json = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(JsonUtility.ToJson(_recipe));
            try { _recipe = Recipe.Parse(json); _lastJson = json; Toast("Redone"); } catch { }
        }

        // ------------------------------------------------------------------ thumbnails

        private readonly Dictionary<string, Texture2D> _thumbs = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private IEnumerator Thumbnail(string pngPath)
        {
            // fire, wait for the shot's best moment, grab the viewport
            Fire();
            float at = _recipe.HasBase ? 0.45f : Mathf.Clamp(_recipe.Length(Rig.Range) * 0.35f, 0.2f, 1.2f);
            float t0 = Time.unscaledTime; while (Time.unscaledTime - t0 < at) yield return null;
            yield return new WaitForEndOfFrame();
            try
            {
                var full = new Texture2D((int)_viewport.width, (int)_viewport.height, TextureFormat.RGB24, false);
                full.ReadPixels(new Rect(_viewport.x, Screen.height - _viewport.yMax, _viewport.width, _viewport.height), 0, 0); full.Apply();
                RenderTexture rt = RenderTexture.GetTemporary(256, 144, 0); Graphics.Blit(full, rt);
                RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
                var small = new Texture2D(256, 144, TextureFormat.RGB24, false); small.ReadPixels(new Rect(0, 0, 256, 144), 0, 0); small.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); Destroy(full);
                File.WriteAllBytes(pngPath, small.EncodeToPNG());
                _thumbs[pngPath] = small;
            }
            catch (Exception e) { Debug.Log("[Studio] thumbnail failed: " + e.Message); }
        }

        private Texture2D Thumb(string jsonPath)
        {
            string png = Path.ChangeExtension(jsonPath, ".png");
            Texture2D t;
            if (_thumbs.TryGetValue(png, out t)) return t;
            t = null;
            try { if (File.Exists(png)) { t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(png)); } } catch { t = null; }
            _thumbs[png] = t;
            return t;
        }

        // ------------------------------------------------------------------ skin

        private static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        private void Skin()
        {
            if (_box != null) return;
            _dark = Solid(new Color(0.07f, 0.08f, 0.06f, 0.96f)); _tile = Solid(new Color(0.12f, 0.12f, 0.09f, 1f)); _gold = Solid(new Color(1f, 0.7f, 0f, 1f)); _line = Solid(new Color(1f, 1f, 1f, 0.08f)); _white = Solid(Color.white);
            _box = new GUIStyle(GUI.skin.box) { normal = { background = _dark }, padding = new RectOffset(10, 10, 8, 8) };
            _btn = new GUIStyle(GUI.skin.button) { normal = { background = _tile, textColor = new Color(0.73f, 0.72f, 0.68f) }, hover = { background = Solid(new Color(0.18f, 0.18f, 0.14f)), textColor = Color.white }, active = { background = _tile, textColor = Color.white }, fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 8, 5, 5), border = new RectOffset(0, 0, 0, 0) };
            _btnOn = new GUIStyle(_btn) { normal = { background = _gold, textColor = new Color(0.09f, 0.07f, 0f) }, hover = { background = _gold, textColor = Color.black }, fontStyle = FontStyle.Bold };
            _btnSmall = new GUIStyle(_btn) { fontSize = 11, padding = new RectOffset(6, 6, 3, 3), alignment = TextAnchor.MiddleCenter };
            _label = new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(0.93f, 0.92f, 0.89f) }, fontSize = 12, wordWrap = true };
            _small = new GUIStyle(_label) { fontSize = 11, normal = { textColor = new Color(0.73f, 0.72f, 0.68f) } };
            _cap = new GUIStyle(_label) { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.7f, 0f) } };
            _field = new GUIStyle(GUI.skin.textField) { normal = { background = Solid(new Color(0.04f, 0.04f, 0.03f)), textColor = Color.white }, focused = { background = Solid(new Color(0.06f, 0.06f, 0.04f)), textColor = Color.white }, fontSize = 12, padding = new RectOffset(6, 6, 4, 4) };
            _tabOff = new GUIStyle(_btn) { alignment = TextAnchor.MiddleCenter, fontSize = 11, padding = new RectOffset(4, 4, 4, 4) };
            _tabOn = new GUIStyle(_tabOff) { normal = { background = Solid(new Color(0.22f, 0.2f, 0.12f)), textColor = new Color(1f, 0.7f, 0f) }, fontStyle = FontStyle.Bold };
            GUI.skin.horizontalSlider.normal.background = _line; GUI.skin.horizontalSliderThumb.normal.background = _gold; GUI.skin.horizontalSliderThumb.fixedWidth = 10f;
            GUI.skin.verticalScrollbar.normal.background = _line; GUI.skin.verticalScrollbarThumb.normal.background = Solid(new Color(0.4f, 0.4f, 0.33f));
            GUI.skin.toggle.normal.textColor = GUI.skin.toggle.onNormal.textColor = new Color(0.93f, 0.92f, 0.89f);
        }

        private void OnGUI()
        {
            Skin();
            float W = Screen.width, H = Screen.height;
            float leftW = _mode == 2 ? LibW : L;
            var top = new Rect(0, 0, W, T); var left = new Rect(0, T, leftW, H - T - B - TL); var right = new Rect(W - Rw, T, Rw, H - T - B - TL);
            var timeline = new Rect(0, H - B - TL, W, TL); var bottom = new Rect(0, H - B, W, B);
            _viewport = new Rect(leftW, T, W - leftW - Rw, H - T - B - TL);
            Vector2 m = new Vector2(Input.mousePosition.x, H - Input.mousePosition.y);
            MouseOverPanel = top.Contains(m) || left.Contains(m) || right.Contains(m) || bottom.Contains(m) || timeline.Contains(m);
            GUI.Box(top, GUIContent.none, _box); GUI.Box(left, GUIContent.none, _box); GUI.Box(right, GUIContent.none, _box); GUI.Box(bottom, GUIContent.none, _box); GUI.Box(timeline, GUIContent.none, _box);
            TopBar(top);
            if (_mode == 2) Library(left); else Left(left);
            Right(right); Timeline(timeline); Bottom(bottom);
            if (!string.IsNullOrEmpty(_toast) && Time.unscaledTime < _toastUntil) { var r = new Rect(W / 2f - 260f, T + 14f, 520f, 26f); GUI.Box(r, GUIContent.none, _box); GUI.Label(new Rect(r.x + 10, r.y + 4, r.width - 20, 20), _toast, _label); }
            if (Playback.Paused) GUI.Label(new Rect(_viewport.x + 12, _viewport.y + 8, 200, 20), "PAUSED  ·  " + Playback.Now.ToString("0.00") + " s", _cap);
            else if (Playback.Speed < 0.99f) GUI.Label(new Rect(_viewport.x + 12, _viewport.y + 8, 200, 20), "×" + Playback.Speed.ToString("0.##") + "  ·  " + Playback.Now.ToString("0.00") + " s", _cap);
            if (Assets != null && !Assets.Ready) GamePathBox(new Rect(leftW + 20f, T + 20f, W - leftW - Rw - 40f, 110f));
        }

        private string _pathEdit;
        private void GamePathBox(Rect r)
        {
            GUI.Box(r, GUIContent.none, _box);
            GUILayout.BeginArea(new Rect(r.x + 12, r.y + 10, r.width - 24, r.height - 20));
            GUILayout.Label("BATTLETECH was not found. Enter the game folder (the one with BattleTech.exe):", _label);
            if (_pathEdit == null) _pathEdit = Assets.GamePath ?? "";
            _pathEdit = GUILayout.TextField(_pathEdit, _field);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Use this folder", _btnOn, GUILayout.Width(140))) { Boot.Instance.SetGamePath(_pathEdit); Init(); }
            GUILayout.Label(Assets.Error ?? "", _small);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void TopBar(Rect r)
        {
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 16));
            GUILayout.BeginHorizontal();
            GUILayout.Label("EFFECTS STUDIO", new GUIStyle(_label) { fontStyle = FontStyle.Bold, fontSize = 14 }, GUILayout.Width(130));
            if (GUILayout.Button("Game effects", _mode == 0 ? _btnOn : _btn, GUILayout.Width(100))) { _mode = 0; ApplyHint(); }
            if (GUILayout.Button("Recipe", _mode == 1 ? _btnOn : _btn, GUILayout.Width(70))) { _mode = 1; ApplyHint(); }
            if (GUILayout.Button("Library", _mode == 2 ? _btnOn : _btn, GUILayout.Width(70))) { _mode = 2; RefreshFiles(); }
            GUILayout.Space(16);
            if (_mode == 0) GUILayout.Label(_effect ?? "", _label, GUILayout.Width(300));
            else
            {
                GUILayout.Label("Name", _small, GUILayout.Width(36));
                GUI.SetNextControlName("recipe_name");
                _recipe.name = GUILayout.TextField(_recipe.name ?? "", _field, GUILayout.Width(200));
                GUILayout.Label(Dirty ? "●  unsaved" : "saved", new GUIStyle(_small) { normal = { textColor = Dirty ? new Color(1f, 0.7f, 0f) : new Color(0.5f, 0.5f, 0.45f) } }, GUILayout.Width(70));
                if (GUILayout.Button("Undo", _undo.Count > 0 ? _btn : _tabOff, GUILayout.Width(50))) Undo();
                if (GUILayout.Button("Redo", _redo.Count > 0 ? _btn : _tabOff, GUILayout.Width(50))) Redo();
                if (GUILayout.Button("Save", _btn, GUILayout.Width(50))) SaveRecipe();
                if (GUILayout.Button("New", _btn, GUILayout.Width(45))) { _recipe = new Recipe { name = "Untitled" }; _recipePath = null; _layer = -1; _solo = -1; _undo.Clear(); _redo.Clear(); MarkSaved(); _mode = 1; }
                if (GUILayout.Button("Duplicate", _btn, GUILayout.Width(75))) { _recipe = Recipe.Parse(_recipe.ToJson()); _recipe.name += " copy"; _recipePath = null; MarkSaved(); _mode = 1; }
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("FIRE  (space)", _btnOn, GUILayout.Width(110))) Fire();
            if (GUILayout.Button("Loop", _loop ? _btnOn : _btn, GUILayout.Width(50))) _loop = !_loop;
            GUILayout.Space(10);
            foreach (float sp in new[] { 1f, 0.5f, 0.25f, 0.1f })
                if (GUILayout.Button("×" + sp.ToString("0.##"), Mathf.Abs(Playback.Speed - sp) < 0.01f && !Playback.Paused ? _tabOn : _tabOff, GUILayout.Width(44))) { Playback.Speed = sp; Playback.Paused = false; }
            if (GUILayout.Button(Playback.Paused ? "▶ Play (P)" : "❚❚ Pause (P)", Playback.Paused ? _tabOn : _tabOff, GUILayout.Width(90))) Playback.Paused = !Playback.Paused;
            if (GUILayout.Button("Step (.)", _tabOff, GUILayout.Width(60))) Playback.Step();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
