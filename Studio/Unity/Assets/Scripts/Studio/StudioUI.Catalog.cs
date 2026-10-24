// The catalogue of everything the game has: every stock weapon effect by its plain name with each of its parts spelled
// out (pre-fire charge, muzzle flash, each impact variation, armour, structure, terrain and building hits), and every
// other VFX prefab in the game under a readable name. One click adds the piece to the recipe as a layer.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GNFX
{
    public partial class StudioUI
    {
        private string _catFilter = "";
        private readonly HashSet<string> _catOpen = new HashSet<string>();
        private readonly Dictionary<string, List<string[]>> _partsOf = new Dictionary<string, List<string[]>>();   // effect id -> [part key, label]
        private List<string[]> _vfxAll;                                                                                 // [id, readable, category]

        /// <summary>The plain name of a stock effect: "Large laser", "Ultra AC/5", "LRM 20", "ER PPC".</summary>
        public static string Pretty(string effectId)
        {
            string e = (effectId ?? "").Replace("WeaponEffect-Weapon_", "").Replace("WeaponEffect-", "");
            Match m;
            if ((m = Regex.Match(e, @"^Laser(ER|Pulse)?_(Small|Medium|Large)(_Aerospace)?$")).Success)
                return (m.Groups[1].Value == "ER" ? "ER " : "") + m.Groups[2].Value.ToLowerInvariant() + (m.Groups[1].Value == "Pulse" ? " pulse" : "") + " laser" + (m.Groups[3].Success ? " (aerospace)" : "");
            if ((m = Regex.Match(e, @"^AC(\d+)(_Ultra|_Single)?$")).Success) return (m.Groups[2].Value == "_Ultra" ? "Ultra AC/" : "AC/") + m.Groups[1].Value + (m.Groups[2].Value == "_Single" ? " (single shot)" : "");
            if ((m = Regex.Match(e, @"^LB(\d+)X$")).Success) return "LB " + m.Groups[1].Value + "-X";
            if ((m = Regex.Match(e, @"^(LRM|SRM)(\d+)$")).Success) return m.Groups[1].Value + " " + m.Groups[2].Value;
            switch (e)
            {
                case "SRMInferno": return "Inferno SRM";
                case "GaussRifle": return "Gauss rifle";
                case "MachineGun": return "Machine gun";
                case "Flamer": return "Flamer";
                case "PPC": return "PPC";
                case "PPCER": return "ER PPC";
                case "PPCSnub": return "Snub PPC";
                case "COILS": return "COIL";
                case "TAG": return "TAG";
                case "Narc": return "Narc beacon";
                case "MeleeAttack": return "Melee";
                case "DFAAttack": return "Death from above";
                case "Artillery_Thumper": return "Thumper artillery";
                case "Artillery_MechMortar": return "Mech mortar";
                case "Art_Thumper_AP": return "Thumper shell (AP)";
                case "Art_Thumper_HE": return "Thumper shell (HE)";
            }
            return e.Replace('_', ' ');
        }

        private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>A readable name for a VFX prefab id: "vfxPrfPrtl_weaponLaserImpact_lrg" becomes "weapon laser impact (large)".</summary>
        public static string ReadableVfx(string id)
        {
            string s = Regex.Replace(id, @"^vfxPrf(Prtl|Line|Dst|Cloth|Decl|Mesh|b)_", "");
            s = Regex.Replace(s, @"([a-z0-9])([A-Z])", "$1 $2").Replace('_', ' ').ToLowerInvariant();
            s = Regex.Replace(s, @"\b(lrg|large)\b", "(large)"); s = Regex.Replace(s, @"\b(med|medium)\b", "(medium)"); s = Regex.Replace(s, @"\bsm\b", "(small)");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private static string VfxCategory(string id)
        {
            if (id.StartsWith("vfxPrfLine_")) return "Beam lines";
            if (id.StartsWith("vfxPrfDst_")) return "Destruction";
            if (id.StartsWith("vfxPrfCloth_")) return "Cloth and flags";
            if (id.StartsWith("vfxPrfDecl_")) return "Decals";
            if (id.StartsWith("vfxPrfMesh_")) return "Meshes";
            string s = id.Replace("vfxPrfPrtl_", "").Replace("vfxPrfb_", "").ToLowerInvariant();
            if (s.Contains("weapon") || s.Contains("bullet") || s.Contains("missile") || s.Contains("laser") || s.Contains("ppc") || s.Contains("flamer")) return "Weapon pieces";
            if (s.Contains("impact") || s.Contains("hit") || s.Contains("explo") || s.Contains("blast") || s.Contains("artillery")) return "Impacts and explosions";
            if (s.Contains("ecm") || s.Contains("probe") || s.Contains("sensor") || s.Contains("jammer")) return "ECM and sensors";
            if (s.Contains("mech") || s.Contains("jumpjet") || s.Contains("thruster") || s.Contains("footstep") || s.Contains("dust") || s.Contains("overheat") || s.Contains("vent")) return "'Mech";
            if (s.Contains("fire") || s.Contains("smoke") || s.Contains("burn") || s.Contains("ember")) return "Fire and smoke";
            if (s.Contains("argo") || s.Contains("room") || s.Contains("ui")) return "Argo and interface";
            if (s.Contains("rain") || s.Contains("snow") || s.Contains("wind") || s.Contains("fog") || s.Contains("cyclone") || s.Contains("arctic") || s.Contains("desert") || s.Contains("lunar") || s.Contains("martian") || s.Contains("water") || s.Contains("leaf") || s.Contains("ambient")) return "Weather and environment";
            return "Other";
        }

        /// <summary>What a layer is, in plain words, for the layer list and the timeline.</summary>
        private string LayerLabel(Layer l)
        {
            if (l.kind == "part") return Pretty(l.effect) + " · " + PartLabel(l.part);
            if (l.kind == "weapon") return Pretty(l.effect) + " · whole effect" + (Mathf.Abs(l.sx - 1f) > 0.01f || Mathf.Abs(l.sy - 1f) > 0.01f || Mathf.Abs(l.sz - 1f) > 0.01f ? " · scaled" : "");
            if (l.kind == "vfx") return string.IsNullOrEmpty(l.prefab) ? "Game VFX (none picked)" : Capital(ReadableVfx(l.prefab));
            return KIND_NAMES[l.kind];
        }

        private static string PartLabel(string part)
        {
            if (part != null && part.StartsWith("impact:")) return "impact " + part.Substring(7);
            int i = Array.IndexOf(StockEffectPlayer.Parts, part);
            return i >= 0 ? StockEffectPlayer.PartNames[i].ToLowerInvariant() : part;
        }

        /// <summary>The parts a stock effect really has, each with its label, found once and cached.</summary>
        private List<string[]> PartsOf(string effectId)
        {
            List<string[]> list;
            if (_partsOf.TryGetValue(effectId, out list)) return list;
            list = new List<string[]>();
            WeaponEffect fx = Stock.Effect(effectId);
            if (fx != null)
            {
                if (Stock.PartPrefab(effectId, "prefire") != null) list.Add(new[] { "prefire", "pre-fire charge" + (fx.preFireDuration > 0f ? " (" + fx.preFireDuration.ToString("0.##") + " s)" : "") });
                if (Stock.PartPrefab(effectId, "flash") != null) list.Add(new[] { "flash", "muzzle flash" });
                string[] vars = Stock.ImpactVariations(effectId);
                if (vars != null && vars.Length > 0) foreach (string v in vars) { if (Stock.PartPrefab(effectId, "impact:" + v) != null) list.Add(new[] { "impact:" + v, "impact " + v }); }
                else if (Stock.PartPrefab(effectId, "impact") != null) list.Add(new[] { "impact", "impact" });
                if (Stock.PartPrefab(effectId, "armor") != null) list.Add(new[] { "armor", "armour hit" });
                if (Stock.PartPrefab(effectId, "structure") != null) list.Add(new[] { "structure", "structure hit" });
                if (Stock.PartPrefab(effectId, "terrain") != null) list.Add(new[] { "terrain", "terrain hit" });
                if (Stock.PartPrefab(effectId, "building") != null) list.Add(new[] { "building", "building hit" });
            }
            _partsOf[effectId] = list;
            return list;
        }

        private List<string[]> VfxAll()
        {
            if (_vfxAll != null) return _vfxAll;
            _vfxAll = new List<string[]>();
            if (Assets != null && Assets.Ready)
                foreach (string id in Assets.PrefabBundle.Keys)
                    if (id.StartsWith("vfx", StringComparison.OrdinalIgnoreCase)) _vfxAll.Add(new[] { id, ReadableVfx(id), VfxCategory(id) });
            _vfxAll = _vfxAll.OrderBy(x => x[2]).ThenBy(x => x[1]).ToList();
            return _vfxAll;
        }

        private void AddWeapon(string effectId)
        {
            Layer l = Layer.New("weapon"); l.effect = effectId;
            _recipe.layers.Add(l); _layer = _recipe.layers.Count - 1; _tab = 0;
            Toast("Added " + Pretty(effectId) + " as a whole-effect layer to " + _recipe.name + (_mode == 0 ? " — see the Recipe tab" : ""));
            Fire();
        }

        private void AddVfx(string id)
        {
            Layer l = Layer.New("vfx"); l.prefab = id;
            _recipe.layers.Add(l); _layer = _recipe.layers.Count - 1;
            Toast("Added " + ReadableVfx(id) + " to " + _recipe.name);
        }

        private bool Fold(string key, string label)
        {
            bool open = _catOpen.Contains(key);
            if (GUILayout.Button((open ? "▾  " : "▸  ") + label, _btn)) { if (open) _catOpen.Remove(key); else _catOpen.Add(key); open = !open; }
            return open;
        }

        /// <summary>The catalogue, drawn inside the add-a-layer section.</summary>
        private void Catalog()
        {
            GUILayout.Label("FROM THE GAME'S OWN EFFECTS · every weapon, every piece", _cap);
            _catFilter = GUILayout.TextField(_catFilter, _field);
            bool searching = _catFilter.Length >= 2;
            if (searching)
            {
                int n = 0;
                foreach (string e in _effects)
                {
                    string name = Pretty(e);
                    if ((name + " whole effect").IndexOf(_catFilter, StringComparison.OrdinalIgnoreCase) >= 0 && ++n <= 40 && GUILayout.Button("+ " + name + " · whole effect", _btn)) AddWeapon(e);
                    foreach (string[] p in PartsOf(e))
                    {
                        string label = name + " · " + p[1];
                        if (label.IndexOf(_catFilter, StringComparison.OrdinalIgnoreCase) < 0 && e.IndexOf(_catFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (++n > 40) break;
                        if (GUILayout.Button("+ " + label, _btn)) AddPart(e, p[0]);
                    }
                }
                foreach (string[] v in VfxAll())
                {
                    if (n > 40) { GUILayout.Label("… type more to narrow the list", _small); break; }
                    if (v[1].IndexOf(_catFilter, StringComparison.OrdinalIgnoreCase) < 0 && v[0].IndexOf(_catFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    n++;
                    if (GUILayout.Button("+ " + Capital(v[1]) + "   ·   " + v[0], _btn)) AddVfx(v[0]);
                }
                if (n == 0) GUILayout.Label("Nothing matches.", _small);
                return;
            }
            // weapons by family, each with its parts spelled out
            string family = null; bool familyOpen = false;
            foreach (string e in _effects)
            {
                string fam = Family(e);
                if (fam != family) { family = fam; familyOpen = Fold("fam:" + fam, fam + "  ·  " + _effects.Count(x => Family(x) == fam) + " weapons"); }
                if (!familyOpen) continue;
                GUILayout.BeginHorizontal(); GUILayout.Space(12);
                bool wOpen = Fold("w:" + e, Pretty(e));
                GUILayout.EndHorizontal();
                if (!wOpen) continue;
                var parts = PartsOf(e);
                GUILayout.BeginHorizontal(); GUILayout.Space(24);
                GUILayout.BeginVertical();
                if (GUILayout.Button("Use as the recipe's base effect (its beam or projectile)", _btnSmall)) { _recipe.baseEffect = e; ApplyHint(); Fire(); }
                if (GUILayout.Button("+ " + Pretty(e) + " · whole effect as a layer (scale and colour it on its own)", _btnSmall)) AddWeapon(e);
                foreach (string[] p in parts) if (GUILayout.Button("+ " + Pretty(e) + " · " + p[1], _btnSmall)) AddPart(e, p[0]);
                if (parts.Count == 0) GUILayout.Label("No separate pieces: this effect is only its projectile.", _small);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            // every other prefab
            var all = VfxAll();
            if (Fold("vfx", "Every other game VFX  ·  " + all.Count + " prefabs"))
            {
                string cat = null; bool catOpen = false;
                foreach (string[] v in all)
                {
                    if (v[2] != cat) { cat = v[2]; GUILayout.BeginHorizontal(); GUILayout.Space(12); catOpen = Fold("cat:" + cat, cat + "  ·  " + all.Count(x => x[2] == cat)); GUILayout.EndHorizontal(); }
                    if (!catOpen) continue;
                    GUILayout.BeginHorizontal(); GUILayout.Space(24);
                    if (GUILayout.Button("+ " + Capital(v[1]) + "   ·   " + v[0], _btnSmall)) AddVfx(v[0]);
                    GUILayout.EndHorizontal();
                }
            }
        }
    }
}
