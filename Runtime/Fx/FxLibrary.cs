// The recipes CombatVFX knows: every *.json under Mods\CombatVFX\effects, keyed by file name (which is the weapon tag
// without its vfx_ prefix). A file watcher marks the folder dirty when the Effects Studio installs or changes a recipe,
// and the next weapon effect frame reloads it, so a recipe can be tuned against a running mission. At combat start the
// game VFX prefabs the recipes name are requested from the DataManager so the vfx layer finds them loaded.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleTech;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace CombatVFX.Fx
{
    /// <summary>The recipe player's own settings, read from the mod's settings.json; all optional.</summary>
    internal static class FxSettings
    {
        internal static bool TestHotkey;          // Ctrl+Shift+E plays a recipe from the selected 'Mech even without the game's test tools
        internal static string TagPrefix = "vfx_";

        internal static void Load(string settingsJson)
        {
            try
            {
                if (string.IsNullOrEmpty(settingsJson)) return;
                JObject o = JObject.Parse(settingsJson);
                TestHotkey = o.Value<bool?>("FxTestHotkey") ?? false;
                string prefix = o.Value<string>("FxTagPrefix");
                if (!string.IsNullOrEmpty(prefix)) TagPrefix = prefix;
            }
            catch (Exception e) { ModEntry.Log("[Fx] settings: " + e.Message); }
        }
    }

    internal static class FxLibrary
    {
        private static readonly Dictionary<string, FxRecipe> _recipes = new Dictionary<string, FxRecipe>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static FileSystemWatcher _watcher;
        private static volatile bool _dirty = true;
        private static bool _loggedEmpty;
        internal static string Dir => Path.Combine(ModEntry.ModDirectory ?? ".", "effects");
        internal static int Count => _recipes.Count;

        internal static void EnsureLoaded()
        {
            if (!_dirty) return;
            _dirty = false;
            _recipes.Clear();
            try
            {
                if (!Directory.Exists(Dir)) { if (!_loggedEmpty) { ModEntry.Log("[Fx] no effects folder at " + Dir + "; recipes from the Effects Studio go there"); _loggedEmpty = true; } Watch(); return; }
                foreach (string file in Directory.GetFiles(Dir, "*.json"))
                {
                    try
                    {
                        FxRecipe r = FxRecipe.Parse(File.ReadAllText(file));
                        r.File = file;
                        _recipes[Path.GetFileNameWithoutExtension(file)] = r;
                    }
                    catch (Exception e) { ModEntry.Log("[Fx] bad recipe " + Path.GetFileName(file) + ": " + e.Message); }
                }
                ModEntry.Log("[Fx] " + _recipes.Count + " recipes loaded from " + Dir + (_recipes.Count > 0 ? ": " + string.Join(", ", _recipes.Keys.OrderBy(k => k).ToArray()) : ""));
                Watch();
            }
            catch (Exception e) { ModEntry.Log("[Fx] recipes: " + e.Message); }
        }

        private static void Watch()
        {
            if (_watcher != null) return;
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                _watcher = new FileSystemWatcher(Dir, "*.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size, IncludeSubdirectories = false };
                _watcher.Changed += (s, e) => _dirty = true; _watcher.Created += (s, e) => _dirty = true; _watcher.Deleted += (s, e) => _dirty = true; _watcher.Renamed += (s, e) => _dirty = true;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e) { ModEntry.Log("[Fx] recipe watcher: " + e.Message); }
        }

        /// <summary>The recipe a weapon's vfx_ tag names, or null.</summary>
        internal static FxRecipe ForWeapon(Weapon weapon)
        {
            var tags = weapon?.componentDef?.ComponentTags;
            if (tags == null) return null;
            EnsureLoaded();
            foreach (string tag in tags)
            {
                if (tag == null || !tag.StartsWith(FxSettings.TagPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                FxRecipe r;
                if (_recipes.TryGetValue(tag.Substring(FxSettings.TagPrefix.Length), out r)) return r;
            }
            return null;
        }

        internal static FxRecipe Get(string key) { EnsureLoaded(); FxRecipe r; return _recipes.TryGetValue(key, out r) ? r : null; }
        internal static List<string> Keys { get { EnsureLoaded(); return _recipes.Keys.OrderBy(k => k).ToList(); } }

        /// <summary>The recipe most recently written, for the test hotkey.</summary>
        internal static FxRecipe Latest()
        {
            EnsureLoaded();
            FxRecipe best = null; DateTime when = DateTime.MinValue;
            foreach (FxRecipe r in _recipes.Values) { try { DateTime w = File.GetLastWriteTimeUtc(r.File); if (w > when) { when = w; best = r; } } catch { } }
            return best;
        }

        /// <summary>Ask the DataManager for every game VFX prefab the recipes use, so the vfx layer finds them in the pool.</summary>
        internal static void PreloadPrefabs()
        {
            EnsureLoaded();
            foreach (FxRecipe r in _recipes.Values) foreach (FxLayer l in r.layers) if (l.kind == "vfx" && !string.IsNullOrEmpty(l.prefab)) RequestPrefab(l.prefab);
        }

        internal static void RequestPrefab(string id)
        {
            if (string.IsNullOrEmpty(id) || _requested.Contains(id)) return;
            _requested.Add(id);
            try
            {
                var dm = UnityGameInstance.BattleTechGame?.DataManager;
                if (dm == null) return;
                if (dm.Exists(BattleTechResourceType.Prefab, id)) return;
                var req = dm.CreateLoadRequest(null, false);
                req.AddBlindLoadRequest(BattleTechResourceType.Prefab, id, false);
                req.ProcessRequests();
            }
            catch (Exception e) { ModEntry.Log("[Fx] prefab request " + id + ": " + e.Message); }
        }

        internal static void CombatEnded() { _requested.Clear(); }
    }

    [HarmonyPatch(typeof(CombatGameState), "_Init")]
    internal static class CombatGameState_Init_Fx
    {
        private static void Postfix()
        {
            try { FxLibrary.EnsureLoaded(); FxLibrary.PreloadPrefabs(); FxTestHotkey.Ensure(); }
            catch (Exception e) { ModEntry.Log("[Fx] combat init: " + e.Message); }
        }
    }
}
