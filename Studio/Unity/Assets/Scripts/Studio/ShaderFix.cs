// The game's VFX shaders ("BattleTech/VFX/Alpha", "AlphaNoLighting", "Blackbody", "Additive", "Distortion",
// "VFX-Deferred") read fog-of-war, biome and camera-depth globals that only the game's renderer fills in, so in the
// studio they draw nothing. Every effect material is moved once onto the built-in particle shader with the same
// blending. The colour those shaders produce comes from two properties: _TintColor (or _Color) and the HDR blackbody
// colour _ColorBB, which is what carries a laser's red or a PPC's blue, and what CustomAmmoCategories writes when a
// weapon's ColorsTable recolours an effect. The two are folded into the one tint the particle shader reads, and the
// originals are remembered per material so a recolour can be folded the same way at run time.
using System.Collections.Generic;
using UnityEngine;

namespace GNFX
{
    public static class ShaderFix
    {
        /// <summary>What a material looked like before the swap: the pieces the game's shader combined.</summary>
        public class Base
        {
            public Color Tint = Color.white;     // _TintColor or _Color, alpha included
            public Color BB = Color.black;       // _ColorBB, HDR
            public bool Mult;                    // _MultEmissive: tint × BB; otherwise tint + BB
            public string TintProp = "_TintColor";
            public string From;
        }

        private static readonly Dictionary<int, Base> _bases = new Dictionary<int, Base>();
        private static readonly Dictionary<string, Shader> _cache = new Dictionary<string, Shader>();
        public static int Swapped, Hidden;
        public const float MaxHdr = 12f;          // brighter than this only widens the bloom

        private static Shader Find(string name)
        {
            Shader s;
            if (_cache.TryGetValue(name, out s)) return s;
            s = Shader.Find(name);
            if (s == null) Debug.LogWarning("[Studio] shader missing from the build: " + name);
            _cache[name] = s;
            return s;
        }

        /// <summary>Fix every material on every renderer under a spawned object, including inactive ones.</summary>
        public static void Apply(GameObject g)
        {
            if (g == null) return;
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
            {
                Material[] ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                    if (ms[i] != null && ms[i].shader != null && ms[i].shader.name.StartsWith("BattleTech/VFX/Distortion")) { r.enabled = false; Hidden++; }
                    else Fix(ms[i]);
            }
        }

        /// <summary>The remembered originals of a swapped material, or null when the material was left alone.</summary>
        public static Base BaseOf(Material m)
        {
            Base b;
            return m != null && _bases.TryGetValue(m.GetInstanceID(), out b) ? b : null;
        }

        public static void Fix(Material m)
        {
            if (m == null || m.shader == null) return;
            int id = m.GetInstanceID();
            if (_bases.ContainsKey(id)) return;
            string from = m.shader.name;
            if (!from.StartsWith("BattleTech/") && m.shader.isSupported) return;
            Shader to; var b = new Base { From = from };
            if (from.Contains("VFX-Deferred")) { to = Find("Standard"); b.TintProp = "_Color"; }
            else if (from.Contains("Additive")) to = Find("Legacy Shaders/Particles/Additive");
            else if (from.StartsWith("BattleTech/VFX/")) to = Find("Legacy Shaders/Particles/Alpha Blended");
            else return;                                        // Mech and terrain shaders from the shaders bundle work as they are
            if (to == null) return;
            _bases[id] = b;
            // what the game's shader combined
            if (m.HasProperty("_ColorBB")) b.BB = m.GetColor("_ColorBB");
            if (from.Contains("Blackbody"))
            {
                b.Tint = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                bool ignoreTemp = m.HasProperty("_IgnoreTemp") && m.GetFloat("_IgnoreTemp") > 0.5f;
                if (!ignoreTemp && m.HasProperty("_TemperatureKelvin")) { Color k = Kelvin(m.GetFloat("_TemperatureKelvin")); b.Tint = new Color(b.Tint.r * k.r, b.Tint.g * k.g, b.Tint.b * k.b, b.Tint.a); }
            }
            else if (from.Contains("Additive")) { b.Tint = Color.white; b.Mult = b.BB.maxColorComponent > 0.001f; }
            else
            {
                b.Tint = m.HasProperty("_TintColor") ? m.GetColor("_TintColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                b.Mult = m.HasProperty("_MultEmissive") && m.GetFloat("_MultEmissive") > 0.5f;
            }
            if (from.Contains("VFX-Deferred")) { b.Tint = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white; b.BB = Color.black; }
            Texture main = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            Vector2 scale = main != null ? m.GetTextureScale("_MainTex") : Vector2.one, offset = main != null ? m.GetTextureOffset("_MainTex") : Vector2.zero;
            int queue = m.renderQueue;
            m.shader = to;
            if (main != null) { m.SetTexture("_MainTex", main); m.SetTextureScale("_MainTex", scale); m.SetTextureOffset("_MainTex", offset); }
            SetBB(m, b, b.BB);
            if (queue > 0) m.renderQueue = queue;
            Swapped++;
        }

        /// <summary>Write a blackbody colour into a swapped material the way its original shader would have shown it.</summary>
        public static void SetBB(Material m, Base b, Color bb)
        {
            if (m == null || b == null) return;
            m.SetColor(b.TintProp, Combine(b, bb));
            m.SetColor("_ColorBB", bb);
        }

        public static Color Combine(Base b, Color bb)
        {
            Color c = b.Mult
                ? new Color(b.Tint.r * bb.r, b.Tint.g * bb.g, b.Tint.b * bb.b, b.Tint.a)
                : new Color(b.Tint.r + bb.r, b.Tint.g + bb.g, b.Tint.b + bb.b, b.Tint.a);
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (max > MaxHdr) { float k = MaxHdr / max; c.r *= k; c.g *= k; c.b *= k; }
            return c;
        }

        /// <summary>Approximate colour of a blackbody at a temperature in Kelvin (1000–40000).</summary>
        public static Color Kelvin(float kelvin)
        {
            float t = Mathf.Clamp(kelvin, 1000f, 40000f) / 100f;
            float r, g, bl;
            if (t <= 66f) { r = 255f; g = 99.4708025861f * Mathf.Log(t) - 161.1195681661f; bl = t <= 19f ? 0f : 138.5177312231f * Mathf.Log(t - 10f) - 305.0447927307f; }
            else { r = 329.698727446f * Mathf.Pow(t - 60f, -0.1332047592f); g = 288.1221695283f * Mathf.Pow(t - 60f, -0.0755148492f); bl = 255f; }
            return new Color(Mathf.Clamp01(r / 255f), Mathf.Clamp01(g / 255f), Mathf.Clamp01(bl / 255f), 1f);
        }
    }
}
