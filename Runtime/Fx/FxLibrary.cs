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

        /// <summary>Ask the DataManager for every game VFX prefab the recipes use, so the vfx and part layers find them in the pool.</summary>
        internal static void PreloadPrefabs()
        {
            EnsureLoaded();
            foreach (FxRecipe r in _recipes.Values) foreach (string e in new[] { r.baseSettings.prefirePart, r.baseSettings.flashPart, r.baseSettings.impactPart.Split('|')[0], r.baseSettings.armorPart, r.baseSettings.structurePart }) if (!string.IsNullOrEmpty(e) && e != "none") RequestPrefab(e);
            foreach (FxRecipe r in _recipes.Values) foreach (FxLayer l in r.layers)
            {
                if (l.kind == "vfx" && !string.IsNullOrEmpty(l.prefab)) RequestPrefab(l.prefab);
                if (l.kind == "part" && !string.IsNullOrEmpty(l.effect)) { RequestPrefab(l.effect); string n = PartName(l.effect, l.part); if (!string.IsNullOrEmpty(n)) RequestPrefab(n); }
            }
        }

        private static readonly Dictionary<string, string> _partNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The prefab name of one part of a stock effect, read off the effect's prefab (pooled and returned); null until that prefab is loaded.</summary>
        internal static string PartName(string effectId, string part)
        {
            if (string.IsNullOrEmpty(effectId)) return null;
            string key = effectId + "|" + part, cached;
            if (_partNames.TryGetValue(key, out cached)) return cached;
            try
            {
                var dm = UnityGameInstance.BattleTechGame?.DataManager;
                if (dm == null) return null;
                UnityEngine.GameObject go = dm.PooledInstantiate(effectId, BattleTechResourceType.Prefab);
                if (go == null) { RequestPrefab(effectId); return null; }
                WeaponEffect fx = go.GetComponent<WeaponEffect>();
                WeaponEffect sub = null;
                var be = fx as BallisticEffect; if (be != null && be.bulletPrefab != null) sub = be.bulletPrefab.GetComponent<WeaponEffect>();
                var me = fx as MissileLauncherEffect; if (me != null && me.MissilePrefab != null) sub = me.MissilePrefab.GetComponent<WeaponEffect>();
                string name = null;
                if (fx != null)
                    switch (part)
                    {
                        case "prefire": name = (fx.preFireVFXPrefab ?? sub?.preFireVFXPrefab)?.name; break;
                        case "flash": name = (fx.muzzleFlashVFXPrefab ?? sub?.muzzleFlashVFXPrefab)?.name; break;
                        case "impact":
                        {
                            string b = !string.IsNullOrEmpty(fx.impactVFXBase) ? fx.impactVFXBase : sub?.impactVFXBase;
                            string[] v = !string.IsNullOrEmpty(fx.impactVFXBase) ? fx.impactVFXVariations : sub?.impactVFXVariations;
                            if (!string.IsNullOrEmpty(b)) name = v != null && v.Length > 0 ? b + "_" + v[UnityEngine.Random.Range(0, v.Length)] : b;
                            break;
                        }
                        case "armor": name = fx.armorDamageVFXName ?? sub?.armorDamageVFXName; break;
                        default:
                            if (part != null && part.StartsWith("impact:")) { string b2 = !string.IsNullOrEmpty(fx.impactVFXBase) ? fx.impactVFXBase : sub?.impactVFXBase; if (!string.IsNullOrEmpty(b2)) name = b2 + "_" + part.Substring(7); }
                            break;
                        case "structure": name = fx.structureDamageVFXName ?? sub?.structureDamageVFXName; break;
                        case "terrain": name = fx.terrainHitVFXBase ?? sub?.terrainHitVFXBase; break;
                        case "building": name = fx.buildingHitOverlayVFXName ?? sub?.buildingHitOverlayVFXName; break;
                    }
                go.SetActive(false); dm.PoolGameObject(effectId, go);
                if (part != "impact") _partNames[key] = name;          // impacts pick a variation each time
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch (Exception e) { ModEntry.Log("[Fx] part " + part + " of " + effectId + ": " + e.Message); return null; }
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

        internal static void CombatEnded() { _requested.Clear(); _partNames.Clear(); }

        /// <summary>Read another stock effect's script off its pooled prefab, run an action on it, and pool it again.</summary>
        internal static void WithEffect(string effectId, Action<WeaponEffect, WeaponEffect> use)
        {
            if (string.IsNullOrEmpty(effectId)) return;
            try
            {
                var dm = UnityGameInstance.BattleTechGame?.DataManager;
                if (dm == null) return;
                UnityEngine.GameObject go = dm.PooledInstantiate(effectId, BattleTechResourceType.Prefab);
                if (go == null) { RequestPrefab(effectId); return; }
                WeaponEffect fx = go.GetComponent<WeaponEffect>(); WeaponEffect sub = null;
                var be = fx as BallisticEffect; if (be != null && be.bulletPrefab != null) sub = be.bulletPrefab.GetComponent<WeaponEffect>();
                var me = fx as MissileLauncherEffect; if (me != null && me.MissilePrefab != null) sub = me.MissilePrefab.GetComponent<WeaponEffect>();
                if (fx != null) use(fx, sub);
                go.SetActive(false); dm.PoolGameObject(effectId, go);
            }
            catch (Exception e) { ModEntry.Log("[Fx] effect " + effectId + ": " + e.Message); }
        }

        /// <summary>Write a recipe's base settings onto the weapon's effect as it fires: numbers, and pieces swapped for another weapon's or switched off.</summary>
        internal static void ApplyBase(WeaponEffect fx, FxRecipe r)
        {
            if (fx == null || r == null || r.baseSettings == null || !r.baseSettings.AnySet) return;
            FxBase b = r.baseSettings;
            if (b.preFireDuration >= 0f) fx.preFireDuration = b.preFireDuration;
            if (b.projectileSpeed >= 0f) fx.projectileSpeed = b.projectileSpeed;
            if (b.delayMin >= 0f) fx.attackSequenceNextDelayMin = b.delayMin;
            if (b.delayMax >= 0f) fx.attackSequenceNextDelayMax = b.delayMax;
            var le = fx as LaserEffect; if (le != null) { if (b.lightIntensity >= 0f) le.lightIntensity = b.lightIntensity; if (b.lightRadius >= 0f) le.lightRadius = b.lightRadius; }
            var pe = fx as PulseLaserEffect; if (pe != null && b.pulseDelay >= 0f) pe.pulseDelay = b.pulseDelay;
            var ba = fx as BallisticEffect; if (ba != null) { if (b.spreadAngle >= 0f) ba.spreadAngle = b.spreadAngle; if (b.shotDelay >= 0f) ba.shotDelay = b.shotDelay; if (b.burstDelay >= 0f) ba.burstDelay = b.burstDelay; }
            var ml = fx as MissileLauncherEffect; if (ml != null) { if (b.firingInterval >= 0f) ml.firingInterval = b.firingInterval; if (b.volleyInterval >= 0f) ml.volleyInterval = b.volleyInterval; if (b.curveStrength >= 0f) ml.missileCurveStrength = b.curveStrength; if (b.curveFrequency >= 0) ml.missileCurveFrequency = b.curveFrequency; }
            var fl = fx as FlamerEffect; if (fl != null && b.impactTime >= 0f) fl.impactTime = b.impactTime;
            if (b.prefirePart == "none") fx.preFireVFXPrefab = null; else if (b.prefirePart != "") WithEffect(b.prefirePart, (o, s) => { var g = o.preFireVFXPrefab ?? s?.preFireVFXPrefab; if (g != null) fx.preFireVFXPrefab = g; });
            if (b.flashPart == "none") fx.muzzleFlashVFXPrefab = null; else if (b.flashPart != "") WithEffect(b.flashPart, (o, s) => { var g = o.muzzleFlashVFXPrefab ?? s?.muzzleFlashVFXPrefab; if (g != null) fx.muzzleFlashVFXPrefab = g; });
            if (b.impactPart == "none") { fx.impactVFXBase = ""; fx.impactVFXVariations = null; }
            else if (b.impactPart != "")
            {
                string[] pick = b.impactPart.Split('|');
                WithEffect(pick[0], (o, s) =>
                {
                    string ob = !string.IsNullOrEmpty(o.impactVFXBase) ? o.impactVFXBase : s?.impactVFXBase;
                    if (string.IsNullOrEmpty(ob)) return;
                    fx.impactVFXBase = ob;
                    fx.impactVFXVariations = pick.Length > 1 ? new[] { pick[1] } : (!string.IsNullOrEmpty(o.impactVFXBase) ? o.impactVFXVariations : s?.impactVFXVariations);
                    if (fx.impactVFXVariations != null) foreach (string v in fx.impactVFXVariations) RequestPrefab(ob + "_" + v); else RequestPrefab(ob);
                });
            }
            if (b.armorPart == "none") fx.armorDamageVFXName = ""; else if (b.armorPart != "") WithEffect(b.armorPart, (o, s) => { string n = o.armorDamageVFXName ?? s?.armorDamageVFXName; if (!string.IsNullOrEmpty(n)) { fx.armorDamageVFXName = n; RequestPrefab(n); } });
            if (b.structurePart == "none") fx.structureDamageVFXName = ""; else if (b.structurePart != "") WithEffect(b.structurePart, (o, s) => { string n = o.structureDamageVFXName ?? s?.structureDamageVFXName; if (!string.IsNullOrEmpty(n)) { fx.structureDamageVFXName = n; RequestPrefab(n); } });
        }
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
