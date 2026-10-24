using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CombatVFX
{
    public class WeightClassConfig
    {
        public string Name                    = "Unknown";
        public float  MaxTonnage              = 9999f;
        public float  DeflectChanceMultiplier = 1.0f;
        public float  DamageFraction          = 0.15f;  // fraction of original damage that applies on deflect
    }

    public class Settings
    {
        public bool TestMode = false;
        public bool DebugLogging = false;   // per-effect TRACE/DIAG lines in BallisticEffects_log.txt

        public float DeflectRadius       = 6.5f;
        public float GlancingAngleBonus  = 0.20f;
        public float MinDeflectChance    = 0.02f;
        public float MaxDeflectChance    = 0.55f;

        // ── Proposal A formula parameters ─────────────────────────────────────
        // chance = BaseDeflectChance × DamageScalar × DistanceScalar
        //        × WeightMultiplier × ArmorScalar
        //        + GlancingAngleBonus × sin(2θ)

        public float BaseDeflectChance = 0.28f;

        // DamageScalar = clamp((ReferenceDamage / weaponDamage)^DamagePower, 0.1, MaxDamageScalar)
        public float ReferenceDamage  = 20f;
        public float DamagePower      = 0.5f;
        public float MaxDamageScalar  = 2.5f;

        // DistanceScalar = lerp(CloseRangeScalar, FarRangeScalar, clamp01(distance / MaxRangeRef))
        public float CloseRangeScalar = 0.50f;
        public float FarRangeScalar   = 1.40f;
        public float MaxRangeRef      = 300f;

        // ── Armor ─────────────────────────────────────────────────────────────
        // ArmorScalar = currentTotalArmor / classAverageTotalArmor, clamped 0-1
        public Dictionary<string, float> AverageTotalArmorByClass
            = new Dictionary<string, float>
            {
                { "Light",   344f },
                { "Medium",  657f },
                { "Heavy",   857f },
                { "Assault", 1128f }
            };

        public float GetAverageTotalArmor(string weightClassName)
        {
            if (AverageTotalArmorByClass.TryGetValue(weightClassName, out float val))
                return val;
            return 500f;
        }

        // ── Weight class ──────────────────────────────────────────────────────
        public List<WeightClassConfig> WeightClassBrackets = new List<WeightClassConfig>();

        public WeightClassConfig GetWeightClass(float tonnage)
        {
            foreach (var w in WeightClassBrackets)
                if (tonnage <= w.MaxTonnage) return w;
            return WeightClassBrackets.Count > 0
                ? WeightClassBrackets[WeightClassBrackets.Count - 1]
                : new WeightClassConfig();
        }

        // ── VFX ───────────────────────────────────────────────────────────────
        // Tier determined by weapon damage per shot
        public float VFXLightMaxDamage  = 12f;   // damage < this → light sparks
        public float VFXHeavyMinDamage  = 60f;   // damage >= this → heavy shower

        // ── Ground deflect ────────────────────────────────────────────────────
        // When a ballistic shot hits terrain, it ricochets. If the reflected ray
        // intersects a mech, that mech takes reduced damage (always to the legs).
        public bool  GroundDeflectEnabled        = true;
        public float GroundDeflectDamageFraction = 0.35f; // fraction of original weapon damage
        public float GroundDeflectMaxBounceRange = 80f;   // max distance the bounce ray checks for mechs

        // ── Singleton ─────────────────────────────────────────────────────────
        private static Settings _instance;
        public  static Settings Instance => _instance ?? (_instance = new Settings());

        public static void Load(string json)
        {
            try
            {
                _instance = JsonConvert.DeserializeObject<Settings>(json) ?? new Settings();
                var s = _instance;

                ModEntry.Log("───────────────────────────────────────────────────────");
                ModEntry.Log("[BESettings] Configuration loaded:");
                ModEntry.Log($"  TestMode             : {s.TestMode}");
                ModEntry.Log($"  DeflectRadius        : {s.DeflectRadius:F1}m");
                ModEntry.Log($"  BaseDeflectChance    : {s.BaseDeflectChance:F2}");
                ModEntry.Log($"  ReferenceDamage      : {s.ReferenceDamage:F1}");
                ModEntry.Log($"  DamagePower          : {s.DamagePower:F2}");
                ModEntry.Log($"  MaxDamageScalar      : {s.MaxDamageScalar:F2}");
                ModEntry.Log($"  CloseRangeScalar     : {s.CloseRangeScalar:F2}");
                ModEntry.Log($"  FarRangeScalar       : {s.FarRangeScalar:F2}");
                ModEntry.Log($"  MaxRangeRef          : {s.MaxRangeRef:F0}m");
                ModEntry.Log($"  MinDeflectChance     : {s.MinDeflectChance:F2}");
                ModEntry.Log($"  MaxDeflectChance     : {s.MaxDeflectChance:F2}");
                ModEntry.Log($"  GlancingAngleBonus   : {s.GlancingAngleBonus:F2}");
                ModEntry.Log($"  GroundDeflectEnabled : {s.GroundDeflectEnabled}");
                ModEntry.Log($"  GroundDamageFraction : {s.GroundDeflectDamageFraction:F2}");
                ModEntry.Log($"  GroundBounceRange    : {s.GroundDeflectMaxBounceRange:F0}m");
                ModEntry.Log($"  VFXLightMaxDamage    : {s.VFXLightMaxDamage:F1}");
                ModEntry.Log($"  VFXHeavyMinDamage    : {s.VFXHeavyMinDamage:F1}");
                ModEntry.Log($"  Weight classes ({s.WeightClassBrackets.Count}):");
                foreach (var wc in s.WeightClassBrackets)
                    ModEntry.Log($"    {wc.Name,-8} ≤{wc.MaxTonnage:F0}T" +
                                 $"  mult={wc.DeflectChanceMultiplier:F2}" +
                                 $"  damageFrac={wc.DamageFraction:F2}");
                ModEntry.Log($"  Avg total armor:");
                foreach (var kvp in s.AverageTotalArmorByClass)
                    ModEntry.Log($"    {kvp.Key,-8} : {kvp.Value:F0}");
                ModEntry.Log("───────────────────────────────────────────────────────");

                if (s.TestMode)
                    ModEntry.Log("[BESettings] *** TEST MODE ACTIVE — 100% deflect chance ***");
            }
            catch (Exception ex)
            {
                _instance = new Settings();
                ModEntry.Log($"[BESettings] Parse error — using defaults. Error: {ex.Message}");
            }
        }
    }
}
