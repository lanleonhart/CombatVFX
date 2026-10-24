// The effect recipe: a stock game effect plus the CustomAmmoCategories knobs that reshape it in a weapon's JSON
// (ColorsTable, ColorChangeRule, ColorSpeedChange, ProjectileScale, ProjectileSpeedMultiplier), and optional drawn
// layers that the CombatVFX mod adds on top. Version 1 files (layers only) still load; baseEffect is empty.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace GNFX
{
    [Serializable]
    public class ColorEntry
    {
        public string C = "#ffffff";     // html colour, as CAC's ColorsTable entry
        public float I = 1f;             // intensity: the colour is multiplied by it (HDR)
    }

    /// <summary>The stock effect's own numbers and pieces, each left at its stock value unless set (-1 and "" mean stock).</summary>
    [Serializable]
    public class BaseSettings
    {
        public float preFireDuration = -1f, projectileSpeed = -1f, delayMin = -1f, delayMax = -1f;
        public float lightIntensity = -1f, lightRadius = -1f, pulseDelay = -1f;                 // lasers
        public float spreadAngle = -1f, shotDelay = -1f, burstDelay = -1f;                      // ballistics
        public float firingInterval = -1f, volleyInterval = -1f, curveStrength = -1f; public int curveFrequency = -1;   // missiles
        public float impactTime = -1f;                                                           // flamer
        public string prefirePart = "", flashPart = "", impactPart = "", armorPart = "", structurePart = "";   // "" stock, "none", or an effect id (impact: "effect|variation")
        public bool matchColors = true;                                                          // tint pre-fire, flash and impact to the colour table

        public bool AnySet => preFireDuration >= 0f || projectileSpeed >= 0f || delayMin >= 0f || delayMax >= 0f || lightIntensity >= 0f || lightRadius >= 0f || pulseDelay >= 0f || spreadAngle >= 0f || shotDelay >= 0f || burstDelay >= 0f || firingInterval >= 0f || volleyInterval >= 0f || curveStrength >= 0f || curveFrequency >= 0 || impactTime >= 0f || prefirePart != "" || flashPart != "" || impactPart != "" || armorPart != "" || structurePart != "";
    }

    [Serializable]
    public class Layer
    {
        public string kind = "beam";
        public bool on = true;
        public float start;
        // beam, bolt, trail, flash, burst, light
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
        // bolt
        public string shape = "bolt";
        public float length = 3f;
        public float speed = 300f;
        public float spin;
        public float arc;
        public float scatter;
        // trail
        public float opacity = 0.6f;
        // flash and burst
        public float size = 2f;
        public float spread = 35f;
        public float life = 0.5f;
        public string texture = "soft";
        public float gravity = 20f;
        public float ring = 8f;
        // light
        public string anchor = "impact";
        public float intensity = 12f;
        public float radius = 60f;
        // ring, glow, scorch, vfx
        public string facing = "camera";   // camera, flat, beam
        public string prefab = "";         // vfx: a game VFX prefab id
        // helix, arc
        public float turns = 3f;
        public float rate = 20f;           // arc re-rolls per second
        // when: "fire" (from the shot) or "prefire" (before it, during the weapon's charge-up)
        public string phase = "fire";
        // part: a piece of one of the game's own effects
        public string effect = "";         // a stock effect id, e.g. WeaponEffect-Weapon_Laser_Large
        public string part = "flash";      // prefire, flash, impact, armor, structure, terrain, building
        public string tintMode = "stock";  // part and vfx: stock | recipe (the colour table) | custom (this layer's colour)

        public Layer Clone() => JsonUtility.FromJson<Layer>(JsonUtility.ToJson(this));

        public static Layer New(string kind)
        {
            var l = new Layer { kind = kind };
            switch (kind)
            {
                case "bolt": l.color = "#e040fb"; l.glow = 2f; break;
                case "trail": l.color = "#ff9ad0"; l.length = 30f; l.width = 0.4f; break;
                case "flash": l.color = "#ffd27a"; l.count = 60; l.size = 2.5f; l.speed = 14f; l.life = 0.35f; break;
                case "burst": l.color = "#ff7a3d"; l.count = 120; l.size = 2f; l.speed = 30f; l.life = 0.8f; l.texture = "spark"; break;
                case "light": l.color = "#ff5fb8"; l.duration = 0.5f; break;
                case "ring": l.color = "#9ad8ff"; l.anchor = "impact"; l.size = 6f; l.duration = 0.5f; l.facing = "camera"; break;
                case "orb": l.color = "#ff5fb8"; l.core = "#ffffff"; l.anchor = "muzzle"; l.size = 1.5f; l.duration = 0.4f; l.glow = 2f; l.fade = "in"; break;
                case "glow": l.color = "#ffb060"; l.anchor = "muzzle"; l.size = 4f; l.duration = 0.25f; l.glow = 3f; l.texture = "soft"; break;
                case "scorch": l.color = "#120a06"; l.anchor = "impact"; l.size = 3f; l.duration = 6f; l.opacity = 0.85f; l.facing = "beam"; l.texture = "soft"; break;
                case "arc": l.color = "#7fd0ff"; l.core = "#ffffff"; l.width = 0.25f; l.glow = 3f; l.duration = 0.4f; l.jitter = 2.5f; l.count = 4; l.length = 6f; l.rate = 24f; l.reach = "beam"; l.anchor = "impact"; l.fade = "flicker"; break;
                case "smoke": l.color = "#6a6760"; l.anchor = "impact"; l.count = 24; l.size = 3f; l.speed = 3f; l.life = 2.5f; l.opacity = 0.5f; break;
                case "embers": l.color = "#ff9a3d"; l.anchor = "impact"; l.count = 60; l.size = 0.25f; l.speed = 12f; l.gravity = 12f; l.life = 2f; break;
                case "debris": l.color = "#3a3a36"; l.anchor = "impact"; l.count = 24; l.size = 0.35f; l.speed = 18f; l.gravity = 25f; l.life = 2f; l.spread = 50f; break;
                case "casing": l.color = "#c9a24a"; l.count = 6; l.size = 0.12f; l.speed = 6f; l.gravity = 20f; l.life = 2f; l.gap = 0.12f; break;
                case "charge": l.color = "#ff5fb8"; l.anchor = "muzzle"; l.count = 80; l.size = 0.35f; l.radius = 5f; l.duration = 0.6f; l.texture = "soft"; break;
                case "pulse": l.color = "#ffffff"; l.count = 6; l.size = 0.8f; l.speed = 400f; l.gap = 0.06f; l.duration = 0.6f; l.texture = "soft"; break;
                case "spray": l.color = "#ff8a3d"; l.count = 300; l.size = 1.5f; l.speed = 60f; l.spread = 8f; l.life = 0.6f; l.duration = 0.8f; l.gravity = 2f; l.texture = "soft"; break;
                case "helix": l.color = "#7cf2c8"; l.count = 2; l.width = 0.25f; l.radius = 1.2f; l.turns = 6f; l.spin = 2f; l.duration = 0.6f; l.reach = "instant"; l.fade = "ease out"; break;
                case "vfx": l.anchor = "impact"; l.size = 1f; l.duration = 2f; l.facing = "beam"; break;
                case "part": l.anchor = "muzzle"; l.size = 1f; l.duration = 2f; l.facing = "beam"; l.part = "flash"; break;
                case "shake": l.anchor = "impact"; l.intensity = 4f; l.duration = 0.35f; break;
            }
            return l;
        }
    }

    [Serializable]
    public class Recipe
    {
        public string name = "Untitled";
        public int version = 2;
        // the game effect this recipe starts from, and CAC's knobs on it
        public string baseEffect = "";                                   // "WeaponEffect-Weapon_LaserPulse_Medium"; empty = layers only
        public List<ColorEntry> colors = new List<ColorEntry>();        // ColorsTable
        public string colorRule = "t0";                                  // ColorChangeRule
        public float colorSpeed = 0f;                                    // ColorSpeedChange
        public Vector3 scale = Vector3.one;                              // ProjectileScale
        public float speedMul = 1f;                                      // ProjectileSpeedMultiplier
        public BaseSettings baseSettings = new BaseSettings();           // the stock effect's own numbers and pieces
        // the recipe in the library and in the game
        public string tags = "";                                         // comma-separated, free text, for the library filter
        public string sound = "";                                        // studio-only: recipes\sounds file played at the muzzle on fire
        public string impactSound = "";                                  // studio-only: played at the impact point
        public bool hideStock;                                           // in the game: hide the stock projectile when the layers replace it
        public float prefire = 0.5f;                                     // length of the pre-fire phase when there is no base effect to take it from
        // drawn on top by the CombatVFX mod
        public List<Layer> layers = new List<Layer>();

        public bool HasBase => !string.IsNullOrEmpty(baseEffect);
        public bool HasLayers { get { foreach (Layer l in layers) if (l != null && l.on) return true; return false; } }

        /// <summary>A copy with only one layer switched on, for soloing.</summary>
        public Recipe Solo(int index)
        {
            Recipe c = JsonUtility.FromJson<Recipe>(JsonUtility.ToJson(this));
            for (int i = 0; i < c.layers.Count; i++) c.layers[i].on = i == index;
            return c;
        }

        /// <summary>How long the shot lasts from fire to the last layer's end, for the scrub bar; the base effect adds its flight.</summary>
        public float Length(float range, float pre = 0f)
        {
            float impact, end; Timeline(range, pre, out impact, out end);
            return Mathf.Max(end, pre + (HasBase ? 1.6f : 0.5f));
        }

        /// <summary>The colour the pre-fire, flash and impact are matched to: the first colour-table entry, normalised.</summary>
        public Color? MatchColor { get { if (colors == null || colors.Count == 0) return null; Color c = Col(colors[0].C); float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b)); return m > 0.001f ? new Color(c.r / m, c.g / m, c.b / m, 1f) : (Color?)null; } }

        public bool HasPrefire { get { foreach (Layer l in layers) if (l != null && l.on && l.phase == "prefire") return true; return false; } }

        public static Recipe Parse(string json)
        {
            Recipe r = JsonUtility.FromJson<Recipe>(json);
            if (r == null) throw new Exception("Not a recipe");
            if (r.layers == null) r.layers = new List<Layer>();
            if (r.colors == null) r.colors = new List<ColorEntry>();
            if (r.baseSettings == null) r.baseSettings = new BaseSettings();
            foreach (Layer l in r.layers) if (string.IsNullOrEmpty(l.tintMode)) l.tintMode = "stock";
            r.layers.RemoveAll(l => l == null || string.IsNullOrEmpty(l.kind));
            if (r.scale == Vector3.zero) r.scale = Vector3.one;
            if (string.IsNullOrEmpty(r.colorRule)) r.colorRule = "t0";
            if (r.speedMul <= 0f) r.speedMul = 1f;
            if (r.tags == null) r.tags = ""; if (r.sound == null) r.sound = ""; if (r.impactSound == null) r.impactSound = "";
            if (!r.HasBase && r.layers.Count == 0) throw new Exception("Not a recipe: no base effect and no layers");
            return r;
        }

        public string ToJson() { version = 2; return JsonUtility.ToJson(this, true); }

        /// <summary>The lines to paste into the weapon's JSON so CAC fires this recipe's base effect the same way.</summary>
        public string CacJson()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("\"WeaponEffectID\": \"").Append(baseEffect).Append("\",\n");
            if (colors.Count > 0)
            {
                sb.Append("\"ColorsTable\": [ ");
                for (int i = 0; i < colors.Count; i++) sb.Append(i > 0 ? ", " : "").Append("{ \"C\": \"").Append(colors[i].C).Append("\", \"I\": ").Append(colors[i].I.ToString("0.##", ci)).Append(" }");
                sb.Append(" ],\n\"ColorChangeRule\": \"").Append(colorRule).Append("\",\n");
                if (colorSpeed > 0f) sb.Append("\"ColorSpeedChange\": ").Append(colorSpeed.ToString("0.##", ci)).Append(",\n");
            }
            if (scale != Vector3.one) sb.Append("\"ProjectileScale\": { \"x\": ").Append(scale.x.ToString("0.##", ci)).Append(", \"y\": ").Append(scale.y.ToString("0.##", ci)).Append(", \"z\": ").Append(scale.z.ToString("0.##", ci)).Append(" },\n");
            if (Math.Abs(speedMul - 1f) > 0.001f) sb.Append("\"ProjectileSpeedMultiplier\": ").Append(speedMul.ToString("0.##", ci)).Append(",\n");
            return sb.ToString().TrimEnd('\n', ',');
        }

        public struct Item { public Layer L; public float Start, End; }

        /// <summary>When each layer starts and ends after Fire, for a range in metres: bursts and impact lights wait for the bolt.</summary>
        public List<Item> Timeline(float range, out float impact, out float end) { return Timeline(range, 0f, out impact, out end); }

        /// <summary>Pre-fire layers run from 0; everything else is shifted by the pre-fire length, so the shot lands after the charge.</summary>
        public List<Item> Timeline(float range, float pre, out float impact, out float end)
        {
            impact = pre;
            bool anyBolt = false;
            foreach (Layer l in layers) if (l.on && l.kind == "bolt" && l.phase != "prefire") { anyBolt = true; impact = Mathf.Max(impact, pre + l.start + range / Mathf.Max(1f, l.speed) + (l.count - 1) * l.gap); }
            if (!anyBolt) foreach (Layer l in layers) if (l.on && l.kind == "beam" && l.phase != "prefire") impact = Mathf.Max(impact, pre + l.start + (l.reach == "sweep" ? Mathf.Min(0.25f, l.duration * 0.5f) : 0f));
            var items = new List<Item>();
            end = 0f;
            foreach (Layer l in layers)
            {
                if (!l.on) continue;
                bool prefire = l.phase == "prefire";
                float s = prefire ? l.start : pre + l.start, e;
                bool anchored = l.kind == "burst" || l.kind == "light" || l.kind == "ring" || l.kind == "orb" || l.kind == "glow" || l.kind == "scorch" || l.kind == "smoke" || l.kind == "embers" || l.kind == "debris" || l.kind == "vfx" || l.kind == "part" || l.kind == "shake" || (l.kind == "arc" && l.reach == "fork");
                if (anchored && !prefire && (l.anchor == "impact" || l.anchor == "ground")) s += impact - pre;
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
