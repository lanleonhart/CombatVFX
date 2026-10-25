// The weapon definitions under Mods, so a recipe can be written straight into the weapon that will fire it: the CAC
// fields (WeaponEffectID, ColorsTable, ColorChangeRule, ColorSpeedChange, ProjectileScale, ProjectileSpeedMultiplier)
// are replaced or inserted at the top level of the JSON, and the CombatVFX tag vfx_<name> goes into ComponentTags when
// the recipe has layers. The file's own formatting is kept; a .bak copy is written first.
// The tag prefix matches CombatVFX's FxTagPrefix setting (vfx_ by default).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace GNFX
{
    public class WeaponDefs
    {
        public class Def { public string Path, Id, Name, Mod; public string Label => Name + "  ·  " + Id + "  ·  " + Mod; }
        public class Chassis { public string Path, Id, Name, Prefab, Mod; }
        public readonly List<Def> Defs = new List<Def>();
        public readonly List<Chassis> ChassisDefs = new List<Chassis>();
        public volatile bool Indexing, Done;
        public string Error;

        public void Index(string gamePath)
        {
            if (Indexing || Done) return;
            Indexing = true;
            new Thread(() =>
            {
                try
                {
                    string mods = System.IO.Path.Combine(gamePath, "Mods");
                    var found = new List<Def>(); var chassisFound = new List<Chassis>();
                    var roots = new List<string[]> { new[] { System.IO.Path.Combine(gamePath, "BattleTech_Data", "StreamingAssets", "data"), "vanilla" } };
                    if (Directory.Exists(mods)) foreach (string modDir in Directory.GetDirectories(mods)) if (!System.IO.Path.GetFileName(modDir).StartsWith(".")) roots.Add(new[] { modDir, System.IO.Path.GetFileName(modDir) });
                    foreach (string[] root in roots)
                    {
                        string modDir = root[0], modName = root[1];
                        if (!Directory.Exists(modDir)) continue;
                        foreach (string file in Directory.GetFiles(modDir, "*.json", SearchOption.AllDirectories))
                        {
                            try
                            {
                                var info = new FileInfo(file);
                                if (info.Length > 300000 || info.Length < 200) continue;
                                string s = File.ReadAllText(file);
                                if (System.IO.Path.GetFileName(file).StartsWith("chassisdef", StringComparison.OrdinalIgnoreCase) && s.IndexOf("\"PrefabIdentifier\"", StringComparison.Ordinal) >= 0)
                                {
                                    Match cid = Regex.Match(s, "\"Id\"\\s*:\\s*\"([^\"]+)\""), cname = Regex.Match(s, "\"UIName\"\\s*:\\s*\"([^\"]+)\""), pre = Regex.Match(s, "\"PrefabIdentifier\"\\s*:\\s*\"([^\"]+)\"");
                                    chassisFound.Add(new Chassis { Path = file, Id = cid.Success ? cid.Groups[1].Value : System.IO.Path.GetFileNameWithoutExtension(file), Name = cname.Success ? cname.Groups[1].Value : "", Prefab = pre.Success ? pre.Groups[1].Value : "", Mod = modName });
                                    continue;
                                }
                                if (s.IndexOf("\"WeaponEffectID\"", StringComparison.Ordinal) < 0 || s.IndexOf("\"Damage\"", StringComparison.Ordinal) < 0) continue;
                                Match id = Regex.Match(s, "\"Id\"\\s*:\\s*\"([^\"]+)\""), name = Regex.Match(s, "\"UIName\"\\s*:\\s*\"([^\"]+)\"");
                                found.Add(new Def { Path = file, Id = id.Success ? id.Groups[1].Value : System.IO.Path.GetFileNameWithoutExtension(file), Name = name.Success ? name.Groups[1].Value : "", Mod = modName });
                            }
                            catch { }
                        }
                    }
                    found.Sort((a, b) => string.Compare(a.Name + a.Id, b.Name + b.Id, StringComparison.OrdinalIgnoreCase));
                    chassisFound.Sort((a, b) => string.Compare(a.Name + a.Id, b.Name + b.Id, StringComparison.OrdinalIgnoreCase));
                    lock (Defs) { Defs.Clear(); Defs.AddRange(found); ChassisDefs.Clear(); ChassisDefs.AddRange(chassisFound); }
                }
                catch (Exception e) { Error = e.Message; }
                Indexing = false; Done = true;
            }) { IsBackground = true }.Start();
        }

        /// <summary>Write the recipe into a weapon file. Returns what was done.</summary>
        public static string Apply(Recipe r, string file)
        {
            string s = File.ReadAllText(file);
            File.Copy(file, file + ".bak", true);
            var ci = CultureInfo.InvariantCulture;
            var set = new List<KeyValuePair<string, string>>();
            var remove = new List<string>();
            if (r.HasBase) set.Add(new KeyValuePair<string, string>("WeaponEffectID", "\"" + r.baseEffect + "\""));
            if (r.colors.Count > 0)
            {
                var sb = new StringBuilder("[ ");
                for (int i = 0; i < r.colors.Count; i++) sb.Append(i > 0 ? ", " : "").Append("{ \"C\": \"").Append(r.colors[i].C).Append("\", \"I\": ").Append(r.colors[i].I.ToString("0.##", ci)).Append(" }");
                sb.Append(" ]");
                set.Add(new KeyValuePair<string, string>("ColorsTable", sb.ToString()));
                set.Add(new KeyValuePair<string, string>("ColorChangeRule", "\"" + r.colorRule + "\""));
                if (r.colorSpeed > 0f) set.Add(new KeyValuePair<string, string>("ColorSpeedChange", r.colorSpeed.ToString("0.##", ci))); else remove.Add("ColorSpeedChange");
            }
            else { remove.Add("ColorsTable"); remove.Add("ColorChangeRule"); remove.Add("ColorSpeedChange"); }
            if (r.scale != UnityEngine.Vector3.one) set.Add(new KeyValuePair<string, string>("ProjectileScale", "{ \"x\": " + r.scale.x.ToString("0.##", ci) + ", \"y\": " + r.scale.y.ToString("0.##", ci) + ", \"z\": " + r.scale.z.ToString("0.##", ci) + " }")); else remove.Add("ProjectileScale");
            if (Math.Abs(r.speedMul - 1f) > 0.001f) set.Add(new KeyValuePair<string, string>("ProjectileSpeedMultiplier", r.speedMul.ToString("0.##", ci))); else remove.Add("ProjectileSpeedMultiplier");
            int changed = 0;
            foreach (string key in remove) if (RemoveTopLevel(ref s, key)) changed++;
            foreach (var kv in set) { SetTopLevel(ref s, kv.Key, kv.Value); changed++; }
            string tagNote = "";
            string tag = "vfx_" + StudioUI.Slug(r.name);
            if (r.HasLayers) tagNote = SetTag(ref s, tag) ? ", tag " + tag + " added" : ", tag " + tag + " already there";
            else if (RemoveTags(ref s, "vfx_")) tagNote = ", vfx_ tag removed";
            File.WriteAllText(file, s);
            return changed + " fields written" + tagNote + "; backup " + Path.GetFileName(file) + ".bak";
        }

        // ---- top-level JSON key surgery that keeps the file's formatting

        /// <summary>Find a key at depth 1: the index of its opening quote and the index just past its value (before any comma).</summary>
        private static bool FindTopLevel(string s, string key, out int keyStart, out int valueEnd)
        {
            keyStart = valueEnd = -1;
            int depth = 0; bool inStr = false;
            string needle = "\"" + key + "\"";
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
                if (c == '"')
                {
                    if (depth == 1 && string.CompareOrdinal(s, i, needle, 0, needle.Length) == 0)
                    {
                        int colon = s.IndexOf(':', i + needle.Length);
                        if (colon < 0) return false;
                        int j = colon + 1; while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                        valueEnd = ValueEnd(s, j);
                        keyStart = i;
                        return valueEnd > 0;
                    }
                    inStr = true; continue;
                }
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
            }
            return false;
        }

        private static int ValueEnd(string s, int i)
        {
            if (i >= s.Length) return -1;
            char c = s[i];
            if (c == '"') { i++; while (i < s.Length) { if (s[i] == '\\') i += 2; else if (s[i] == '"') return i + 1; else i++; } return -1; }
            if (c == '{' || c == '[')
            {
                int depth = 0; bool inStr = false;
                for (; i < s.Length; i++)
                {
                    char d = s[i];
                    if (inStr) { if (d == '\\') i++; else if (d == '"') inStr = false; continue; }
                    if (d == '"') inStr = true;
                    else if (d == '{' || d == '[') depth++;
                    else if (d == '}' || d == ']') { depth--; if (depth == 0) return i + 1; }
                }
                return -1;
            }
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != '\n' && s[i] != '\r') i++;
            return i;
        }

        private static bool RemoveTopLevel(ref string s, string key)
        {
            int ks, ve;
            if (!FindTopLevel(s, key, out ks, out ve)) return false;
            int end = ve; while (end < s.Length && char.IsWhiteSpace(s[end])) end++;
            if (end < s.Length && s[end] == ',') end++;
            int start = ks; while (start > 0 && (s[start - 1] == ' ' || s[start - 1] == '\t')) start--;
            if (start > 0 && (s[start - 1] == '\n')) { start--; if (start > 0 && s[start - 1] == '\r') start--; }
            s = s.Remove(start, end - start);
            return true;
        }

        private static void SetTopLevel(ref string s, string key, string value)
        {
            int ks, ve;
            if (FindTopLevel(s, key, out ks, out ve))
            {
                int colon = s.IndexOf(':', ks); int j = colon + 1; while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                s = s.Substring(0, j) + value + s.Substring(ve);
                return;
            }
            int brace = s.IndexOf('{');
            if (brace < 0) return;
            Match m = Regex.Match(s.Substring(brace + 1), "\\r?\\n([ \\t]*)\"");
            string nl = s.Contains("\r\n") ? "\r\n" : "\n", indent = m.Success ? m.Groups[1].Value : "    ";
            s = s.Substring(0, brace + 1) + nl + indent + "\"" + key + "\": " + value + "," + s.Substring(brace + 1);
        }

        private static bool SetTag(ref string s, string tag)
        {
            if (s.Contains("\"" + tag + "\"")) return false;
            RemoveTags(ref s, "vfx_");
            Match m = Regex.Match(s, "\"ComponentTags\"\\s*:\\s*\\{\\s*\"items\"\\s*:\\s*\\[");
            if (m.Success)
            {
                int at = m.Index + m.Length;
                int close = s.IndexOf(']', at);
                bool empty = s.Substring(at, close - at).Trim().Length == 0;
                s = s.Substring(0, at) + (empty ? " \"" + tag + "\" " : " \"" + tag + "\",") + s.Substring(at);
                return true;
            }
            SetTopLevel(ref s, "ComponentTags", "{ \"items\": [ \"" + tag + "\" ], \"tagSetSourceFile\": \"\" }");
            return true;
        }

        private static bool RemoveTags(ref string s, string prefix)
        {
            string before = s;
            s = Regex.Replace(s, "\"" + prefix + "[^\"]*\"\\s*,\\s*", "");
            s = Regex.Replace(s, ",\\s*\"" + prefix + "[^\"]*\"", "");
            s = Regex.Replace(s, "\"" + prefix + "[^\"]*\"", "");
            return s != before;
        }
    }
}
