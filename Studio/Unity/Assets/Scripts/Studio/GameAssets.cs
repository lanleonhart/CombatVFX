// Finds the BATTLETECH install and loads prefabs straight from its asset bundles (vanilla and the Mods' CAB packs), the
// way the game's DataManager does: VersionManifest.csv and each mod.json say which bundle holds a prefab; the
// "assetbundles-info" manifest says which bundles a bundle needs first. The "shaders" bundle is always loaded first,
// because the Mech materials reference shaders that live only there.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GNFX
{
    public class GameAssets
    {
        public string GamePath;
        public string Error;
        public readonly Dictionary<string, string> PrefabBundle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> BundlePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> PrefabPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AssetBundle> _loaded = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
        private AssetBundleManifest _manifest;
        public readonly List<string> Log = new List<string>();

        public List<string> EffectIds => PrefabBundle.Keys.Where(k => k.StartsWith("WeaponEffect-", StringComparison.OrdinalIgnoreCase)).OrderBy(k => k).ToList();
        public List<string> MechIds => PrefabBundle.Keys.Where(k => k.StartsWith("chrPrfMech_", StringComparison.OrdinalIgnoreCase) && !k.Contains("_portrait")).OrderBy(k => k).ToList();
        public bool Ready => string.IsNullOrEmpty(Error) && PrefabBundle.Count > 0;

        /// <summary>Every Steam library folder (from libraryfolders.vdf) and the usual GOG folders, first one with BattleTech.exe wins.</summary>
        public static string DetectGamePath()
        {
            var candidates = new List<string>();
            foreach (string steam in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam", Environment.GetEnvironmentVariable("ProgramFiles(x86)") + @"\Steam" })
            {
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                candidates.Add(Path.Combine(steam, "steamapps", "common", "BATTLETECH"));
                try
                {
                    if (!File.Exists(vdf)) continue;
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), @"""path""\s+""([^""]+)"""))
                        candidates.Add(Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps", "common", "BATTLETECH"));
                }
                catch { }
            }
            foreach (char d in "CDEFGH") { candidates.Add(d + @":\SteamLibrary\steamapps\common\BATTLETECH"); candidates.Add(d + @":\Games\BATTLETECH"); candidates.Add(d + @":\GOG Games\BATTLETECH"); }
            candidates.Add(@"C:\Program Files (x86)\GOG Galaxy\Games\BATTLETECH");
            foreach (string c in candidates) { try { if (File.Exists(Path.Combine(c, "BattleTech.exe"))) return c; } catch { } }
            return null;
        }

        public void Index(string gamePath)
        {
            GamePath = gamePath; Error = null; PrefabBundle.Clear(); BundlePath.Clear(); PrefabPath.Clear(); Log.Clear();
            try
            {
                string data = Path.Combine(gamePath, "BattleTech_Data", "StreamingAssets", "data");
                string manifest = Path.Combine(data, "VersionManifest.csv");
                if (!File.Exists(manifest)) { Error = "No VersionManifest.csv under " + gamePath; return; }
                foreach (string line in File.ReadLines(manifest).Skip(1))
                {
                    string[] f = line.Split(',');
                    if (f.Length < 8 || f[1] != "Prefab" || string.IsNullOrEmpty(f[7])) continue;
                    PrefabBundle[f[0]] = f[7];
                    PrefabPath[f[0]] = f[2];
                }
                string bundles = Path.Combine(data, "assetbundles");
                foreach (string file in Directory.GetFiles(bundles)) if (!file.EndsWith(".manifest") && !file.EndsWith(".txt")) BundlePath[Path.GetFileName(file)] = file;
                Log.Add("Vanilla: " + PrefabBundle.Count + " prefabs in " + BundlePath.Count + " bundles");
                string mods = Path.Combine(gamePath, "Mods");
                int modPrefabs = 0;
                if (Directory.Exists(mods))
                    foreach (string modDir in Directory.GetDirectories(mods))
                    {
                        string modJson = Path.Combine(modDir, "mod.json");
                        if (!File.Exists(modJson)) continue;
                        ModJson mj = null;
                        try { mj = JsonUtility.FromJson<ModJson>(Strip(File.ReadAllText(modJson))); } catch { }
                        if (mj == null || mj.Manifest == null || mj.Enabled == false) continue;
                        foreach (ModEntry e in mj.Manifest)
                        {
                            if (e.Type == "AssetBundle" && !string.IsNullOrEmpty(e.Path))
                            {
                                string dir = Path.Combine(modDir, e.Path);
                                if (Directory.Exists(dir)) foreach (string file in Directory.GetFiles(dir)) if (!file.EndsWith(".manifest")) BundlePath[Path.GetFileName(file)] = file;
                            }
                            else if (e.Type == "Prefab" && !string.IsNullOrEmpty(e.AssetBundleName) && !string.IsNullOrEmpty(e.Path))
                            {
                                string id = Path.GetFileNameWithoutExtension(e.Path);
                                if (!PrefabBundle.ContainsKey(id)) { PrefabBundle[id] = e.AssetBundleName; PrefabPath[id] = e.Path; modPrefabs++; }
                            }
                        }
                    }
                Log.Add("Mods: " + modPrefabs + " prefabs; " + BundlePath.Count + " bundle files in all");
            }
            catch (Exception e) { Error = e.Message; }
        }

        [Serializable] private class ModJson { public bool Enabled = true; public ModEntry[] Manifest; }
        [Serializable] private class ModEntry { public string Type; public string Path; public string AssetBundleName; }

        /// <summary>JsonUtility chokes on comments and trailing commas that mod files sometimes carry.</summary>
        private static string Strip(string json)
        {
            var sb = new System.Text.StringBuilder(json.Length);
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) inStr = !inStr;
                if (!inStr && c == '/' && i + 1 < json.Length && json[i + 1] == '/') { while (i < json.Length && json[i] != '\n') i++; continue; }
                if (!inStr && c == '/' && i + 1 < json.Length && json[i + 1] == '*') { i += 2; while (i + 1 < json.Length && !(json[i] == '*' && json[i + 1] == '/')) i++; i++; continue; }
                sb.Append(c);
            }
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @",\s*([}\]])", "$1");
        }

        public AssetBundle Bundle(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            AssetBundle b;
            if (_loaded.TryGetValue(name, out b)) return b;
            string path;
            if (!BundlePath.TryGetValue(name, out path)) { Log.Add("No bundle file for " + name); _loaded[name] = null; return null; }
            _loaded[name] = null;     // placed before the dependencies load, so a cycle cannot recurse forever
            if (name != "assetbundles-info")
            {
                if (name != "shaders") Bundle("shaders");
                if (_manifest == null)
                {
                    AssetBundle info = Bundle("assetbundles-info");
                    if (info != null) _manifest = info.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                }
                if (_manifest != null) foreach (string dep in _manifest.GetAllDependencies(name)) if (dep != name) Bundle(dep);
            }
            b = AssetBundle.LoadFromFile(path);
            if (b == null) Log.Add("Could not load bundle " + name);
            else Debug.Log("[Studio] loaded bundle " + name);
            _loaded[name] = b;
            return b;
        }

        public GameObject Prefab(string id)
        {
            if (string.IsNullOrEmpty(id) || !PrefabBundle.TryGetValue(id, out string bundleName)) return null;
            AssetBundle b = Bundle(bundleName);
            if (b == null) return null;
            GameObject go = b.LoadAsset<GameObject>(id);
            if (go == null && PrefabPath.TryGetValue(id, out string p)) go = b.LoadAsset<GameObject>(p);
            if (go == null) go = b.LoadAsset<GameObject>(id.ToLowerInvariant());
            if (go == null) Log.Add("Prefab " + id + " not in bundle " + bundleName);
            return go;
        }
    }
}
