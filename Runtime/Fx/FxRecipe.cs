// An effect recipe as the Effects Studio writes it: a stock game effect plus CustomAmmoCategories' knobs (which the
// weapon JSON carries and CAC applies) and the drawn layers CombatVFX plays on top. Only the layers matter here; the
// base effect and CAC fields are read for information. The JSON field names are the studio's (Studio\Unity\Assets\
// Scripts\Studio\Recipe.cs) and must stay in step with it. JsonUtility reads both, so a file the studio wrote loads here
// unchanged.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CombatVFX.Fx
{
    [Serializable]
    public class FxColor { public string C = "#ffffff"; public float I = 1f; }

    /// <summary>The stock effect's own numbers and pieces; -1 and "" mean "as the game has it".</summary>
    [Serializable]
    public class FxBase
    {
        public float preFireDuration = -1f, projectileSpeed = -1f, delayMin = -1f, delayMax = -1f;
        public float lightIntensity = -1f, lightRadius = -1f, pulseDelay = -1f;
        public float spreadAngle = -1f, shotDelay = -1f, burstDelay = -1f;
        public float firingInterval = -1f, volleyInterval = -1f, curveStrength = -1f; public int curveFrequency = -1;
        public float impactTime = -1f;
        public string prefirePart = "", flashPart = "", impactPart = "", armorPart = "", structurePart = "";
        public bool matchColors = true;
        public bool AnySet => preFireDuration >= 0f || projectileSpeed >= 0f || delayMin >= 0f || delayMax >= 0f || lightIntensity >= 0f || lightRadius >= 0f || pulseDelay >= 0f || spreadAngle >= 0f || shotDelay >= 0f || burstDelay >= 0f || firingInterval >= 0f || volleyInterval >= 0f || curveStrength >= 0f || curveFrequency >= 0 || impactTime >= 0f || prefirePart != "" || flashPart != "" || impactPart != "" || armorPart != "" || structurePart != "";
    }

    [Serializable]
    public class FxLayer
    {
        public string kind = "beam";
        public bool on = true;
        public float start;
        public string color = "#ff5fb8";
        public string core = "#ffffff";
        public float width = 0.5f;
        public float glow = 3f;
        public float duration = 0.6f;
        public string fade = "ease out";
        public string reach = "instant";
        public int count = 1;
        public float gap = 0.08f;
        public float jitter;
        public string shape = "bolt";
        public float length = 3f;
        public float speed = 300f;
        public float spin;
        public float arc;
        public float scatter;
        public float opacity = 0.6f;
        public float size = 2f;
        public float spread = 35f;
        public float life = 0.5f;
        public string texture = "soft";
        public float gravity = 20f;
        public float ring = 8f;
        public string anchor = "impact";
        public float intensity = 12f;
        public float radius = 60f;
        public string facing = "camera";
        public string prefab = "";
        public float turns = 3f;
        public float rate = 20f;
        public string phase = "fire";      // fire | prefire
        public string effect = "";         // part: a stock effect id
        public string part = "flash";      // part: prefire, flash, impact, armor, structure, terrain, building
        public string tintMode = "stock";  // part and vfx: stock | recipe | custom
    }

    [Serializable]
    public class FxRecipe
    {
        public string name = "Untitled";
        public int version = 2;
        public string baseEffect = "";
        public List<FxColor> colors = new List<FxColor>();
        public string colorRule = "t0";
        public float colorSpeed;
        public Vector3 scale = Vector3.one;
        public float speedMul = 1f;
        public FxBase baseSettings = new FxBase();
        public string tags = "";
        public string sound = "";
        public string impactSound = "";
        public bool hideStock;
        public float prefire = 0.5f;
        public List<FxLayer> layers = new List<FxLayer>();

        [NonSerialized] public string File;

        public bool HasLayers { get { foreach (FxLayer l in layers) if (l != null && l.on) return true; return false; } }

        /// <summary>Anything for the game to do beyond CAC: layers, base settings, or a colour match with a colour table.</summary>
        public bool Matters => HasLayers || (baseSettings != null && baseSettings.AnySet) || (baseSettings != null && baseSettings.matchColors && colors.Count > 0);

        /// <summary>The colour the pre-fire, flash and impact are matched to: the first colour-table entry, normalised.</summary>
        public Color? MatchColor { get { if (colors == null || colors.Count == 0) return null; Color c = Col(colors[0].C); float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b)); return m > 0.001f ? new Color(c.r / m, c.g / m, c.b / m, 1f) : (Color?)null; } }
        public bool HasPhase(string phase) { foreach (FxLayer l in layers) if (l != null && l.on && (l.phase ?? "fire") == phase) return true; return false; }

        public static FxRecipe Parse(string json)
        {
            FxRecipe r = JsonUtility.FromJson<FxRecipe>(json);
            if (r == null) throw new Exception("not a recipe");
            if (r.layers == null) r.layers = new List<FxLayer>();
            if (r.baseSettings == null) r.baseSettings = new FxBase();
            if (r.colors == null) r.colors = new List<FxColor>();
            r.layers.RemoveAll(l => l == null || string.IsNullOrEmpty(l.kind));
            foreach (FxLayer l in r.layers) if (string.IsNullOrEmpty(l.tintMode)) l.tintMode = "stock";
            return r;
        }

        public struct Item { public FxLayer L; public float Start, End; }

        /// <summary>When each layer starts and ends after the shot, for a range in metres; impact-anchored layers wait for the bolt.</summary>
        public List<Item> Timeline(float range, out float impact, out float end)
        {
            impact = 0f;
            bool anyBolt = false;
            foreach (FxLayer l in layers) if (l.on && l.kind == "bolt") { anyBolt = true; impact = Mathf.Max(impact, l.start + range / Mathf.Max(1f, l.speed) + (l.count - 1) * l.gap); }
            if (!anyBolt) foreach (FxLayer l in layers) if (l.on && l.kind == "beam") impact = Mathf.Max(impact, l.start + (l.reach == "sweep" ? Mathf.Min(0.25f, l.duration * 0.5f) : 0f));
            var items = new List<Item>();
            end = 0f;
            foreach (FxLayer l in layers)
            {
                if (!l.on) continue;
                float s = l.start, e;
                bool anchored = l.kind == "burst" || l.kind == "light" || l.kind == "ring" || l.kind == "orb" || l.kind == "glow" || l.kind == "scorch" || l.kind == "smoke" || l.kind == "embers" || l.kind == "debris" || l.kind == "vfx" || l.kind == "part" || l.kind == "shake" || (l.kind == "arc" && l.reach == "fork");
                if (anchored && (l.phase ?? "fire") != "prefire" && (l.anchor == "impact" || l.anchor == "ground")) s += impact;
                switch (l.kind)
                {
                    case "beam": e = s + l.count * l.duration + (l.count - 1) * l.gap; break;
                    case "bolt": e = s + range / Mathf.Max(1f, l.speed) + (l.count - 1) * l.gap; break;
                    case "trail": e = Mathf.Max(s, impact) + 0.4f; break;
                    case "flash": e = s + l.life; break;
                    case "burst": e = s + l.life + 0.2f; break;
                    case "light": case "ring": case "orb": case "glow": case "scorch": case "arc": case "helix": case "vfx": case "part": case "shake": case "charge": e = s + l.duration; break;
                    case "smoke": case "embers": case "debris": case "casing": e = s + l.life * 1.4f + 0.3f; break;
                    case "pulse": e = s + l.duration + range / Mathf.Max(1f, l.speed); break;
                    case "spray": e = s + l.duration + l.life; break;
                    default: e = s; break;
                }
                items.Add(new Item { L = l, Start = s, End = e });
                end = Mathf.Max(end, e);
            }
            return items;
        }

        public static Color Col(string hex, float a = 1f)
        {
            Color c;
            if (!ColorUtility.TryParseHtmlString(hex ?? "#ffffff", out c)) c = Color.white;
            c.a = a;
            return c;
        }
    }
}
